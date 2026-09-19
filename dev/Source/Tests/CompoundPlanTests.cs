using System;
using System.IO;
using System.Linq;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class CompoundPlanTests
{
    static TileMapState Recorded()=>MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(ProviderResponse.Command(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"compound-fixtures","compound-01-response.json"))).GetObject("params")));
    static TileMapState Edit(TileMapState s,string json)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(json)));
    const string Interior="{\"structure_ops\":[{\"op\":\"update\",\"id\":\"inner_ruins\",\"changes\":{\"region_part\":\"enclosed\"}}]}";
    public static void RunAll()
    {
        Check("Recorded compound ring and ruins choose different areas only with explicit enclosed",()=>
        {
            var original=Recorded();var state=Edit(original,Interior);
            Equal(null,original.structures[0].region_part);Equal("enclosed",state.Clone().structures[0].region_part);
            Equal(MapStateCodec.Serialize(state),MapStateCodec.Serialize(MapStateCodec.Deserialize(MapStateCodec.Serialize(state))));
            var map=new Map{Size=new IntVec3(100,1,100)};MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,state))
            {
                var ring=state.elevationShapes[0];SdfComposite.ApplyComposite(ring.compositeShapes,ring.compositeOps,map,new MapGenFloatGrid(),ring.edge_roughness,ring.id);
                var grid=GenerationContext.Regions(map);var inside=grid.Mask(ring.id,null);var enclosed=grid.Mask(ring.id,"enclosed");
                Equal(false,inside[5050]);Equal(true,enclosed[5050]);Equal(false,enclosed[0]);
                Equal(false,inside.Where((b,i)=>b && enclosed[i]).Any());
                var positions=PlacementPlanner.Find(100,100,enclosed,new bool[10000],9,7,2,50,50,3);
                Equal(2,positions.Count);
                foreach(var r in positions)for(int z=r.z;z<r.z+r.height;z++)for(int x=r.x;x<r.x+r.width;x++)Equal(true,enclosed[z*100+x]);
            }
            MapGenerator.Fertility=null;
        });
        Check("Compound sequential width fraction count edits preserve every unrelated condition",()=>
        {
            var state=Edit(Recorded(),Interior);string before=MapStateCodec.Serialize(state);
            var next=Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"south_exit\",\"changes\":{\"width\":12}},{\"op\":\"update\",\"id\":\"inner_rich_soil\",\"changes\":{\"coverage\":0.5}}],\"structure_ops\":[{\"op\":\"update\",\"id\":\"inner_ruins\",\"changes\":{\"count\":3}}]}");
            Equal(12,next.elevationShapes.Single(s=>s.type=="passage").width);Equal("0.5",next.elevationShapes.Single(s=>s.type=="region_fill").coverage);Equal(3,next.structures[0].count);
            var restored=next.Clone();restored.elevationShapes.Single(s=>s.type=="passage").width=8;restored.elevationShapes.Single(s=>s.type=="region_fill").coverage="0.7";restored.structures[0].count=2;
            Equal(before,MapStateCodec.Serialize(restored));Equal(before,MapStateCodec.Serialize(state));
        });
        Check("Interior cannot silently fall back to entire map or selected paint when rebinding",()=>
        {
            var state=Edit(Recorded(),Interior);string saved=MapStateCodec.Serialize(state);
            foreach(string changes in new[]{"{\"region\":null}","{\"region_part\":\"outside\"}","{\"region\":\"inner_rich_soil\"}"})
                Throws(()=>Edit(state,"{\"structure_ops\":[{\"op\":\"update\",\"id\":\"inner_ruins\",\"changes\":"+changes+"}]}"));
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"donut_mountain\"}]}"));
            Equal(saved,MapStateCodec.Serialize(state));
            var cleared=Edit(state,"{\"structure_ops\":[{\"op\":\"update\",\"id\":\"inner_ruins\",\"changes\":{\"region\":null,\"region_part\":null,\"position\":[0.2,0.3]}}]}");
            Equal(null,cleared.structures[0].region_part);
        });
        Check("Enclosed structure labels explain the hollow in Korean and English without IDs",()=>
        {
            var state=Edit(Recorded(),Interior);
            foreach(bool ko in new[]{true,false})
            {
                string text=new MapPlanDescription(ko).Describe(new TileMapState(),state);
                Equal(true,text.Contains(ko?"둘러싸인 빈 내부":"enclosed interior"));Equal(false,text.Contains("inner_ruins"));
            }
        });
    }
}
