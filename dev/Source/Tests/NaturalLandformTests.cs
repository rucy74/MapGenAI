using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class NaturalLandformTests
{
    const int Size=80;
    static readonly string[] Kinds={"open_basin","winding_valley","foothills"};
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static ElevationShape Shape(string kind,int seed=23)=>new ElevationShape{id="landscape",type="landform",landform=kind,direction="bottom",variant=seed.ToString(CultureInfo.InvariantCulture)};
    static NaturalLandformGeometry.Cell[] Raster(ElevationShape shape)
    {
        var layout=new NaturalLandformGeometry(shape);var cells=new NaturalLandformGeometry.Cell[Size*Size];
        for(int z=0;z<Size;z++)for(int x=0;x<Size;x++)cells[z*Size+x]=layout.Sample((x+.5f)/Size,(z+.5f)/Size);
        return cells;
    }
    static bool[] Reachable(bool[] mask,int start)
    {
        var seen=new bool[mask.Length];var q=new Queue<int>();
        if(start<0 || !mask[start])return seen;q.Enqueue(start);seen[start]=true;
        while(q.Count>0)
        {
            int i=q.Dequeue();int x=i%Size,z=i/Size;
            void Visit(int n){if(mask[n] && !seen[n]){seen[n]=true;q.Enqueue(n);}}
            if(x>0)Visit(i-1);if(x+1<Size)Visit(i+1);if(z>0)Visit(i-Size);if(z+1<Size)Visit(i+Size);
        }
        return seen;
    }
    static int Connected(bool[] mask)=>Reachable(mask,Array.FindIndex(mask,b=>b)).Count(b=>b);
    public static void RunAll()
    {
        // A disconnected floor, sealed basin, vanishing wall, or retry-based seed filtering fails this corpus.
        foreach(string kind in Kinds)Check("Natural "+kind+": 100 unfiltered variants x four directions retain connected usable ground",()=>
        {
            int failures=0;string first=null;var patterns=new HashSet<string>();
            for(int seed=0;seed<100;seed++)foreach(string direction in new[]{"bottom","right","top_left","top_right"})
            {
                var s=Shape(kind,seed);s.direction=direction;var cells=Raster(s);var floor=cells.Select(c=>c.floor).ToArray();
                int area=floor.Count(b=>b), walls=cells.Count(c=>c.influence>.99f && c.elevation>.7f);
                bool good=area>Size*Size*.08 && area<Size*Size*.7 && walls>Size*Size*.05;
                good&=cells.All(c=>!float.IsNaN(c.elevation) && (!c.floor || c.elevation<.1f));
                // Flood all non-rock space on a neutral base: the floor must reach the map edge.
                var passable=cells.Select(c=>c.influence*c.elevation+(1-c.influence)*.2f<.7f).ToArray();
                var enclosed=RegionCoverage.Enclosed(Size,Size,passable.Select(b=>!b).ToArray());
                good&=!floor.Where((b,i)=>b && enclosed[i]).Any();
                // The floor mask excludes the low, walkable bank transition. Judge actual
                // accessibility on the height field, not disconnected pixel fringes of the mask.
                var reachable=Reachable(passable,Array.FindIndex(floor,b=>b));
                good&=!floor.Where((b,i)=>b && !reachable[i]).Any();
                if(!good){failures++;if(first==null)first=seed+"/"+direction+" area="+area+" walls="+walls+" connected="+Connected(floor);}
                if(direction=="bottom")patterns.Add(string.Join("",floor.Select(b=>b?"1":"0")));
            }
            Console.WriteLine(kind+": 400 layouts, failures="+failures+", unique masks="+patterns.Count+", first="+first);
            Equal(0,failures);Equal(true,patterns.Count>95);
        });
        Check("Natural edits preserve layout seed, dependent 70% soil, unrelated shape and preset/undo snapshot",()=>
        {
            var before=Edit(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"landscape\",\"type\":\"landform\",\"landform\":\"open_basin\",\"variant\":23,\"direction\":\"bottom\"}},{\"op\":\"add\",\"shape\":{\"id\":\"other\",\"type\":\"bump\",\"position\":\"top_left\"}},{\"op\":\"add\",\"shape\":{\"id\":\"soil\",\"type\":\"region_fill\",\"region\":\"landscape\",\"region_part\":\"inside\",\"coverage\":0.7,\"fill\":\"rich_soil\"}}]}");
            string saved=MapStateCodec.Serialize(before);
            var after=Edit(before,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"landscape\",\"changes\":{\"gap\":0.30,\"opening\":0.2}}]}");
            var old=Raster(before.elevationShapes[0]);var wide=Raster(after.elevationShapes[0]);
            Equal(true,wide.Count(c=>c.floor)>old.Count(c=>c.floor)+100);
            Equal("23",after.elevationShapes[0].variant);Equal(saved,MapStateCodec.Serialize(before));
            Equal(SimpleJson.Serialize(before.elevationShapes[1]),SimpleJson.Serialize(after.elevationShapes[1]));
            Equal("0.7",after.elevationShapes[2].coverage);
            var copy=MapStateCodec.Deserialize(MapStateCodec.Serialize(after));Equal("0.2",copy.elevationShapes[0].opening);
            Equal("open_basin",copy.Clone().elevationShapes[0].landform);
            Equal(saved,MapStateCodec.Serialize(MapStateCodec.Deserialize(saved)));
            var moved=Edit(after,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"landscape\",\"position\":[0.55,0.45]}]}");
            var a=new NaturalLandformGeometry(after.elevationShapes[0]);var b=new NaturalLandformGeometry(moved.elevationShapes[0]);
            for(int z=10;z<60;z+=5)for(int x=10;x<60;x+=5)Equal(true,Math.Abs(a.Sample(x/80f,z/80f).elevation-b.Sample(x/80f+.05f,z/80f-.05f).elevation)<.00005);
            var mask=wide.Select(c=>c.floor).ToArray();var selected=RegionCoverage.Select(Size,Size,mask,new bool[Size*Size],.7f);
            Equal((int)Math.Round(mask.Count(v=>v)*.7),selected.Count(v=>v));
            Throws(()=>Edit(after,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"landscape\"}]}"));
            Throws(()=>Edit(after,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"soil\",\"changes\":{\"region_part\":\"enclosed\"}}]}"));
        });
        Check("Natural parameter bounds reject unsupported fields without changing prior state",()=>
        {
            foreach(string tail in new[]{"\"gap\":0.8","\"variant\":1.5","\"variant\":-1","\"variant\":1000000","\"size\":0.01","\"fill\":\"soil\"","\"strength\":1","\"edge_roughness\":\"medium\"","\"opening\":0","\"position\":[0.2]"})
                Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"open_basin\","+tail+"}")));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"ring\",\"landform\":\"open_basin\"}")));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"winding_valley\",\"opening\":0.2}")));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"volcano\"}")));
        });
        Check("Moving a basin exit retains the opposite walls; widening its floor retains the outer footprint",()=>
        {
            var s=Shape("open_basin");var baseline=Raster(s);s.direction="right";var east=Raster(s);
            // Northwest is away from both the original south exit and the new east exit.
            int unchangedWalls=0;for(int z=Size/2;z<Size;z++)for(int x=0;x<Size/2;x++)
            {int i=z*Size+x;if(baseline[i].elevation>.7f){unchangedWalls++;Equal(true,Math.Abs(baseline[i].elevation-east[i].elevation)<.00001);}}
            Equal(true,unchangedWalls>100);Equal(true,baseline.Where((c,i)=>c.floor!=east[i].floor).Count()>50);
            s.direction="bottom";s.gap="0.3";var wider=Raster(s);
            for(int i=0;i<baseline.Length;i++)Equal(baseline[i].influence,wider[i].influence);
            Equal(true,wider.Count(c=>c.floor)>baseline.Count(c=>c.floor));
        });
        Check("Natural layouts stay usable across supported scale and gap boundaries",()=>
        {
            foreach(string kind in Kinds)for(int seed=0;seed<100;seed++)
            {
                var s=Shape(kind,seed);s.size=new[]{"0.35","medium","large","1"}[seed%4];s.gap=seed%2==0?"0.1":"0.32";s.direction=(seed*13%360).ToString(CultureInfo.InvariantCulture);
                var cells=Raster(s);var floor=cells.Select(c=>c.floor).ToArray();var clear=cells.Select(c=>c.influence*c.elevation+(1-c.influence)*.2f<.7f).ToArray();
                var reached=Reachable(clear,Array.FindIndex(floor,b=>b));Equal(true,floor.Count(b=>b)>10);
                int missed=floor.Where((b,i)=>b && !reached[i]).Count();
                if(missed>0)throw new Exception(kind+" seed="+seed+" size="+s.size+" gap="+s.gap+" direction="+s.direction+" unreachable floor="+missed+"/"+floor.Count(b=>b));
            }
        });
        Check("Changing natural layout kind clears only the inapplicable basin opening and preserves references",()=>
        {
            var s=Shape("open_basin");s.opening="0.2";var before=new TileMapState();before.elevationShapes.Add(s);
            foreach(string kind in new[]{"winding_valley","foothills"})
            {
                var after=Edit(before,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"landscape\",\"changes\":{\"landform\":\""+kind+"\"}}]}");
                Equal(kind,after.elevationShapes[0].landform);Equal(null,after.elevationShapes[0].opening);Equal("23",after.elevationShapes[0].variant);Equal("0.2",before.elevationShapes[0].opening);
            }
            var clear=Edit(before,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"landscape\",\"changes\":{\"opening\":null}}]}");Equal(null,clear.elevationShapes[0].opening);
        });
        // Removing the runtime write or masking the wall instead of the floor makes this fail.
        Check("Natural raster writes heights and floor region, preserves outside terrain and authored water/materials",()=>
        {
            var map=new Map{Size=new IntVec3(Size,1,Size)};var grid=new MapGenFloatGrid();var s=Shape("open_basin");
            foreach(var cell in CellRect.WholeMap(map))grid[cell]=.8f;
            using(GenerationContext.Enter(1,new TileMapState()))
            {
                var regions=GenerationContext.Regions(map);var water=new IntVec3(Size/2,0,Size/2);
                regions.Record("lake",water,true,"water",true);grid[water]=.1f;
                NaturalLandformGeneration.Apply(s,map,grid);
                var mask=regions.Mask(s.id);Equal(true,mask.Count(b=>b)>800);Equal(.1f,grid[water]);Equal("WaterDeep",regions.Materials[regions.Index(water)]);
                Equal(false,mask[regions.Index(water)]);Equal(.8f,grid[new IntVec3(0,0,0)]);
                foreach(var c in CellRect.WholeMap(map))if(mask[regions.Index(c)]){Equal(true,grid[c]<.1f);Equal(true,regions.Flatten[regions.Index(c)]);Equal(null,regions.Materials[regions.Index(c)]);}
            }
        });
        Check("Natural descriptions are localized without exposing internal kind names",()=>
        {
            foreach(string kind in Kinds)foreach(bool ko in new[]{false,true})
            {string text=new MapPlanDescription(ko).Shape(Shape(kind));Equal(false,text.Contains("_"));Equal(true,text.Length>15);}
        });
    }
}
