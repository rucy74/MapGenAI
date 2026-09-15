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
        Check("Mountain cuts keep irregular boundaries and leave low ground and holes untouched",()=>
        {
            var mask=PassageGeometry.Mask(10,10,new[]{new[]{.5f,1f},new[]{.5f,0f}},3);
            var heights=Enumerable.Repeat(.1f,100).ToArray();
            foreach(int i in new[]{24,25,26,35,36,44,46,55,65,66})heights[i]=.8f;
            heights[75]=.7f;heights[85]=.6999f;heights[0]=.9f;
            PassageGeometry.RestrictToMountains(mask,heights);
            string selected=string.Join(",",Enumerable.Range(0,100).Where(i=>mask[i]));
            Equal("24,25,26,35,36,44,46,55,65,66,75",selected);
            var again=PassageGeometry.Mask(10,10,new[]{new[]{.5f,1f},new[]{.5f,0f}},3);
            PassageGeometry.RestrictToMountains(again,heights);Equal(selected,string.Join(",",Enumerable.Range(0,100).Where(i=>again[i])));
            PassageGeometry.RestrictToMountains(again,new float[100]);Equal(0,again.Count(b=>b));
        });
        Check("Mountain scope survives clone presets move and width edits without converting old paths",()=>
        {
            var original=Start();Equal(null,original.elevationShapes[0].scope);
            Equal(false,ShapeEdits.ToObject(original.elevationShapes[0]).ContainsKey("scope"));
            var scoped=Edit(original,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"scope\":\"mountains\"}}]");
            Equal("mountains",scoped.Clone().elevationShapes[0].scope);
            Equal("mountains",MapStateCodec.Deserialize(MapStateCodec.Serialize(scoped)).elevationShapes[0].scope);
            var wider=Edit(scoped,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"width\":12}}]");Equal("mountains",wider.elevationShapes[0].scope);
            var moved=Edit(scoped,"[{\"op\":\"move\",\"id\":\"exit\",\"position\":[0.5,0.5]}]");Equal("mountains",moved.elevationShapes[0].scope);
            var full=Edit(scoped,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"scope\":\"full\"}}]");Equal("full",full.elevationShapes[0].scope);
            Equal(null,original.elevationShapes[0].scope);
            Equal(true,new MapPlanDescription(true).Shape(scoped.elevationShapes[0]).Contains("산 부분만, 평지 유지"));
            Equal(true,new MapPlanDescription(false).Shape(scoped.elevationShapes[0]).Contains("mountains only; open ground preserved"));
        });
        Check("Invalid passage scopes reject without changing old terrain",()=>
        {
            var initial=Start();string saved=MapStateCodec.Serialize(initial);
            foreach(string scope in new[]{"null","true","\"mountain\"","\"\""})Throws(()=>Edit(initial,"[{\"op\":\"update\",\"id\":\"exit\",\"changes\":{\"scope\":"+scope+"}}]"));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"bump\",\"scope\":\"mountains\"}")));
            Equal(saved,MapStateCodec.Serialize(initial));
        });
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
