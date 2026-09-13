using System;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class SpatialRelationTests
{
    public static void RunAll()
    {
        Check("Spatial distance matches independent exhaustive Euclidean distances and nearest coordinates",()=>
        {
            const int w=23,h=17;var target=new bool[w*h];target[2*w+3]=true;target[13*w+19]=true;target[8*w+11]=true;
            var field=new SpatialDistance(w,h,target);
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                int expected=int.MaxValue;
                for(int tz=0;tz<h;tz++)for(int tx=0;tx<w;tx++)if(target[tz*w+tx])expected=Math.Min(expected,(x-tx)*(x-tx)+(z-tz)*(z-tz));
                int i=z*w+x;Equal((float)expected,field.squared[i]);Equal(true,target[field.nearestZ[i]*w+field.nearestX[i]]);
                Equal(expected,(x-field.nearestX[i])*(x-field.nearestX[i])+(z-field.nearestZ[i])*(z-field.nearestZ[i]));
            }
        });
        Check("Spatial distance handles all-target and target-free maps without invented geometry",()=>
        {
            var all=new SpatialDistance(7,11,Enumerable.Repeat(true,77).ToArray());Equal(true,all.squared.All(d=>d==0));
            var empty=new SpatialDistance(7,11,new bool[77]);Equal(false,empty.HasTarget);
            bool rejected=false;try{empty.Constrain(new bool[77],new SpatialRelation{target="river"});}catch(InvalidOperationException){rejected=true;}Equal(true,rejected);
        });
        Check("River side and footprint distances apply to the nearest wall instead of the center",()=>
        {
            const int n=80;var river=new bool[n*n];var allowed=Enumerable.Repeat(true,n*n).ToArray();
            for(int z=0;z<n;z++)river[z*n+40]=true;
            var field=new SpatialDistance(n,n,river);
            var predicate=field.Constrain(allowed,new SpatialRelation{target="river",side="east",min_distance=3,max_distance=5});
            var placed=PlacementPlanner.Find(n,n,allowed,new bool[n*n],11,9,3,40,40,8,predicate);Equal(3,placed.Count);
            foreach(var r in placed){Equal(true,r.x>=43 && r.x<=45);Equal(11,r.width);}
            Equal(false,predicate(new PlannedRect{x=24,z=20,width=11,height=9}));
            Equal(false,predicate(new PlannedRect{x=46,z=20,width=11,height=9}));
        });
        Check("Configured spacing leaves the requested empty cells and capacity remains atomic",()=>
        {
            const int n=70;var allowed=Enumerable.Repeat(true,n*n).ToArray();
            var rectangles=PlacementPlanner.Find(n,n,allowed,new bool[n*n],9,7,4,35,35,12);
            Equal(4,rectangles.Count);
            for(int i=0;i<rectangles.Count;i++)for(int j=i+1;j<rectangles.Count;j++)
            {var a=rectangles[i];var b=rectangles[j];Equal(true,a.x>=b.x+b.width+12 || b.x>=a.x+a.width+12 || a.z>=b.z+b.height+12 || b.z>=a.z+a.height+12);}
            var occupied=new bool[20*20];Equal(null,PlacementPlanner.Find(20,20,Enumerable.Repeat(true,400).ToArray(),occupied,9,7,4,10,10,12));Equal(false,occupied.Any(x=>x));
        });
        Check("Region boundary includes internal holes and respects full-footprint inset",()=>
        {
            const int n=40;var region=new bool[n*n];
            for(int z=3;z<37;z++)for(int x=3;x<37;x++)region[z*n+x]=!(x>=16 && x<=23 && z>=16 && z<=23);
            var edge=SpatialDistance.InteriorEdge(n,n,region);Equal(true,edge[15*n+18]);Equal(false,edge[18*n+18]);Equal(true,edge[3*n+20]);
            var allowed=(bool[])region.Clone();var field=new SpatialDistance(n,n,edge);
            var predicate=field.Constrain(allowed,new SpatialRelation{target="region_edge",min_distance=2,max_distance=3});
            var placed=PlacementPlanner.Find(n,n,allowed,new bool[n*n],5,5,2,20,20,1,predicate);Equal(2,placed.Count);
            foreach(var r in placed)for(int z=r.z;z<r.z+r.height;z++)for(int x=r.x;x<r.x+r.width;x++){Equal(true,region[z*n+x]);Equal(true,field.squared[z*n+x]>=4);}
        });
        Check("Relation rotation and spacing survive presets cloning and partial edits",()=>
        {
            var initial=MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""add"",""structure"":{""id"":""bank"",""relation"":{""target"":""river"",""side"":""east"",""min_distance"":2,""max_distance"":12},""rotation"":90,""spacing"":15,""width"":13,""height"":7}}]}")));
            var round=MapStateCodec.Deserialize(MapStateCodec.Serialize(initial));Equal(MapStateCodec.Serialize(initial),MapStateCodec.Serialize(round));
            round.structures[0].relation.max_distance=20;Equal(12,initial.structures[0].relation.max_distance);
            var changed=MapStateEditor.Merge(initial,MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""update"",""id"":""bank"",""changes"":{""count"":3}}]}")));
            Equal("east",changed.structures[0].relation.side);Equal(90,changed.structures[0].rotation);Equal(15,changed.structures[0].spacing);
            var cleared=MapStateEditor.Merge(changed,MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""update"",""id"":""bank"",""changes"":{""relation"":null,""position"":[0.7,0.4]}}]}")));
            Equal(null,cleared.structures[0].relation);Equal(3,cleared.structures[0].count);
        });
        Check("Bad or unsupported relations reject before state mutation",()=>
        {
            foreach(string data in new[]{@"""target"":""missing""",@"""target"":""river"",""side"":""left""",@"""target"":""river"",""min_distance"":-1",@"""target"":""river"",""min_distance"":12,""max_distance"":3",@"""target"":""river"",""max_distance"":65",@"""target"":""river"",""max_distance"":3.5",@"""target"":""river"",""unknown"":true",@"""target"":""region_edge"""})
                Throws(()=>MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""add"",""structure"":{""id"":""bad"",""relation"":{"+data+"}}}]}"))));
            foreach(string data in new[]{@"""rotation"":45",@"""spacing"":0",@"""spacing"":61"})
                Throws(()=>MapStateEditor.Merge(new TileMapState(),MapParameterParser.Parse(SimpleJson.Parse(@"{""structure_ops"":[{""op"":""add"",""structure"":{""id"":""bad"",""position"":[0.5,0.5],"+data+"}}]}"))));
        });
    }
}

