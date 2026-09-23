using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

static class LandscapeBlendTests
{
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));
    public static void RunAll()
    {
        // Break: upgrading an old snapshot, dropping details on a partial edit, or applying it to exact shapes.
        Check("Landscape details default only on new landforms and survive partial edits, clone and presets",()=>
        {
            var old=new TileMapState();old.elevationShapes.Add(new ElevationShape{id="old",type="landform",landform="open_basin",layout="organic"});
            var restored=MapStateCodec.Deserialize(MapStateCodec.Serialize(old));Equal(null,restored.elevationShapes[0].details);
            var changed=Edit(restored,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"old\",\"changes\":{\"gap\":0.3}}]}");Equal(null,changed.elevationShapes[0].details);
            var fresh=Edit(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"land\",\"type\":\"landform\",\"landform\":\"open_basin\"}}]}");
            Equal("natural",fresh.elevationShapes[0].details);
            var next=Edit(fresh,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"land\",\"changes\":{\"opening\":0.2}}]}");
            Equal("natural",MapStateCodec.Deserialize(MapStateCodec.Serialize(next.Clone())).elevationShapes[0].details);
            var off=Edit(next,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"land\",\"changes\":{\"details\":\"none\"}}]}");Equal("none",off.elevationShapes[0].details);Equal("natural",next.elevationShapes[0].details);
            var full=Edit(new TileMapState(),"{\"elevation_shapes\":[{\"id\":\"old\",\"type\":\"landform\",\"landform\":\"open_basin\",\"layout\":\"organic\"}]}");Equal(null,full.elevationShapes[0].details);
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"landform\",\"landform\":\"open_basin\",\"details\":\"lush\"}")));
            Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"type\":\"ring\",\"details\":\"natural\"}")));
        });
        // Break: changing elevations/floor masks when enabling a decorative surface layer.
        Check("Enabling surface details never changes the landform geometry",()=>
        {
            foreach(var kind in new[]{"open_basin","winding_valley","foothills"})
            {
                var shape=new ElevationShape{type="landform",landform=kind,variant="23",layout="organic"};
                var before=new NaturalLandformGeometry(shape);shape.details="natural";var after=new NaturalLandformGeometry(shape);
                for(int z=0;z<80;z++)for(int x=0;x<80;x++)
                {var a=before.Sample(x/80f,z/80f);var b=after.Sample(x/80f,z/80f);Equal(a.influence,b.influence);Equal(a.elevation,b.elevation);Equal(a.floor,b.floor);}
            }
        });
        // Break: broad repainting, water without a source, dry-biome soil conversion, or unbounded plant density.
        Check("Landscape surface fields: 100 unfiltered variants retain open ground and bounded coherent banks",()=>
        {
            int failures=0,minPainted=6400,maxPainted=0;var unique=new HashSet<string>();
            for(int seed=0;seed<100;seed++)
            {
                var field=new LandscapeBlendField(seed.ToString());var mask=new char[6400];int painted=0;
                for(int z=0;z<80;z++)for(int x=0;x<80;x++)
                {
                    float water=Math.Abs(x-10),rock=Math.Abs(x-70);
                    var cell=field.Sample(x,z,water,rock,true);mask[z*80+x]=(char)('A'+(int)cell.ground);
                    if(cell.ground!=LandscapeBlendField.Ground.Keep)painted++;
                    if(cell.vegetation<.38f || cell.vegetation>1.22f || !float.IsFinite(cell.vegetation))failures++;
                    if(water>9 && rock>=9 && cell.ground!=LandscapeBlendField.Ground.Keep)failures++;
                    if(field.Sample(x,z,water,rock,false).ground==LandscapeBlendField.Ground.Soil)failures++;
                    if(field.Sample(x,z,1000000,1000000,true).ground!=LandscapeBlendField.Ground.Keep)failures++;
                }
                minPainted=Math.Min(minPainted,painted);maxPainted=Math.Max(maxPainted,painted);unique.Add(new string(mask));
                if(painted<400 || painted>2400)failures++;
            }
            Console.WriteLine("Landscape fields:100 variants, failures="+failures+", changed="+minPainted+".."+maxPainted+"/6400, unique="+unique.Count);
            Equal(0,failures);Equal(true,unique.Count>95);
        });
        Check("Surface fields are repeatable and vegetation favors water over rocky edges",()=>
        {
            double nearWater=0,nearRock=0;int differences=0;
            var a=new LandscapeBlendField("123");var b=new LandscapeBlendField("123");var c=new LandscapeBlendField("456");
            for(int z=0;z<80;z++)for(int x=0;x<80;x++)
            {
                var first=a.Sample(x,z,2,20,true);var second=b.Sample(x,z,2,20,true);
                Equal(first.ground,second.ground);Equal(first.vegetation,second.vegetation);
                nearWater+=first.vegetation;nearRock+=a.Sample(x,z,20,2,true).vegetation;
                if(first.vegetation!=c.Sample(x,z,2,20,true).vegetation)differences++;
            }
            Equal(true,nearWater>nearRock*1.3);Equal(true,differences>6000);
        });
        Check("Automatic surfaces exclude water, rich soil, floors and native or mod feature terrain",()=>
        {
            Equal(true,LandscapeBlendField.OrdinaryGround("Soil"));
            foreach(string name in new[]{"Gravel","SoilRich","WaterShallow","WaterMovingChestDeep","WaterOceanDeep","HotSpring","LavaDeep","VolcanicRock","Ice","MarshyTerrain","Mud","Bridge","PavedTile","CustomSoil","RoadDirt"})Equal(false,LandscapeBlendField.OrdinaryGround(name));
        });
    }
}
