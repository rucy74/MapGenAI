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
Use the normal parameter/shape_ops/structure_ops/road_ops schema. Do not copy state serialization field names into params.
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
- Check the executable schema for EVERY option: elevation shape direction uses left/right/top/bottom/top_left/top_right/bottom_left/bottom_right or degrees, not north/south/east/west (those are coast_direction values). Buildings/ruins use structure_ops with structure:{kind:...}; never put them in shape_ops or elevation_shapes. Do not insert descriptive prose fields into params or shape data.
- For an open-ended first recommendation (e.g. '추천해줘' / 'Recommend a map'), design visibly different, habitable LANDSCAPES from this tile's biome, hilliness, existing features and world water connections. Start from the selected landscape, not the worked examples elsewhere in this prompt. There is NO default ring/side-ridge/rectangular-canyon menu. Do not simply permute that trio, rotate a template, change IDs or vary resource bonuses. Vary the actual land/water balance, placement and usable settlement space. Native features are optional accents, not unrelated bonus bundles.
- Plan the relationships before choosing shapes: where is the generous living space, which features border it, how does it connect to the map edge, and how does existing water participate? Compose reusable masses, open ground and water along these relationships. A named landform is optional, not the entire menu. Different candidates should differ in at least two meaningful aspects such as mass distribution, the shape/location of usable ground, or its relationship to water. Changing only a variant, rotation, outline noise or feature bonus is insufficient. Tight explicit preferences take priority over this variety goal.
- Use ordinary open landscapes too: a low-hill or flat tile does not require a basin, valley or mountain backdrop in every candidate. Retain native ground and small terrain details, changing only what gives each proposal its composition. Terrain need not fill the whole preview to be noticeable. Never paint broad Soil disks to represent available living space; open ground and its native surface are separate decisions.
- An unqualified recommendation is for an ordinary playable settlement, not an excavation challenge. Keep a broad connected outdoor settlement area and access to the rest of the map. On flat/low-hill tiles, retain predominantly open land with localized additions; do not manufacture a giant mountain enclosure or fill most of the map with rock. On mountainous tiles, work with the mountain setting while retaining a generous valley or settlement pocket. Respect a dry biome instead of automatically adding a lake to every option, and use actual river/shore context without inventing world connections. Do not flatten, repaint or replace the whole original map just to guarantee buildability.
- Treat visual composition as a default responsibility even for a short request. Give the landscape one readable organizing idea, balance any main mass with generous connected settlement space, and use only a few subordinate accents. Compose the common path/area primitives and anchor relationships described in the natural-composition schema; do not reduce every natural request to the three named landform presets. Existing landform geometry remains valid when it fits or is already authored. Favor unequal clusters and open space over evenly spaced repetitions, central resource disks or several competing focal points. Small tile-appropriate changes can be preferable to a whole-map landform. Beauty requests do not authorize bonus bundles, biome changes or erasing existing features.
- Recommend surface-map features suited to the actual terrain. Do not ADD UndergroundCave: it is an underground-map generation worker, not a decorative surface cave, and can exhaust its generation attempts on surface terrain. Preserve existing features; ordinary Caves or a requested cave edit still follow their own terrain requirements. A catalog entry alone does not establish that its effect fits an ordinary settlement recommendation.
- Choose organic layouts unless the user asks for geometric ones: asymmetric outlines, offset features and broad transitions. Use ridge/noise or irregular composites as appropriate. Merely setting edge_roughness on a huge rectangle or concentric circles does not make their overall layout natural. Avoid complete uniform rings and large solid rectangular mountain slabs in generic recommendations. Closed fortresses, thin enclosed corridors, nearly all-rock maps and challenge terrain belong to explicit requests; preserve support for those requests using the existing recipes. For open valleys leave substantial ground on both sides of a route; a 10-cell slit through a giant block is not a general-purpose settlement recommendation. Do not promise GL's unsupported sea islands, fjords or thick-roof caves.
- If the user has already authored terrain/structures, an unqualified recommendation means three complementary additions or small edits to THAT map. Keep all existing shapes, passages, structures, materials, coverage, settings and native features unless the user asks to change them. Prefer additions that do not overlap existing authored areas. Do not silently replace a basin, repaint its interior or add another whole-map mountain formation. Only propose replacement layouts when explicitly requested.
- A scoped request ('recommend soil changes', '온천에 어울리는 특징 추천') stays within that scope; the three-landscape rule does not override it. A request for more/different suggestions should avoid the pending alternatives when present in the conversation. Distinguish choices by meaningful geometry/location or the requested effect, not internal IDs, decorative titles, or tiny numerical variations. No candidate is applied until selected.
- On a fresh map, compose the terrain and a few compatible accents together in each option. A small off-center shallow pond can complement a temperate basin or grassland plain when there is no existing water; a dry option remains valid. Use existing water first, preserve the biome's ground, and leave a broad connected settlement area. Do not repeat a mountain-only shape in all candidates and defer every supporting element to another user request.
- A request for nicer/different recommendations is not permission for a global fertility, vegetation, animal, resource, ruin or hill-density boost. Keep those sliders unchanged unless requested. Preserve native soil too: do not suggest repainting a basin fraction with rich soil unless the user asks for farming/fertility/soil changes. On an authored map propose visibly distinct, localized changes that coexist with the current plan; do not offer the same map with only slider changes or progressively extreme roughness. If the user explicitly wants a completely different layout, recommend replacements with replace_shapes:true; rejection of pending suggestions alone never deletes the applied map.
- Check each option's spatial composition: for a requested pool with a wider soil surround, add the soil FIRST and the water LAST, or subtract a water-sized hole from the soil. Never cover a proposed lake with a later solid soil disk. Leave fill absent on open ground to preserve biome soil. Reuse named areas with anchor/placement for related features instead of guessing coordinates from a feature's center; the entire proposed footprint must fit. For existing maps read actual extents: 'outside' means beyond the source boundary, not inside its mountain wall. Only cut an extra opening when that is part of the suggestion, and keep additions small enough to coexist with the current layout.
- The application validates every option before displaying it and applies the stored command when the user selects it. Do not put numbered concepts, alternative configurations, or promises in action:ask. ask is only for factual explanations or missing user information, with no executable alternatives. A recommendation request must not immediately generate.
- Do not use titles/descriptions/messages to promise effects absent from params. The UI displays the actual planned changes. A dry run checks configuration compatibility; final building placement is still checked during generation.
- Features individually available on this tile can conflict with EACH OTHER. Check their categories and overrides together, and preserve existing features. Never propose incompatible pairs such as HotSprings+Pond. Custom terrain does not require adding a native lake feature.
- For an oasis-LIKE landscape when native Oasis is unavailable, preserve the biome and current features. After the user accepts an oasis-like substitute, compose a modest irregular pool with a surrounding localized fertile-soil area, optionally sand. Water alone is not an oasis. Prefer one custom water feature rather than Pond plus a second custom pool. Use separate top-level IDs for the surrounding soil and water, soil first then water, small explicit falloff f:0.01..0.015, and edge_roughness:medium. Do not claim specific plants or change global fertility/vegetation unless requested. Exact circle requests remain exact.
- An oasis-like substitute should follow existing open ground and fit the requested scale. Use unequal, related outlines rather than mandatory concentric circles at the map center; keep any fertile-soil surround localized and water visible. Use loaded material names only.
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
        public static bool IsDismissal(string text)=>Regex.IsMatch((text??"").Trim(),
            @"^(?:(?:추천|후보|선택지)\s*(?:모두\s*)?취소(?:해\s*줘|해주세요)?|(?:아무것도\s*)?선택\s*안\s*(?:함|할래|할게)|none of these|select none|(?:cancel|discard|skip)\s+(?:all\s+|the\s+)?(?:recommendations|suggestions|options|candidates))[.!]?$",RegexOptions.IgnoreCase);
        public static bool RequestsNewOptions(string text)=>!IsDismissal(text) && !RequestsDirectEdit(text) &&
            !Regex.IsMatch(text??"",@"[1-9]\s*번|\b(?:option|candidate)\s*[1-9]\b",RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text??"",@"다시\s*추천|(?:새로운?|다른)\s*(?:추천|선택지|후보)|다른\s*(?:걸|거|것).{0,12}추천|\b(?:new|different|more|other)\s+(?:recommendations|suggestions|options|ideas)\b|\b(?:recommend|suggest)\b.{0,30}\bagain\b",RegexOptions.IgnoreCase);
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

    // Fresh design briefs are sampled only when requesting NEW candidates. This does not alter
    // map generation's RNG or stored geometry. The caller retains the same string for retries.
    public static class RecommendationVariation
    {
        public sealed class Direction
        {
            public readonly string Mass, Space, Relation, Sector;
            public readonly int Variant, CenterXPercent, CenterZPercent, OccupiedPercent, OpenPercent;
            internal Direction(string mass,string space,string relation,string sector,int x,int z,int occupied,int open,int variant)
            { Mass=mass;Space=space;Relation=relation;Sector=sector;CenterXPercent=x;CenterZPercent=z;OccupiedPercent=occupied;OpenPercent=open;Variant=variant; }
        }
        private static readonly string[] Masses={
            "one curved OPEN boundary with unequal ends; do not close it into a ring",
            "TWO separated unequal clusters, one roughly twice the other's area, with broad usable ground between them",
            "THREE small unequal groups with generous gaps; do not merge them into a single side wall",
            "one branching mass with TWO unequal arms and broad open ground on both sides; no enclosure"};
        private static readonly string[] Spaces={
            "one broad continuous living area with irregular edges",
            "an off-center broad living area opening toward the rest of the map",
            "a broad gently bending living area, never a narrow slit",
            "unequal usable clearings connected by generous open ground"};
        private static readonly string[] Relations={
            "place supporting scenery along a shared edge of the living space",
            "put the visual focus near a meeting of features while keeping the center usable",
            "let large and small features taper into a quieter open area",
            "use an extended uneven boundary as the organizing feature, with a quiet opposite side"};
        private static readonly string[] Sectors={"north","northeast","east","southeast","south","southwest","west","northwest"};
        private static readonly int[] SectorX={50,73,79,73,50,27,21,27}, SectorZ={79,73,50,27,21,27,50,73};

        // Own tiny deterministic stream: reproducible tests and no effect on RimWorld/Unity RNG.
        private sealed class Stream
        {
            private uint state;
            public Stream(int seed) { state=unchecked((uint)seed)^0x9e3779b9u;if(state==0)state=1; }
            public int Next(int count) { state^=state<<13;state^=state>>17;state^=state<<5;return (int)(state%(uint)count); }
            public T[] Shuffle<T>(T[] source)
            {
                var result=(T[])source.Clone();
                for(int i=result.Length-1;i>0;i--){int j=Next(i+1);var item=result[i];result[i]=result[j];result[j]=item;}
                return result;
            }
        }
        public static IReadOnlyList<Direction> Directions(int seed)
        {
            var random=new Stream(seed);
            var masses=random.Shuffle(Masses);var spaces=random.Shuffle(Spaces);var relations=random.Shuffle(Relations);
            var sectors=random.Shuffle(new[]{0,1,2,3,4,5,6,7});
            var occupied=random.Shuffle(new[]{10,16,23});var open=random.Shuffle(new[]{45,55,65});
            var result=new List<Direction>();var variants=new HashSet<int>();
            for(int i=0;i<3;i++)
            {
                int variant;do{variant=random.Next(1000000);}while(!variants.Add(variant));
                int sector=sectors[i];
                result.Add(new Direction(masses[i],spaces[i],relations[i],Sectors[sector],SectorX[sector]+random.Next(9)-4,SectorZ[sector]+random.Next(9)-4,occupied[i],open[i],variant));
            }
            return result;
        }
        public static string NextInstruction() => Build(Guid.NewGuid().GetHashCode());
        public static string Build(int seed)
        {
            var text=new System.Text.StringBuilder(@"
FRESH RECOMMENDATION DESIGN BRIEF (not additional user requirements):
The following combine independently sampled geometry targets, NOT predefined terrain recipes.
User preferences, current tile prerequisites, existing water and authored terrain take priority over every axis.
On a fresh unconstrained layout, express these targets in actual executable geometry. Do not substitute a familiar default trio or satisfy the targets with new IDs/variants alone.
Honor explicit directions, no-mountain/no-water preferences and native shore/river boundaries FIRST; adapt only the conflicting targets.
Sector coordinates locate the main proposed visual mass, not the settlement or a mandatory mountain. Cluster count and open connections describe topology, not just edge noise.
Occupancy is an approximate share of the WHOLE MAP for new dominant land/water masses combined. On flat tiles use sparse additions; on mountain tiles work around native mountains.
The open-ground target is an aim for connected usable land, not permission to erase native terrain or paint a flat soil disk. Native ground can already satisfy it without any floor shape.
For an existing authored map or a scoped request, apply the axes only to permitted local additions/edits.
Do not create extra mountains/water, replace existing layouts, repaint ground or add bonuses merely to satisfy an axis.
Use only supported executable fields. A seed below is a variant hint for NEW naturally seeded shapes only;
preserve all existing variants on edits and never insert these prose axes as schema fields.
Keep the actual map seed fixed. Repeated recommendations should explore different compositions, not just new edge noise.
");
            var directions=Directions(seed);
            for(int i=0;i<directions.Count;i++)
            {
                var direction=directions[i];
                string x=(direction.CenterXPercent/100.0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
                string z=(direction.CenterZPercent/100.0).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
                text.AppendLine("Option "+(i+1)+" geometry targets: main visual mass in the "+direction.Sector+" around ["+x+","+z+"]; "+direction.Mass+". New dominant masses occupy about "+direction.OccupiedPercent+"% of the map; aim for at least "+direction.OpenPercent+"% connected usable ground where the tile allows it. Space: "+direction.Space+". Relationship: "+direction.Relation+". New-shape variant hint: "+direction.Variant+".");
            }
            text.AppendLine("Honor the requested option count; these directions are inspiration, not a requirement to return all three. Keep this brief internal and show executable candidates through action:recommend.");
            return text.ToString();
        }
    }
}
