using System;
using System.Collections.Generic;
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
        public static string DefName(string fill, bool deep = true) => fill == "water" && !deep ? "WaterShallow" :
            Aliases.TryGetValue(fill, out var name) ? name : fill;
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
                if (!string.IsNullOrEmpty(shape.fill)) Resolve(shape.fill);
                if (shape.compositeOps != null)
                    foreach (var op in shape.compositeOps)
                        if (!string.IsNullOrEmpty(op.fill)) Resolve(op.fill);
            }
        }
        public static string Catalog() => string.Join(", ", DefDatabase<TerrainDef>.AllDefsListForReading.Where(Supported)
            .OrderBy(d => d.defName).Select(d => d.defName + " (" + d.label + ")"));
    }
}
