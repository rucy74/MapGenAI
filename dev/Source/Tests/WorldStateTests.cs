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
        foreach(string name in new[]{"A","B","Lake","Island","Caves","River","RiverDelta"}) DefDatabase<TileMutatorDef>.Definitions[name]=new TileMutatorDef {defName=name};
        DefDatabase<TileMutatorDef>.Definitions["Lake"].categories.Add("Lake");
        DefDatabase<TileMutatorDef>.Definitions["Island"].categories.Add("Lake");
        DefDatabase<TileMutatorDef>.Definitions["River"].categories.Add("River");
        DefDatabase<TileMutatorDef>.Definitions["RiverDelta"].categories.Add("River");
    }
    public static void RunAll()
    {
        Check("World river deletion rejects the whole request and keeps map edits unchanged",()=>
        {
            Setup();var tile=(SurfaceTile)Find.WorldGrid[1];tile.Rivers.Add(new SurfaceTile.RiverLink{neighbor=2});tile.AddMutator(DefDatabase<TileMutatorDef>.Definitions["RiverDelta"]);
            var before=MapStateCodec.Serialize(MapGenParams.CaptureState(1));
            foreach(string json in new[]{"{\"river\":{\"present\":false},\"animal_density\":1.2}","{\"remove_categories\":[\"River\"]}","{\"remove_mutators\":[\"River\"]}"}) Throws(()=>Apply(1,json));
            Equal(before,MapStateCodec.Serialize(MapGenParams.CaptureState(1)));Equal("RiverDelta",tile.Mutators.Single().defName);Equal(1,tile.Rivers.Count);
        });
        Check("Deleting a river variant retains plain river and Undo restores delta",()=>
        {
            Setup();var tile=(SurfaceTile)Find.WorldGrid[1];tile.Rivers.Add(new SurfaceTile.RiverLink{neighbor=2});tile.AddMutator(DefDatabase<TileMutatorDef>.Definitions["RiverDelta"]);
            Apply(1,"{\"remove_mutators\":[\"RiverDelta\"]}");Equal("River",tile.Mutators.Single().defName);
            Apply(1,"{\"river\":{\"present\":true,\"direction_angle\":90}}");Equal("River",tile.Mutators.Single().defName);
            MapGenParams.ClearTile(1);Equal("RiverDelta",tile.Mutators.Single().defName);
        });
        Check("Resolved biome road hilliness and third-party worker conditions reject atomically",()=>
        {
            Setup();var tile=(SurfaceTile)Find.WorldGrid[1];var def=DefDatabase<TileMutatorDef>.Definitions["Lake"];
            Action reject=()=>{Throws(()=>Apply(1,"{\"mutators\":[\"Lake\"],\"animal_density\":1.7}"));Equal(0,tile.Mutators.Count);Equal(false,MapGenAIWorldComponent.Get().HasState(1));};
            def.biomeBlacklist=new List<RimWorld.BiomeDef>{tile.PrimaryBiome};reject();def.biomeBlacklist=null;
            def.minHilliness=RimWorld.Hilliness.Mountainous;reject();def.minHilliness=RimWorld.Hilliness.Undefined;
            tile.Roads.Add(new object());def.canSpawnOnRoad=false;reject();def.canSpawnOnRoad=true;
            def.Worker=new TileMutatorWorker{Eligibility=_=>false};reject();def.Worker.Eligibility=_=>true;
            Apply(1,"{\"mutators\":[\"Lake\"]}");Equal("Lake",tile.Mutators.Single().defName);
            MapGenParams.ClearTile(1);
        });
        Check("Inland coast and unavailable presets reject; internal lakes remain allowed",()=>
        {
            Setup();DefDatabase<TileMutatorDef>.Definitions["Coast"]=new TileMutatorDef{defName="Coast",categories=new List<string>{"Coast"}};
            Throws(()=>Apply(1,"{\"mutators\":[\"Coast\"]}"));Throws(()=>Apply(1,"{\"coast_direction\":\"east\"}"));
            var preset=new TileMapState();preset.mutators.Add("Coast");Throws(()=>MapGenParams.RestoreSnapshot(preset,1));
            var riverPreset=new TileMapState{riverDirectionAngle=90};Throws(()=>MapGenParams.RestoreSnapshot(riverPreset,1));
            Apply(1,"{\"mutators\":[\"Lake\"]}");Equal("Lake",Find.WorldGrid[1].Mutators.Single().defName);
        });
        Check("Stored legacy river suppression upgrades while imported suppression is rejected",()=>
        {
            Setup();var tile=(SurfaceTile)Find.WorldGrid[1];tile.Rivers.Add(new SurfaceTile.RiverLink{neighbor=2});
            var old=new TileMapState();old.removeFeatureCategories.Add("River");old.animalDensity=1.3f;
            Throws(()=>MapGenParams.RestoreSnapshot(old,1));
            MapGenAIWorldComponent.Get().SetState(1,old);MapGenParams.LoadFromTile(1);
            Equal("River",tile.Mutators.Single().defName);Equal(0,MapGenParams.CaptureState(1).removeFeatureCategories.Count);Equal(1.3f,MapGenParams.AnimalDensity);
        });
        Check("Pollution side effect rolls back on failed feature transaction and Reset",()=>
        {
            Setup();var tile=Find.WorldGrid[1];tile.pollution=.15f;
            tile.BeforeAdd=d=>{tile.pollution+=.2f;if(d.defName=="B")throw new InvalidOperationException("injected");};
            bool failed=false;try{Apply(1,"{\"mutators\":[\"A\",\"B\"]}");}catch(InvalidOperationException){failed=true;}
            Equal(true,failed);Equal(.15f,tile.pollution);Equal(0,tile.Mutators.Count);
            Apply(1,"{\"mutators\":[\"A\"]}");Equal(true,Math.Abs(.35f-tile.pollution)<.00001f);MapGenParams.ClearTile(1);Equal(.15f,tile.pollution);
        });
        Check("Generic category suppression restores baseline and requires explicit re-enable for additions",()=>
        {
            Setup();var tile=Find.WorldGrid[1];tile.AddMutator(DefDatabase<TileMutatorDef>.Definitions["Lake"]);tile.AddMutator(DefDatabase<TileMutatorDef>.Definitions["A"]);
            Apply(1,"{\"remove_categories\":[\"Lake\"]}");Equal("A",tile.Mutators.Single().defName);
            var before=MapStateCodec.Serialize(MapGenParams.CaptureState(1));
            Throws(()=>Apply(1,"{\"mutators\":[\"Island\"]}"));Equal(before,MapStateCodec.Serialize(MapGenParams.CaptureState(1)));
            Apply(1,"{\"restore_categories\":[\"Lake\"]}");Equal(true,tile.Mutators.Any(d=>d.defName=="Lake"));
            MapGenParams.ClearTile(1);
        });
        Check("Unknown removal targets and category contradictions reject the whole patch",()=>
        {
            Setup();var before=MapStateCodec.Serialize(MapGenParams.CaptureState(1));
            foreach(string json in new[]{"{\"remove_mutators\":[\"DeltaInvented\"],\"animal_density\":1.5}","{\"remove_categories\":[\"Imaginary\"]}","{\"remove_categories\":[\"River\"],\"restore_categories\":[\"River\"]}","{\"river\":{\"present\":true},\"remove_categories\":[\"River\"]}"})
                Throws(()=>Apply(1,json));
            Equal(before,MapStateCodec.Serialize(MapGenParams.CaptureState(1)));Equal(false,MapGenAIWorldComponent.Get().HasState(1));
            Throws(()=>Apply(1,"{\"remove_mutators\":[\"River\"],\"restore_categories\":[\"River\"]}"));
            Throws(()=>Apply(1,"{\"remove_mutators\":[\"River\"],\"river\":{\"present\":true}}"));
        });
        Check("Legacy false river snapshots remain unspecified and missing removed mods allow unrelated edits",()=>
        {
            Setup();var old=MapStateCodec.Deserialize("{\"river\":{\"present\":false}}");Equal(0,old.removeFeatureCategories.Count);
            old.removeMutators.Add("DisabledOldModFeature");MapGenParams.RestoreSnapshot(old,1);
            Apply(1,"{\"animal_density\":1.1}");Equal(1.1f,MapGenParams.CaptureState(1).animalDensity);
            MapGenParams.ClearTile(1);
        });
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
