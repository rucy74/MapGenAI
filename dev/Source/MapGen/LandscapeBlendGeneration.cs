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
            var owners=new LandscapeBlendField[count];var water=new bool[count];var rock=new bool[count];var locked=new bool[count];var explicitAreas=new bool[count];
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
                for(int i=0;i<count;i++){explicitAreas[i]|=mask[i];locked[i]|=mask[i];}
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
            // Only water actually intersecting an opted-in area may extend its effect outside.
            // Never follow the whole connected river: a small authored area must remain a local edit.
            var shoreSources=new bool[count];var freshwater=new bool[count];var nativeSources=new bool[count];
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*cols+cell.x;
                var terrain=map.terrainGrid.TerrainAt(cell);
                freshwater[i]=water[i] && terrain.waterBodyType==WaterBodyType.Freshwater && LandscapeBlendField.OrdinaryFreshwater(terrain.defName);
                nativeSources[i]=regions.NativeWater[i]!=0 && water[i] && terrain.waterBodyType==WaterBodyType.Freshwater && !terrain.IsRiver;
                shoreSources[i]=owners[i]!=null && !explicitAreas[i] && freshwater[i] && regions.NativeWater[i]==0;
            }
            var shoreWater=new ShorelineWater(cols,rows,shoreSources,freshwater,explicitAreas);
            var shoreDistance=shoreWater.OriginDistance;
            var nativeDistance=new SpatialDistance(cols,rows,nativeSources);
            var waterDistance=new SpatialDistance(cols,rows,water);var rockDistance=new SpatialDistance(cols,rows,rock);
            var plants=Enumerable.Repeat(1f,count).ToArray();regions.VegetationWeights=plants;
            using(map.pathing.DisableIncrementalScope())
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*cols+cell.x;if(locked[i])continue;
                bool bank=shoreDistance.HasTarget && shoreDistance.squared[i]<=36;
                bool nativeBank=regions.NativeShore[i] && nativeDistance.HasTarget && nativeDistance.squared[i]<=36;
                if(owners[i]==null && !bank && !nativeBank)continue;
                var biome=map.BiomeAt(cell);var before=map.terrainGrid.TerrainAt(cell);
                bool soil=biome.terrainsByFertility.Any(t=>t.terrain==TerrainDefOf.Soil);
                var sample=owners[i]!=null?owners[i].Sample(cell.x,cell.z,(float)Math.Sqrt(waterDistance.squared[i]),(float)Math.Sqrt(rockDistance.squared[i]),soil):
                    new LandscapeBlendField.Cell{ground=LandscapeBlendField.Ground.Keep,vegetation=1f};
                if(bank)
                {
                    int source=shoreDistance.nearestZ[i]*cols+shoreDistance.nearestX[i];
                    bool nativeMud=NativeGround(biome,TerrainDefOf.Mud);
                    bool damp=LandscapeBlendField.DampSoilBank(soil && nativeMud,biome.wildPlantsCareAboutLocalFertility,map.TileInfo.temperature,map.TileInfo.rainfall,before.defName);
                    // The authored mask bounds permission, not the water's final feathered edge.
                    // Follow only locally connected water, not a separate nearby river or pool.
                    float rockGap=(float)Math.Sqrt(rockDistance.squared[i]);
                    var shore=owners[source].SampleShore(cell.x,cell.z,(float)Math.Sqrt(shoreWater.WaterDistance.squared[i]),rockGap,damp,shoreWater.Influence(i));
                    sample.ground=LandscapeBlendField.BankMaterial(shore.ground,before.defName,NativeGround(biome,TerrainDefOf.Sand),NativeGround(biome,TerrainDefOf.Gravel),rockGap);
                    if(owners[i]==null)sample.vegetation=shore.vegetation;
                }
                TerrainDef after=sample.ground==LandscapeBlendField.Ground.Sand?TerrainDefOf.Sand:
                    sample.ground==LandscapeBlendField.Ground.Gravel?TerrainDefOf.Gravel:
                    sample.ground==LandscapeBlendField.Ground.Mud?TerrainDefOf.Mud:before;
                if(nativeBank && MapGenUtility.ShouldGenerateBeachSand(cell,map))
                {
                    // This is the same field as the submerged shelves. Honor dedicated
                    // biome/mutator lakeshores (which need not occur in fertility patches).
                    // Do not re-run TerrainFrom here: its patch makers were cleaned at 210.
                    var shore=MapGenUtility.LakeshoreTerrainAt(cell,map);
                    after=TerrainMaterials.Supported(shore) && !shore.IsWater && !shore.dangerous?shore:before;
                    sample.vegetation=1f; // let the new terrain and native plant rules decide
                }
                // A Soil sample retains the current native Soil/Sand; details do not create farmland.
                if(after!=before){map.terrainGrid.SetTerrain(cell,after);if(AuthoringGeneration.Current!=null)AuthoringGeneration.Current.surfaceBlendCells++;}
                // Deserts and special native density rules keep their own distribution.
                if(biome.wildPlantsCareAboutLocalFertility)
                {plants[i]=sample.vegetation;if(AuthoringGeneration.Current!=null)AuthoringGeneration.Current.vegetationPlanCells++;}
            }
        }
        static bool NativeGround(BiomeDef biome,TerrainDef terrain)=>biome.terrainsByFertility.Any(t=>t.terrain==terrain) ||
            biome.terrainPatchMakers?.Any(p=>p?.thresholds?.Any(t=>t?.terrain==terrain)==true)==true;
    }
}
