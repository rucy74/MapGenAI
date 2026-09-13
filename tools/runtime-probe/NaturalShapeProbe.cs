using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapGenAI.ImageInput;
using MapGenAI.MapGen;
using MapGenAI.TestFixtures;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    static class NaturalShapeProbe
    {
        public static void Generate(string output)
        {
            var nearby=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(Find.CurrentMap.Tile,nearby);
            var target=nearby.First(t=>Find.WorldGrid[t].PrimaryBiome?.canBuildBase==true&&!Find.WorldGrid[t].WaterCovered&&!Find.WorldObjects.AnyMapParentAt(t));
            var tile=Find.WorldGrid[target];tile.hilliness=Hilliness.Flat;
            foreach(var mutator in tile.Mutators.ToList())tile.RemoveMutator(mutator);
            var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            parent.Tile=target;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
            var state=new TileMapState{imageMap=new ImageMapData{width=1,height=1,cells="G",replaceElevation=true},vegetationDensity=0,animalDensity=0,ruinDensity=0,dangerDensity=0};
            var kinds=new[]{"circle","star","heart","donut"};
            for(int row=0;row<4;row++)for(int col=0;col<2;col++)
            {
                var shape=NaturalShapeFixtures.Make(kinds[row],col==0?"none":"medium",col==0?.25f:.75f,.875f-row*.25f,.095f);
                shape.id+="_"+col;state.elevationShapes.Add(shape);
            }
            MapStateValidation.Validate(state);
            File.WriteAllText(Path.Combine(output,"natural-fixture-state.json"),MapStateCodec.Serialize(state));
            MapGenParams.RestoreSnapshot(state,target);
            var source=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
            var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(source,null);
            generator.genSteps=new List<GenStepDef>(source.genSteps);
            generator.genSteps.Add(new GenStepDef{defName="NaturalShapeProbeCapture",order=99999,genStep=new RealImageProbe.CaptureStep{output=output,id="natural-shapes"}});
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var map=MapGenerator.GenerateMap(new IntVec3(512,1,512),parent,generator);
            var water=new char[512*512];int count=0;
            foreach(var cell in map.AllCells){bool wet=map.terrainGrid.TerrainAt(cell).IsWater;water[cell.z*512+cell.x]=wet?'W':'G';if(wet)count++;}
            if(count<5000 || count>120000)throw new Exception("Native natural fixture water area outside broad bounds: "+count);
            for(int col=0;col<2;col++)
                if(map.terrainGrid.TerrainAt(new IntVec3(col==0?128:384,0,64)).IsWater)throw new Exception("Native donut island was filled");
            File.WriteAllText(Path.Combine(output,"natural-actual-terrain.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"width",512},{"height",512},{"cells",new string(water)}}));
            File.WriteAllText(Path.Combine(output,"natural-observations.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"waterCells",count},{"generationSeconds",watch.Elapsed.TotalSeconds},{"columns",new[]{"precise","natural medium"}},{"rows",new[]{"circle","star","heart","donut"}},{"scope","Actual full RimWorld generation; fixed test soil base; native Map Preview texture. Not manual UI interaction."}}));
        }
    }
}
