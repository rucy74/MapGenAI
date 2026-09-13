using System;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using MapGenAI.ImageInput;
using Verse;
using static CoreRegressionTests;

static class TextRegionTests
{
    public static void SeedMaterials()
    {
        foreach(var name in new[]{"WaterDeep","WaterShallow","Sand","Soil","SoilRich","MarshyTerrain","Mud","Ice","Gravel"})
            DefDatabase<TerrainDef>.Definitions[name]=new TerrainDef{defName=name,label=name,fertility=name=="Soil"?1:0};
    }
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static TileMapState Island()=>Edit(new TileMapState(),@"{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""island"",""type"":""composite"",""shapes"":[{""id"":""c"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.2}],""compose"":[{""op"":""add"",""s"":""c"",""e"":0.05,""fill"":""soil""}]}}]}");
    const string Add=@"{""structure_ops"":[{""op"":""add"",""structure"":{""id"":""r1"",""kind"":""ruin"",""region"":""island"",""width"":11,""height"":9}}]}";
    public static void RunAll()
    {
        Check("Nested generation isolates terrain masks and diagnostics and unwinds after failure",()=>
        {
            var map=new Map{Size=new IntVec3(10,1,10)};
            using(GenerationContext.Enter(1,Island()))
            {
                GenerationContext.Report=new AuthoringResult{state="outer"};
                var outer=GenerationContext.Regions(map);outer.Record("island",new IntVec3(5,0,5),true,"soil",true);
                try{using(GenerationContext.Enter(2,new TileMapState())){GenerationContext.Report=new AuthoringResult{state="inner"};Equal(null,GenerationContext.Regions(map).Materials[55]);throw new InvalidOperationException("fixture");}}
                catch(InvalidOperationException){}
                Equal("outer",GenerationContext.Report.state);Equal("Soil",GenerationContext.Regions(map).Materials[55]);
            }
            Equal(null,GenerationContext.Report);
        });
        Check("Materials use active native defs; unavailable lava never becomes water",()=>
        {
            Equal("WaterShallow",TerrainMaterials.Resolve("water",false).defName);
            Throws(()=>TerrainMaterials.Resolve("lava"));Throws(()=>SdfComposite.FillToFertility("lavva"));
            var lava=new TerrainDef{defName="LavaDeep",label="lava"};DefDatabase<TerrainDef>.Definitions[lava.defName]=lava;
            Equal(lava,TerrainMaterials.Resolve("lava"));Equal(0f,SdfComposite.FillToFertility("lava"));
            DefDatabase<TerrainDef>.Definitions.Remove(lava.defName);
            Throws(()=>TerrainMaterials.Validate(Edit(new TileMapState(),@"{""elevation_shapes"":[{""type"":""bump"",""fill"":""lava""}]}")));
        });
        Check("Loaded mod material extends catalog while temporary ocean road and floors are excluded",()=>
        {
            var custom=new TerrainDef{defName="ModCrystalSoil",label="crystal"};DefDatabase<TerrainDef>.Definitions[custom.defName]=custom;
            Equal(custom,TerrainMaterials.Resolve(custom.defName));Equal(true,TerrainMaterials.Catalog().Contains(custom.defName));
            custom.temporary=true;Throws(()=>TerrainMaterials.Resolve(custom.defName));custom.temporary=false;
            custom.tags.Add("Road");Throws(()=>TerrainMaterials.Resolve(custom.defName));custom.tags.Clear();
            custom.costStuffCount=2;Throws(()=>TerrainMaterials.Resolve(custom.defName));
            Equal(false,TerrainMaterials.Supported(new TerrainDef{defName="WaterOceanDeep"}));
            DefDatabase<TerrainDef>.Definitions.Remove(custom.defName);
        });
        Check("Structure edits preserve terrain density image and stable IDs through presets and cloning",()=>
        {
            var initial=Island();initial.ruinDensity=.3f;initial.imageMap=new ImageMapData{width=1,height=1,cells="W"};
            var added=Edit(initial,Add);Equal(0,initial.structures.Count);Equal(1,added.structures.Count);Equal(.3f,added.ruinDensity);
            var changed=Edit(added,@"{""structure_ops"":[{""op"":""update"",""id"":""r1"",""changes"":{""count"":2,""position"":[0.6,0.7]}}]}");
            Equal(1,added.structures[0].count);Equal(2,changed.structures[0].count);Equal("W",changed.imageMap.cells);
            Equal(SimpleJson.Serialize(added.elevationShapes),SimpleJson.Serialize(changed.elevationShapes));
            var round=MapStateCodec.Deserialize(MapStateCodec.Serialize(changed));Equal(MapStateCodec.Serialize(changed),MapStateCodec.Serialize(round));
            round.structures[0].position[0]=.2f;Equal(.6f,changed.structures[0].position[0]);
            var removed=Edit(changed,@"{""structure_ops"":[{""op"":""remove"",""id"":""r1""}]}");Equal(0,removed.structures.Count);Equal(1,removed.elevationShapes.Count);
            Equal(true,MapStateDescription.Describe(added,changed,true).Contains("2개"));
        });
        Check("Dangling region edits reject atomically; combined removal or rebind succeeds",()=>
        {
            var before=Edit(Island(),Add);string stored=MapStateCodec.Serialize(before);
            Throws(()=>Edit(before,@"{""shape_ops"":[{""op"":""remove"",""id"":""island""}]}"));Equal(stored,MapStateCodec.Serialize(before));
            var removed=Edit(before,@"{""shape_ops"":[{""op"":""remove"",""id"":""island""}],""structure_ops"":[{""op"":""remove"",""id"":""r1""}]}");Equal(0,removed.structures.Count);
            var rebound=Edit(before,@"{""shape_ops"":[{""op"":""remove"",""id"":""island""}],""structure_ops"":[{""op"":""update"",""id"":""r1"",""changes"":{""region"":null,""position"":[0.2,0.8]}}]}");Equal(null,rebound.structures[0].region);
        });
        Check("Invalid structure kinds references sizes fractions bounds and duplicate IDs reject",()=>
        {
            foreach(var fields in new[]{@"""kind"":""ancient_danger""",@"""region"":""missing""",@"""width"":32",@"""width"":5.5",@"""count"":0",@"""bounds"":[0.8,0,0.2,1]",@"""position"":[2,0.3]",@"""unknown"":true"})
                Throws(()=>Edit(Island(),"{\"structure_ops\":[{\"op\":\"add\",\"structure\":{\"id\":\"r2\",\"position\":[0.5,0.5],"+fields+"}}]}"));
            Throws(()=>Edit(Edit(Island(),Add),Add));
            Throws(()=>Edit(Island(),@"{""structure_ops"":null}"));
        });
        Check("CSG donut mask retains its hole under a material override and follows movement",()=>
        {
            var state=Edit(new TileMapState(),@"{""elevation_shapes"":[{""id"":""ring"",""type"":""composite"",""fill"":""sand"",""shapes"":[{""id"":""outer"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.3},{""id"":""inner"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.15}],""compose"":[{""op"":""sub"",""a"":""inner"",""from"":""outer"",""fill"":""water""}]}]}");
            var map=new Map{Size=new IntVec3(100,1,100)};MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,state))
            {
                var s=state.elevationShapes[0];SdfComposite.ApplyComposite(s.compositeShapes,s.compositeOps,map,new MapGenFloatGrid(),s.edge_roughness,s.id,s.fill);
                var grid=GenerationContext.Regions(map);Equal(false,grid.Contains("ring",50,50));Equal(null,grid.Materials[5050]);
                Equal(true,grid.Contains("ring",72,50));Equal("Sand",grid.Materials[5072]);
                using(GenerationContext.Enter(2,new TileMapState()))Equal(false,GenerationContext.Regions(map).Contains("ring",72,50));
                Equal(true,GenerationContext.Regions(map).Contains("ring",72,50));
            }
            var moved=Edit(state,@"{""shape_ops"":[{""op"":""move"",""id"":""ring"",""position"":[0.35,0.5]}]}");
            using(GenerationContext.Enter(1,moved))
            {
                var s=moved.elevationShapes[0];SdfComposite.ApplyComposite(s.compositeShapes,s.compositeOps,map,new MapGenFloatGrid(),s.edge_roughness,s.id,s.fill);
                Equal(false,GenerationContext.Regions(map).Contains("ring",72,50));Equal(true,GenerationContext.Regions(map).Contains("ring",57,50));
            }
        });
        Check("Footprint planner avoids holes and keeps every cell inside irregular masks",()=>
        {
            const int n=70;var allowed=new bool[n*n];for(int z=0;z<n;z++)for(int x=0;x<n;x++){double d=Math.Sqrt((x-35)*(x-35)+(z-35)*(z-35));allowed[z*n+x]=d>14 && d<31;}
            var rectangles=PlacementPlanner.Find(n,n,allowed,new bool[n*n],7,7,4,35,35);Equal(4,rectangles.Count);
            foreach(var r in rectangles)for(int z=r.z;z<r.z+r.height;z++)for(int x=r.x;x<r.x+r.width;x++)Equal(true,allowed[z*n+x]);
            for(int i=0;i<rectangles.Count;i++)for(int j=i+1;j<rectangles.Count;j++)Equal(false,rectangles[i].Contains(rectangles[j].x,rectangles[j].z));
        });
        Check("Capacity failure leaves occupied mask untouched and never places a partial count",()=>
        {
            var allowed=new bool[400];for(int z=5;z<12;z++)for(int x=5;x<12;x++)allowed[z*20+x]=true;
            var occupied=new bool[400];Equal(null,PlacementPlanner.Find(20,20,allowed,occupied,7,7,2,10,10));Equal(false,occupied.Any(x=>x));
            Equal(1,PlacementPlanner.Find(20,20,allowed,occupied,7,7,1,10,10).Count);
        });
        Check("A later mountain or legacy height edit overrides earlier flattening without clearing distant flat land",()=>
        {
            var state=Island();var map=new Map{Size=new IntVec3(100,1,100)};var e=new MapGenFloatGrid();MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,state))
            {
                var flat=state.elevationShapes[0];SdfComposite.ApplyComposite(flat.compositeShapes,flat.compositeOps,map,e,null,flat.id);
                var regions=GenerationContext.Regions(map);Equal(true,regions.Flatten[5050]);Equal(true,regions.Flatten[5065]);
                var raised=flat.Clone();raised.id="new_hill";raised.compositeShapes[0].r=.05f;raised.compositeOps[0].fill=null;raised.compositeOps[0].e=1;
                SdfComposite.ApplyComposite(raised.compositeShapes,raised.compositeOps,map,e,null,raised.id);
                Equal(false,regions.Flatten[5050]);Equal(true,regions.Flatten[5065]);
                var snapshot=regions.CaptureFlattened(e);e[new IntVec3(65,0,50)]+=.8f;regions.PreserveLaterHeightEdit(e,snapshot);
                Equal(false,regions.Flatten[5065]);Equal(true,regions.Flatten[5040]);
            }
        });
        Check("Paused image policy persists old data through text edits and nested generation scopes",()=>
        {
            var old=Island();old.imageMap=new ImageMapData{width=2,height=1,cells="WH",replaceElevation=true};
            var stored=MapStateCodec.Deserialize(MapStateCodec.Serialize(old));var changed=Edit(stored,Add);
            Equal("WH",changed.imageMap.cells);Equal(false,ImageFeatureGate.Enabled);
            bool rejected=false;try{ImageFeatureGate.RequireEnabled();}catch(InvalidOperationException){rejected=true;}Equal(true,rejected);
            using(GenerationContext.Enter(4,changed)){Equal(null,GenerationContext.State.imageMap);Equal(null,MapGenParams.ImageMap);Equal(1,GenerationContext.State.structures.Count);}
            Equal("WH",stored.imageMap.cells);Equal(false,GenerationContext.Active);
        });
    }
}
