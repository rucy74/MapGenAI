using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace MapGenAI.MapGen
{
    // Resolve against loaded definitions, so DLC and mod materials use the same path.
    public static class TerrainMaterials
    {
        static readonly Dictionary<string,string> Aliases = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"water","WaterDeep"}, {"sand","Sand"}, {"soil","Soil"}, {"rich_soil","SoilRich"},
            {"marsh","MarshyTerrain"}, {"mud","Mud"}, {"ice","Ice"}, {"lava","LavaDeep"},
            {"cooled_lava","CooledLava"}, {"gravel","Gravel"}, {"volcanic_rock","VolcanicRock"}
        };
        public static string DefName(string fill, bool deep = true) => string.Equals(fill,"water",StringComparison.OrdinalIgnoreCase) && !deep ? "WaterShallow" :
            Aliases.TryGetValue(fill, out var name) ? name : fill;

        // Query explicit fills on validated shapes using the composite render-queue rules.
        // This describes planned paint, not whether later geometry/placement leaves a visible cell.
        public static bool HasRenderedFill(ElevationShape shape, Func<string,bool> predicate)
        {
            if(shape==null)return false;
            if(shape.type!="composite")
            {
                if(shape.type=="region_fill" && (!float.TryParse(shape.coverage,NumberStyles.Float,CultureInfo.InvariantCulture,out var coverage) || coverage<=0))return false;
                return !string.IsNullOrEmpty(shape.fill) && predicate(shape.fill);
            }
            if(shape.compositeShapes==null || shape.compositeShapes.Count==0 || shape.compositeOps==null)return false;
            for(int i=0;i<shape.compositeOps.Count;i++)
            {
                var op=shape.compositeOps[i];
                // union/sub/inter can paint directly, while an out-only intermediate cannot.
                bool renders=op.e!=0 || !string.IsNullOrEmpty(op.fill) ||
                    (i==shape.compositeOps.Count-1 && !string.IsNullOrEmpty(shape.fill));
                string fill=shape.fill ?? op.fill;
                if(renders && !string.IsNullOrEmpty(fill) && predicate(fill))return true;
            }
            return false;
        }
        public static bool Supported(TerrainDef def) => def != null && !def.temporary && !def.bridge && !def.isFoundation
            && def.defName != "Underwall"
            && !def.dontRender && !def.exposesToVacuum && !def.IsRiver && !def.HasTag("Road")
            && def.defName.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) < 0
            && def.designationCategory == null && (def.costList == null || def.costList.Count == 0) && def.costStuffCount == 0;
        public static TerrainDef Resolve(string fill, bool deep = true)
        {
            var def = DefDatabase<TerrainDef>.GetNamedSilentFail(DefName(fill, deep));
            if (!Supported(def)) throw new FormatException("사용할 수 없는 채움 재료 / Unavailable fill material: " + fill +
                ". 활성 재료 목록을 사용하세요. 임시 용암·바다·강·건축 바닥은 별도 기능입니다. / Use the active material catalog; temporary lava, ocean/river and constructed floors need separate generators.");
            return def;
        }
        public static void Validate(TileMapState state)
        {
            foreach (var shape in state.elevationShapes)
            {
                if (!string.IsNullOrEmpty(shape.fill))
                {
                    var material=Resolve(shape.fill);
                    if(shape.type=="passage" && (material.IsWater || material.dangerous))throw new FormatException("통로에는 마른 안전한 바닥 재료가 필요합니다. / Passage requires dry, safe ground.");
                }
                if (shape.compositeOps != null)
                    foreach (var op in shape.compositeOps)
                        if (!string.IsNullOrEmpty(op.fill)) Resolve(op.fill);
            }
        }
        public static string Catalog() => string.Join(", ", DefDatabase<TerrainDef>.AllDefsListForReading.Where(Supported)
            .OrderBy(d => d.defName).Select(d => d.defName + " (" + d.label + ")"));
    }
}
