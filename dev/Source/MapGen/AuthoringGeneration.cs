using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MapGenAI.MapGen
{
    public static class AuthoringGeneration
    {
        static readonly ConcurrentDictionary<int,AuthoringResult> Results = new ConcurrentDictionary<int,AuthoringResult>();
        // Accessed only while the generation scope holds the shared generator lock.
        static AuthoringResult working { get => GenerationContext.Report; set => GenerationContext.Report=value; }
        public static AuthoringResult Current => working;
        public static AuthoringResult Latest(int tile, TileMapState state)
        {
            return Results.TryGetValue(tile,out var r) && r.state == Fingerprint(state) ? r : null;
        }
        static string Fingerprint(TileMapState state)
        {
            var copy=state?.Clone(); if(copy!=null && !ImageInput.ImageFeatureGate.Enabled)copy.imageMap=null;
            return MapStateCodec.Serialize(copy);
        }
        public static void Begin(bool preview) { working=new AuthoringResult { state=Fingerprint(GenerationContext.State),preview=preview }; }
        public static void Finish(int tile)
        {
            if(working==null)return;
            if(Results.Count>128)Results.Clear();
            Results[tile]=working;working=null;
        }
        public static void Fail(Exception error)
        {
            working?.issues.Add(error.Message); Log.Warning("[MapGenAI] Authored generation: " + error.Message);
        }
        public static void ApplyTerrain(Map map)
        {
            var regions=GenerationContext.Regions(map);if(regions==null)return;
            var definitions=new Dictionary<string,TerrainDef>();
            foreach(var name in regions.Materials.Where(n=>n!=null).Distinct()) definitions[name]=TerrainMaterials.Resolve(name);
            var removed=new List<IntVec3>();
            using(map.pathing.DisableIncrementalScope())
            foreach(var cell in map.AllCells)
            {
                var name=regions.Materials[regions.Index(cell)];if(name==null)continue;
                var before=map.terrainGrid.TerrainAt(cell);
                // Never paint across world river/ocean connections or roads after their workers run.
                if(before.IsRiver || before.HasTag("Road") || before.defName.IndexOf("Ocean",StringComparison.OrdinalIgnoreCase)>=0)
                {if(working!=null)working.protectedCells++;continue;}
                var def=definitions[name];
                if(!def.supportsRock || regions.Flatten[regions.Index(cell)])
                    MapGenerator.Elevation[cell]=Math.Min(MapGenerator.Elevation[cell],.3f);
                var rock=cell.GetEdifice(map);
                if(rock!=null && rock.def.building.isNaturalRock && (!def.supportsRock || regions.Flatten[regions.Index(cell)]))
                {rock.Destroy(); removed.Add(cell); map.roofGrid.SetRoof(cell,null);}
                map.terrainGrid.SetTerrain(cell,def);
                if(working!=null)working.terrainCells++;
            }
            RoofCollapseCellsFinder.RemoveBulkCollapsingRoofs(removed,map);
        }
        public static void ApplyCoverage(Map map)
        {
            var regions=GenerationContext.Regions(map);if(regions==null)return;
            // Plan the whole coverage batch before changing any of its cells.
            var jobs=new List<Tuple<ElevationShape,TerrainDef,bool[]>>();
            var planned=new Dictionary<int,TerrainDef>();
            var restore=new Dictionary<int,CoverageCell>();
            // A later native structure may consume painted ground. Recount after generation, restoring
            // only our still-unmodified paint; native floors/buildings and occupied cells remain intact.
            foreach(var entry in regions.CoverageOriginal)
            {
                var cell=new IntVec3(entry.Key%map.Size.x,0,entry.Key/map.Size.x);var old=entry.Value;
                if(map.terrainGrid.TerrainAt(cell)==old.painted && MapGenerator.Elevation[cell]==old.paintedElevation && cell.GetEdifice(map)==null && !cell.GetThingList(map).Any(t=>t is Pawn))
                {restore[entry.Key]=old;planned[entry.Key]=old.original;}
            }
            var stats=new List<CoverageResult>();
            foreach(var shape in GenerationContext.State.elevationShapes.Where(s=>s.type=="region_fill"))
            {
                var area=regions.Mask(shape.region);
                if(shape.region_part=="enclosed")area=RegionCoverage.Enclosed(map.Size.x,map.Size.z,area);
                var def=TerrainMaterials.Resolve(shape.fill);var existing=new bool[area.Length];
                foreach(var cell in map.AllCells)
                {
                    int i=regions.Index(cell);if(!area[i])continue;
                    var terrain=planned.TryGetValue(i,out var p)?p:map.terrainGrid.TerrainAt(cell);
                    // The percentage is of usable cells, after slopes and world connections are generated.
                    bool protectedCell=!TerrainMaterials.Supported(terrain);
                    area[i]=!protectedCell && cell.GetEdifice(map)==null && !cell.GetThingList(map).Any(t=>t is Pawn) && !(MapGenerator.Elevation[cell]>=.7f && MapGenerator.Caves[cell]<=0);
                    existing[i]=area[i] && terrain==def;
                }
                float fraction=float.Parse(shape.coverage,System.Globalization.CultureInfo.InvariantCulture);
                var selected=RegionCoverage.Select(map.Size.x,map.Size.z,area,existing,fraction,shape.direction);
                for(int i=0;i<selected.Length;i++)if(selected[i])planned[i]=def;
                jobs.Add(Tuple.Create(shape,def,selected));
                stats.Add(new CoverageResult{id=shape.id,eligible=area.Count(b=>b),selected=selected.Count(b=>b),existing=existing.Count(b=>b)});
            }
            using(map.pathing.DisableIncrementalScope())
            {
            foreach(var entry in restore)
            {
                var cell=new IntVec3(entry.Key%map.Size.x,0,entry.Key/map.Size.x);
                map.terrainGrid.SetTerrain(cell,entry.Value.original);MapGenerator.Elevation[cell]=entry.Value.originalElevation;
            }
            regions.CoverageOriginal.Clear();
            foreach(var job in jobs)
            {
                regions.SetMask(job.Item1.id,job.Item3);
                foreach(var cell in map.AllCells)if(job.Item3[regions.Index(cell)])
                {
                    int i=regions.Index(cell);
                    if(!regions.CoverageOriginal.TryGetValue(i,out var saved))regions.CoverageOriginal[i]=saved=new CoverageCell{original=map.terrainGrid.TerrainAt(cell),originalElevation=MapGenerator.Elevation[cell]};
                    map.terrainGrid.SetTerrain(cell,job.Item2);
                    if(!job.Item2.supportsRock)MapGenerator.Elevation[cell]=Math.Min(MapGenerator.Elevation[cell],.3f);
                    saved.painted=job.Item2;saved.paintedElevation=MapGenerator.Elevation[cell];
                    if(working!=null)working.terrainCells++;
                }
            }
            }
            if(working!=null){working.coverage.Clear();working.coverage.AddRange(stats);}
        }
        public static void PlaceStructures(Map map)
        {
            if(working?.issues.Count>0)return;
            var plans=GenerationContext.State?.structures;
            if(plans==null || plans.Count==0)return;
            int cols=map.Size.x,rows=map.Size.z;
            var occupied=new bool[cols*rows];
            var jobs=new List<Tuple<StructurePlan,PlannedRect>>();
            var regions=GenerationContext.Regions(map);
            var distances=new Dictionary<string,SpatialDistance>();
            var used=MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
            foreach(var r in used)
                foreach(var cell in r.ExpandedBy(1).ClipInsideMap(map))occupied[cell.z*cols+cell.x]=true;
            // Keep planned ruins off the complete access route, without painting the open ground.
            // Native generators retain their existing UsedRects behavior.
            foreach(var passage in GenerationContext.State.elevationShapes.Where(s=>s.type=="passage" && s.scope=="mountains"))
            {
                var route=PassageGeometry.Mask(cols,rows,passage.points,passage.width);
                for(int i=0;i<occupied.Length;i++)occupied[i]|=route[i];
            }
            foreach(var p in plans)
            {
                var allowed=new bool[cols*rows];double sumX=0,sumZ=0;int area=0;
                var region=p.region==null?null:regions.Mask(p.region,p.region_part);
                foreach(var cell in map.AllCells)
                {
                    float x=(cell.x+.5f)/cols,z=(cell.z+.5f)/rows;
                    bool inside=region==null || region[cell.z*cols+cell.x];
                    if(p.bounds!=null)inside &= x>=p.bounds[0] && z>=p.bounds[1] && x<=p.bounds[2] && z<=p.bounds[3];
                    // A point request has a bounded neighborhood; it never relocates to a distant open field.
                    if(p.region==null && p.bounds==null && p.position!=null)
                        inside &= Math.Abs(x-p.position[0])<=.10f && Math.Abs(z-p.position[1])<=.10f;
                    if(!inside)continue;
                    sumX+=cell.x;sumZ+=cell.z;area++;
                    var terrain=map.terrainGrid.TerrainAt(cell);
                    bool solidRock=MapGenerator.Elevation[cell]>.7f && MapGenerator.Caves[cell]<=0f;
                    allowed[cell.z*cols+cell.x]=!solidRock && !terrain.dangerous && !terrain.IsWater && !terrain.IsRiver && !terrain.HasTag("Road")
                        && cell.GetEdifice(map)==null && GenConstruct.CanBuildOnTerrain(ThingDefOf.Wall,cell,map,Rot4.North);
                }
                float targetX=p.position==null?(float)(sumX/Math.Max(1,area)):p.position[0]*cols;
                float targetZ=p.position==null?(float)(sumZ/Math.Max(1,area)):p.position[1]*rows;
                Func<PlannedRect,bool> relation=null;
                if(p.relation!=null)
                {
                    string key=p.relation.target+":"+(p.relation.target=="region_edge"?p.region+":"+p.region_part:"");
                    if(!distances.TryGetValue(key,out var distance))
                    {
                        var target=new bool[cols*rows];
                        foreach(var cell in map.AllCells)
                        {
                            var terrain=map.terrainGrid.TerrainAtIgnoreTemp(map.cellIndices.CellToIndex(cell));
                            target[cell.z*cols+cell.x]=p.relation.target=="river"?terrain.IsRiver:p.relation.target=="water"?terrain.IsWater:
                                p.relation.target=="mountain"?(cell.GetEdifice(map)?.def.building.isNaturalRock==true || MapGenerator.Elevation[cell]>.7f && MapGenerator.Caves[cell]<=0):region[cell.z*cols+cell.x];
                        }
                        if(p.relation.target=="region_edge")target=SpatialDistance.InteriorEdge(cols,rows,target);
                        distances[key]=distance=new SpatialDistance(cols,rows,target);
                    }
                    relation=distance.Constrain(allowed,p.relation);
                }
                bool Constraint(PlannedRect r)=> (relation==null || relation(r)) && jobs.All(j=>Separated(r,j.Item2,Math.Max(p.spacing,j.Item1.spacing)));
                int width=p.rotation%180==0?p.width:p.height,height=p.rotation%180==0?p.height:p.width;
                var positions=PlacementPlanner.Find(cols,rows,allowed,occupied,width,height,p.count,targetX,targetZ,p.spacing,Constraint);
                if(positions==null)throw new InvalidOperationException("구조물 배치 실패 / Structure placement failed ["+p.id+"]: 지정 영역에 전체 크기 "+p.width+"×"+p.height+", "+p.count+"개를 놓을 안전한 공간이 없습니다. 영역 확대·크기/개수 축소·평탄화를 요청하세요. / Expand the region, reduce size/count or flatten it. No positioned structures were spawned.");
                jobs.AddRange(positions.Select(r=>Tuple.Create(p,r)));
            }
            // All plans are feasible before any positioned structure is spawned.
            var ordinals=new Dictionary<string,int>();
            foreach(var job in jobs)
            {
                var r=job.Item2;
                ordinals.TryGetValue(job.Item1.id,out int ordinal);ordinals[job.Item1.id]=ordinal+1;
                var result=job.Item1.kind=="ancient_danger"?AncientDangerGeneration.Generate(map,job.Item1,r,working?.preview==true):SpawnRuin(map,job.Item1,r,ordinal);
                working?.placements.Add(result);
                used.Add(new CellRect(r.x,r.z,r.width,r.height));
            }
        }
        static bool Separated(PlannedRect a,PlannedRect b,int gap)=>a.x>=b.x+b.width+gap || b.x>=a.x+a.width+gap || a.z>=b.z+b.height+gap || b.z>=a.z+a.height+gap;
        static StructurePlacement SpawnRuin(Map map,StructurePlan plan,PlannedRect r,int ordinal)
        {
            var result=new StructurePlacement{id=plan.id,rect=r};
            var stuff=ThingDef.Named("BlocksGranite");
            var floor=TerrainDef.Named("TileGranite");
            uint seed=2166136261;foreach(char c in plan.id)seed=unchecked((seed^c)*16777619);seed^=unchecked((uint)plan.seed);
            seed=unchecked(seed+(uint)ordinal*2654435761);
            for(int z=0;z<plan.height;z++)for(int x=0;x<plan.width;x++)
            {
                int rx=x,rz=z;
                if(plan.rotation==90){rx=plan.height-1-z;rz=x;}
                else if(plan.rotation==180){rx=plan.width-1-x;rz=plan.height-1-z;}
                else if(plan.rotation==270){rx=z;rz=plan.width-1-x;}
                var cell=new IntVec3(r.x+rx,0,r.z+rz);
                uint h=unchecked((seed^(uint)(x*374761393)^(uint)(z*668265263))*1274126177);
                bool edge=x==0 || z==0 || x==plan.width-1 || z==plan.height-1;
                bool doorway=(x==plan.width/2 && (z==0 || z==plan.height-1));
                if(!edge && h%7!=0){map.terrainGrid.SetTerrain(cell,floor);result.floors++;}
                if(edge && !doorway && h%5!=0)
                {
                    result.wallCells.Add(new[]{cell.x,cell.z});result.walls++;
                    if(working?.preview!=true)
                    {
                        var wall=ThingMaker.MakeThing(ThingDefOf.Wall,stuff);
                        wall.HitPoints=Math.Max(1,(int)(wall.MaxHitPoints*(.3f+(h%50)/100f)));
                        GenSpawn.Spawn(wall,cell,map);
                        if(!wall.Spawned || cell.GetEdifice(map)!=wall)throw new InvalidOperationException("Failed to spawn positioned ruin wall: "+plan.id);
                        result.spawnedWalls++;
                    }
                }
            }
            return result;
        }
    }
}
