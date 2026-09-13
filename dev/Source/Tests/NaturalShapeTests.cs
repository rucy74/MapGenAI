using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.TestFixtures;
using MapGenAI.UI;
using UnityEngine;
using Verse;
using static CoreRegressionTests;

static class NaturalShapeTests
{
    const int Size=256;
    static TileMapState Edit(TileMapState s,string json)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(json)));
    public static void RunAll()
    {
        Check("Natural contour patch is reversible and preserves unrelated geometry/preset/clone",()=>
        {
            var initial=new TileMapState{elevationShapes=new List<ElevationShape>{NaturalShapeFixtures.Make("circle"),NaturalShapeFixtures.Make("star")}};
            var changed=Edit(initial,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"circle\",\"changes\":{\"edge_roughness\":\"medium\"}}]}");
            Equal(null,initial.elevationShapes[0].edge_roughness);
            Equal(SimpleJson.Serialize(initial.elevationShapes[1]),SimpleJson.Serialize(changed.elevationShapes[1]));
            Equal("medium",MapStateCodec.Deserialize(MapStateCodec.Serialize(changed)).elevationShapes[0].edge_roughness);
            Equal("medium",changed.Clone().elevationShapes[0].edge_roughness);
            Equal("medium",ShapeEdits.Describe(changed.elevationShapes)[0]["edge_roughness"]);
            Equal("elevationShapes",string.Join(",",MapStateCodec.ChangedFields(initial,changed)));
            var restored=Edit(changed,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"circle\",\"changes\":{\"edge_roughness\":\"none\"}}]}");
            Equal(true,Mask(initial.elevationShapes[0]).SequenceEqual(Mask(restored.elevationShapes[0])));
        });
        Check("Contour bounds reject malformed values and unsupported terrain types atomically",()=>
        {
            var s=new TileMapState{elevationShapes=new List<ElevationShape>{NaturalShapeFixtures.Make("circle")}};
            foreach(var value in new[]{"null","-0.1","1.1","\"NaN\"","\"organic\"","true"})
                Throws(()=>Edit(s,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"circle\",\"changes\":{\"edge_roughness\":"+value+"}}]}"));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"ring\",\"edge_roughness\":\"medium\"}")));
            Equal(.5f,ContourWarp.Amount(Edit(s,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"circle\",\"changes\":{\"edge_roughness\":0.5}}]}").elevationShapes[0].edge_roughness));
        });
        foreach(string kind in new[]{"circle","star","heart","donut"})
        Check(kind+" precise/default equality, natural deterministic outline and connected shape",()=>
        {
            var exact=Mask(NaturalShapeFixtures.Make(kind));
            Equal(true,exact.SequenceEqual(Mask(NaturalShapeFixtures.Make(kind,"none"))));
            Equal(true,exact.SequenceEqual(Mask(NaturalShapeFixtures.Make(kind,"0"))));
            foreach(string level in new[]{"low","medium","high"})
            {
                var shape=NaturalShapeFixtures.Make(kind,level);var natural=Mask(shape);
                Equal(true,natural.SequenceEqual(Mask(shape)));
                int difference=exact.Zip(natural,(a,b)=>a!=b?1:0).Sum();
                Equal(true,difference>35);
                double area=(double)natural.Count(b=>b)/exact.Count(b=>b);
                Equal(true,area>.75 && area<1.25);
                Equal(1,Components(natural,true));
                Equal(kind=="donut"?2:1,Components(natural,false));
                Equal(false,natural[0]);Equal(kind!="donut",natural[(Size/2)*Size+Size/2]);
            }
        });
        Check("Moving a natural composite translates its contour without rerolling",()=>
        {
            var shape=NaturalShapeFixtures.Make("heart","high");var warp=new ContourWarp(shape.compositeShapes,shape.id,1);
            var state=Edit(new TileMapState{elevationShapes=new List<ElevationShape>{shape}},"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"heart\",\"position\":[0.6,0.4]}]}");
            var moved=state.elevationShapes[0];var delta=moved.compositeShapes[0].GetCenter()-shape.compositeShapes[0].GetCenter();
            var other=new ContourWarp(moved.compositeShapes,moved.id,1);
            for(int x=0;x<20;x++)for(int y=0;y<20;y++)
            {var p=new Vector2(x/20f,y/20f);Equal(true,(warp.Sample(p)+delta-other.Sample(p+delta)).magnitude<.00001f);}
        });
        Check("Narrow donut retains a hole and bridge across seeds at high roughness",()=>
        {
            for(int i=0;i<12;i++)
            {
                var s=NaturalShapeFixtures.Make("donut","high");s.id="ring_"+i;s.compositeShapes[1].r=.25f;
                var mask=Mask(s);Equal(1,Components(mask,true));Equal(2,Components(mask,false));
            }
        });
    }
    static bool[] Mask(ElevationShape shape)
    {
        var map=new Map{Size=new IntVec3(Size,1,Size)};var elevation=new MapGenFloatGrid();
        MapGenerator.Fertility=new MapGenFloatGrid();
        try
        {
            SdfComposite.ApplyComposite(shape.compositeShapes,shape.compositeOps,map,elevation,shape.edge_roughness,shape.id);
            var mask=new bool[Size*Size];foreach(var c in CellRect.WholeMap(map))mask[c.z*Size+c.x]=MapGenerator.Fertility[c]<-1000;return mask;
        }
        finally{MapGenerator.Fertility=null;}
    }
    static int Components(bool[] mask,bool value)
    {
        var visited=new bool[mask.Length];int count=0;var queue=new Queue<int>();
        for(int i=0;i<mask.Length;i++)if(!visited[i]&&mask[i]==value)
        {
            count++;visited[i]=true;queue.Enqueue(i);
            while(queue.Count>0)
            {
                int p=queue.Dequeue(),x=p%Size,y=p/Size;
                foreach(int q in new[]{x>0?p-1:-1,x<Size-1?p+1:-1,y>0?p-Size:-1,y<Size-1?p+Size:-1})
                    if(q>=0&&!visited[q]&&mask[q]==value){visited[q]=true;queue.Enqueue(q);}
            }
        }
        return count;
    }
}
