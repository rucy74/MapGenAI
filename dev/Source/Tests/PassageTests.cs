using System;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class PassageTests
{
    const string Shape="{\"id\":\"exit\",\"type\":\"passage\",\"points\":[[0.3,0.7],[0.5,0.3],[0.8,0.4]],\"width\":8,\"fill\":\"Soil\"}";
    static TileMapState Edit(TileMapState s,string edits)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse("{\"shape_ops\":"+edits+"}")));
    static TileMapState Start()=>Edit(new TileMapState(),"[{\"op\":\"add\",\"shape\":"+Shape+"}]");
    public static void RunAll()
    {
        Check("Passage width is counted in cells on rectangular maps and reaches endpoints",()=>
        {
            foreach(int w in new[]{1,8,9,64})
            {
                var mask=PassageGeometry.Mask(100,80,new[]{new[]{0f,.5f},new[]{1f,.5f}},w);
                Equal(100*w,mask.Count(b=>b));Equal(true,mask[4000]);Equal(true,mask[4099]);
            }
        });
        Check("Bent passage covers requested waypoints and stays four-connected at one-cell width",()=>
        {
            var shape=Start().elevationShapes.Single();var thin=PassageGeometry.Mask(250,250,shape.points,1);
            var visited=new bool[thin.Length];var q=new System.Collections.Generic.Queue<int>();int first=Array.FindIndex(thin,b=>b);visited[first]=true;q.Enqueue(first);
            while(q.Count>0){int i=q.Dequeue(),x=i%250,z=i/250;foreach(int n in new[]{x>0?i-1:-1,x<249?i+1:-1,z>0?i-250:-1,z<249?i+250:-1})if(n>=0 && thin[n] && !visited[n]){visited[n]=true;q.Enqueue(n);}}
            Equal(thin.Count(b=>b),visited.Count(b=>b));
            var wide=PassageGeometry.Mask(250,250,shape.points,8);
            // An independent square-clearance check around every centerline cell, including each bend.
            for(int i=0;i<thin.Length;i++)if(thin[i])for(int z=-3;z<=4;z++)for(int x=-3;x<=4;x++)Equal(true,wide[(i/250+z)*250+i%250+x]);
            Equal(false,wide[0]);Equal(false,wide[249*250+249]);
        });
        Check("Passage clone preset and partial edits preserve route width and other shapes",()=>
        {
            var initial=Start();var clone=initial.Clone();clone.elevationShapes[0].points[0][0]=.2f;Equal(.3f,initial.elevationShapes[0].points[0][0]);
            Equal(MapStateCodec.Serialize(initial),MapStateCodec.Serialize(MapStateCodec.Deserialize(MapStateCodec.Serialize(initial))));
            var wider=Edit(initial,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"width\":12}}]");Equal(12,wider.elevationShapes[0].width);Equal(8,initial.elevationShapes[0].width);
            var moved=Edit(initial,"[{\"op\":\"move\",\"id\":\"exit\",\"position\":[0.5,0.5]}]");Equal(true,Math.Abs(moved.elevationShapes[0].points.Average(p=>p[0])-.5)<.00001);
            Equal(0,Edit(initial,"[{\"op\":\"remove\",\"id\":\"exit\"}]").elevationShapes.Count);
            string label=new MapPlanDescription(false,(k,id)=>new PlanDefinition("soil")).Shape(initial.elevationShapes[0]);Equal(true,label.Contains("8 cells") && !label.Contains("exit"));
        });
        Check("Bad passages reject atomically and do not extend legacy shape contracts",()=>
        {
            var initial=Start();string saved=MapStateCodec.Serialize(initial);
            foreach(string changes in new[]{"{\"width\":0}","{\"width\":8.5}","{\"width\":65}","{\"points\":[[0,0]]}","{\"points\":[[0,0],[0,0]]}","{\"points\":[[-0.1,0],[1,1]]}","{\"points\":null}","{\"edge_roughness\":\"medium\"}","{\"fill\":null}"})Throws(()=>Edit(initial,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":"+changes+"}]"));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"bump\",\"points\":[[0,0],[1,1]],\"width\":8}")));
            Equal(saved,MapStateCodec.Serialize(initial));
            var water=Edit(initial,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"fill\":\"WaterDeep\"}}]");Throws(()=>TerrainMaterials.Validate(water));
        });
    }
}
