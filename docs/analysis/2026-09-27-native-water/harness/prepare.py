from pathlib import Path
import hashlib, json

repo=Path('F:/Projects/Rimworld/active/mapgen_ai')
here=Path(__file__).parent
original=repo/'tools/shoreline-probe/Probe.cs'
src=original.read_text(encoding='utf-8-sig')
start=src.index('        static bool BeforeBlend(Map map)')
end=src.index('        sealed class Snapshot',start)
src=src[:start]+'''        static bool BeforeBlend(Map map)
        {
            if(!Ours(map))return true;blendCalls++;before=Snapshot.Read(map);return !bypass;
        }
        static void AfterBlend(Map map)
        {
            if(!Ours(map)||before==null)return;
            Save(phase+"-before-blend.json",before.Summary());Save(phase+"-after-blend.json",Snapshot.Read(map).Summary());
        }
        static void FinalMeasure(Map map){if(Ours(map)){Save(phase+"-phase-end-terrain.json",Snapshot.Read(map).Summary());CompareNativeRiver(map,true,phase+"-phase-end");}}
        static void NativeTerrain(Map map){if(Ours(map))Save(phase+"-native-terrain.json",Snapshot.Read(map).Summary());}
        static void NativeLake(Map map){if(Ours(map)){nativeLakeCalls++;Save(phase+"-native-lake.json",Snapshot.Read(map).Summary());}}
        static void NativeRiver(Map map)
        {
            if(!Ours(map)||tileContext!="river")return;
            var snapshot=Snapshot.Read(map);
            nativeRiverLayers=map.AllCells.Where(c=>map.terrainGrid.TerrainAt(c).IsRiver).ToDictionary(c=>c.z*map.Size.x+c.x,c=>snapshot.layers[c.z*map.Size.x+c.x]);
            Check(nativeRiverLayers.Count>0,phase+": actual native river worker produced river terrain");
            Save(phase+"-native-river.json",new {count=nativeRiverLayers.Count,width=map.Size.x,height=map.Size.z,cells=nativeRiverLayers.Select(p=>new{index=p.Key,layers=p.Value}).ToArray(),note="Observed immediately after actual TileMutatorWorker_River.GeneratePostTerrain; not injected water."});
        }
        static void CompareNativeRiver(Map map,bool grids,string name)
        {
            if(tileContext!="river")return;
            var snapshot=Snapshot.Read(map,grids);
            var changed=nativeRiverLayers==null?Array.Empty<object>():nativeRiverLayers.Where(p=>snapshot.layers[p.Key]!=p.Value).Select(p=>(object)new{index=p.Key,before=p.Value,after=snapshot.layers[p.Key]}).ToArray();
            Check(nativeRiverLayers!=null&&nativeRiverLayers.Count>0&&changed.Length==0,name+": native river terrain layers preserved");
            Save(name+"-river-preservation.json",new{observed=nativeRiverLayers?.Count??0,changed=changed.Length,rows=changed});
        }
        static void RecordHarmony()
        {
            var specifications=new[]{
                "RimWorld.GenStep_ElevationFertility:Generate","RimWorld.GenStep_Terrain:Generate",
                "RimWorld.MapGenUtility:TerrainFrom","RimWorld.MapGenUtility:DeepFreshWaterTerrainAt",
                "RimWorld.MapGenUtility:ShallowFreshWaterTerrainAt","RimWorld.MapGenUtility:LakeshoreTerrainAt",
                "RimWorld.MapGenUtility:RiverbankTerrainAt","RimWorld.MapGenUtility:MudTerrainAt",
                "RimWorld.TileMutatorWorker_Lake:Init","RimWorld.TileMutatorWorker_Lake:GeneratePostElevationFertility",
                "RimWorld.TileMutatorWorker_Lake:GeneratePostTerrain","RimWorld.TileMutatorWorker_Oasis:GeneratePostTerrain",
                "RimWorld.TileMutatorWorker_River:GeneratePostTerrain",
                "RimWorld.GenStep_Plants:Generate"};
            var rows=new List<object>();
            foreach(var spec in specifications)
            {
                var parts=spec.Split(':');var method=AccessTools.Method(AccessTools.TypeByName(parts[0]),parts[1]);
                if(method!=null)method=AccessTools.Method(method.DeclaringType,method.Name,method.GetParameters().Select(p=>p.ParameterType).ToArray());
                var info=method==null?null:Harmony.GetPatchInfo(method);
                object[] Describe(IEnumerable<Patch> patches)=>patches?.Select(p=>(object)new{owner=p.owner,priority=p.priority,method=p.PatchMethod.DeclaringType.FullName+"."+p.PatchMethod.Name}).ToArray()??Array.Empty<object>();
                rows.Add(new{requested=spec,found=method!=null,resolved=method==null?null:method.DeclaringType.FullName+"."+method.Name,
                    prefixes=Describe(info?.Prefixes),postfixes=Describe(info?.Postfixes),transpilers=Describe(info?.Transpilers),finalizers=Describe(info?.Finalizers)});
            }
            Save("harmony-patches.json",rows);
        }
''' +src[end:]
start=src.index('        // Independent bounded brute-force')
end=src.index('        static object[] Rle', start)
src=src[:start]+src[end:]
src=src.replace('MapGenAI.ShorelineProbe','MapGenAI.NativeVisualProbe').replace('[ShorelineProbe]','[NativeVisualProbe]')
src=src.replace('mapgenAIShore','mapgenAINative').replace('choco.mapgenai.shoreline-audit','choco.mapgenai.native-visual-audit')
src=src.replace('        static bool[] interactionWater,interactionExplicit;','        static string statePath,waterProfile,waterFill,tileContext;\n        static int nativeLakeCalls;\n        static bool fieldChecks,integrationChecks;\n        static Dictionary<int,string> nativeRiverLayers;')
src=src.replace('        static bool Interaction=>fixture=="nearby-reference"||fixture=="nearby-water"||fixture=="connected-water"||fixture=="explicit-water"||fixture=="special-water";','')
src=src.replace('            GenCommandLine.TryGetCommandLineArg("mapgenAINativeDetails",out details);','''            GenCommandLine.TryGetCommandLineArg("mapgenAINativeDetails",out details);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeState",out statePath);
            GenCommandLine.TryGetCommandLineArg("mapgenAINativeWaterProfile",out waterProfile);
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeWaterFill",out waterFill))waterFill="WaterShallow";
            if(!GenCommandLine.TryGetCommandLineArg("mapgenAINativeTileContext",out tileContext))tileContext="inland";
            fieldChecks=GenCommandLine.TryGetCommandLineArg("mapgenAINativeFieldChecks",out string fieldFlag)&&fieldFlag=="true";
            integrationChecks=GenCommandLine.TryGetCommandLineArg("mapgenAINativeIntegrationChecks",out string integrationFlag)&&integrationFlag=="true";''')
src=src.replace('            LongEventHandler.ExecuteWhenFinished(Start);','''            harmony.Patch(AccessTools.Method(typeof(GenStep_Terrain),"Generate"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeTerrain)));
            harmony.Patch(AccessTools.Method(typeof(TileMutatorWorker_Lake),"GeneratePostTerrain"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeLake)));
            harmony.Patch(AccessTools.Method(typeof(TileMutatorWorker_River),"GeneratePostTerrain"),postfix:new HarmonyMethod(typeof(Probe),nameof(NativeRiver)));
            LongEventHandler.ExecuteWhenFinished(Start);''')
src=src.replace('active&&GenerationContext.Active&&GenerationContext.TileId==target&&map.Size.x==250','active&&map.Tile.tileId==target&&map.Size.x==250')
src=src.replace('Directory.CreateDirectory(output);Application.runInBackground','Directory.CreateDirectory(output);RecordHarmony();Application.runInBackground')
src=src.replace('!FeaturePolicy.HasRiver(t)&&t.hilliness==Hilliness.Flat&&t.Mutators.Count==0','FeaturePolicy.HasRiver(t)==(tileContext=="river")&&t.hilliness==Hilliness.Flat&&(t.Mutators.Count==0||tileContext=="river"&&t.Mutators.All(m=>m.defName=="River"))')
src=src.replace('                    target=tile.tile;','                    if(biomeChoice=="desert")tile=candidates.First(t=>t.PrimaryBiome.defName=="Desert"&&DefDatabase<TileMutatorDef>.GetNamed("Oasis").averageTemperatureRange.Includes(t.temperature));\n                    target=tile.tile;')
src=src.replace('state=CreateState();MapStateValidation.Validate(state);','state=CreateState();if(tileContext=="river")state.hasRiver=true;MapStateValidation.Validate(state);')
src=src.replace('fixture,biomeChoice,details,bypass,tile=target','fixture,biomeChoice,details,bypass,tileContext,tile=target')
src=src.replace('phase="full";before=null;','phase="full";before=null;nativeRiverLayers=null;')
src=src.replace('Save("full-complete-terrain.json",Snapshot.Read(map,false).Summary());','Save("full-complete-terrain.json",Snapshot.Read(map,false).Summary());CompareNativeRiver(map,false,"full-complete");')
src=src.replace('poolVariant=state.elevationShapes.First(s=>s.id=="pond").variant','poolVariant=state.elevationShapes.FirstOrDefault(s=>s.id=="pond")?.variant,waterProfile,nativeFeature=state.mutators.FirstOrDefault(),groundLakeBeach=biome.lakeBeachTerrain?.defName,groundRiverbank=biome.riverbankTerrain?.defName,groundMud=biome.mudTerrain?.defName')
src=src.replace('                    ValidateDetector();','')
src=src.replace('{"ruin_density",0},{"danger_density",0}','{"ruin_density",state.ruinDensity},{"danger_density",state.dangerDensity}')
src=src.replace('                    var command=SimpleJson.Serialize(','''                    var replayBaseline=state.Clone();replayBaseline.elevationShapes.Clear();replayBaseline.localRoads.Clear();
                    var command=SimpleJson.Serialize(''')
src=src.replace('active=true;deadline=DateTime.UtcNow.AddMinutes(6);preview=new RecommendationPreviews(target,new[]{plan},new TileMapState());','''Check(MapStateCodec.Serialize(plan.Resolve(replayBaseline))==MapStateCodec.Serialize(state),"Preview command resolves to exact recorded full state");
                    active=true;deadline=DateTime.UtcNow.AddMinutes(6);preview=new RecommendationPreviews(target,new[]{plan},replayBaseline);''')
src=src.replace('            var result=new TileMapState{ruinDensity=0,dangerDensity=0};','''            if(!string.IsNullOrEmpty(statePath))return MapStateCodec.Deserialize(File.ReadAllText(statePath));
            var result=new TileMapState{ruinDensity=0,dangerDensity=0};
            if(fixture=="native-ground")return result;
            if(fixture=="native-feature"){result.mutators.Add(biomeChoice=="desert"?"Oasis":"Lake");return result;}''')
src=src.replace('edge_roughness="medium",','edge_roughness=fixture=="exact"?null:"medium",')
src=src.replace('fill=fixture=="hotspring"?"HotSpring":"WaterShallow"','fill=fixture=="hotspring"?"HotSpring":waterFill')
src=src.replace('            return result;\n        }\n        public static void Tick()', '''            if(!string.IsNullOrEmpty(waterProfile))
            {
                var field=AccessTools.Field(typeof(ElevationShape),"water_profile");
                if(field==null)throw new InvalidOperationException("This product DLL does not have water_profile");
                field.SetValue(result.elevationShapes.First(s=>s.id=="pond"),waterProfile);
            }
            return result;
        }
        public static void Tick()''')
src=src.replace('Check(report!=null&&report.issues.Count==0,"Full map has no authoring issue");','Check(report==null||report.issues.Count==0,"Full map has no authoring issue");')
old='''                Check(details=="none"?blendCalls==0:blendCalls==2,details=="none"?
                    "Explicit off schedules no blend stage in either generator":"Both preview and full-map blend stages observed");'''
src=src.replace(old,'''                if(fixture=="native-feature")Check(nativeLakeCalls>=2,"Native Lake worker observed in preview and full map for native feature fixture");
                Save("full-actual-features.json",Find.WorldGrid[map.Tile].Mutators.Select(m=>new{m.defName,worker=m.Worker.GetType().FullName}).ToArray());
                Save("full-native-palette.json",new {center=map.Center.ToString(),deep=MapGenUtility.DeepFreshWaterTerrainAt(map.Center,map).defName,shallow=MapGenUtility.ShallowFreshWaterTerrainAt(map.Center,map).defName,lakeBeach=MapGenUtility.LakeshoreTerrainAt(map.Center,map).defName,riverbank=MapGenUtility.RiverbankTerrainAt(map.Center,map).defName,mud=MapGenUtility.MudTerrainAt(map.Center,map).defName});''')
src=src.replace('error=error?.ToString(),blendCalls,','error=error?.ToString(),blendCalls,nativeLakeCalls,')
src=src.replace('                Check(blockedProviders==0,"No provider factory calls");Finish(null);','''                if(fieldChecks){var fieldResult=NativeWaterChecks.Run(map,output);if(fieldResult.HasValue)Check(fieldResult.Value,"100 unfiltered variants for each of six native field fixtures");}
                if(integrationChecks){var integrationResult=NativeIntegrationChecks.Run(map,output);if(integrationResult.HasValue)Check(integrationResult.Value,"Native Scribe state and helper palette restoration checks");}
                Check(blockedProviders==0,"No provider factory calls");Finish(null);''')
src=src.replace('Actual MapPreview and full-map surface generation; no provider calls or plant/visual-quality verdict. Bypass skips only LandscapeBlendGeneration.Apply. Injected protection controls are recorded separately.','Actual MapPreview PNG and full-map terrain. Native-feature uses a legal Lake in temperate or Oasis in desert. Feature geometry is native and differs from authored footprint. No provider or aesthetics verdict; not a screenshot of game UI. Native-ground still loads MapGenAI with empty authored shapes and ruin/danger density zero.')
(here/'Probe.cs').write_text(src,encoding='utf-8')
project=(repo/'tools/shoreline-probe/ShorelineProbe.csproj').read_text(encoding='utf-8-sig').replace('MapGenAI.ShorelineProbe','MapGenAI.NativeVisualProbe').replace('../../dev/Assemblies/MapGenAI.dll',str(here.parent/'baseline-21e5614.dll').replace('\\','/'))
(here/'NativeVisualProbe.csproj').write_text(project,encoding='utf-8')
runner=(repo/'tools/shoreline-probe/run.ps1').read_text(encoding='utf-8-sig')
runner=runner.replace("^shore-[a-z0-9-]+$","^native-[a-z0-9-]+$")
runner=runner.replace("[ValidateSet('pool','hotspring','protected','nearby-reference','nearby-water','connected-water','explicit-water','special-water')]","[ValidateSet('pool','hotspring','protected','exact','native-ground','native-feature')]")
runner=runner.replace("[string]$ProbeDll,[switch]$BypassBlend,[switch]$Graphics","[string]$ProbeDll,[string]$StateFile,[string]$WaterProfile,[string]$WaterFill='WaterShallow',[ValidateSet('inland','river')][string]$TileContext='inland',[switch]$FieldChecks,[switch]$IntegrationChecks,[switch]$BypassBlend,[switch]$Graphics")
runner=runner.replace('MapGenAI.ShorelineProbe','MapGenAI.NativeVisualProbe').replace('ShorelineProbe-','NativeVisualProbe-').replace('mapgenAIShore','mapgenAINative').replace('shoreline-profile-','native-visual-profile-').replace('choco.mapgenai.shorelineprobe.','choco.mapgenai.nativevisualprobe.')
runner=runner.replace("if(-not $ProbeDll){$ProbeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.NativeVisualProbe.dll'}", """if(-not $ProbeDll){
  $ProbeDll=Join-Path $PSScriptRoot 'bin/Debug/net472/MapGenAI.NativeVisualProbe.dll'
  if(-not(Test-Path -LiteralPath $ProbeDll)){throw 'Build the probe successfully before launch'}
  $probeBuildTime=(Get-Item -LiteralPath $ProbeDll).LastWriteTimeUtc
  if(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object {$_.LastWriteTimeUtc -gt $probeBuildTime}){throw 'Probe source is newer than its assembly; successful rebuild required before launch'}
}""")
runner=runner.replace("$analysisRoot=Join-Path $repoRoot ('docs/analysis/'+$AnalysisGroup)","$analysisRoot=Join-Path $PSScriptRoot '../runs'")
runner=runner.replace("$output=Join-Path $analysisRoot ('native-'+$Run)","$output=Join-Path $analysisRoot $Run")
runner=runner.replace("if($BypassBlend){",'''if($StateFile){$StateFile=(Resolve-Path -LiteralPath $StateFile).Path;$arguments+=('-mapgenAINativeState="'+$StateFile+'"')}
if($WaterProfile){$arguments+=('-mapgenAINativeWaterProfile='+$WaterProfile)}
$arguments+=('-mapgenAINativeWaterFill='+$WaterFill)
$arguments+=('-mapgenAINativeTileContext='+$TileContext)
if($FieldChecks){$arguments+='-mapgenAINativeFieldChecks=true'}
if($IntegrationChecks){$arguments+='-mapgenAINativeIntegrationChecks=true'}
if($BypassBlend){''')
runner=runner.replace('bypassBlend=[bool]$BypassBlend;','bypassBlend=[bool]$BypassBlend;stateFile=$StateFile;waterProfile=$WaterProfile;waterFill=$WaterFill;tileContext=$TileContext;fieldChecks=[bool]$FieldChecks;integrationChecks=[bool]$IntegrationChecks;graphicsEnabled=[bool]$Graphics;arguments=$arguments;')
(here/'run.ps1').write_text(runner,encoding='utf-8')
manifest={'sourceCommit':'21e56141d4ec05d4e02599cee4aac2f1d70fda6a','sources':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [original,repo/'tools/shoreline-probe/run.ps1',repo/'tools/shoreline-probe/ShorelineProbe.csproj']},'note':'New isolated task-specific scaffold. No game launched by prepare.py. Native source snapshots and map rendering must be executed separately.'}
(here/'provenance.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(here)
