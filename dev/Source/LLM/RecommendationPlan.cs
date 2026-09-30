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
        readonly float nativeRiverDirection;
        private RecommendationPlan(string command,string summary,float nativeRiverDirection=-1f):this(new[]{command},summary,nativeRiverDirection){}
        private RecommendationPlan(IReadOnlyList<string> commands,string summary,float nativeRiverDirection=-1f){Commands=commands;Command=commands[0];Summary=summary;this.nativeRiverDirection=nativeRiverDirection;}
        public List<MapParamsData> Edits()=>Commands.Select(c=>MapParameterParser.Parse(ProviderResponse.Command(c).GetObject("params"))).ToList();
        public TileMapState Resolve(TileMapState before)
        {
            var state=before;
            foreach(var data in Edits())state=MapStateEditor.Merge(state,data,nativeRiverDirection);
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
            var pending=new RecommendationPlan(commands,"",old.nativeRiverDirection);
            validate(pending.Edits());
            var after=pending.Resolve(before);
            if(MapStateCodec.ChangedFields(old.Resolve(before),after).Count==0)throw new FormatException("Candidate revision has no changes");
            for(int i=0;i<plans.Count;i++)if(i!=number-1 && MapStateCodec.Serialize(plans[i].Resolve(before))==MapStateCodec.Serialize(after))
                throw new FormatException("Revised candidate duplicates another option");
            return new RecommendationPlan(commands,new MapPlanDescription(korean,lookup).Describe(before,after),old.nativeRiverDirection);
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
Recommendations:
- Recommendations or alternative configurations use action:recommend, normally THREE distinct options; honor one/two or fewer valid choices. Format: {""action"":""recommend"",""options"":[{""params"":{...}},{""params"":{...}},{""params"":{...}}]}. Each is an independent COMPLETE PATCH against CURRENT state using generate's schema, not a sequence. No application until selection.
- Validate every option: shape directions left/right/top/bottom/top_left/top_right/bottom_left/bottom_right/degrees (not compass words); coast_direction uses north/east/south/west. Structures belong in structure_ops, never shapes. No prose fields in params/shapes. UI shows actual changes; no titles/messages promising unencoded effects. Final building placement is checked during generation.
- Fresh unqualified recommendations: distinct, habitable landscapes suited to this tile's biome/hilliness/features/world water. Plan connected living space, bordering masses, edge access and existing water FIRST. Vary at least TWO meaningful aspects (mass distribution, usable ground shape/location, water relationship), not IDs/rotation/noise/bonuses. Tight user preferences outrank variety. No fixed ring/side-ridge/rectangle trio or permutations of examples.
- Short requests still deserve composition: one readable idea, generous connected settlement space, few subordinate accents, unequal clusters/open areas rather than repeated spots or resource disks. Use reusable path/area primitives and anchor relationships; named landforms are optional, not the whole menu. Keep valid existing landforms.
- Flat/low-hill tiles should remain predominantly open with localized changes; ordinary open landscapes need no mandatory mountain backdrop/basin. Mountain tiles need generous valleys/pockets. Use existing water first, respect dry biomes, never invent world connections. Avoid global flattening/repainting/replacement just to make building easy; unpainted open ground retains native soil. Small changes can be enough.
- Default organic composition: asymmetry, offset features, broad transitions. Noise on a giant rectangle/concentric circles is insufficient. Avoid uniform closed rings, rock slabs, nearly all-rock maps or a thin slit through a giant block for ordinary settlements. Fortresses/geometric/challenge terrain remain supported when explicitly requested. Do not promise unsupported GL sea islands/fjords/thick-roof caves.
- Fresh options may combine terrain with a FEW compatible accents. If water is absent, a modest off-center shallow pond can suit a temperate basin/grassland; dry options remain valid. Do not repeat mountains alone and defer all supporting details.
- Existing authored map: unqualified recommendations mean complementary LOCAL additions/small edits. Preserve shapes/passages/structures/materials/coverage/settings/native features; prefer nonoverlap. No silent basin replacement, interior repaint or additional whole-map mountains. Rejection of suggestions does NOT authorize deleting the applied map. Only an explicit request for a completely different layout permits replace_shapes:true.
- Scoped requests stay scoped (soil, spring-compatible features, etc.). More/different suggestions avoid pending choices and differ meaningfully in geometry/location/requested effect, not tiny numbers or decorative names. Nicer/different does NOT authorize global fertility/vegetation/animal/resource/ruin/hill-density boosts, biome changes or rich-soil patches unless asked. Localized alternatives must coexist with the current plan.
- Spatial check: use anchor/placement and actual extents, not guessed centers; whole footprint must fit. 'Outside' is beyond the source boundary, not in its wall. Extra openings require that suggestion's intent. For a pool with soil surround, soil FIRST/water LAST or subtract a water-sized hole from soil. Never cover water with a later solid soil disk; leave fill absent on ordinary open ground.
- Native features are optional coherent accents. Catalog availability alone does not make a surface recommendation suitable: do not ADD UndergroundCave (underground worker can exhaust surface generation attempts). Preserve existing features; ordinary Caves/explicit cave edits follow requirements. Check pairwise categories/overrides too: HotSprings+Pond conflict. Custom water requires no native lake feature.
- ask is ONLY factual explanation/missing information, not numbered concepts/executable alternatives/promises. Recommendation requests must not immediately generate.
- If Oasis is unavailable, explain; only after the user accepts an oasis-LIKE substitute, preserve biome/features and compose a modest irregular pool with localized fertile surround/optional sand. Water alone is insufficient. Prefer one custom pool over Pond plus another pool. Separate soil/water IDs, soil first, small f:0.01..0.015, edge_roughness:medium. Follow existing open ground and scale with unequal related outlines, not mandatory central concentric circles. Loaded materials only; no specific-plant promises/global fertility/vegetation changes unless asked. Exact circle requests remain exact.
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
            => Validate(command,before,validate,korean,lookup,-1f);
        public static List<RecommendationPlan> Validate(SimpleJsonObject command,TileMapState before,Action<MapParamsData> validate,bool korean,Func<string,string,PlanDefinition> lookup,float nativeRiverDirection)
        {
            var plans=new List<RecommendationPlan>();
            var outcomes=new HashSet<string>(StringComparer.Ordinal);
            foreach(var option in Options(command))
            {
                var parameters=option.GetObject("params");
                var data=MapParameterParser.Parse(parameters);
                validate(data);
                var after=MapStateEditor.Merge(before,data,nativeRiverDirection);
                if(MapStateCodec.ChangedFields(before,after).Count==0)throw new FormatException("Recommendation has no changes");
                if(!outcomes.Add(MapStateCodec.Serialize(after)))throw new FormatException("Recommendations produce the same settings. Provide distinct alternatives against the current state.");
                var envelope=new SimpleJsonObject();envelope.SetString("action","generate");envelope.SetObject("params",parameters);
                string summary=new MapPlanDescription(korean,lookup).Describe(before,after);
                plans.Add(new RecommendationPlan(SimpleJson.Serialize(envelope),summary,nativeRiverDirection));
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
