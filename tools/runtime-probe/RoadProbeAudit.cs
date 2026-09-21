using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    // Reads actual terrain layers without depending on a new product bridge report.
    // The same probe can therefore measure the pre-bridge product as a control.
    static class RoadProbeAudit
    {
        sealed class Layers
        {
            public string[] surface, top, under, foundation, temp;
            public bool[] water, bridge, protectedFloor;
            public string At(int i)=>string.Join("/",new[]{surface[i],top[i],under[i],foundation[i],temp[i]}.Select(s=>s??"<null>"));
        }
        static Map lastMap;
        static Layers before;
        static float[] elevation;
        static string worldBefore, riversBefore;
        static Dictionary<string,object> immediate;
        static List<RoadPlacement> placed;
        static int faultAfter;
        static bool injectExisting;
        static Dictionary<string,object> existingFixture;
        static string faultTarget;
        static string[] faultOwners;
        [ThreadStatic] static Map paintingMap;
        [ThreadStatic] static int bridgeWrites;
        [ThreadStatic] static bool faultInjected;
        const string FaultOwner="choco.mapgenai.probe.road-bridge-fault";
        public static void Configure(Harmony h)
        {
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIRoadBridgeExisting",out var existing) && !bool.TryParse(existing,out injectExisting))
                throw new ArgumentException("mapgenAIRoadBridgeExisting must be true or false");
            h.Patch(AccessTools.Method(typeof(LocalRoadGeneration),nameof(LocalRoadGeneration.Apply)),prefix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(Before)),finalizer:new HarmonyMethod(typeof(RoadProbeAudit),nameof(After)));
            h.Patch(AccessTools.Method(typeof(LocalRoadGeneration),nameof(LocalRoadGeneration.Check)),postfix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(CaptureElevation)));
            if(GenCommandLine.TryGetCommandLineArg("mapgenAIRoadBridgeFaultAfter",out var count))
            {
                if(!int.TryParse(count,out faultAfter) || faultAfter<1)throw new ArgumentException("mapgenAIRoadBridgeFaultAfter must be a positive bridge-write count");
                // The new helper is optional so this probe still loads against the
                // original DLL. Its postfix covers both native SetFoundation and
                // the minimal preview's direct foundation-grid write.
                var bridgeType=typeof(LocalRoadGeneration).Assembly.GetType("MapGenAI.MapGen.RoadBridges");
                var bridgePlace=bridgeType==null?null:AccessTools.Method(bridgeType,"Place");
                var injector=new Harmony(FaultOwner);
                if(bridgePlace!=null)
                {
                    faultTarget=bridgePlace.DeclaringType.FullName+"."+bridgePlace.Name;
                    injector.Patch(bridgePlace,postfix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(AfterBridgePlace)){priority=Priority.Last});
                    faultOwners=Harmony.GetPatchInfo(bridgePlace)?.Owners.ToArray();
                }
                else
                {
                    faultTarget="Verse.TerrainGrid.SetTerrain";
                    var method=AccessTools.Method(typeof(TerrainGrid),nameof(TerrainGrid.SetTerrain),new[]{typeof(IntVec3),typeof(TerrainDef)});
                    injector.Patch(method,
                        prefix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(BeforeTerrainWrite)){priority=Priority.First},
                        postfix:new HarmonyMethod(typeof(RoadProbeAudit),nameof(AfterTerrainWrite)){priority=Priority.Last});
                    faultOwners=Harmony.GetPatchInfo(method)?.Owners.ToArray();
                }
            }
        }
        static void AfterBridgePlace(Map map,IntVec3 cell)
        {
            if(paintingMap!=map || faultInjected || before.bridge[cell.z*map.Size.x+cell.x] ||
                (map.terrainGrid.TerrainAt(cell)?.bridge!=true && map.terrainGrid.FoundationAt(cell)?.bridge!=true))return;
            CountBridgeWrite();
        }
        static void BeforeTerrainWrite(TerrainGrid __instance,IntVec3 c,TerrainDef newTerr,out bool __state)
        {
            __state=paintingMap!=null && !faultInjected && ReferenceEquals(__instance,paintingMap.terrainGrid) && newTerr?.bridge==true &&
                !before.bridge[c.z*paintingMap.Size.x+c.x];
        }
        static void AfterTerrainWrite(TerrainGrid __instance,IntVec3 c,bool __state)
        {
            if(!__state || faultInjected || (__instance.TerrainAt(c)?.bridge!=true && __instance.FoundationAt(c)?.bridge!=true))return;
            CountBridgeWrite();
        }
        static void CountBridgeWrite()
        {
            bridgeWrites++;
            if(bridgeWrites!=faultAfter)return;
            faultInjected=true;
            throw new InvalidOperationException("Intentional road bridge audit failure after "+bridgeWrites+" completed bridge writes");
        }
        static Layers Read(Map map)
        {
            int n=map.Size.x*map.Size.z;
            var result=new Layers{surface=new string[n],top=new string[n],under=new string[n],foundation=new string[n],temp=new string[n],water=new bool[n],bridge=new bool[n],protectedFloor=new bool[n]};
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*map.Size.x+cell.x;var t=map.terrainGrid.TerrainAt(cell);var f=map.terrainGrid.FoundationAt(cell);
                result.surface[i]=t?.defName;result.top[i]=map.terrainGrid.TopTerrainAt(cell)?.defName;
                result.under[i]=map.terrainGrid.UnderTerrainAt(cell)?.defName;result.foundation[i]=f?.defName;result.temp[i]=map.terrainGrid.TempTerrainAt(cell)?.defName;
                result.bridge[i]=t?.bridge==true || f?.bridge==true;result.water[i]=t?.IsWater==true || t?.IsRiver==true;
                result.protectedFloor[i]=result.bridge[i] || f!=null || t!=null && (t.HasTag("Road") || t.designationCategory!=null || t.costList?.Count>0 || t.costStuffCount>0);
            }
            return result;
        }
        static void CaptureElevation(Map map)
        {
            if(lastMap!=map)return;
            foreach(var cell in map.AllCells)elevation[cell.z*map.Size.x+cell.x]=MapGenerator.Elevation[cell];
        }
        static string WorldRoads(Map map)
        {
            var tile=map.TileInfo as SurfaceTile;
            return SimpleJson.Serialize(new Dictionary<string,object>{
                {"active",tile?.Roads?.Select(r=>((int)r.neighbor)+":"+r.road?.defName).OrderBy(s=>s).ToArray()},
                {"potential",tile?.potentialRoads?.Select(r=>((int)r.neighbor)+":"+r.road?.defName).OrderBy(s=>s).ToArray()}});
        }
        static string WorldRivers(Map map)
        {
            var tile=map.TileInfo as SurfaceTile;
            return SimpleJson.Serialize(new Dictionary<string,object>{
                {"active",tile?.Rivers?.Select(r=>((int)r.neighbor)+":"+r.river?.defName).OrderBy(s=>s).ToArray()},
                {"potential",tile?.potentialRivers?.Select(r=>((int)r.neighbor)+":"+r.river?.defName).OrderBy(s=>s).ToArray()}});
        }
        static void Before(Map map)
        {
            // Fixture writes precede both the measured baseline and fault scope.
            // They are test setup, not effects attributed to the road generator.
            paintingMap=null;lastMap=null;existingFixture=null;
            if(injectExisting)PrepareExistingFixture(map);
            lastMap=map;before=Read(map);elevation=new float[map.Size.x*map.Size.z];
            foreach(var c in map.AllCells)elevation[c.z*map.Size.x+c.x]=MapGenerator.Elevation[c];
            worldBefore=WorldRoads(map);riversBefore=WorldRivers(map);placed=null;immediate=null;
            bridgeWrites=0;faultInjected=false;paintingMap=map;
        }
        static void PrepareExistingFixture(Map map)
        {
            if(map.Size.x!=250 || map.Size.z!=250 || GenerationContext.State?.localRoads?.Any(r=>r.kind=="DirtPath")!=true)
                throw new InvalidOperationException("Existing-bridge audit fixture requires a 250x250 DirtPath road case");
            var bridge=DefDatabase<TerrainDef>.GetNamedSilentFail("Bridge");
            var floor=DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneSandstone");
            if(bridge==null || !bridge.bridge || !bridge.isFoundation || bridge.terrainAffordanceNeeded==null || floor==null || !floor.layerable)
                throw new InvalidOperationException("Existing-bridge audit fixture requires native Bridge and layerable FlagstoneSandstone definitions");
            var grid=map.terrainGrid;var selected=new List<IntVec3>();var originalWater=new List<object>();
            for(int z=124;z<=126;z++)for(int x=110;x<=140;x++)
            {
                var cell=new IntVec3(x,0,z);var terrain=grid.TerrainAt(cell);
                if(!(terrain.IsWater || terrain.IsRiver) || terrain.dangerous || !terrain.changeable ||
                    !terrain.affordances.Contains(bridge.terrainAffordanceNeeded) || grid.UnderTerrainAt(cell)!=null ||
                    grid.FoundationAt(cell)!=null || grid.TempTerrainAt(cell)!=null)continue;
                selected.Add(cell);originalWater.Add(new object[]{x,z,terrain.defName,grid.TopTerrainAt(cell)?.defName});
            }
            var floorCell=new IntVec3(50,0,125);var floorBase=grid.TopTerrainAt(floorCell);var floorSurface=grid.TerrainAt(floorCell);
            if(selected.Count==0 || floorSurface.IsWater || floorSurface.IsRiver || floorSurface.dangerous ||
                floorSurface.passability==Traversability.Impassable || MapGenerator.Elevation[floorCell]>=.7f ||
                grid.UnderTerrainAt(floorCell)!=null || grid.FoundationAt(floorCell)!=null || grid.TempTerrainAt(floorCell)!=null)
                throw new InvalidOperationException("Existing-bridge audit fixture has no eligible water or its fixed floor cell is not bare dry ground");
            string roads=WorldRoads(map),rivers=WorldRivers(map);bool preview=AuthoringGeneration.Current?.preview==true;
            if(preview)
            {
                var foundations=(TerrainDef[])AccessTools.Field(typeof(TerrainGrid),"foundationGrid").GetValue(grid);
                var under=(TerrainDef[])AccessTools.Field(typeof(TerrainGrid),"underGrid").GetValue(grid);
                foreach(var c in selected)foundations[map.cellIndices.CellToIndex(c)]=bridge;
                // Map Preview's SetTerrain shortcut omits native layering; explicitly
                // reproduce this bare-ground floor's native layerable storage.
                int index=map.cellIndices.CellToIndex(floorCell);under[index]=floorBase;
                grid.topGrid[index]=floor;grid.colorGrid[index]=null;
            }
            else
            {
                foreach(var c in selected)grid.SetFoundation(c,bridge);
                grid.SetTerrain(floorCell,floor);
            }
            if(selected.Any(c=>grid.FoundationAt(c)!=bridge) || grid.TerrainAt(floorCell)!=floor || grid.UnderTerrainAt(floorCell)!=floorBase)
                throw new InvalidOperationException("Existing-bridge audit fixture did not install the expected native terrain layers");
            existingFixture=new Dictionary<string,object>{
                {"enabled",true},{"origin","controlled test setup injected before the road baseline; not original map content"},
                {"preview",preview},{"bridgeBounds",new[]{110,124,140,126}},{"bridgeCellsInstalled",selected.Count},
                {"originalWaterColumns",new[]{"x","z","surface","top"}},{"originalWater",originalWater},
                {"floorCell",new[]{50,125}},{"floorSurface",grid.TerrainAt(floorCell).defName},{"floorUnder",grid.UnderTerrainAt(floorCell)?.defName},
                {"worldRoadsUnchangedByFixture",roads==WorldRoads(map)},{"worldRiversUnchangedByFixture",rivers==WorldRivers(map)}};
        }
        static bool WaterPreserved(Layers after,int i)=>after.under[i]==before.surface[i] ||
            after.foundation[i]!=null && after.bridge[i] && after.top[i]==before.surface[i];
        static void After(Map map,Exception __exception)
        {
            if(lastMap!=map || before==null){paintingMap=null;return;}
            try
            {
                var after=Read(map);var mask=GenerationContext.Regions(map).LocalRoadCells;
                placed=AuthoringGeneration.Current?.roads.ToList()??new List<RoadPlacement>();
                var footprint=new bool[before.surface.Length];
                foreach(var road in placed)foreach(var p in road.footprint)footprint[p[1]*map.Size.x+p[0]]=true;
                int changes=0,top=0,under=0,foundation=0,temp=0,all=0,outside=0,protectedChanges=0,bridgeChanges=0,height=0,newBridges=0,baseLost=0,waterLost=0,waterWithoutBridge=0,maskDifferences=0,protectedLayers=0;
                foreach(var c in map.AllCells)
                {
                    int i=c.z*map.Size.x+c.x;bool changed=before.At(i)!=after.At(i);
                    if(before.surface[i]!=after.surface[i])changes++;
                    if(before.top[i]!=after.top[i])top++;if(before.under[i]!=after.under[i])under++;if(before.foundation[i]!=after.foundation[i])foundation++;if(before.temp[i]!=after.temp[i])temp++;
                    if(changed){all++;if(!footprint[i])outside++;if(before.protectedFloor[i])protectedChanges++;if(before.bridge[i])bridgeChanges++;}
                    if(changed && (before.under[i]!=null || before.foundation[i]!=null || before.temp[i]!=null))protectedLayers++;
                    if(elevation[i]!=MapGenerator.Elevation[c])height++;
                    if(!before.bridge[i] && after.bridge[i])newBridges++;
                    if(!before.bridge[i] && after.bridge[i] && !WaterPreserved(after,i))baseLost++;
                    if(before.water[i] && changed && !after.bridge[i])waterWithoutBridge++;
                    if(before.water[i] && after.bridge[i] && !WaterPreserved(after,i))waterLost++;
                    if(mask[i]!=footprint[i])maskDifferences++;
                }
                immediate=new Dictionary<string,object>{
                    {"existingFixture",existingFixture},
                    {"changedCells",changes},{"rawTopChanges",top},{"underTerrainChanges",under},{"foundationChanges",foundation},{"tempTerrainChanges",temp},{"anyLayerChanges",all},
                    {"outsideFootprintChanges",outside},{"footprintMaskDifferences",maskDifferences},{"elevationChanges",height},
                    {"worldRoadsUnchanged",worldBefore==WorldRoads(map)},{"worldRoadLinks",worldBefore},{"worldRiversUnchanged",riversBefore==WorldRivers(map)},{"worldRiverLinks",riversBefore},
                    {"protectedFloorChanges",protectedChanges},{"protectedFloorCells",before.protectedFloor.Count(p=>p)},{"existingBridgeChanges",bridgeChanges},{"existingBridgeCells",before.bridge.Count(p=>p)},
                    {"newBridgeCells",newBridges},{"newBridgesMissingOriginalBase",baseLost},{"bridgedWaterMissingOriginalWater",waterLost},{"waterChangedWithoutBridge",waterWithoutBridge},{"protectedLayerChanges",protectedLayers},
                    {"applyFailed",__exception!=null},{"exception",__exception==null?null:__exception.GetType().FullName+": "+__exception.Message},{"failureLayersUnchanged",__exception==null?(object)null:all==0},
                    {"faultRequestedAfter",faultAfter},{"faultInjected",faultInjected},{"completedBridgeWritesBeforeFault",bridgeWrites},{"faultPatchOwner",faultAfter>0?FaultOwner:null},{"faultPatchTarget",faultTarget},{"faultTargetPatchOwners",faultOwners},
                    {"beforeLayerHash",LayerHash(before)},{"afterLayerHash",LayerHash(after)},{"footprintLayerHash",LayerHash(after,footprint)},
                    {"bridgeLayers",BridgeLayers(map,after,footprint,true)}};
            }
            finally{paintingMap=null;}
        }
        // Preserve per-bridge layer evidence; whole-map layers are represented by hashes.
        static object BridgeLayers(Map map,Layers after,bool[] mask,bool allBridges=false)
        {
            var cells=new List<object>();
            for(int i=0;i<mask.Length;i++)if((allBridges || mask[i]) && (before.bridge[i] || after.bridge[i]))
                cells.Add(new object[]{i%map.Size.x,i/map.Size.x,before.surface[i],before.top[i],before.under[i],before.foundation[i],after.surface[i],after.top[i],after.under[i],after.foundation[i]});
            return new Dictionary<string,object>{{"columns",new[]{"x","z","beforeSurface","beforeTop","beforeUnder","beforeFoundation","surface","top","under","foundation"}},{"cells",cells},{"hash",Hash(SimpleJson.Serialize(cells))}};
        }
        public static object Measure(Map map)
        {
            if(lastMap!=map || immediate==null)return null;
            var after=Read(map);var roads=new List<object>();int w=map.Size.x,h=map.Size.z;
            var passable=new bool[w*h];var actualWalkable=new bool[w*h];
            foreach(var cell in map.AllCells)
            {
                int i=cell.z*w+cell.x;var t=map.terrainGrid.TerrainAt(cell);actualWalkable[i]=cell.Walkable(map);
                passable[i]=actualWalkable[i] && t.passability!=Traversability.Impassable && !t.dangerous &&
                    (!(t.IsWater || t.IsRiver) || after.bridge[i]) && elevation[i]<.7f;
            }
            foreach(var road in placed)
            {
                var path=road.path;var footprint=new bool[w*h];foreach(var p in road.footprint)footprint[p[1]*w+p[0]]=true;
                var allowed=Enumerable.Range(0,footprint.Length).Select(i=>footprint[i] && passable[i]).ToArray();
                int start=path[0][1]*w+path[0][0],end=path.Last()[1]*w+path.Last()[0];var seen=Flood(allowed,w,h,start);
                int blocked=path.Count(p=>!passable[p[1]*w+p[0]]);var terrain=road.footprint.Select(p=>after.surface[p[1]*w+p[0]]).ToArray();
                var bridgeCells=Enumerable.Range(0,footprint.Length).Where(i=>footprint[i] && after.bridge[i]).ToArray();
                var crossings=Crossings(path,allowed,after,w,h);
                roads.Add(new Dictionary<string,object>{
                    {"id",road.id},{"kind",road.kind},{"path",path},{"paintedCells",road.paintedCells},{"protectedCells",road.protectedCells},{"blockedPathCells",blocked},
                    {"unwalkablePathCells",path.Count(p=>!actualWalkable[p[1]*w+p[0]])},{"connectedWithinFootprint",seen[end]},
                    {"bridgeCells",bridgeCells.Length},{"bridgeUnwalkableCells",bridgeCells.Count(i=>!actualWalkable[i])},
                    {"bridgedWaterCells",bridgeCells.Count(i=>before.water[i])},{"bridgedWaterMissingOriginalWater",bridgeCells.Count(i=>before.water[i] && !WaterPreserved(after,i))},
                    {"bridgeWaterUnderCells",bridgeCells.Count(i=>before.water[i] && after.under[i]==before.surface[i])},
                    {"bridgeWaterFoundationBaseCells",bridgeCells.Count(i=>before.water[i] && after.foundation[i]!=null && after.top[i]==before.surface[i])},
                    {"crossings",crossings},{"pathHash",Hash(SimpleJson.Serialize(path))},{"terrainHash",Hash(string.Join("|",terrain))},{"layerHash",LayerHash(after,footprint)},
                    {"bridgeLayers",BridgeLayers(map,after,footprint)},{"terrains",terrain.GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count())}});
            }
            return new Dictionary<string,object>{{"immediate",immediate},{"worldRoadsStillUnchanged",worldBefore==WorldRoads(map)},
                {"worldRiversStillUnchanged",riversBefore==WorldRivers(map)},{"finalLayerHash",LayerHash(after)},{"roads",roads}};
        }
        static List<object> Crossings(List<int[]> path,bool[] allowed,Layers after,int w,int h)
        {
            var result=new List<object>();var indices=path.Select(p=>p[1]*w+p[0]).ToArray();
            // Measurement correction: a pre-existing bridge hides its water surface.
            // Treat original water AND existing bridge cells as one crossing span,
            // so an old bridge in the middle is not mistaken for a dry riverbank.
            // Keep before.water unchanged for all preservation/rollback measurements.
            var span=Enumerable.Range(0,allowed.Length).Select(i=>before.water[i] || before.bridge[i]).ToArray();
            var dry=Enumerable.Range(0,allowed.Length).Select(i=>allowed[i] && !span[i] && !after.bridge[i]).ToArray();
            for(int n=0;n<indices.Length;n++)
            {
                if(!span[indices[n]])continue;int first=n;
                while(n+1<indices.Length && span[indices[n+1]])n++;int last=n;
                int bankA=first>0?indices[first-1]:-1,bankB=last+1<indices.Length?indices[last+1]:-1;
                bool banks=bankA>=0 && bankB>=0 && dry[bankA] && dry[bankB];
                bool allBridge=Enumerable.Range(first,last-first+1).All(k=>after.bridge[indices[k]] && allowed[indices[k]]);
                // Unbridged original water cannot make a ford count as a bridge crossing.
                var crossing=Enumerable.Range(0,allowed.Length).Select(i=>allowed[i] && (!span[i] || after.bridge[i])).ToArray();
                bool connected=banks && allBridge && Flood(crossing,w,h,bankA)[bankB];
                result.Add(new Dictionary<string,object>{{"firstPathIndex",first},{"lastPathIndex",last},{"crossingPathCells",last-first+1},
                    {"waterPathCells",Enumerable.Range(first,last-first+1).Count(k=>before.water[indices[k]])},
                    {"existingBridgePathCells",Enumerable.Range(first,last-first+1).Count(k=>before.bridge[indices[k]])},
                    {"bankA",bankA<0?null:new[]{bankA%w,bankA/w}},{"bankB",bankB<0?null:new[]{bankB%w,bankB/w}},
                    {"dryBanksPresent",banks},{"entireWaterPathBridged",allBridge},{"roadBridgeRoadConnected",connected},
                    {"separateDryBanksWithinFootprint",banks && !Flood(dry,w,h,bankA)[bankB]}});
            }
            return result;
        }
        static bool[] Flood(bool[] allowed,int w,int h,int start)
        {
            var seen=new bool[allowed.Length];var queue=new Queue<int>();
            if(start>=0 && start<allowed.Length && allowed[start]){seen[start]=true;queue.Enqueue(start);}
            while(queue.Count>0)
            {
                int i=queue.Dequeue(),x=i%w,z=i/w;
                foreach(int n in new[]{x>0?i-1:-1,x+1<w?i+1:-1,z>0?i-w:-1,z+1<h?i+w:-1})
                    if(n>=0 && allowed[n] && !seen[n]){seen[n]=true;queue.Enqueue(n);}
            }
            return seen;
        }
        static string LayerHash(Layers layers,bool[] mask=null)=>Hash(string.Join("|",Enumerable.Range(0,layers.surface.Length).Where(i=>mask==null || mask[i]).Select(i=>i+":"+layers.At(i))));
        static string Hash(string text){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();}
    }
}
