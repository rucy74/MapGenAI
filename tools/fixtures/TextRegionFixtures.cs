using System.Collections.Generic;
using MapGenAI.MapGen;

namespace MapGenAI.Fixtures
{
    public static class TextRegionFixtures
    {
        public static ElevationShape Circle(string id,string fill,float x=.5f,float z=.5f,float radius=.14f) => new ElevationShape {
            id=id,type="composite",compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="c",prim="circle",center=new[]{x,z},r=radius}},
            compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="c",fill=fill,e=.05f,f=.002f}}
        };
        public static TileMapState Island(string fill="lava",bool ruins=true)
        {
            var state=new TileMapState{hillAmount=.4f,ruinDensity=0,dangerDensity=0,vegetationDensity=0,animalDensity=0,geyserCount=0,hasRockChunks=false};
            state.elevationShapes.Add(new ElevationShape{id="moat",type="composite",
                compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="outer",prim="circle",center=new[]{.5f,.5f},r=.34f},new ShapePrimitive{id="inner",prim="circle",center=new[]{.5f,.5f},r=.14f}},
                compositeOps=new List<ComposeOp>{new ComposeOp{op="sub",a="inner",from="outer",fill=fill,f=.002f}}});
            state.elevationShapes.Add(Circle("island","soil"));
            if(ruins)state.structures.Add(new StructurePlan{id="ruins_1",region="island",width=11,height=9,count=2});
            return state;
        }
    }
}
