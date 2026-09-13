using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using Verse;
using static CoreRegressionTests;

static class ShapeEditTests
{
    static TileMapState Edit(TileMapState state, string json) => MapStateEditor.Merge(state, MapParameterParser.Parse(SimpleJson.Parse(json)));
    static TileMapState Layout() => Edit(null, "{\"elevation_shapes\":[{\"id\":\"west\",\"type\":\"ridge\",\"direction\":\"left\"},{\"id\":\"lake\",\"type\":\"bump\",\"position\":[0.7,0.2],\"fill\":\"water\",\"strength\":\"negative_strong\",\"size\":\"small\"}]}");
    public static void RunAll()
    {
        Check("Targeted edits preserve unrelated geometry through repeated changes", () =>
        {
            var state = Layout(); string originalWest = SimpleJson.Serialize(state.elevationShapes[0]);
            state = Edit(state, "{\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"id\":\"east\",\"type\":\"ridge\",\"direction\":\"right\"}}]}");
            state = Edit(state, "{\"shape_ops\":[{\"op\":\"update\",\"id\":\"lake\",\"changes\":{\"size\":\"medium\"}}]}");
            state = Edit(state, "{\"shape_ops\":[{\"op\":\"move\",\"id\":\"lake\",\"position\":[0.6,0.3]}]}");
            state = Edit(state, "{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"east\"}]}");
            Equal(2, state.elevationShapes.Count);
            Equal(originalWest, SimpleJson.Serialize(state.elevationShapes[0]));
            Equal("medium", state.elevationShapes[1].size); Equal("water", state.elevationShapes[1].fill);
            Equal(.6f, ElevationShape.ParsePosition(state.elevationShapes[1].position).x);
        });
        Check("Unknown target rejects entire multi-operation patch", () =>
        {
            var original = Layout(); string before = MapStateCodec.Serialize(original);
            Throws(() => Edit(original,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"west\"},{\"op\":\"remove\",\"id\":\"missing\"}]}"));
            Equal(before, MapStateCodec.Serialize(original));
        });
        Check("Whole-list omission cannot accidentally delete existing terrain", () =>
        {
            var state=Layout();
            Throws(()=>Edit(state,"{\"elevation_shapes\":[{\"type\":\"bump\"}]}"));
            var rebuilt=Edit(state,"{\"replace_shapes\":true,\"elevation_shapes\":[{\"type\":\"bump\"}]}");
            Equal(1,rebuilt.elevationShapes.Count); Equal(2,state.elevationShapes.Count);
        });
        Check("Legacy IDs remain stable after the first targeted edit and preset reload", () =>
        {
            var state=Layout(); state.elevationShapes.ForEach(s=>s.id=null);
            var description=ShapeEdits.Describe(state.elevationShapes);
            Equal("terrain_1",description[0]["id"]); Equal("terrain_2",description[1]["id"]);
            state=Edit(state,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"terrain_1\"}]}");
            state=MapStateCodec.Deserialize(MapStateCodec.Serialize(state));
            Equal("terrain_2",state.elevationShapes.Single().id);
        });
        Check("Auto hills removal keeps a custom lake", () =>
        {
            var state=Edit(Layout(),"{\"hills\":\"right\"}");
            state=Edit(state,"{\"hills\":\"none\"}");
            Equal("west,lake",string.Join(",",state.elevationShapes.Select(s=>s.id)));
        });
        Check("Prompt geometry uses parser-compatible composite fields", () =>
        {
            var shape=Composite();
            var parsed=ShapeEdits.ParseShape(SimpleJson.Parse(SimpleJson.Serialize(ShapeEdits.ToObject(shape))));
            Equal(SimpleJson.Serialize(shape),SimpleJson.Serialize(parsed));
            var state=new TileMapState {elevationShapes=new List<ElevationShape>{shape}};
            state=Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"island\",\"position\":[0.6,0.4]}]}");
            Equal(.6f,state.elevationShapes[0].compositeShapes[0].center[0]);
            Equal(.4f,state.elevationShapes[0].compositeShapes[0].center[1]);
            Equal(.5f,shape.compositeShapes[0].center[0]);
        });
        Check("Relative positions are bounded and preserve target size", () =>
        {
            var state=Layout(); state.elevationShapes.Add(Composite());
            state=Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"lake\",\"relative_to\":\"island\",\"relation\":\"below\",\"distance\":0.25}]}");
            var lake=state.elevationShapes.Find(s=>s.id=="lake");
            Equal(.25f,ElevationShape.ParsePosition(lake.position).y); Equal("small",lake.size);
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"lake\",\"relative_to\":\"island\",\"relation\":\"below\",\"distance\":0.9}]}"));
        });
        Check("Invalid geometry cannot silently become default terrain", () =>
        {
            foreach(string shape in new[]{"{\"type\":\"unknown\"}","{\"type\":\"bump\",\"fill\":\"lava\"}","{\"type\":\"bump\",\"position\":[1.2,0.2]}","{\"type\":\"bump\",\"size\":\"enormous\"}","{\"type\":\"composite\",\"shapes\":[],\"compose\":[]}"})
                Throws(()=>ShapeEdits.ParseShape(SimpleJson.Parse(shape)));
            var invalid=Composite(); invalid.compositeOps[0].s="missing"; Throws(()=>ShapeValidation.Validate(invalid));
            invalid=Composite(); invalid.compositeShapes[0].r=0; Throws(()=>ShapeValidation.Validate(invalid));
            invalid=Composite(); invalid.compositeOps[0].e=0; Throws(()=>ShapeValidation.Validate(invalid));
        });
        Check("Exponential SDF operand reuse is rejected before rendering", () =>
        {
            var shape=Composite(); shape.compositeOps.Clear(); string id="circle";
            for(int i=0;i<9;i++) { string next="u"+i; shape.compositeOps.Add(new ComposeOp {op="union",a=id,b=id,outId=next,e=1}); id=next; }
            Throws(()=>ShapeValidation.Validate(shape));
        });
        Check("SDF lake changes intended cells and leaves the distant terrain unchanged", () =>
        {
            var shape=Composite(); shape.compositeOps[0].e=-.5f;
            var map=new Map {Size=new IntVec3(100,1,100)}; var elevation=new MapGenFloatGrid(); MapGenerator.Fertility=new MapGenFloatGrid();
            foreach(var cell in CellRect.WholeMap(map)) { elevation[cell]=.6f; MapGenerator.Fertility[cell]=.5f; }
            SdfComposite.ApplyComposite(shape.compositeShapes,shape.compositeOps,map,elevation);
            Equal(-2005f,MapGenerator.Fertility[new IntVec3(50,0,50)]); Equal(.3f,elevation[new IntVec3(50,0,50)]);
            Equal(.5f,MapGenerator.Fertility[new IntVec3(5,0,5)]); Equal(.6f,elevation[new IntVec3(5,0,5)]);
            MapGenerator.Fertility=null;
        });
    }
    static ElevationShape Composite() => new ElevationShape {id="island",type="composite",compositeShapes=new List<ShapePrimitive>{new ShapePrimitive {id="circle",prim="circle",center=new[]{.5f,.5f},r=.15f}},compositeOps=new List<ComposeOp>{new ComposeOp {op="add",s="circle",e=.8f}}};
}
