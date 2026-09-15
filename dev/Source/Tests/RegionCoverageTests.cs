using System;
using System.IO;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.LLM;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class RegionCoverageTests
{
    static TileMapState Edit(TileMapState s,string json)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static string Recorded(int line)=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"coverage-fixtures","line-"+line.ToString("0000")+"-response.json"));
    static TileMapState Donut()=>MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(ProviderResponse.Command(Recorded(58)).GetObject("params")));
    const string Fill="{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"inner_fill\",\"type\":\"region_fill\",\"region\":\"central_mountain_donut\",\"region_part\":\"enclosed\",\"coverage\":0.7,\"fill\":\"SoilRich\"}}]}";
    static void GenerationFails(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected generation rejection");}
    public static void RunAll()
    {
        // Removing the counted selector, or using the nominal radius, overfills this recorded geometry.
        Check("Recorded donut soil feathering covers the visible hole; counted coverage leaves thirty percent",()=>
        {
            var before=Donut();var soil=MapParameterParser.Parse(ProviderResponse.Command(Recorded(72)).GetObject("params"));var after=MapStateEditor.Merge(before,soil);
            var map=new Map{Size=new IntVec3(250,1,250)};var e=new MapGenFloatGrid();MapGenerator.Fertility=new MapGenFloatGrid();
            using(GenerationContext.Enter(1,before))
            {
                var donut=before.elevationShapes[0];foreach(var c in CellRect.WholeMap(map))e[c]=.15f;
                SdfComposite.ApplyComposite(donut.compositeShapes,donut.compositeOps,map,e,donut.edge_roughness,donut.id);
                var hole=RegionCoverage.Enclosed(250,250,GenerationContext.Regions(map).Mask(donut.id));var eligible=(bool[])hole.Clone();
                foreach(var c in CellRect.WholeMap(map))eligible[c.z*250+c.x]&=e[c]<.7f;
                var fill=after.elevationShapes.Last();SdfComposite.ApplyComposite(fill.compositeShapes,fill.compositeOps,map,e,null,fill.id);
                int available=eligible.Count(b=>b),painted=0;foreach(var c in CellRect.WholeMap(map))if(eligible[c.z*250+c.x] && GenerationContext.Regions(map).Materials[c.z*250+c.x]=="SoilRich")painted++;
                Equal(true,available>1000 && painted/(double)available>.95);
                var selected=RegionCoverage.Select(250,250,eligible,new bool[62500],.7f);
                Equal(true,Math.Abs(selected.Count(b=>b)/(double)available-.7)<.001);
                Equal(false,selected.Where((b,i)=>b && !eligible[i]).Any());
                Console.WriteLine("Recorded coverage reproduction: old="+painted+"/"+available+", corrected="+selected.Count(b=>b)+"/"+available);
            }
            MapGenerator.Fertility=null;
        });
        Check("Coverage percentages count usable cells and support direction without feathering",()=>
        {
            var area=Enumerable.Repeat(true,100).ToArray();var existing=new bool[100];
            foreach(float fraction in new[]{0f,.5f,.7f,1f})Equal((int)(fraction*100),RegionCoverage.Select(10,10,area,existing,fraction).Count(b=>b));
            area[44]=false;area[45]=false;area[46]=false;
            var selected=RegionCoverage.Select(10,10,area,existing,.7f);Equal(68,selected.Count(b=>b));Equal(false,selected[45]);
            area=Enumerable.Repeat(true,100).ToArray();selected=RegionCoverage.Select(10,10,area,existing,.5f,"left");
            Equal(true,Enumerable.Range(0,100).All(i=>selected[i]==(i%10<5)));
        });
        Check("Enclosed region ignores exterior and open shapes cannot silently paint the whole map",()=>
        {
            var ring=new bool[100];for(int z=2;z<=7;z++)for(int x=2;x<=7;x++)ring[z*10+x]=x==2||x==7||z==2||z==7;
            var hole=RegionCoverage.Enclosed(10,10,ring);Equal(16,hole.Count(b=>b));Equal(true,hole[44]);Equal(false,hole[0]);
            ring[23]=false;hole=RegionCoverage.Enclosed(10,10,ring);Equal(0,hole.Count(b=>b));GenerationFails(()=>RegionCoverage.Select(10,10,hole,new bool[100],.7f));
        });
        Check("Existing material counts toward the quota and excessive baseline is reported",()=>
        {
            var area=Enumerable.Repeat(true,100).ToArray();var existing=new bool[100];for(int i=0;i<20;i++)existing[i]=true;
            var selected=RegionCoverage.Select(10,10,area,existing,.7f);Equal(70,selected.Count(b=>b));Equal(true,Enumerable.Range(0,20).All(i=>selected[i]));
            GenerationFails(()=>RegionCoverage.Select(10,10,area,existing,.1f));
        });
        Check("Coverage follow-up updates soil alone and survives clone preset and source movement",()=>
        {
            var baseline=Donut();string mountain=SimpleJson.Serialize(baseline.elevationShapes[0]);var filled=Edit(baseline,Fill);
            var half=Edit(filled,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"inner_fill\",\"changes\":{\"coverage\":0.5}}]}");
            Equal("0.7",filled.elevationShapes[1].coverage);Equal("0.5",half.elevationShapes[1].coverage);Equal(mountain,SimpleJson.Serialize(half.elevationShapes[0]));
            Equal(MapStateCodec.Serialize(half),MapStateCodec.Serialize(MapStateCodec.Deserialize(MapStateCodec.Serialize(half))));
            var moved=Edit(filled,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"central_mountain_donut\",\"position\":[0.6,0.5]}]}");
            Equal("central_mountain_donut",moved.Clone().elevationShapes[1].region);
            string text=new MapPlanDescription(false,(kind,id)=>new PlanDefinition("rich soil")).Describe(baseline,filled);Equal(true,text.Contains("70%") && text.Contains("enclosed") && text.Contains("rich soil"));Equal(false,text.Contains("inner_fill"));
        });
        Check("Bad coverage references numbers and independent geometry reject atomically",()=>
        {
            var filled=Edit(Donut(),Fill);string saved=MapStateCodec.Serialize(filled);
            foreach(var v in new[]{"null","true","-0.1","1.01","\"NaN\""})Throws(()=>Edit(filled,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"inner_fill\",\"changes\":{\"coverage\":"+v+"}}]}"));
            Throws(()=>Edit(filled,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"central_mountain_donut\"}]}"));
            Throws(()=>Edit(filled,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"inner_fill\",\"changes\":{\"strength\":0.7}}]}"));
            Equal(saved,MapStateCodec.Serialize(filled));
        });
    }
}
