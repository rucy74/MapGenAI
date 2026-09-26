using System;
using System.Linq;
using RimWorld;
using Verse;

namespace MapGenAI.MapGen
{
    public static class LandscapeBlendGeneration
    {
        public static void Apply(Map map)
        {
            var state=GenerationContext.State;
            var shapes=state?.elevationShapes.Where(s=>(s.type=="landform" || s.type=="composite") && s.details=="natural").ToList();
            if(shapes==null || shapes.Count==0)return;
            var regions=GenerationContext.Regions(map);int cols=map.Size.x,rows=map.Size.z,count=cols*rows;
            var owners=new LandscapeBlendField[count];var water=new bool[count];var rock=new bool[count];var locked=new bool[count];
            foreach(var shape in shapes)
            {
                var mask=regions.Mask(shape.id);var field=new LandscapeBlendField(shape.variant);
                for(int i=0;i<count;i++)if(mask[i])owners[i]=field;
            }
            // Explicit authored material/geometry and the whole counted-fill area take priority.
            // Do not change the eligible area or its unpainted remainder behind a 70% request.
            foreach(var shape in state.elevationShapes)
            {
                if(shapes.Contains(shape))continue;
                var mask=shape.type=="region_fill"?regions.Mask(shape.region,shape.region_part):regions.Mask(shape.id);
                for(int i=0;i<count;i++)locked[i]|=mask[i];
            }
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*cols+cell.x;var terrain=map.terrainGrid.TerrainAt(cell);
                water[i]=terrain.IsWater && !terrain.dangerous;
                rock[i]=cell.GetEdifice(map)?.def.building.isNaturalRock==true || MapGenerator.Elevation[cell]>=.7f && MapGenerator.Caves[cell]<=0;
                locked[i]|=regions.Materials[i]!=null || regions.LocalRoadCells[i] || terrain.HasTag("Road") || map.terrainGrid.FoundationAt(cell)!=null ||
                    !LandscapeBlendField.OrdinaryGround(terrain.defName) || rock[i] ||
                    map.roofGrid.RoofAt(cell)!=null || cell.GetThingList(map).Any(t=>t is Building || t is Pawn || t.def.category==ThingCategory.Item);
            }
            var waterDistance=new SpatialDistance(cols,rows,water);var rockDistance=new SpatialDistance(cols,rows,rock);
            var plants=Enumerable.Repeat(1f,count).ToArray();regions.VegetationWeights=plants;
            using(map.pathing.DisableIncrementalScope())
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*cols+cell.x;if(owners[i]==null || locked[i])continue;
                var biome=map.BiomeAt(cell);var before=map.terrainGrid.TerrainAt(cell);
                bool soil=biome.terrainsByFertility.Any(t=>t.terrain==TerrainDefOf.Soil);
                var sample=owners[i].Sample(cell.x,cell.z,(float)Math.Sqrt(waterDistance.squared[i]),(float)Math.Sqrt(rockDistance.squared[i]),soil);
                TerrainDef after=sample.ground==LandscapeBlendField.Ground.Sand?TerrainDefOf.Sand:
                    sample.ground==LandscapeBlendField.Ground.Gravel?TerrainDefOf.Gravel:
                    sample.ground==LandscapeBlendField.Ground.Soil && before!=TerrainDefOf.Sand?TerrainDefOf.Soil:before;
                if(after!=before){map.terrainGrid.SetTerrain(cell,after);if(AuthoringGeneration.Current!=null)AuthoringGeneration.Current.surfaceBlendCells++;}
                // Deserts and special native density rules keep their own distribution.
                if(biome.wildPlantsCareAboutLocalFertility)
                {plants[i]=sample.vegetation;if(AuthoringGeneration.Current!=null)AuthoringGeneration.Current.vegetationPlanCells++;}
            }
        }
    }
}
