using System;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using static CoreRegressionTests;

// State compatibility only. Actual terrain, water depth and Scribe persistence need the native game probe.
static class WaterProfileTests
{
    const string Pond="{\"id\":\"pond\",\"type\":\"composite\",\"edge_roughness\":\"medium\",\"shapes\":[{\"id\":\"p\",\"prim\":\"ellipse\",\"center\":[0.5,0.5],\"w\":0.3,\"h\":0.2}],\"compose\":[{\"op\":\"add\",\"s\":\"p\",\"fill\":\"WaterDeep\",\"e\":0.05}]}";
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static TileMapState Add(string json)=>Edit(new TileMapState(),"{\"shape_ops\":[{\"op\":\"add\",\"shape\":"+json+"}]}");
    static TileMapState Update(TileMapState state,string changes)=>Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"pond\",\"changes\":"+changes+"}]}");
    static string With(string field)=>Pond.Replace("\"edge_roughness\"",field+",\"edge_roughness\"");

    public static void RunAll()
    {
        Check("Water profile defaults only for new rough standing freshwater additions",()=>
        {
            foreach(string fill in new[]{"water","WaterDeep","WaterShallow"})
            {
                foreach(string rough in new[]{"medium","0.5"})
                {
                    var added=Add(Pond.Replace("WaterDeep",fill).Replace("medium",rough)).elevationShapes.Single();
                    Equal("native",added.water_profile);Equal("natural",added.details);
                }
            }
            var topFill=Pond.Replace("\"edge_roughness\"","\"fill\":\"water\",\"edge_roughness\"").Replace(",\"fill\":\"WaterDeep\"","");
            Equal("native",Add(topFill).elevationShapes.Single().water_profile);
            foreach(string raw in new[]{Pond.Replace("\"edge_roughness\":\"medium\",",""),Pond.Replace("medium","none"),Pond.Replace("medium","0"),With("\"details\":\"none\"")})
                Equal(null,Add(raw).elevationShapes.Single().water_profile);
            Equal("legacy",Add(With("\"water_profile\":\"legacy\"")).elevationShapes.Single().water_profile);
        });
        Check("Special water and dry composites do not opt into the freshwater depth profile",()=>
        {
            foreach(string fill in new[]{"HotSpring","LavaDeep","WaterOceanShallow","WaterMovingShallow","WaterMovingChestDeep","Marsh","CustomWater","Soil","SoilRich"})
                Equal(null,Add(Pond.Replace("WaterDeep",fill)).elevationShapes.Single().water_profile);
            var overridden=Pond.Replace("\"edge_roughness\"","\"fill\":\"HotSpring\",\"edge_roughness\"");
            Equal(null,Add(overridden).elevationShapes.Single().water_profile);
            var mixed=SimpleJson.Parse(Pond);
            var operations=mixed.GetObjectArray("compose");
            operations.Add(SimpleJson.Parse("{\"op\":\"add\",\"s\":\"p\",\"fill\":\"SoilRich\",\"e\":0.8}"));
            mixed.SetObjectArray("compose",operations);
            var expected=ShapeEdits.ParseShape(mixed);
            var actual=Add(SimpleJson.Serialize(mixed)).elevationShapes.Single();
            Equal("native",actual.water_profile);
            Equal(SimpleJson.Serialize(expected.compositeOps),SimpleJson.Serialize(actual.compositeOps));
        });
        Check("Stored rough water never upgrades during load, movement or ordinary partial edits",()=>
        {
            var old=Edit(new TileMapState(),"{\"elevation_shapes\":["+With("\"details\":\"natural\"")+"]}");
            var restored=MapStateCodec.Deserialize(MapStateCodec.Serialize(old));
            Equal(null,restored.elevationShapes.Single().water_profile);
            var revised=Update(restored,"{\"variant\":\"17\",\"edge_roughness\":\"high\"}");
            var moved=Edit(revised,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"pond\",\"position\":[0.6,0.4]}]}");
            Equal(null,moved.elevationShapes.Single().water_profile);
            Equal(null,MapStateCodec.Deserialize(MapStateCodec.Serialize(moved.Clone())).elevationShapes.Single().water_profile);
            var legacy=MapStateCodec.Deserialize("{\"elevation_shapes\":["+Pond+"]}");
            Equal(null,legacy.elevationShapes.Single().water_profile);
            Equal(null,old.elevationShapes.Single().water_profile);
        });
        Check("Native profile survives partial edits, movement, cloning and preset roundtrip",()=>
        {
            var original=Add(Pond);var before=MapStateCodec.Serialize(original);
            var revised=Update(original,"{\"variant\":\"19\"}");
            var moved=Edit(revised,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"pond\",\"position\":[0.6,0.4]}]}");
            var restored=MapStateCodec.Deserialize(MapStateCodec.Serialize(moved.Clone()));
            Equal("native",restored.elevationShapes.Single().water_profile);
            Equal("19",restored.elevationShapes.Single().variant);
            Equal(.6f,restored.elevationShapes.Single().compositeShapes[0].center[0]);
            Equal(before,MapStateCodec.Serialize(original));
            Equal(MapStateCodec.Serialize(moved),MapStateCodec.Serialize(restored));
        });
        Check("Surface-off edit retains the chosen water profile and all authored water geometry",()=>
        {
            var state=Add(Pond);var original=state.elevationShapes.Single();
            var off=Update(state,"{\"details\":\"none\"}").elevationShapes.Single();
            Equal("native",off.water_profile);Equal("none",off.details);
            var expected=ShapeEdits.ToObject(original);expected["details"]="none";
            Equal(SimpleJson.Serialize(expected),SimpleJson.Serialize(ShapeEdits.ToObject(off)));
            Equal("natural",original.details);
        });
        Check("Water profile supports explicit upgrade, legacy opt-out and null without silent re-upgrade",()=>
        {
            var state=Edit(new TileMapState(),"{\"elevation_shapes\":["+Pond+"]}");
            var native=Update(state,"{\"water_profile\":\"native\"}");
            Equal("native",native.elevationShapes.Single().water_profile);
            var legacy=Update(native,"{\"water_profile\":\"legacy\"}");
            Equal("legacy",MapStateCodec.Deserialize(MapStateCodec.Serialize(legacy)).elevationShapes.Single().water_profile);
            var cleared=Update(legacy,"{\"water_profile\":null}");
            Equal(null,cleared.elevationShapes.Single().water_profile);
            Equal(null,Update(cleared,"{\"details\":\"natural\"}").elevationShapes.Single().water_profile);
            Equal(null,ShapeEdits.ParseShape(SimpleJson.Parse(With("\"water_profile\":null"))).water_profile);
        });
        Check("Invalid water profile values and geometry types reject without changing the original state",()=>
        {
            var state=Add(Pond);var before=MapStateCodec.Serialize(state);
            foreach(string value in new[]{"\"auto\"","\"Native\"","\"\"","0","true","{}","[]"})
                Throws(()=>Update(state,"{\"water_profile\":"+value+"}"));
            foreach(string type in new[]{"bump","ring","ridge","passage","region_fill","landform"})
                Throws(()=>ShapeValidation.Validate(new ElevationShape{type=type,water_profile="native"}));
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"pond\",\"changes\":{\"water_profile\":\"legacy\"}},{\"op\":\"update\",\"id\":\"missing\",\"changes\":{\"water_profile\":\"native\"}}]}"));
            Equal(before,MapStateCodec.Serialize(state));
        });
    }
}
