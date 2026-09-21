using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.BaseGen;
using Verse;

namespace MapGenAI.MapGen
{
    // Uses native terrain definitions/probability profiles, never native bulldozing or world links.
    public static class LocalRoadGeneration
    {
        sealed class Job
        {
            public RoadPlan plan;
            public RoadPlacement result;
            public readonly Dictionary<int,TerrainDef> paint=new Dictionary<int,TerrainDef>();
            public bool[] footprint;
        }
        public static void Apply(Map map)
        {
            var plans=GenerationContext.State?.localRoads;
            if(plans==null || plans.Count==0)return;
            if(AuthoringGeneration.Current!=null)AuthoringGeneration.Current.nativeRoadPreviewLimited=
                (map.TileInfo as RimWorld.Planet.SurfaceTile)?.Roads?.Count>0;
            int cols=map.Size.x,rows=map.Size.z;var ground=new bool[cols*rows];
            foreach(var cell in map.AllCells)
            {
                var terrain=map.terrainGrid.TerrainAt(cell);
                // Map Preview omits rocks and other Things. Elevation/terrain are the shared input.
                ground[cell.z*cols+cell.x]=!terrain.IsWater && !terrain.IsRiver && !terrain.dangerous
                    && (!terrain.isFoundation || terrain.bridge) && terrain.passability!=Traversability.Impassable && MapGenerator.Elevation[cell]<.7f;
            }
            var jobs=new List<Job>();
            foreach(var plan in plans)
            {
                var def=DefDatabase<RoadDef>.GetNamedSilentFail(plan.kind)??throw new InvalidOperationException("Unavailable road type: "+plan.kind);
                var layers=def.roadGenSteps.OfType<RoadDefGenStep_Place>().Where(s=>s.place is TerrainDef t && !t.bridge && !t.isFoundation).ToList();
                if(layers.Count==0 || layers.Any(s=>s.periodicSpacing!=0 || s.chancePerPositionCurve==null))
                    throw new InvalidOperationException("Unsupported terrain profile for road: "+plan.kind);
                float radius=layers.Max(s=>s.chancePerPositionCurve.Points.Max(p=>p.x)+Math.Abs(s.antialiasingMultiplier)*.5f);
                if(radius<=0 || radius>12)throw new InvalidOperationException("Unsupported road profile width");
                var bridgeLayer=def.roadGenSteps.OfType<RoadDefGenStep_Place>().FirstOrDefault(s=>s.place is TerrainDef t && t.bridge && t.isFoundation);
                var bridge=bridgeLayer?.place as TerrainDef;
                if(bridgeLayer!=null && (bridgeLayer.chancePerPositionCurve==null || bridgeLayer.periodicSpacing!=0 || bridgeLayer.antialiasingMultiplier!=0 || bridgeLayer.chancePerPositionCurve.Evaluate(0)<1))
                    throw new InvalidOperationException("Unsupported bridge profile for road: "+plan.kind);
                float bridgeRadius=bridgeLayer==null?0:bridgeLayer.chancePerPositionCurve.Points.Max(p=>p.x);
                if(bridgeRadius>12)throw new InvalidOperationException("Unsupported road bridge width");
                radius=Math.Max(radius,bridgeRadius);
                var bridgeable=new bool[ground.Length];
                foreach(var cell in map.AllCells)bridgeable[cell.z*cols+cell.x]=MapGenerator.Elevation[cell]<.7f && RoadBridges.Supports(map,cell,bridge);
                List<int> path;
                try{path=RoadRouting.PlanWithBridges(cols,rows,ground,bridgeable,plan.points,plan.route,radius);}
                catch(Exception e){throw new InvalidOperationException(RoadPlans.Label(plan.kind,true)+" / "+RoadPlans.Label(plan.kind,false)+": "+e.Message,e);}
                var distance=Distances(cols,rows,path,radius,bridgeable,out var bridgeOrigins);
                var job=new Job{plan=plan,result=new RoadPlacement{id=plan.id,kind=plan.kind,length=path.Count},footprint=new bool[ground.Length]};
                foreach(int i in path)job.result.path.Add(new[]{i%cols,i/cols});
                int seed=Gen.HashCombineInt(Gen.HashCombineInt(GenText.StableStringHash(plan.id),plan.seed),(int)map.Tile);
                TerrainDef stone=null;
                if(plan.kind=="StoneRoad")
                {
                    Rand.PushState(seed);
                    try{stone=BaseGenUtility.RegionalRockTerrainDef(map.Tile,false);}
                    finally{Rand.PopState();}
                }
                for(int i=0;i<distance.Length;i++)
                {
                    if(distance[i]>radius)continue;
                    var cell=new IntVec3(i%cols,0,i/cols);
                    var before=map.terrainGrid.TerrainAt(cell);
                    if((!ground[i] && !bridgeable[i]) || cell.GetEdifice(map)!=null || map.terrainGrid.TempTerrainAt(cell)!=null || cell.GetThingList(map).Any(t=>t is Pawn || t is Blueprint || t is Frame))
                        throw new InvalidOperationException("도로 공간에 기존 장애물이 있습니다. 위치를 조정하세요. / Existing obstacles occupy the road footprint; adjust its route. No local roads were painted.");
                    if(bridgeable[i])
                    {
                        // Wet shoulders remain water. Use the road's native solid
                        // bridge width only where the centerline also crosses water.
                        if(!bridgeOrigins[i] || bridgeLayer.chancePerPositionCurve.Evaluate(distance[i])<1f)continue;
                        job.paint[i]=bridge;
                        job.footprint[i]=true;job.result.footprint.Add(new[]{i%cols,i/cols});
                        continue;
                    }
                    job.footprint[i]=true;job.result.footprint.Add(new[]{i%cols,i/cols});
                    // Preserve existing native roads/floors, including stone roads without a Road tag.
                    if(before.bridge || map.terrainGrid.FoundationAt(cell)!=null || before.HasTag("Road") || before.designationCategory!=null || before.costList?.Count>0 || before.costStuffCount>0)
                    {job.result.protectedCells++;continue;}
                    float d=distance[i];
                    for(int n=0;n<layers.Count;n++)
                    {
                        var layer=layers[n];float warped=Math.Abs(d+Sample(seed,i,n*2)-.5f);
                        float x=d+(warped-d)*layer.antialiasingMultiplier;
                        if(Sample(seed,i,n*2+1)>=layer.chancePerPositionCurve.Evaluate(x))continue;
                        var terrain=(TerrainDef)layer.place;
                        if(terrain.defName=="FlagstoneSandstone" && stone!=null)terrain=stone;
                        job.paint[i]=terrain;
                    }
                }
                jobs.Add(job);
            }
            // Plan/check the entire batch first. Road RNG never advances world/map generation RNG.
            var original=new Dictionary<int,RoadBridges.Snapshot>();var regions=GenerationContext.Regions(map);
            try
            {
                using(map.pathing.DisableIncrementalScope())
                foreach(var job in jobs)foreach(var entry in job.paint)
                {
                    var cell=new IntVec3(entry.Key%cols,0,entry.Key/cols);
                    if(!original.ContainsKey(entry.Key))original[entry.Key]=new RoadBridges.Snapshot(map,cell);
                    if(entry.Value.bridge)RoadBridges.Place(map,cell,entry.Value);
                    else map.terrainGrid.SetTerrain(cell,entry.Value);
                    job.result.paintedCells++;
                }
            }
            catch
            {
                foreach(var entry in original)entry.Value.Restore(map,new IntVec3(entry.Key%cols,0,entry.Key/cols));
                throw;
            }
            foreach(var job in jobs)
            {
                for(int i=0;i<job.footprint.Length;i++)regions.LocalRoadCells[i]|=job.footprint[i];
                Reserve(map,job.footprint);
                AuthoringGeneration.Current?.roads.Add(job.result);
            }
        }
        static float Sample(int seed,int index,int layer)
        {
            unchecked
            {
                uint x=(uint)(seed ^ index*73856093 ^ layer*19349663);
                x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;x^=x>>16;
                return (x&0xffffff)/16777216f;
            }
        }
        static float[] Distances(int cols,int rows,List<int> path,float radius,bool[] bridgeable,out bool[] bridgeOrigins)
        {
            bridgeOrigins=new bool[cols*rows];
            var values=Enumerable.Repeat(float.PositiveInfinity,cols*rows).ToArray();int bound=(int)Math.Ceiling(radius);
            for(int n=1;n<path.Count;n++)
            {
                float ax=path[n-1]%cols,az=path[n-1]/cols,bx=path[n]%cols,bz=path[n]/cols;
                float dx=bx-ax,dz=bz-az,den=dx*dx+dz*dz;
                for(int z=Math.Max(0,(int)Math.Min(az,bz)-bound);z<=Math.Min(rows-1,(int)Math.Max(az,bz)+bound);z++)
                for(int x=Math.Max(0,(int)Math.Min(ax,bx)-bound);x<=Math.Min(cols-1,(int)Math.Max(ax,bx)+bound);x++)
                {
                    float t=den==0?0:Math.Max(0,Math.Min(1,((x-ax)*dx+(z-az)*dz)/den));
                    float cx=x-ax-t*dx,cz=z-az-t*dz;
                    float distance=(float)Math.Sqrt(cx*cx+cz*cz);int i=z*cols+x;
                    if(distance<values[i]){values[i]=distance;bridgeOrigins[i]=bridgeable[path[n-1]] || bridgeable[path[n]];}
                }
            }
            return values;
        }
        static void Reserve(Map map,bool[] mask)
        {
            var used=MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
            for(int z=0;z<map.Size.z;z++)for(int x=0;x<map.Size.x;x++)
            {
                if(!mask[z*map.Size.x+x])continue;
                int start=x;while(x+1<map.Size.x && mask[z*map.Size.x+x+1])x++;
                used.Add(new CellRect(start,z,x-start+1,1));
            }
        }
        public static void Check(Map map)
        {
            var report=AuthoringGeneration.Current;if(report==null)return;
            foreach(var road in report.roads)
            {
                road.blockedCells=road.path.Count(p=>{
                    var c=new IntVec3(p[0],0,p[1]);var t=map.terrainGrid.TerrainAt(c);
                    return t.IsWater || t.IsRiver || t.dangerous || MapGenerator.Elevation[c]>=.7f || c.GetEdifice(map)!=null;
                });
                if(road.blockedCells>0)AuthoringGeneration.RoadFailure(new InvalidOperationException(RoadPlans.Label(road.kind,true)+" / "+RoadPlans.Label(road.kind,false)+": 생성 후 경로에 장애물 "+road.blockedCells+"칸이 남았습니다. 기존 건물/지형을 유지했습니다. / Later generation obstructed the road; existing structures and terrain were preserved."));
            }
        }
    }
}
