using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;

// Six bounded first-response cases, or three independent recommendation cases.
// Run only with a freshly captured production prompt.
// No prompt block refresh, repair request, automatic retry, or hidden provider call.
static class LandscapeBench
{
    public static async Task<int> Run(GeminiClient client,string capture,string output,bool diversityOnly=false)
    {
        if(Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Fresh empty output directory required");
        string template=File.ReadAllText(capture);
        bool korean=template.Contains("\n## 현재 적용된 파라미터");
        string marker=korean?"\n## 현재 적용된 파라미터":"\n## Currently applied parameters";
        string rules=korean?"\n규칙:":"\nRules:";
        int start=template.IndexOf(marker,StringComparison.Ordinal);
        int end=start<0?-1:template.IndexOf(rules,start,StringComparison.Ordinal);
        if(start<0 || end<0 || template.IndexOf(marker,start+marker.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("Expected one captured production current-state section followed by rules");
        Save("captured-system-prompt.txt",template);
        var results=new List<Dictionary<string,object>>();
        int calls=0;bool all=true;
        int plannedCalls=diversityOnly?3:6;
        string mode=diversityOnly?"landscape-diversity":"landscape";
        int seedA=RandomNumberGenerator.GetInt32(int.MaxValue),seedB;
        do{seedB=RandomNumberGenerator.GetInt32(int.MaxValue);}while(seedB==seedA);
        int seedGuide;
        do{seedGuide=RandomNumberGenerator.GetInt32(int.MaxValue);}while(seedGuide==seedA || seedGuide==seedB);
        Save("run-context.json",Json(new {mode,plannedCalls,capture=Path.GetFullPath(capture),utc=DateTime.UtcNow.ToString("o"),
            recommendationSeeds=new[]{seedA,seedB,seedGuide},korean,
            scope="Exact captured native prompt except its checked current-state section; current production variation is appended for new recommendations. "+
                (diversityOnly?"All three recommendation calls are independent and use the same empty authored state and captured native tile. ":"The capture's initial few-shot examples remain in case 6, whereas the live dialog omits them after authored terrain exists. ")+
                "Schema and intent observations use linked production code with offline game stubs. Loaded TerrainDef, TileMutator feature prerequisites, native geometry placement, vegetation, aesthetics and visual candidate diversity are NOT validated here. No application to live game state."}));

        const string lakeside="산 없이 자연스러운 호숫가 정착지를 만들어 줘. 넓게 이어진 마른 땅을 남겨 줘.";
        CaseResult first=null;
        if(!diversityOnly)
        {
            first=await Case("landscape-01-lakeside",lakeside,new TileMapState(),new List<ChatMessage>(),false,null);
            await Case("landscape-02-valley","완만하게 굽은 골짜기를 만들어 줘. 양쪽 산줄기는 비대칭으로, 정착할 평지는 넓게.",new TileMapState(),new List<ChatMessage>(),false,null);
        }
        await Case(diversityOnly?"diversity-01-recommend":"landscape-03-recommend","그냥 추천해 줘.",new TileMapState(),new List<ChatMessage>(),true,seedA);
        await Case(diversityOnly?"diversity-02-recommend":"landscape-04-recommend","그냥 추천해 줘.",new TileMapState(),new List<ChatMessage>(),true,seedB);

        var guide=new RecommendationGuide(true,false);
        var choices=new Dictionary<string,string>{{"priority","scenery"},{"mountains","edge"},{"water","small"},
            {"space","flowing"},{"distinctive","mixed"},{"focus","water"},{"features","layout"}};
        var guideTrace=new List<object>();
        while(!guide.Reviewing)
        {
            var question=guide.Current;
            if(!choices.TryGetValue(question.Id,out var selected))throw new InvalidOperationException("Unspecified guide question: "+question.Id);
            guideTrace.Add(new {question=question.Id,title=question.Title,hint=question.Hint,
                choices=question.Choices.Select(c=>new {id=c.Id,label=c.Label,detail=c.Detail}).ToArray(),selected});
            if(!guide.Select(selected) || !guide.Next())throw new InvalidOperationException("Guide rejected planned choice: "+question.Id);
        }
        Save("guide-questions-and-answers.json",Json(guideTrace));
        Save("guide-summary.txt",guide.Summary());
        if(!guide.Submit(out string guidedRequest))throw new InvalidOperationException("Guide did not submit");
        Save("guide-submitted-request.txt",guidedRequest);
        await Case(diversityOnly?"diversity-03-guided":"landscape-05-guided",guidedRequest,new TileMapState(),new List<ChatMessage>(),true,seedGuide);

        if(!diversityOnly && first.State!=null)
        {
            var history=new List<ChatMessage>{new ChatMessage("user",lakeside),new ChatMessage("assistant",first.Reply)};
            await Case("landscape-06-followup","연못은 평지 안쪽 가장자리로 옮겨 줘. 나머지는 유지해.",first.State,history,false,null);
        }
        else if(!diversityOnly)
        {
            all=false;
            results.Add(new Dictionary<string,object>{{"id","landscape-06-followup"},{"ok",false},{"called",false},
                {"skipped","First lakeside response did not provide a schema-valid generated state; no invented replacement fixture or additional call."}});
        }
        Summary();
        return all?0:1;

        async Task<CaseResult> Case(string id,string request,TileMapState before,List<ChatMessage> history,bool recommend,int? seed)
        {
            string frozen=MapStateCodec.Serialize(before),current;
            using(GenerationContext.Enter(1,before))current=MapGenParams.BuildCurrentParamsText(korean);
            string prompt=template.Substring(0,start)+current+template.Substring(end);
            if(seed.HasValue)
            {
                string variation=RecommendationVariation.Build(seed.Value);
                Save(id+"-variation.txt",variation);prompt+=variation;
            }
            history.Add(new ChatMessage("user",request));
            Save(id+"-before.json",frozen);Save(id+"-prompt.txt",prompt);
            Save(id+"-request.txt",request);Save(id+"-history.json",Json(history));
            var result=new Dictionary<string,object>{{"id",id},{"request",request},{"called",false},{"ok",false},
                {"expectedAction",recommend?"recommend":"generate"},{"variationSeed",seed},
                {"featureValidator","Not run: native feature/material validation is unavailable in this linked offline harness."}};
            var returned=new CaseResult();bool received=false;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
                result["called"]=true;calls++;
                string reply=await client.SendChatAsync(history,prompt,timeout.Token);
                received=true;returned.Reply=reply;Save(id+"-response.json",reply);
                var command=ProviderResponse.Command(reply);string action=command.GetString("action");
                Save(id+"-command.json",SimpleJson.Serialize(command));result["action"]=action;
                if(action!=(recommend?"recommend":"generate"))throw new InvalidOperationException("Unexpected response action: "+action);
                if(recommend)
                {
                    var options=RecommendationPlan.Options(command);var observations=new List<object>();bool optionChecks=true;
                    result["optionCount"]=options.Count;
                    for(int i=0;i<options.Count;i++)
                    {
                        try
                        {
                            var single=new SimpleJsonObject();single.SetString("action","recommend");
                            single.SetObjectArray("options",new List<SimpleJsonObject>{options[i]});
                            var plan=RecommendationPlan.Validate(single,before,data=>{},korean).Single();
                            var after=plan.Resolve(before);MapStateValidation.Validate(after);
                            Save(id+"-option-"+(i+1)+"-state.json",MapStateCodec.Serialize(after));
                            Save(id+"-option-"+(i+1)+"-summary.txt",plan.Summary);
                            bool noBonus=NoGlobalBonuses(before,after);
                            bool featurePreference=!id.EndsWith("guided") ||
                                before.mutators.SequenceEqual(after.mutators) && before.removeMutators.SequenceEqual(after.removeMutators) && before.removeFeatureCategories.SequenceEqual(after.removeFeatureCategories);
                            optionChecks &= noBonus && featurePreference;
                            observations.Add(new {option=i+1,schemaValid=true,noGlobalBonuses=noBonus,
                                guidedFeaturePreferencePreserved=featurePreference,
                                usesCommonPath=UsesPath(after),persistentRelations=after.elevationShapes.Count(s=>s.anchor!=null),
                                hasWater=after.elevationShapes.Any(HasWater),nativePlacementVerified=false});
                        }
                        catch(Exception error)
                        {
                            optionChecks=false;observations.Add(new {option=i+1,schemaValid=false,errorType=error.GetType().Name,error=error.Message});
                        }
                    }
                    result["options"]=observations;
                    bool batchValid=false;
                    try{RecommendationPlan.Validate(command,before,data=>{},korean);batchValid=true;}
                    catch(Exception error){result["batchErrorType"]=error.GetType().Name;result["batchError"]=error.Message;}
                    result["batchValid"]=batchValid;
                    result["ok"]=batchValid && options.Count==3 && optionChecks;
                }
                else
                {
                    var after=MapStateEditor.Merge(before,MapParameterParser.Parse(command.GetObject("params")));
                    MapStateValidation.Validate(after);Save(id+"-after.json",MapStateCodec.Serialize(after));returned.State=after;
                    bool sameGlobal=SameGlobalSettings(before,after) && SimpleJson.Serialize(before.structures)==SimpleJson.Serialize(after.structures) &&
                        SimpleJson.Serialize(before.localRoads)==SimpleJson.Serialize(after.localRoads);
                    bool hasWater=after.elevationShapes.Any(HasWater),path=UsesPath(after);
                    bool intended=id.EndsWith("lakeside")?NoGlobalBonuses(before,after) && after.hills=="none" && after.hillAmount<=before.hillAmount && hasWater && !after.elevationShapes.Any(HasMountain) && !after.elevationShapes.Any(HasSoilFill):
                        id.EndsWith("valley")?sameGlobal && after.elevationShapes.Any(HasMountain) && HasDryFloor(after) && !after.elevationShapes.Any(HasSoilFill):
                        WaterOnlyEdgeMove(before,after);
                    result["schemaValid"]=true;result["intentCheck"]=intended;result["sameGlobalSettings"]=sameGlobal;
                    result["usesCommonPath"]=path;result["hasWater"]=hasWater;
                    result["persistentRelations"]=after.elevationShapes.Count(s=>s.anchor!=null);result["ok"]=intended;
                }
            }
            catch(Exception error)
            {
                result["errorType"]=error.GetType().Name;result["error"]=error.Message;
            }
            finally
            {
                bool untouched=frozen==MapStateCodec.Serialize(before);
                result["inputSnapshotUntouched"]=untouched;
                result["ok"]=(bool)result["ok"] && untouched;
                if(received)
                {
                    result["modelVersion"]=client.LastModelVersion;result["inputTokens"]=client.LastInputTokens;
                    result["outputTokens"]=client.LastOutputTokens;result["thinkingTokens"]=client.LastThinkingTokens;
                }
                all &= (bool)result["ok"];results.Add(result);Save(id+"-result.json",Json(result));Summary();
                Console.WriteLine(id+": ok="+result["ok"]+", snapshotUntouched="+untouched);
            }
            return returned;
        }
        void Save(string name,string value)=>File.WriteAllText(Path.Combine(output,name),value);
        void Summary()=>Save("result.json",Json(new {mode,ok=all,calls,plannedCalls,results,
            scope="Real first responses only; no repair/retry. "+
                (diversityOnly?"Three independent recommendations use the same empty authored state and captured native tile: two basic requests with fresh variation seeds, then the actual local-guide summary with another fresh seed. ":"Cases 1 through 5 have independent empty authored states on the same captured native tile. Case 6 uses the actual valid state/history from case 1. ")+
                "Recommendation states are proposals, never applied to the input snapshot. Native feature validation, physical placement and visual quality remain separate."}));
    }

    sealed class CaseResult { public TileMapState State;public string Reply; }
    static string Json(object value)=>System.Text.Json.JsonSerializer.Serialize(value,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
    static bool UsesPath(TileMapState state)=>state.elevationShapes.Any(s=>s.compositeShapes?.Any(p=>p.prim=="path")==true);
    static bool Water(string fill)=>fill!=null && (fill.StartsWith("Water",StringComparison.OrdinalIgnoreCase) || fill=="water");
    static bool HasWater(ElevationShape shape)=>Water(shape.fill) || shape.compositeOps?.Any(o=>Water(o.fill) || o.fill==null && o.e<0)==true;
    static bool HasSoilFill(ElevationShape shape)
    {
        bool Soil(string value)=>value!=null && (value.StartsWith("Soil",StringComparison.OrdinalIgnoreCase) || value=="soil" || value=="rich_soil");
        return Soil(shape.fill) || shape.compositeOps?.Any(o=>Soil(o.fill))==true;
    }
    static bool HasMountain(ElevationShape shape)=>shape.type=="landform" ||
        shape.type=="composite" && shape.compositeOps.Any(o=>o.e>=.1f && !Water(o.fill)) ||
        new[]{"ridge","split","radial","bump","noise","ring","slope"}.Contains(shape.type) && !HasWater(shape) && ElevationShape.ParseStrength(shape.strength)>0;
    static bool HasDryFloor(TileMapState state)=>state.elevationShapes.Any(s=>s.type=="landform" ||
        s.compositeOps?.Any(o=>o.e>0 && o.e<.1f && !Water(o.fill))==true);
    static bool NoGlobalBonuses(TileMapState before,TileMapState after)=>before.fertilityOffset==after.fertilityOffset &&
        before.vegetationDensity==after.vegetationDensity && before.animalDensity==after.animalDensity &&
        before.oreDensity==after.oreDensity && before.ruinDensity==after.ruinDensity && before.dangerDensity==after.dangerDensity;
    static bool SameGlobalSettings(TileMapState before,TileMapState after)
    {
        var copy=after.Clone();copy.elevationShapes=before.Clone().elevationShapes;copy.structures=before.Clone().structures;
        copy.localRoads=before.Clone().localRoads;
        return MapStateCodec.Serialize(before)==MapStateCodec.Serialize(copy);
    }
    static bool WaterOnlyEdgeMove(TileMapState before,TileMapState after)
    {
        if(!SameGlobalSettings(before,after) || SimpleJson.Serialize(before.structures)!=SimpleJson.Serialize(after.structures) ||
            SimpleJson.Serialize(before.localRoads)!=SimpleJson.Serialize(after.localRoads) || before.elevationShapes.Count!=after.elevationShapes.Count)return false;
        int changed=0;
        foreach(var old in before.elevationShapes)
        {
            var next=after.elevationShapes.SingleOrDefault(s=>s.id==old.id);if(next==null)return false;
            if(SimpleJson.Serialize(old)==SimpleJson.Serialize(next))continue;
            if(!HasWater(old) || !HasWater(next) || next.anchor==null || next.placement!="edge")return false;
            var source=after.elevationShapes.Find(s=>s.id==next.anchor);
            if(source==null || !HasDryFloor(new TileMapState{elevationShapes=new List<ElevationShape>{source}}))return false;
            var expected=next.Clone();expected.anchor=old.anchor;expected.placement=old.placement;expected.direction=old.direction;
            // A persistent placement edit must preserve the water's geometry, size, material and variant.
            if(SimpleJson.Serialize(expected)!=SimpleJson.Serialize(old))return false;
            changed++;
        }
        return changed==1;
    }
}
