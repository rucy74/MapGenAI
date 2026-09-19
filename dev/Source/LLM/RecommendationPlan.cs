using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MapGenAI.MapGen;
using MapGenAI.UI;

namespace MapGenAI.LLM
{
    // Store commands, not prose promises. Validation is supplied by the UI thread.
    public sealed class RecommendationPlan
    {
        public readonly string Command;
        public readonly string Summary;
        public readonly IReadOnlyList<string> Commands;
        private RecommendationPlan(string command,string summary):this(new[]{command},summary){}
        private RecommendationPlan(IReadOnlyList<string> commands,string summary){Commands=commands;Command=commands[0];Summary=summary;}
        public List<MapParamsData> Edits()=>Commands.Select(c=>MapParameterParser.Parse(ProviderResponse.Command(c).GetObject("params"))).ToList();
        public TileMapState Resolve(TileMapState before)
        {
            var state=before;
            foreach(var data in Edits())state=MapStateEditor.Merge(state,data);
            return state;
        }

        public static RecommendationPlan Refine(IReadOnlyList<RecommendationPlan> plans,int number,SimpleJsonObject parameters,
            TileMapState before,Action<IReadOnlyList<MapParamsData>> validate,bool korean,Func<string,string,PlanDefinition> lookup=null)
        {
            if(number<1 || number>plans.Count)throw new FormatException("Choose an existing candidate number");
            var old=plans[number-1];
            if(old.Commands.Count>=33)throw new FormatException("Candidate revision limit reached; select it or request new options");
            var envelope=new SimpleJsonObject();envelope.SetString("action","generate");envelope.SetObject("params",parameters);
            var commands=old.Commands.Concat(new[]{SimpleJson.Serialize(envelope)}).ToArray();
            var pending=new RecommendationPlan(commands,"");
            validate(pending.Edits());
            var after=pending.Resolve(before);
            if(MapStateCodec.ChangedFields(old.Resolve(before),after).Count==0)throw new FormatException("Candidate revision has no changes");
            for(int i=0;i<plans.Count;i++)if(i!=number-1 && MapStateCodec.Serialize(plans[i].Resolve(before))==MapStateCodec.Serialize(after))
                throw new FormatException("Revised candidate duplicates another option");
            return new RecommendationPlan(commands,new MapPlanDescription(korean,lookup).Describe(before,after));
        }

        public static string PendingInstruction(IReadOnlyList<RecommendationPlan> plans,TileMapState before)
        {
            var text=new System.Text.StringBuilder(@"
PENDING RECOMMENDATION EDITOR: the displayed candidates are NOT applied to the current map.
To modify a candidate return {""action"":""revise"",""option"":3,""params"":{...}}.
option is its existing 1-based number. params is a MINIMAL PATCH against THAT candidate's complete proposed state below.
Only that candidate changes; all others remain. Do not send action generate or apply the candidate. The user selects it separately.
Use the normal parameter/shape_ops/structure_ops schema. Do not copy state serialization field names into params.
Preserve unspecified parts. A more natural straight passage keeps points/width/fill/scope and updates only edge_roughness.
If the referenced candidate is unclear, ask which number. For new/different recommendations return recommend against the actual CURRENT map, not a candidate.
");
            for(int i=0;i<plans.Count;i++)
            {
                var state=plans[i].Resolve(before);state.imageMap=null;
                text.AppendLine("Candidate "+(i+1)+" proposed state: "+MapStateCodec.Serialize(state));
                text.AppendLine("Editable terrain IDs/schema: "+SimpleJson.Serialize(ShapeEdits.Describe(state.elevationShapes)));
            }
            return text.ToString();
        }

        public const string Rules = @"
Recommendations and selectable alternatives:
- When the user asks for recommendations, or you offer alternative map configurations, return action:recommend. Normally provide THREE distinct options; honor an explicit request for one or two, and offer fewer if constraints leave fewer valid alternatives. Each option has params using the SAME patch schema as generate. Every option is a complete independent patch against the CURRENT state, not a sequence.
- Format: {""action"":""recommend"",""options"":[{""params"":{...}},{""params"":{...}},{""params"":{...}}]}.
- For an open-ended first recommendation (e.g. '추천해줘' / 'Recommend a map'), propose visibly different LANDSCAPES, not three fertility/vegetation sliders or the same layout with different resources. Use supported terrain compositions: a mountain-enclosed settlement basin with an exit, an open valley with an offset lake, a mountain range along one side with open settlement ground, a narrow traversable canyon, or a small irregular pool with localized fertile ground. Choose three suited to this tile and the user's preferences; these are examples, not a fixed menu. Use the landform rules for dry floors and separate mountain-only exits; prefer natural outlines unless exact geometry was requested. Keep native features as optional accents, not unrelated bonus bundles. Do not promise GL's unsupported sea islands, fjords or thick-roof caves.
- If the user has already authored terrain/structures, an unqualified recommendation means three complementary additions or small edits to THAT map. Keep all existing shapes, passages, structures, materials, coverage, settings and native features unless the user asks to change them. Prefer additions that do not overlap existing authored areas. Do not silently replace a basin, repaint its interior or add another whole-map mountain formation. Only propose replacement layouts when explicitly requested.
- A scoped request ('recommend soil changes', '온천에 어울리는 특징 추천') stays within that scope; the three-landscape rule does not override it. A request for more/different suggestions should avoid the pending alternatives when present in the conversation. Distinguish choices by meaningful geometry/location or the requested effect, not internal IDs, decorative titles, or tiny numerical variations. No candidate is applied until selected.
- Check each option's spatial composition: for a pool with a wider soil surround, add the soil FIRST and the water LAST, or subtract a water-sized hole from the soil. Never cover a proposed lake with a later solid soil disk. For a dry basin keep the hidden floor smooth beneath the irregular mountain ring. For existing maps read the actual extents: 'outside the basin' must be beyond its OUTER mountain boundary, not in the mountain wall. Only cut a new opening when proposing a clearly described additional passage. Keep suggestions small enough to coexist with the existing layout.
- The application validates every option before displaying it and applies the stored command when the user selects it. Do not put numbered concepts, alternative configurations, or promises in action:ask. ask is only for factual explanations or missing user information, with no executable alternatives. A recommendation request must not immediately generate.
- Do not use titles/descriptions/messages to promise effects absent from params. The UI displays the actual planned changes. A dry run checks configuration compatibility; final building placement is still checked during generation.
- Features individually available on this tile can conflict with EACH OTHER. Check their categories and overrides together, and preserve existing features. Never propose incompatible pairs such as HotSprings+Pond. Custom terrain does not require adding a native lake feature.
- For an oasis-LIKE landscape when native Oasis is unavailable, preserve the biome and current features. After the user accepts an oasis-like substitute, compose a modest irregular pool with a surrounding localized fertile-soil area, optionally sand. Water alone is not an oasis. Prefer one custom water feature rather than Pond plus a second custom pool. Use separate top-level IDs for the surrounding soil and water, soil first then water, small explicit falloff f:0.01..0.015, and edge_roughness:medium. Do not claim specific plants or change global fertility/vegetation unless requested. Exact circle requests remain exact.
- Oasis-like example geometry: surrounding SoilRich circle r:0.16 and WaterShallow circle r:0.10 at [0.5,0.5], each a composite with edge_roughness:medium, compose fill and e:0.05 (soil) / e:0 (water), f:0.01. Adapt size/location to the request and existing terrain. Use loaded material names only.
";
        public static bool IsRequest(string text)=>!Regex.IsMatch(text??"",@"추천\s*(말고|하지\s*마|필요\s*없)|\b(don't|do not|no)\s+(recommend\w*|suggest\w*|options)\b",RegexOptions.IgnoreCase)
            && Regex.IsMatch(text??"",@"추천|\b(recommend\w*|suggest\w*|ideas|options)\b",RegexOptions.IgnoreCase);
        public static bool IsNumberedOffer(string text)=>Regex.IsMatch(text??"",@"(?m)^\s*(?:\*\*)?(?:[1-9][.)번]|[①②③])\s*");
        public static int Selection(string text)
        {
            var match=Regex.Match((text??"").Trim(),@"^(?:option\s*)?([1-9])\s*(?:번|번째)?\s*(?:으로|을|를)?\s*(?:해\s*줘|해주세요|선택|적용|please)?[.!]?$",RegexOptions.IgnoreCase);
            return match.Success?int.Parse(match.Groups[1].Value):-1;
        }
        public static bool IsAmbiguousAcceptance(string text)=>Regex.IsMatch((text??"").Trim(),@"^(그래|응|네|좋아|yes|ok|okay|go ahead)[.!]?$",RegexOptions.IgnoreCase);
        public static bool RequestsDirectEdit(string text)=>Regex.IsMatch(text??"",@"(?:추천|후보|선택지)\s*(?:말고|무시|취소)|(?:현재|실제)\s*맵에?\s*(?:바로|직접)|\b(?:ignore|cancel|skip)\s+(?:the\s+)?(?:recommendations|options|candidates)\b",RegexOptions.IgnoreCase);
        public static List<SimpleJsonObject> Options(SimpleJsonObject command)
        {
            var options=command.GetObjectArray("options");
            if(options==null || options.Count<1 || options.Count>3 || options.Any(o=>o.GetObject("params")==null))
                throw new FormatException("recommend requires 1..3 options, each with params");
            return options;
        }
        public static List<RecommendationPlan> Validate(SimpleJsonObject command,TileMapState before,Action<MapParamsData> validate,bool korean,Func<string,string,PlanDefinition> lookup=null)
        {
            var plans=new List<RecommendationPlan>();
            var outcomes=new HashSet<string>(StringComparer.Ordinal);
            foreach(var option in Options(command))
            {
                var parameters=option.GetObject("params");
                var data=MapParameterParser.Parse(parameters);
                validate(data);
                var after=MapStateEditor.Merge(before,data);
                if(MapStateCodec.ChangedFields(before,after).Count==0)throw new FormatException("Recommendation has no changes");
                if(!outcomes.Add(MapStateCodec.Serialize(after)))throw new FormatException("Recommendations produce the same settings. Provide distinct alternatives against the current state.");
                var envelope=new SimpleJsonObject();envelope.SetString("action","generate");envelope.SetObject("params",parameters);
                string summary=new MapPlanDescription(korean,lookup).Describe(before,after);
                plans.Add(new RecommendationPlan(SimpleJson.Serialize(envelope),summary));
            }
            return plans; // Nothing is published when any option failed.
        }
    }
}
