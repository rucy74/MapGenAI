using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld.Planet;
using Verse;
using static CoreRegressionTests;

// Real parser/reducer/Apply paths. Verse serialization shims do not prove game save/load.
static class MdpApplyTests
{
    static MapParamsData Patch(string json) => MapParameterParser.Parse(SimpleJson.Parse(json));
    static TileMapState Edit(TileMapState state,string json) => MapStateEditor.Merge(state,Patch(json));
    public static void RunAll()
    {
        Check("Adding hills preserves earlier hills", () =>
        {
            var left=Edit(null,"{\"hills\":\"left\"}");
            var both=Edit(left,"{\"hills\":\"right\"}");
            Equal(1,left.elevationShapes.Count); Equal(2,both.elevationShapes.Count);
            Equal(true,both.elevationShapes.Any(s=>s.direction=="left"));
            Equal(true,both.elevationShapes.Any(s=>s.direction=="right"));
        });
        Check("Unrelated edits preserve complete geometry", () =>
        {
            var initial=CompositeState(); var changed=Edit(initial,"{\"animal_density\":1.5}");
            Equal(SimpleJson.Serialize(initial.elevationShapes),SimpleJson.Serialize(changed.elevationShapes));
            Equal(1.5f,changed.animalDensity); Equal(1f,initial.animalDensity);
        });
        Check("Empty and unknown patches are no-op", () =>
        {
            var state=CompositeState(); state.oreDensity=2f;
            Equal(MapStateCodec.Serialize(state),MapStateCodec.Serialize(Edit(state,"{}")));
            Equal(MapStateCodec.Serialize(state),MapStateCodec.Serialize(Edit(state,"{\"unsupported\":42}")));
        });
        Check("Explicit empty shapes clear without regeneration", () =>
        {
            var before=Edit(null,"{\"hills\":\"left\"}"); var cleared=Edit(before,"{\"elevation_shapes\":[]}");
            Equal(0,cleared.elevationShapes.Count);
            Equal(0,Edit(cleared,"{\"animal_density\":1.2}").elevationShapes.Count);
        });
        Check("Null and wrong-type patches rejected", () =>
        {
            foreach(var input in new[] {"{\"caves\":null}","{\"river\":null}","{\"mutators\":null}","{\"elevation_shapes\":{}}","{\"hill_amount\":\"bad\"}","{\"caves\":\"maybe\"}"}) Throws(()=>Patch(input));
        });
        Check("River X and Z edits remain independent", () =>
        {
            var state=new TileMapState {hasRiver=true,riverXPosition=.2f,riverZPosition=.8f,riverDirectionAngle=45f};
            var x=Edit(state,"{\"river\":{\"x_position\":0.3}}");
            Equal(.3f,x.riverXPosition); Equal(.8f,x.riverZPosition); Equal(45f,x.riverDirectionAngle);
            var z=Edit(x,"{\"river_position\":\"down\"}");
            Equal(.3f,z.riverXPosition); Equal(.2f,z.riverZPosition);
        });
        Check("Removed features persist and can be re-added", () =>
        {
            var state=Edit(null,"{\"mutators\":[\"A\",\"B\"]}");
            state=Edit(state,"{\"remove_mutators\":[\"A\"]}"); state=Edit(state,"{\"remove_mutators\":[\"B\"]}");
            Equal("A,B",string.Join(",",state.removeMutators));
            state=Edit(state,"{\"mutators\":[\"A\"]}"); Equal("A",string.Join(",",state.mutators)); Equal("B",string.Join(",",state.removeMutators));
        });
        Check("Contradictory feature patch leaves original intact", () =>
        {
            var before=CompositeState(); var expected=MapStateCodec.Serialize(before);
            Throws(()=>Edit(before,"{\"mutators\":[\"A\"],\"remove_mutators\":[\"A\"]}"));
            Equal(expected,MapStateCodec.Serialize(before));
        });
        Check("Composite clone does not share nested arrays or ops", () =>
        {
            var original=CompositeState(); var clone=original.Clone();
            clone.elevationShapes[0].compositeShapes[0].verts[0][0]=.9f;
            clone.elevationShapes[0].compositeShapes[0].center[0]=.7f;
            clone.elevationShapes[0].compositeOps[0].fill="ice";
            Equal(.1f,original.elevationShapes[0].compositeShapes[0].verts[0][0]);
            Equal(.5f,original.elevationShapes[0].compositeShapes[0].center[0]);
            Equal("water",original.elevationShapes[0].compositeOps[0].fill);
        });
        Check("Preset v2 preserves every state field and composite vertices", () =>
        {
            var original=CompositeState(); original.fertilityOffset=.7f; original.straightRiver=true; original.removeMutators.Add("A");
            string json=MapStateCodec.Serialize(original);
            Equal(json,MapStateCodec.Serialize(MapStateCodec.Deserialize(json)));
        });
        Check("Legacy preset loads without inventing explicitly empty shapes", () =>
        {
            var state=MapStateCodec.Deserialize("{\"hills\":\"left\",\"hill_amount\":1.2,\"river\":null,\"elevation_shapes\":[]}");
            Equal(1.2f,state.hillAmount); Equal(0,state.elevationShapes.Count);
            Throws(()=>MapStateCodec.Deserialize("{\"schema_version\":99,\"state\":{}}"));
        });
        Check("Apply targets supplied tile and snapshot restores exactly", () =>
        {
            MapGenParams.Reset(); Find.World=new World(); Find.WorldSelector=new WorldSelector {SelectedTile=20}; Find.WorldGrid=new WorldGrid();
            var a=new SurfaceTile(); var b=new SurfaceTile(); Find.WorldGrid.Tiles[10]=a; Find.WorldGrid.Tiles[20]=b;
            DefDatabase<TileMutatorDef>.Definitions["A"]=new TileMutatorDef {defName="A",label="A"};
            MapGenParams.ApplyPatch(Patch("{\"mutators\":[\"A\"]}"),10);
            Equal(1,a.Mutators.Count); Equal(0,b.Mutators.Count);
            var before=CompositeState(); before.hills="left";
            MapGenParams.RestoreSnapshot(before,10);
            Equal(MapStateCodec.Serialize(before),MapStateCodec.Serialize(MapGenParams.CaptureState(10)));
            var snapshot=MapGenParams.ToSnapshot(); snapshot.elevation_shapes[0].compositeShapes[0].verts[0][0]=.8f;
            Equal(.1f,MapGenParams.CaptureState(10).elevationShapes[0].compositeShapes[0].verts[0][0]);
            MapGenParams.RestoreSnapshot(null,10); Equal(false,MapGenAIWorldComponent.Get().HasState(10));
            Find.World=null; Find.WorldGrid=null; Find.WorldSelector=null;
        });
    }
    static TileMapState CompositeState() => new TileMapState
    {
        elevationShapes=new List<ElevationShape> {new ElevationShape {type="composite",
            compositeShapes=new List<ShapePrimitive> {new ShapePrimitive {id="island",prim="poly",center=new[]{.5f,.5f},verts=new[]{new[]{.1f,.2f},new[]{.8f,.2f},new[]{.5f,.8f}}}},
            compositeOps=new List<ComposeOp> {new ComposeOp {op="add",s="island",fill="water",f=.05f}}}}
    };
}
