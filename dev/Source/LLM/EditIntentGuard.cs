using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    // A conservative preflight, not a second natural-language planner. Only an
    // explicit road edit or a short correction of a confirmed road-only edit is
    // guarded. Other topics and ambiguous requests still use normal validation.
    public static class EditIntentGuard
    {
        const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        const string RoadWords = @"도로|흙길|돌길|포장길|아스팔트|고속도로|\b(?:roads?|highways?|asphalt|dirt\s+paths?|stone\s+paths?)\b";
        const string OtherWords = @"산맥|산악|산(?:을|은|도|만|에|이|과|으로)?(?=\s|$)|호수|연못|해안|바다|강물|강(?:을|은|도|만|에|이|과)?(?=\s|$)|토양|비옥|온천|유적|동굴|건물|식물|동물|날씨|\b(?:mountains?|hills?|lakes?|ponds?|rivers?|coasts?|oceans?|soil|fertility|ruins?|caves?|buildings?|plants?|animals?|weather)\b";
        const string CorrectionWords = @"십자|교차|가운데|중앙|중심|일자|직선|반듯|곧게|자연스럽|구불|울퉁|넓|좁|두껍|얇|옮겨|이동|수정|바꿔|고쳐|지워|없애|\b(?:cross|center|centre|centered|centred|straight|straighter|winding|natural|rougher|wider|narrower|thicker|thinner|move|adjust|correct|change|remove|delete)\b";
        const string RepairReason = "The request targets local roads, but this command replaces them with another kind of map edit. Nothing was applied. Use road_ops for the requested roads and preserve their existing IDs and unrelated terrain. Only when the user requests an exact centered cross, use route:direct with centered orthogonal waypoints. If the target is unclear, return action:ask instead of inventing a mountain, passage or terrain fill.";

        public static string Rejection(List<ChatMessage> history, string response)
        {
            if (history == null || history.Count == 0) return null;
            SimpleJsonObject command;
            try { command = ProviderResponse.Command(response); }
            catch (FormatException) { return null; } // The existing envelope validator owns syntax errors.
            string action=command.GetString("action");
            if (action != "generate" && action != "revise") return null;
            var parameters = command.GetObject("params");
            if (parameters == null) return null;
            int user = history.FindLastIndex(m => m?.Role == "user");
            if (user < 0) return null;
            string request = history[user].Content ?? "";
            if(RelationshipMoveChangesGeometry(request,parameters))return "The user requested a location-only move, but this relationship update also replaces the area's shape, size or material. Nothing was applied. Preserve shapes/compose/fill/variant and update only anchor, placement and direction. If the existing footprint cannot fit, explain the limitation or ask whether resizing is acceptable; never silently shrink or replace it.";
            if(action!="generate")return null; // Existing road-target guard applies to committed edits only.
            bool explicitRoad = Matches(request, RoadWords);
            if (explicitRoad)
            {
                // Keeping an existing road while editing soil/mountains, or an
                // explicit compound request, must remain legal.
                if (AdditionalTarget(request)) return null;
            }
            else if (!TerseCorrection(request) || !ConfirmedRoadTarget(history, user)) return null;

            var roads = parameters.GetObjectArray("road_ops");
            if (roads == null || roads.Count == 0) return RepairReason;
            // Never let a road-only correction quietly add a mountain alongside
            // a token road operation. Compound edits were excluded above.
            if (parameters.Keys.Any(k => k != "road_ops")) return RepairReason;
            return null;
        }

        static bool TerseCorrection(string text) => text.Length <= 220 &&
            !Matches(text, OtherWords) && Matches(text, CorrectionWords);

        static bool RelationshipMoveChangesGeometry(string request,SimpleJsonObject parameters)
        {
            if(!Matches(request,@"옮겨|옮기|이동|\b(?:move|relocate|reposition)\b"))return false;
            if(Matches(request,@"줄여|줄이|키워|넓혀|좁혀|작게|크게|모양.*바|추가|만들|\b(?:resize|shrink|enlarge|reshape|smaller|larger|add|create)\b"))return false;
            var edits=parameters.GetObjectArray("shape_ops");if(edits==null)return false;
            return edits.Any(e=>e.GetString("op")=="update" && e.GetObject("changes") is SimpleJsonObject c &&
                (c.ContainsKey("anchor") || c.ContainsKey("placement") || c.ContainsKey("direction")) &&
                c.Keys.Any(k=>k!="anchor" && k!="placement" && k!="direction"));
        }

        static bool AdditionalTarget(string text)
        {
            // A named non-road feature alone can be a location ("road across a
            // river"). Require an edit of that feature, not merely its mention.
            if (!Matches(text, OtherWords)) return false;
            if (Matches(text, @"말고|대신|아니라|유지|그대로|\b(?:instead|rather|not|keep|preserve|leave)\b")) return true;
            const string nouns = @"(?:mountains?|hills?|lakes?|ponds?|rivers?|coasts?|oceans?|soil|fertility|ruins?|caves?|buildings?|plants?|animals?|weather)";
            if (Matches(text, @"\b(?:add|create|make|build|remove|delete|move|change|replace|flatten|raise|lower|fill|adjust)\s+(?:(?:a|an|the|new|another|some|more|less|existing)\s+){0,3}" + nouns + @"\b")) return true;
            if (Matches(text, nouns + @"\s+(?:(?:and|too|as\s+well)\b|(?:should|must)\s+be\b)")) return true;
            if (Matches(text, @"\b(?:and|plus)\s+(?:(?:a|an|the|new|another|some|more|less)\s+){0,3}" + nouns + @"\b")) return true;
            // Match a Korean target clause without crossing a road noun. This
            // preserves "강을 건너는 도로" while allowing "산을 추가하고 도로".
            var clauses = Regex.Split(text, RoadWords, Options);
            foreach (var clause in clauses)
                if (Matches(clause, @"(?:산맥|산|호수|연못|해안|바다|강|토양|비옥|온천|유적|동굴|건물|식물|동물|날씨).{0,24}(?:추가|만들|생성|지워|제거|없애|넣|옮|이동|바꿔|변경|높여|낮춰|채워|평평)")) return true;
            return false;
        }

        static bool ConfirmedRoadTarget(List<ChatMessage> history, int user)
        {
            for (int i = user - 1; i >= 0; i--)
            {
                var message = history[i];
                if (message == null) continue;
                string text = message.Content ?? "";
                if (message.Role == "user")
                {
                    if (!Matches(text, RoadWords) && !TerseCorrection(text)) return false;
                    continue;
                }
                if (message.Role != "assistant") continue;
                if (text.StartsWith("STATE REPLACED\n", StringComparison.Ordinal)) return false;
                if (text.StartsWith("NOT APPLIED\n", StringComparison.Ordinal)) continue;
                if (!text.StartsWith("APPLIED\n", StringComparison.Ordinal)) return false;
                // Only the app's receipt is authoritative. Never treat a raw
                // provider proposal, pending candidate or rejected edit as applied.
                int end = text.IndexOf('\n', 8);
                string json = end < 0 ? text.Substring(8) : text.Substring(8, end - 8);
                try
                {
                    var applied = ProviderResponse.Command(json);
                    var commands=applied.GetString("action")=="applied_plan"?applied.GetObjectArray("commands"):
                        new List<SimpleJsonObject>{applied};
                    if(commands==null || commands.Count==0)return false;
                    return commands.All(c=>c.GetString("action")=="generate" &&
                        c.GetObject("params")?.GetObjectArray("road_ops")?.Count>0 &&
                        c.GetObject("params").Keys.All(k=>k=="road_ops")) &&
                        commands.Last().GetObject("params").GetObjectArray("road_ops").Any(e=>e.GetString("op")!="remove");
                }
                catch (FormatException) { return false; }
            }
            return false;
        }

        static bool Matches(string text, string expression) => Regex.IsMatch(text ?? "", expression, Options);
    }
}
