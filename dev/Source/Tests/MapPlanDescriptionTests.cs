using System;
using System.Collections.Generic;
using MapGenAI.MapGen;
using MapGenAI.LLM;
using MapGenAI.UI;
using static CoreRegressionTests;

static class MapPlanDescriptionTests
{
    static TileMapState Patch(TileMapState before,string json)=>MapStateEditor.Merge(before,MapParameterParser.Parse(SimpleJson.Parse(json)));
    public static void RunAll()
    {
        Check("Recommendations use translated definition names and help without internal identifiers",()=>
        {
            var definitions=new Dictionary<string,PlanDefinition>{{"HotSprings",new PlanDefinition("온천","지열로 데워진 온수 샘입니다.")},{"Mod_Foo",new PlanDefinition("별 보기 좋은 곳","하늘을 보는 활동으로 얻는 즐거움이 증가합니다.")}};
            Func<string,string,PlanDefinition> lookup=(kind,id)=>definitions.TryGetValue(id,out var entry)?entry:null;
            var raw=SimpleJson.Parse("{\"options\":[{\"title\":\"UNVERIFIED LAVA\",\"params\":{\"mutators\":[\"HotSprings\",\"Mod_Foo\"],\"vegetation_density\":1.3}}]}");
            string summary=RecommendationPlan.Validate(raw,new TileMapState(),data=>{},true,lookup)[0].Summary;
            Equal(true,summary.Contains("온천") && summary.Contains("온수 샘") && summary.Contains("별 보기 좋은 곳") && summary.Contains("즐거움"));
            foreach(string code in new[]{"HotSprings","Mod_Foo","UNVERIFIED","vegetationDensity","1.3"})Equal(false,summary.Contains(code));
            definitions["HotSprings"]=new PlanDefinition("Translated hot springs","Warm geothermal pools.");
            string english=RecommendationPlan.Validate(raw,new TileMapState(),data=>{},false,lookup)[0].Summary;
            Equal(true,english.Contains("Translated hot springs") && english.Contains("Warm geothermal pools"));
        });
        Check("Radial and split descriptions follow generator sign semantics",()=>
        {
            var view=new MapPlanDescription(true);
            Equal(true,view.Shape(new ElevationShape{type="radial",strength="strong"}).Contains("중앙이 낮은 분지"));
            Equal(true,view.Shape(new ElevationShape{type="radial",strength="negative_strong"}).Contains("중앙이 높고"));
            Equal(true,view.Shape(new ElevationShape{type="split",strength="strong"}).Contains("협곡"));
            Equal(true,view.Shape(new ElevationShape{type="split",strength="negative_strong"}).Contains("산맥"));
        });
        Check("Shape display names location and effective fill without leaking shape identifiers",()=>
        {
            var before=new TileMapState();var after=Patch(before,"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"secret_id\",\"type\":\"composite\",\"edge_roughness\":\"medium\",\"fill\":\"lava\",\"shapes\":[{\"id\":\"c\",\"prim\":\"circle\",\"center\":[0.8,0.8],\"r\":0.1}],\"compose\":[{\"op\":\"add\",\"s\":\"c\",\"fill\":\"WaterShallow\",\"e\":0}]}}]}");
            var view=new MapPlanDescription(true,(kind,id)=>new PlanDefinition(id=="lava"?"깊은 용암":"얕은 물"));string summary=view.Describe(before,after);
            foreach(string text in new[]{"북동쪽","둥근","깊은 용암","불규칙한 가장자리"})Equal(true,summary.Contains(text));
            foreach(string text in new[]{"secret_id","composite","WaterShallow","얕은 물"})Equal(false,summary.Contains(text));
        });
        Check("Readable moves and removals preserve source state and mention the actual operation",()=>
        {
            var before=Patch(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"pool\",\"type\":\"bump\",\"position\":[0.2,0.2],\"strength\":\"negative_strong\",\"fill\":\"water\"}}]}");string saved=MapStateCodec.Serialize(before);
            var after=Patch(before,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"pool\",\"position\":[0.8,0.8]}]}");var view=new MapPlanDescription(true,(kind,id)=>new PlanDefinition("물"));
            string moved=view.Describe(before,after);Equal(true,moved.Contains("북동쪽") && moved.Contains("위치 조정"));Equal(false,moved.Contains(" 추가"));
            string removed=view.Describe(before,new TileMapState());Equal(true,removed.Contains("원래 지형으로"));Equal(saved,MapStateCodec.Serialize(before));
        });
        Check("Structure region and relation display human terrain names and distances",()=>
        {
            var before=new TileMapState();var after=Patch(before,"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"secret_region\",\"type\":\"bump\",\"position\":\"center\",\"strength\":\"weak\"}}],\"structure_ops\":[{\"op\":\"add\",\"structure\":{\"id\":\"secret_building\",\"region\":\"secret_region\",\"kind\":\"ruin\",\"width\":9,\"height\":11,\"count\":2,\"relation\":{\"target\":\"river\",\"min_distance\":2,\"max_distance\":12,\"side\":\"east\"}}}]}");
            string text=new MapPlanDescription(true).Describe(before,after);
            foreach(string word in new[]{"폐허","안쪽","9×11","강 동쪽","2~12칸"})Equal(true,text.Contains(word));
            foreach(string word in new[]{"secret_region","secret_building","river","east","ruin"})Equal(false,text.Contains(word));
        });
        Check("Features removed and explicit cave settings stay visible in plain summaries",()=>
        {
            var before=new TileMapState();before.mutators.Add("HotSprings");
            var after=Patch(before,"{\"remove_mutators\":[\"HotSprings\"],\"caves\":false}");string text=new MapPlanDescription(true,(kind,id)=>new PlanDefinition("온천","온수 샘입니다.")).Describe(before,after);
            Equal(true,text.Contains("온천 제거") && text.Contains("동굴 생성을 명시적으로 차단"));Equal(false,text.Contains("온수 샘입니다"));
        });
    }
}
