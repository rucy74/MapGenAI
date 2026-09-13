using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;
using static CoreRegressionTests;

static class WorldStateTests
{
    static void Apply(int tile, string json) => MapGenParams.ApplyPatch(MapParameterParser.Parse(SimpleJson.Parse(json)),tile);
    static void Setup()
    {
        MapGenParams.Reset(); Find.World=new World(); Find.WorldGrid=new WorldGrid();
        Find.WorldGrid.Tiles[1]=new SurfaceTile(); Find.WorldGrid.Tiles[2]=new SurfaceTile();
        DefDatabase<TileMutatorDef>.Definitions.Clear();
        foreach(string name in new[]{"A","B","Lake","Island","Caves"}) DefDatabase<TileMutatorDef>.Definitions[name]=new TileMutatorDef {defName=name};
        DefDatabase<TileMutatorDef>.Definitions["Lake"].categories.Add("Lake");
        DefDatabase<TileMutatorDef>.Definitions["Island"].categories.Add("Lake");
    }
    public static void RunAll()
    {
        Check("Editing tile B and clearing cache do not revert tile A", () =>
        {
            Setup(); Apply(1,"{\"mutators\":[\"A\"]}"); Apply(2,"{\"mutators\":[\"B\"]}"); MapGenParams.Reset();
            Equal("A",string.Join(",",Find.WorldGrid[1].Mutators.Select(d=>d.defName)));
            Equal("B",string.Join(",",Find.WorldGrid[2].Mutators.Select(d=>d.defName)));
            MapGenParams.ClearTile(1);
            Equal(0,Find.WorldGrid[1].Mutators.Count); Equal(1,Find.WorldGrid[2].Mutators.Count);
            Equal(false,MapGenAIWorldComponent.Get().HasState(1)); Equal(true,MapGenAIWorldComponent.Get().HasState(2));
        });
        Check("Rejected category conflict preserves state and actual features", () =>
        {
            Setup(); Apply(1,"{\"mutators\":[\"Lake\"]}");
            var before=MapStateCodec.Serialize(MapGenParams.CaptureState(1));
            Throws(()=>Apply(1,"{\"mutators\":[\"Island\"]}"));
            Equal(before,MapStateCodec.Serialize(MapGenParams.CaptureState(1)));
            Equal("Lake",Find.WorldGrid[1].Mutators.Single().defName);
            Apply(1,"{\"mutators\":[\"Island\"],\"remove_mutators\":[\"Lake\"]}");
            Equal("Island",Find.WorldGrid[1].Mutators.Single().defName);
        });
        Check("World add failure restores metadata and leaves no successful state", () =>
        {
            Setup(); var tile=Find.WorldGrid[1]; tile.AddMutator(DefDatabase<TileMutatorDef>.Definitions["Lake"]);
            bool failed=false; tile.BeforeAdd=def=>{if(def.defName=="Island") throw new InvalidOperationException("injected AddMutator failure");};
            try {Apply(1,"{\"mutators\":[\"Island\"]}");} catch(InvalidOperationException) {failed=true;}
            Equal(true,failed); Equal("Lake",tile.Mutators.Single().defName);
            Equal(false,MapGenAIWorldComponent.Get().HasState(1)); Equal(true,MapGenAIWorldComponent.Get().GetBaseline(1)==null);
        });
        Check("Original tile features survive edit remove undo and clear", () =>
        {
            Setup(); Find.WorldGrid[1].AddMutator(DefDatabase<TileMutatorDef>.Definitions["A"]);
            Apply(1,"{\"remove_mutators\":[\"A\"],\"mutators\":[\"B\"]}"); var old=MapGenParams.CaptureState(1);
            Apply(1,"{\"mutators\":[\"A\"]}"); Equal(2,Find.WorldGrid[1].Mutators.Count);
            MapGenParams.RestoreSnapshot(old,1); Equal("B",Find.WorldGrid[1].Mutators.Single().defName);
            MapGenParams.ClearTile(1); Equal("A",Find.WorldGrid[1].Mutators.Single().defName);
        });
        Check("Invalid tile cannot report an applied state", () =>
        {
            Setup(); Throws(()=>Apply(99,"{\"animal_density\":1.5}")); Equal(false,MapGenAIWorldComponent.Get().HasState(99));
        });
        Check("External feature additions and hilliness changes survive edits and clear", () =>
        {
            Setup(); Apply(1,"{\"animal_density\":1.5}");
            Find.WorldGrid[1].AddMutator(DefDatabase<TileMutatorDef>.Definitions["B"]);
            Find.WorldGrid[1].hilliness=RimWorld.Hilliness.Mountainous;
            Apply(1,"{\"ore_density\":1.2}");
            MapGenParams.ClearTile(1);
            Equal("B",Find.WorldGrid[1].Mutators.Single().defName);
            Equal(RimWorld.Hilliness.Mountainous,Find.WorldGrid[1].hilliness);
        });
        Check("Disabled baseline definitions do not lock tile editing", () =>
        {
            Setup(); Find.WorldGrid[1].AddMutator(DefDatabase<TileMutatorDef>.Definitions["A"]);
            Apply(1,"{\"animal_density\":1.5}");
            Find.WorldGrid[1].Mutators.Clear(); DefDatabase<TileMutatorDef>.Definitions.Remove("A");
            Apply(1,"{\"ore_density\":1.2}"); MapGenParams.ClearTile(1);
            Equal(false,MapGenAIWorldComponent.Get().HasState(1));
        });
        Check("Generation uses its own tile snapshot despite UI selection changes", () =>
        {
            Setup(); Apply(1,"{\"animal_density\":1.5}"); Apply(2,"{\"animal_density\":0.5}");
            var frozen=MapGenParams.CaptureState(1);
            using(GenerationContext.Enter(1,frozen))
            {
                Equal(1.5f,MapGenParams.AnimalDensity); Equal(1,MapGenParams.CurrentTileId);
                MapGenParams.Reset(); frozen.animalDensity=2;
                Equal(1.5f,MapGenParams.AnimalDensity);
                using(GenerationContext.Enter(99,null)) Equal(false,MapGenParams.HasParams);
                Equal(1.5f,MapGenParams.AnimalDensity);
            }
            Equal(false,GenerationContext.Active); Equal(false,MapGenParams.HasParams);
            MapGenParams.LoadFromTile(2); Equal(.5f,MapGenParams.AnimalDensity);
        });
        MapGenParams.Reset(); Find.World=null; Find.WorldGrid=null; Find.WorldSelector=null;
    }
}
