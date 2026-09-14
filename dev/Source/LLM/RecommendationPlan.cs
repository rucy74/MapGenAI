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
        private RecommendationPlan(string command,string summary){Command=command;Summary=summary;}

        public const string Rules = @"
Recommendations and selectable alternatives:
- When the user asks for recommendations, or you offer alternative map configurations, return action:recommend with 1..3 options. Each option has params using the SAME patch schema as generate. Every option is a complete independent patch against the CURRENT state, not a sequence.
- Format: {""action"":""recommend"",""options"":[{""params"":{""vegetation_density"":1.3}},{""params"":{""fertility_offset"":0.2}}]}.
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
        public static List<SimpleJsonObject> Options(SimpleJsonObject command)
        {
            var options=command.GetObjectArray("options");
            if(options==null || options.Count<1 || options.Count>3 || options.Any(o=>o.GetObject("params")==null))
                throw new FormatException("recommend requires 1..3 options, each with params");
            return options;
        }
        public static List<RecommendationPlan> Validate(SimpleJsonObject command,TileMapState before,Action<MapParamsData> validate,bool korean)
        {
            var plans=new List<RecommendationPlan>();
            foreach(var option in Options(command))
            {
                var parameters=option.GetObject("params");
                var data=MapParameterParser.Parse(parameters);
                validate(data);
                var after=MapStateEditor.Merge(before,data);
                if(MapStateCodec.ChangedFields(before,after).Count==0)throw new FormatException("Recommendation has no changes");
                var envelope=new SimpleJsonObject();envelope.SetString("action","generate");envelope.SetObject("params",parameters);
                string summary=MapStateDescription.Describe(before,after,korean)
                    .Replace(korean?"설정 변경 내용:":"Settings changed:",korean?"적용할 설정:":"Planned settings:");
                // Shape IDs alone do not tell the user which materials a proposal will place.
                foreach(var shape in after.elevationShapes.Where(s=>!before.elevationShapes.Any(b=>b.id==s.id)))
                {
                    var fills=new List<string>();
                    if(!string.IsNullOrEmpty(shape.fill))fills.Add(shape.fill);
                    if(shape.compositeOps!=null)fills.AddRange(shape.compositeOps.Where(c=>!string.IsNullOrEmpty(c.fill)).Select(c=>c.fill));
                    summary+="\n  "+shape.id+": "+shape.type+(fills.Count==0?"":" · "+string.Join(", ",fills.Distinct()));
                }
                plans.Add(new RecommendationPlan(SimpleJson.Serialize(envelope),summary));
            }
            return plans; // Nothing is published when any option failed.
        }
    }
}
