using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class TerrainWaterSemanticsTests
{
    static bool IsWater(string fill)
    {
        var def=DefDatabase<TerrainDef>.GetNamedSilentFail(TerrainMaterials.DefName(fill));
        return def!=null && def.IsWater && !def.dangerous;
    }
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));

    public static void RunAll()
    {
        // A valid union/sub/inter paints without a trailing add. Compare the actual production rasterizer.
        Check("Authored water detection follows rendered add union sub and intersection fills",()=>
        {
            foreach(string operation in new[]{"add","union","sub","inter"})
            {
                var shape=Composite(operation,"water");
                AssertWater(shape,true);
                shape.fill="soil";
                AssertWater(shape,false);
                shape.fill="WaterShallow";shape.compositeOps[0].fill="soil";
                AssertWater(shape,true);
            }
        });
        Check("Final composite top fill renders without an op fill while empty override suppresses nested water",()=>
        {
            var shape=Composite("union",null);shape.fill="WaterShallow";
            AssertWater(shape,true);
            shape.fill="";shape.compositeOps[0].fill="water";
            AssertWater(shape,false);
            shape=Composite("union",null);shape.compositeOps[0].outId="joined";
            shape.compositeOps.Add(new ComposeOp{op="add",s="joined",fill="water"});
            AssertWater(shape,true);
            shape.fill="soil";
            AssertWater(shape,false);
        });
        // Without case-insensitive deep:false normalization, Water/WATER paints deep feather cells.
        Check("Water alias capitalization retains the actual shallow feather material",()=>
        {
            var lower=Raster(Composite("add","water"));
            Equal(true,lower.Contains("WaterDeep"));Equal(true,lower.Contains("WaterShallow"));
            foreach(string alias in new[]{"Water","WATER"})
            {
                var shape=Composite("add",alias);var actual=Raster(shape);
                Equal(true,TerrainMaterials.HasRenderedFill(shape,IsWater));
                for(int i=0;i<actual.Length;i++)Equal(lower[i],actual[i]);
                Equal("WaterDeep",TerrainMaterials.DefName(alias));
                Equal("WaterShallow",TerrainMaterials.DefName(alias,false));
            }
        });
        Check("Zero coverage and empty shapes do not advertise authored water",()=>
        {
            var fill=new ElevationShape{id="f",type="region_fill",region="source",region_part="inside",coverage="0",fill="water"};
            ShapeValidation.Validate(fill);Equal(false,TerrainMaterials.HasRenderedFill(fill,IsWater));
            fill.coverage="0.5";Equal(true,TerrainMaterials.HasRenderedFill(fill,IsWater));
            Equal(false,TerrainMaterials.HasRenderedFill(null,IsWater));
            Equal(false,TerrainMaterials.HasRenderedFill(new ElevationShape{type="composite",fill="water"},IsWater));
            Equal(true,TerrainMaterials.HasRenderedFill(new ElevationShape{type="bump",fill="water"},IsWater));
            Equal(false,TerrainMaterials.HasRenderedFill(new ElevationShape{type="landform",landform="open_basin"},IsWater));
        });
        Check("New rough ordinary water additions use normalized rendered fills for natural detail defaults",()=>
        {
            foreach(string operation in new[]{"add","union","sub","inter"})
            foreach(string fill in new[]{"water","Water","WATER","WaterShallow"})
            {
                var shape=Composite(operation,fill);shape.edge_roughness="medium";
                Equal("natural",Add(shape).elevationShapes[0].details);
                shape.fill="soil";
                Equal(null,Add(shape).elevationShapes[0].details);
            }
            var top=Composite("union",null);top.fill="WATER";top.edge_roughness="0.5";
            Equal("natural",Add(top).elevationShapes[0].details);
        });
        Check("Natural water defaults preserve special materials exact geometry and saved or partial states",()=>
        {
            foreach(string special in new[]{"HotSpring","LavaDeep","CustomWater"})
            {
                var shape=Composite("add",special);shape.edge_roughness="medium";
                Equal(null,Add(shape).elevationShapes[0].details);
            }
            var exact=Composite("add","WATER");Equal(null,Add(exact).elevationShapes[0].details);
            exact.edge_roughness="none";Equal(null,Add(exact).elevationShapes[0].details);
            var natural=Composite("add","WATER");natural.edge_roughness="medium";natural.details="none";
            Equal("none",Add(natural).elevationShapes[0].details);
            natural.details=null;
            var restored=Edit(new TileMapState(),"{\"elevation_shapes\":["+SimpleJson.Serialize(ShapeEdits.ToObject(natural))+"]}");
            Equal(null,MapStateCodec.Deserialize(MapStateCodec.Serialize(restored)).elevationShapes[0].details);
            Equal(null,Edit(restored,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"water_test\",\"changes\":{\"variant\":\"8\"}}]}").elevationShapes[0].details);
            natural.compositeOps[0].fill=null;natural.compositeOps[0].e=-.5f;
            Equal(null,Add(natural).elevationShapes[0].details); // Legacy implicit-water behavior is unchanged.
        });
    }

    static TileMapState Add(ElevationShape shape)=>Edit(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":"+SimpleJson.Serialize(ShapeEdits.ToObject(shape))+"}]}");

    static void AssertWater(ElevationShape shape,bool expected)
    {
        var actual=Raster(shape);
        Equal(expected,actual.Any(fill=>fill!=null && IsWater(fill)));
        Equal(expected,TerrainMaterials.HasRenderedFill(shape,IsWater));
    }

    static string[] Raster(ElevationShape shape)
    {
        ShapeValidation.Validate(shape);
        var map=new Map{Size=new IntVec3(80,1,80)};
        var savedFertility=MapGenerator.Fertility;
        try
        {
            MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,new TileMapState{elevationShapes=new List<ElevationShape>{shape}}))
            {
                SdfComposite.ApplyComposite(shape.compositeShapes,shape.compositeOps,map,new MapGenFloatGrid(),shape.edge_roughness,shape.id,shape.fill);
                return (string[])GenerationContext.Regions(map).Materials.Clone();
            }
        }
        finally{MapGenerator.Fertility=savedFertility;}
    }

    static ElevationShape Composite(string operation,string fill)=>new ElevationShape
    {
        id="water_test",type="composite",
        compositeShapes=new List<ShapePrimitive>
        {
            new ShapePrimitive{id="outer",prim="circle",center=new[]{.45f,.5f},r=.22f},
            new ShapePrimitive{id="inner",prim="circle",center=new[]{.55f,.5f},r=.12f}
        },
        compositeOps=new List<ComposeOp>
        {
            new ComposeOp{op=operation,s="outer",a=operation=="sub"?"inner":"outer",b="inner",from="outer",fill=fill}
        }
    };
}
