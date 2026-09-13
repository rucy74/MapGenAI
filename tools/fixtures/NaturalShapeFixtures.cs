using System.Collections.Generic;
using MapGenAI.MapGen;

namespace MapGenAI.TestFixtures
{
    public static class NaturalShapeFixtures
    {
        public static ElevationShape Make(string kind, string roughness = null, float x = .5f, float y = .5f, float radius = .27f)
        {
            var part = new ShapePrimitive {id="outer",prim=kind=="donut"?"circle":kind,center=new[]{x,y},r=radius,r2=radius*.48f,n=5,size=radius*1.55f};
            if(kind=="heart")part.center[1]-=radius*.7f;
            var shape=new ElevationShape {id=kind,type="composite",edge_roughness=roughness,compositeShapes=new List<ShapePrimitive>{part},compositeOps=new List<ComposeOp>()};
            if(kind=="donut")
            {
                shape.compositeShapes.Add(new ShapePrimitive{id="inner",prim="circle",center=new[]{x,y},r=radius*.72f});
                shape.compositeOps.Add(new ComposeOp{op="sub",a="inner",from="outer",fill="water",e=0,f=.006f});
            }
            else shape.compositeOps.Add(new ComposeOp{op="add",s="outer",fill="water",e=0,f=.006f});
            return shape;
        }
    }
}
