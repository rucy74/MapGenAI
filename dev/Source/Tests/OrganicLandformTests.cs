using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class OrganicLandformTests
{
    const int N=80;
    static readonly string[] Kinds={"open_basin","winding_valley","foothills"};
    static TileMapState Edit(TileMapState s,string text)=>MapStateEditor.Merge(s,MapParameterParser.Parse(SimpleJson.Parse(text)));
    static ElevationShape Shape(string kind,int variant)=>new ElevationShape{id="landscape",type="landform",landform=kind,layout="organic",variant=variant.ToString(),direction="bottom"};
    static NaturalLandformGeometry.Cell[] Raster(ElevationShape s)
    {
        var g=new NaturalLandformGeometry(s);var cells=new NaturalLandformGeometry.Cell[N*N];
        for(int z=0;z<N;z++)for(int x=0;x<N;x++)cells[z*N+x]=g.Sample((x+.5f)/N,(z+.5f)/N);
        return cells;
    }
    static bool ConnectedFloor(NaturalLandformGeometry.Cell[] cells,out int square)
    {
        var walk=cells.Select(c=>c.influence*c.elevation+(1-c.influence)*.2f<.7f).ToArray();
        int start=Array.FindIndex(cells,c=>c.floor);square=0;if(start<0)return false;
        var visited=new bool[N*N];var queue=new Queue<int>();visited[start]=true;queue.Enqueue(start);bool edge=false;
        while(queue.Count>0)
        {
            int i=queue.Dequeue(),x=i%N,z=i/N;edge|=x==0 || z==0 || x==N-1 || z==N-1;
            void Visit(int j){if(walk[j] && !visited[j]){visited[j]=true;queue.Enqueue(j);}}
            if(x>0)Visit(i-1);if(x+1<N)Visit(i+1);if(z>0)Visit(i-N);if(z+1<N)Visit(i+N);
        }
        var sizes=new int[N*N];
        for(int i=0;i<cells.Length;i++)if(cells[i].floor)
        {
            if(!visited[i] || !walk[i] || cells[i].elevation>=.1f)return false;
            sizes[i]=i%N==0 || i<N?1:1+Math.Min(sizes[i-1],Math.Min(sizes[i-N],sizes[i-N-1]));
            square=Math.Max(square,sizes[i]);
        }
        return edge;
    }
    public static void RunAll()
    {
        // Break: auto-upgrading a loaded state, forgetting layout in Clone/Scribe/codec, or
        // losing it during a minimal patch changes existing floors and dependent objects.
        Check("Organic defaults apply only to new adds; legacy presets and follow-ups retain their layout",()=>
        {
            var old=new TileMapState();var shape=Shape("open_basin",23);shape.layout=null;old.elevationShapes.Add(shape);
            string stored=MapStateCodec.Serialize(old);
            var restored=MapStateCodec.Deserialize(stored);Equal(null,restored.elevationShapes[0].layout);
            var edited=Edit(restored,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"landscape\",\"changes\":{\"gap\":0.3}}]}");Equal(null,edited.elevationShapes[0].layout);
            var added=Edit(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"new\",\"type\":\"landform\",\"landform\":\"open_basin\"}}]}");
            Equal("organic",added.elevationShapes[0].layout);
            var next=Edit(added,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"new\",\"changes\":{\"opening\":0.2}}]}");
            Equal("organic",MapStateCodec.Deserialize(MapStateCodec.Serialize(next.Clone())).elevationShapes[0].layout);
            Equal(stored,MapStateCodec.Serialize(old));
            var classic=ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"open_basin\",\"layout\":\"classic\",\"variant\":23,\"direction\":\"bottom\"}"));
            var a=Raster(shape);var b=Raster(classic);
            for(int i=0;i<a.Length;i++){Equal(a[i].elevation,b[i].elevation);Equal(a[i].influence,b[i].influence);Equal(a[i].floor,b[i].floor);}
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"open_basin\",\"layout\":\"fancy\"}")));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"ring\",\"layout\":\"organic\"}")));
        });
        Check("Organic connectivity detector rejects a deliberately severed settlement floor",()=>
        {
            var cells=Raster(Shape("foothills",23));Equal(true,ConnectedFloor(cells,out int square));
            for(int x=0;x<N;x++)cells[(N/2)*N+x]=new NaturalLandformGeometry.Cell{influence=1,elevation=1,floor=false};
            Equal(false,ConnectedFloor(cells,out square));
        });
        // Break: a periodic unvarying template, blocked/fragmented floor, or a solid rock slab.
        // These are geometry/usability checks, not an automatic beauty score.
        foreach(string kind in Kinds)Check("Organic "+kind+": 100 unfiltered variants retain open connected settlement space",()=>
        {
            int failures=0,minSquare=N;string first=null;var distinct=new HashSet<string>();
            for(int seed=0;seed<100;seed++)foreach(string direction in new[]{"bottom","right","top_left","top_right"})
            {
                var s=Shape(kind,seed);s.direction=direction;var cells=Raster(s);
                int floor=cells.Count(c=>c.floor),rock=cells.Count(c=>c.influence>.99f && c.elevation>=.7f);
                bool connected=ConnectedFloor(cells,out int square);minSquare=Math.Min(minSquare,square);
                bool good=connected && square>=12 && floor>N*N*.12 && floor<N*N*.75 && rock>N*N*.05;
                good&=cells.All(c=>float.IsFinite(c.elevation) && float.IsFinite(c.influence));
                if(!good){failures++;if(first==null)first=seed+"/"+direction+" square="+square+" floor="+floor+" rock="+rock+" connected="+connected;}
                if(direction=="bottom")distinct.Add(string.Concat(cells.Select(c=>c.floor?'1':'0')));
            }
            Console.WriteLine(kind+": 400 organic layouts, failures="+failures+", min build square="+minSquare+", unique="+distinct.Count+", first="+first);
            Equal(0,failures);Equal(true,distinct.Count>95);
        });
        Check("Organic floor stays connected at valid scale width opening and arbitrary-angle boundaries",()=>
        {
            int failures=0;string first=null;
            foreach(string kind in Kinds)for(int seed=0;seed<100;seed++)
            {
                var s=Shape(kind,seed);s.size=new[]{"0.35","medium","large","1"}[seed%4];s.gap=seed%2==0?"0.1":"0.32";s.direction=(seed*13%360).ToString(CultureInfo.InvariantCulture);
                if(kind=="open_basin")s.opening=seed%2==0?"0.08":"0.3";
                if(!ConnectedFloor(Raster(s),out int square)){failures++;if(first==null)first=kind+"/"+seed+"/"+s.direction;}
            }
            Console.WriteLine("Organic boundaries:300 unfiltered layouts, failures="+failures+", first="+first);Equal(0,failures);
        });
        Check("Organic floor widening preserves every existing floor cell and a basin exit edit preserves distant walls",()=>
        {
            foreach(string kind in Kinds)
            {
                var s=Shape(kind,23);var before=Raster(s);s.gap="0.32";var after=Raster(s);
                Equal(true,after.Count(c=>c.floor)>before.Count(c=>c.floor));
                Equal(0,before.Where((c,i)=>c.floor && !after[i].floor).Count());
            }
            var basin=Shape("open_basin",23);var south=Raster(basin);basin.direction="right";var east=Raster(basin);
            int checkedCells=0;
            for(int z=N/2;z<N;z++)for(int x=0;x<N/2;x++)
            {int i=z*N+x;if(south[i].elevation>.7f && east[i].elevation>.7f){Equal(south[i].elevation,east[i].elevation);checkedCells++;}}
            Equal(true,checkedCells>100);
        });
    }
}
