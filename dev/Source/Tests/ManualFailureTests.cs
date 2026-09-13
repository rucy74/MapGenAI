using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class ManualFailureTests
{
    static TileMapState Parse(string json)=>MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(json)));
    public static void RunAll()
    {
        Check("A soil-filled dry passage lowers an existing mountain before terrain generation",()=>
        {
            // Geometry/e copied from both D04 responses in user-evidence lines 257/294.
            foreach(var dimensions in new[]{new[]{.15f,.35f,.02f},new[]{.25f,.6f,.03f}})
            {
                var cut=new ElevationShape{id="pass",type="composite",
                    compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="cut",prim="rect",center=new[]{dimensions[0],.5f},w=dimensions[1],h=.08f}},
                    compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="cut",e=.05f,fill="soil",f=dimensions[2]}}};
                var state=new TileMapState();state.elevationShapes.Add(cut);
                var map=new Map{Size=new IntVec3(100,1,100)};var height=new MapGenFloatGrid();MapGenerator.Fertility=new MapGenFloatGrid();
                foreach(var c in CellRect.WholeMap(map))height[c]=1.4f;
                // Existing distant lake must not become land while carving the west mountain.
                var distant=new IntVec3(75,0,25);MapGenerator.Fertility[distant]=-2005f;height[distant]=.2f;
                using(GenerationContext.Enter(1,state))
                {
                    SdfComposite.ApplyComposite(cut.compositeShapes,cut.compositeOps,map,height,null,cut.id);
                    for(int x=1;x<30;x++)
                    {
                        var c=new IntVec3(x,0,50);Equal(.05f,height[c]);Equal(-2085f,MapGenerator.Fertility[c]);
                        Equal("Soil",GenerationContext.Regions(map).Materials[5000+x]);Equal(true,GenerationContext.Regions(map).Flatten[5000+x]);
                    }
                    Equal(1.4f,height[new IntVec3(15,0,75)]);Equal(-2005f,MapGenerator.Fertility[distant]);Equal(.2f,height[distant]);
                }
            }
        });
        Check("Legacy negative-height composites remain lakes across a saved-state roundtrip",()=>
        {
            var state=Parse(@"{""elevation_shapes"":[{""id"":""legacy"",""type"":""composite"",""shapes"":[{""id"":""c"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.2}],""compose"":[{""op"":""add"",""s"":""c"",""e"":-0.5}]}]}");
            state=MapStateCodec.Deserialize(MapStateCodec.Serialize(state));var s=state.elevationShapes.Single();
            var map=new Map{Size=new IntVec3(100,1,100)};MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,state))
            {
                SdfComposite.ApplyComposite(s.compositeShapes,s.compositeOps,map,new MapGenFloatGrid(),null,s.id);
                Equal(-2005f,MapGenerator.Fertility[new IntVec3(50,0,50)]);Equal("WaterDeep",GenerationContext.Regions(map).Materials[5050]);
            }
        });
        Check("Material-only replacement reaches an out/add moat without filling its hole",()=>
        {
            var lava=new TerrainDef{defName="LavaDeep",label="lava"};DefDatabase<TerrainDef>.Definitions[lava.defName]=lava;
            try
            {
                var state=Parse(@"{""elevation_shapes"":[{""id"":""water_moat"",""type"":""composite"",""shapes"":[{""id"":""outer"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.325},{""id"":""inner"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.14}],""compose"":[{""op"":""sub"",""a"":""inner"",""from"":""outer"",""out"":""moat""},{""op"":""add"",""s"":""moat"",""fill"":""water"",""e"":0}]}]}");
                var before=state.Clone();state=MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(@"{""shape_ops"":[{""op"":""update"",""id"":""water_moat"",""changes"":{""fill"":""LavaDeep""}}]}")));
                var s=state.elevationShapes.Single();Equal(SimpleJson.Serialize(before.elevationShapes[0].compositeOps),SimpleJson.Serialize(s.compositeOps));
                var map=new Map{Size=new IntVec3(100,1,100)};MapGenerator.Fertility=new MapGenFloatGrid();
                using(GenerationContext.Enter(1,state))
                {
                    SdfComposite.ApplyComposite(s.compositeShapes,s.compositeOps,map,new MapGenFloatGrid(),null,s.id,s.fill);
                    var r=GenerationContext.Regions(map);Equal(null,r.Materials[5050]);Equal(false,r.Contains(s.id,50,50));
                    for(int x=67;x<=80;x++)Equal("LavaDeep",r.Materials[5000+x]);
                    Equal(false,r.Materials.Any(m=>m=="WaterDeep"||m=="WaterShallow"));
                }
            }
            finally{DefDatabase<TerrainDef>.Definitions.Remove(lava.defName);}
        });
    }
}
