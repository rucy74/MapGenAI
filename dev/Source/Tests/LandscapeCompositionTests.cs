using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using UnityEngine;
using Verse;
using static CoreRegressionTests;

static class LandscapeCompositionTests
{
    const int N=100;
    static TileMapState Edit(TileMapState state,string json)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse(json)));
    static ElevationShape Plain()=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"id\":\"plain\",\"type\":\"composite\",\"details\":\"natural\",\"shapes\":[{\"id\":\"p\",\"prim\":\"path\",\"verts\":[[0.25,0.5],[0.5,0.55],[0.75,0.5]],\"w\":0.45}],\"compose\":[{\"op\":\"add\",\"s\":\"p\",\"e\":0.05,\"f\":0.01}]}"));
    static ElevationShape Pond(string id="pond")=>ShapeEdits.ParseShape(SimpleJson.Parse("{\"id\":\""+id+"\",\"type\":\"composite\",\"anchor\":\"plain\",\"placement\":\"edge\",\"direction\":\"right\",\"edge_roughness\":\"medium\",\"variant\":42,\"shapes\":[{\"id\":\"p\",\"prim\":\"ellipse\",\"center\":[0.5,0.5],\"w\":0.12,\"h\":0.08}],\"compose\":[{\"op\":\"add\",\"s\":\"p\",\"e\":0.05,\"f\":0.008,\"fill\":\"WaterShallow\"}]}"));
    sealed class Raster { public Dictionary<string,bool[]> masks;public string[] materials;public float[] heights;public string[] issues;public bool blocksStructures; }
    static Raster Render(TileMapState state)
    {
        var map=new Map{Size=new IntVec3(N,1,N)};var grid=new MapGenFloatGrid();
        MapGenerator.Fertility=new MapGenFloatGrid();
        try
        {
            using(GenerationContext.Enter(1,state))
            {
                GenerationContext.Report=new AuthoringResult();
                foreach(var c in CellRect.WholeMap(map))grid[c]=.35f;
                foreach(var s in LandscapePlacement.Order(state.elevationShapes))
                    SdfComposite.ApplyComposite(s.compositeShapes,s.compositeOps,map,grid,s.edge_roughness,s.id,s.fill,s.anchor,s.placement,s.direction,s.variant);
                var regions=GenerationContext.Regions(map);
                return new Raster{masks=state.elevationShapes.ToDictionary(s=>s.id,s=>regions.Mask(s.id)),materials=(string[])regions.Materials.Clone(),
                    heights=CellRect.WholeMap(map).Select(c=>grid[c]).ToArray(),issues=GenerationContext.Report.issues.ToArray(),blocksStructures=GenerationContext.Report.blockStructures};
            }
        }
        finally{MapGenerator.Fertility=null;}
    }
    public static void RunAll()
    {
        Check("Recorded move-only relationship response cannot silently replace a lake with a smaller ellipse",()=>
        {
            var raw=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"landscape-fixtures/failed-move-response.json"));
            var history=new List<ChatMessage>{new ChatMessage("user","연못은 평지 안쪽 가장자리로 옮겨 줘. 나머지는 유지해.")};
            Equal(true,EditIntentGuard.Rejection(history,raw)!=null);
            history[0]=new ChatMessage("user","연못을 작게 줄여서 평지 가장자리로 옮겨 줘");Equal(null,EditIntentGuard.Rejection(history,raw));
            history[0]=new ChatMessage("user","Move the lake to the edge, keeping everything else.");Equal(true,EditIntentGuard.Rejection(history,raw)!=null);
            var directionOnly=ProviderResponse.Command(raw);
            var changes=directionOnly.GetObject("params").GetObjectArray("shape_ops")[0].GetObject("changes");
            changes.Values.Remove("anchor");changes.Values.Remove("placement");
            Equal(true,EditIntentGuard.Rejection(history,SimpleJson.Serialize(directionOnly))!=null);
            directionOnly.SetString("action","revise");directionOnly.Values["option"]=1;
            Equal(true,EditIntentGuard.Rejection(history,SimpleJson.Serialize(directionOnly))!=null);
            var valid="{\"action\":\"generate\",\"params\":{\"shape_ops\":[{\"op\":\"update\",\"id\":\"lake\",\"changes\":{\"anchor\":\"plain\",\"placement\":\"edge\",\"direction\":\"top\"}}]}}";
            Equal(null,EditIntentGuard.Rejection(history,valid));
        });
        foreach(string kind in new[]{"lakeside","winding_valley","branching_ridges","open_basin"})
        Check("Common components express independent "+kind+" fixture without placement failure",()=>
        {
            var state=MapGenAI.TestFixtures.LandscapeCompositionFixtures.Create(kind);MapStateValidation.Validate(state);
            var r=Render(state);Equal(0,r.issues.Length);Equal(false,r.materials.Any(m=>m=="Soil"));
            foreach(var s in state.elevationShapes)Equal(true,r.masks[s.id].Count(x=>x)>10);
        });
        Check("Shared path straight width/endpoints and wider footprint are geometric",()=>
        {
            var a=new LandscapePath(new[]{new Vector2(.2f,.5f),new Vector2(.8f,.5f)},.1f);
            Equal(true,Math.Abs(a.Sample(new Vector2(.5f,.55f)))<.0001);
            Equal(true,a.Sample(new Vector2(.84f,.5f))<0 && a.Sample(new Vector2(.86f,.5f))>0);
            var b=new LandscapePath(new[]{new Vector2(.2f,.5f),new Vector2(.5f,.7f),new Vector2(.8f,.5f)},.2f);
            var c=new LandscapePath(new[]{new Vector2(.2f,.5f),new Vector2(.5f,.7f),new Vector2(.8f,.5f)},.3f);
            for(int y=0;y<N;y++)for(int x=0;x<N;x++){var p=new Vector2(x/(float)N,y/(float)N);if(b.Sample(p)<=0)Equal(true,c.Sample(p)<=0);}
        });
        Check("Natural path has varied shoulders, connected centerline and stable translated geometry",()=>
        {
            var points=new[]{new Vector2(.15f,.5f),new Vector2(.85f,.5f)};
            var path=new LandscapePath(points,.18f,.65f,"sample");
            var same=new LandscapePath(points,.18f,.65f,"sample");
            var changed=new LandscapePath(points,.18f,.65f,"different");
            var moved=new LandscapePath(points.Select(p=>p+new Vector2(0,.1f)).ToArray(),.18f,.65f,"sample");
            var widths=new List<int>();bool differs=false;
            for(int x=20;x<=80;x+=5)
            {
                Equal(true,path.Sample(new Vector2(x/100f,.5f))<0);int width=0;
                for(int z=25;z<75;z++)
                {
                    var p=new Vector2(x/100f,z/100f);float value=path.Sample(p);
                    Equal(value,same.Sample(p));Equal(true,Math.Abs(value-moved.Sample(p+new Vector2(0,.1f)))<.00001f);
                    differs|=Math.Abs(value-changed.Sample(p))>.005f;if(value<=0)width++;
                }
                widths.Add(width);
            }
            Equal(true,widths.Max()-widths.Min()>=3);Equal(true,differs);
            var located=Plain();located.compositeShapes[0].verts=new[]{new[]{.8f,.7f},new[]{.9f,.9f}};
            Equal(true,new MapPlanDescription(true).Shape(located).Contains("북동"));
            Equal(true,new MapPlanDescription(false).Shape(located).Contains("northeast"));
        });
        Check("Path parser/clone/preset/move retain original and translate geometry",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain()}};var original=MapStateCodec.Serialize(state);
            var restored=MapStateCodec.Deserialize(original);Equal(true,Render(state).heights.SequenceEqual(Render(restored).heights));
            var moved=Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"plain\",\"position\":[0.5,0.6]}]}");
            Equal(original,MapStateCodec.Serialize(state));Equal(false,Render(state).heights.SequenceEqual(Render(moved).heights));
            var before=state.elevationShapes[0].compositeShapes[0].verts;var after=moved.elevationShapes[0].compositeShapes[0].verts;
            for(int i=1;i<before.Length;i++)Equal(true,Math.Abs((after[i][1]-before[i][1])-(after[0][1]-before[0][1]))<.00001);
        });
        Check("Attached water including feather fits inside actual plain; source first is inferred",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Pond(),Plain()}};MapStateValidation.Validate(state);
            var r=Render(state);Equal(0,r.issues.Length);Equal(true,r.materials.Count(s=>s=="WaterShallow")>30);
            for(int i=0;i<N*N;i++)if(r.materials[i]!=null)Equal(true,r.masks["plain"][i]);
            Equal(false,r.materials.Any(s=>s=="Soil"));
            var sorted=state.Clone();sorted.elevationShapes.Reverse();Equal(true,r.heights.SequenceEqual(Render(sorted).heights));
        });
        Check("Attached siblings stay separate and parent move carries their relation",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),Pond(),Pond("pond2")}};
            var r=Render(state);Equal(0,r.issues.Length);Equal(false,r.masks["pond"].Where((b,i)=>b && r.masks["pond2"][i]).Any());
            var moved=Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"plain\",\"position\":[0.5,0.65]}]}");
            Equal("plain",moved.elevationShapes[1].anchor);var next=Render(moved);Equal(0,next.issues.Length);
            double Z(bool[] mask)=>Enumerable.Range(0,mask.Length).Where(i=>mask[i]).Average(i=>i/N);
            Equal(true,Z(next.masks["pond"])-Z(r.masks["pond"])>8);
        });
        Check("Beside is outside and on requested side; no-space never silently crops",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),Pond()}};
            state.elevationShapes[1].placement="beside";state.elevationShapes[1].direction="bottom";
            var r=Render(state);Equal(0,r.issues.Length);
            Equal(false,r.masks["pond"].Where((b,i)=>b && r.masks["plain"][i]).Any());
            var huge=state.Clone();huge.elevationShapes[1].compositeShapes[0].w=.9f;huge.elevationShapes[1].compositeShapes[0].h=.9f;
            var failed=Render(huge);Equal(1,failed.issues.Length);Equal(false,failed.materials.Any(s=>s!=null));
        });
        Check("Relation resolves after later obstacles and re-centers the original shape without reshaping it",()=>
        {
            var parent=Plain();var child=Pond();child.compositeShapes[0].center=new[]{.1f,.9f};
            var obstacle=ShapeEdits.ParseShape(SimpleJson.Parse("{\"id\":\"rocks\",\"type\":\"composite\",\"shapes\":[{\"id\":\"r\",\"prim\":\"rect\",\"center\":[0.75,0.55],\"w\":0.16,\"h\":0.24}],\"compose\":[{\"op\":\"add\",\"s\":\"r\",\"e\":1,\"f\":0.01}]}"));
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{parent,child,obstacle}};var r=Render(state);
            Equal(0,r.issues.Length);Equal(false,r.masks["pond"].Where((b,i)=>b && r.masks["rocks"][i]).Any());
            var centered=state.Clone();centered.elevationShapes[1].compositeShapes[0].center=new[]{.5f,.5f};
            Equal(true,r.masks["pond"].SequenceEqual(Render(centered).masks["pond"]));
        });
        Check("Beside cannot erase existing rock or explicit ground outside its anchor",()=>
        {
            var child=Pond();child.placement="beside";child.direction="bottom";
            var clear=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),child}};
            var free=Render(clear);
            // Positive placement control: this same request fits before obstacles are added.
            Equal(0,free.issues.Length);Equal(true,free.materials.Count(m=>m=="WaterShallow")>30);
            foreach(bool material in new[]{false,true})
            {
                // Cover all of the anchor's exterior, including the previously valid pond site.
                // This is a legal authored CSG area, not a mock of the new blocked-mask logic.
                var barrier=Plain();barrier.id="existing";barrier.details=null;
                barrier.compositeShapes.Add(new ShapePrimitive{id="all",prim="rect",center=new[]{.5f,.5f},w=2f,h=2f});
                barrier.compositeOps=new List<ComposeOp>{new ComposeOp{op="sub",a="p",from="all",e=material ? .05f : 1f,f=.001f,fill=material?"SoilRich":null}};
                var before=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),barrier}};
                MapStateValidation.Validate(before);
                var original=Render(before);
                Equal(0,original.issues.Length);
                Equal(true,Enumerable.Range(0,N*N).Any(i=>free.masks["pond"][i] &&
                    (material?original.materials[i]=="SoilRich":original.heights[i]>=.7f)));
                var after=before.Clone();after.elevationShapes.Add(child.Clone());
                var result=Render(after);
                Equal(1,result.issues.Length);Equal(false,result.masks["pond"].Any(b=>b));
                Equal(true,original.heights.SequenceEqual(result.heights));
                Equal(true,original.materials.SequenceEqual(result.materials));
            }
        });
        Check("A relationship that cannot fit reports its failure without globally disabling structures",()=>
        {
            var fitting=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),Pond()}};
            var okay=Render(fitting);Equal(0,okay.issues.Length);Equal(true,okay.masks["pond"].Any(b=>b));
            var oversized=fitting.Clone();oversized.elevationShapes[1].compositeShapes[0].w=.9f;oversized.elevationShapes[1].compositeShapes[0].h=.9f;
            var failed=Render(oversized);
            // The nonempty issue and absent pond prove that the failure branch actually ran.
            Equal(1,failed.issues.Length);Equal(false,failed.masks["pond"].Any(b=>b));
            Equal(false,failed.blocksStructures);
            Equal(true,okay.masks["plain"].SequenceEqual(failed.masks["plain"]));
        });
        Check("Relationships roundtrip; dangling/cyclic anchors and absolute attached moves reject atomically",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),Pond()}};
            var wire=MapStateCodec.Serialize(state);Equal(wire,MapStateCodec.Serialize(MapStateCodec.Deserialize(wire)));
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"remove\",\"id\":\"plain\"}]}"));
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"plain\",\"changes\":{\"anchor\":\"pond\",\"placement\":\"inside\"}}]}"));
            Throws(()=>Edit(state,"{\"shape_ops\":[{\"op\":\"move\",\"id\":\"pond\",\"position\":[0.7,0.5]}]}"));
            Equal(wire,MapStateCodec.Serialize(state));
            var detached=Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"pond\",\"changes\":{\"anchor\":null,\"placement\":null}},{\"op\":\"move\",\"id\":\"pond\",\"position\":[0.7,0.5]}]}");
            Equal(null,detached.elevationShapes[1].anchor);
        });
        Check("New variant affects only requested contour; material edit preserves footprint",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain(),Pond()}};var first=Render(state);
            var material=Edit(state,"{\"shape_ops\":[{\"op\":\"update\",\"id\":\"pond\",\"changes\":{\"fill\":\"SoilRich\"}}]}");
            Equal(true,first.masks["pond"].SequenceEqual(Render(material).masks["pond"]));
            Equal("42",material.elevationShapes[1].variant);
            var changed=state.Clone();changed.elevationShapes[1].variant="471";var next=Render(changed);
            Equal(false,first.masks["pond"].SequenceEqual(next.masks["pond"]));
            Equal(true,first.masks["plain"].SequenceEqual(next.masks["plain"]));
        });
        Check("Plain area remains a counted coverage source without forced Soil",()=>
        {
            var state=new TileMapState{elevationShapes=new List<ElevationShape>{Plain()}};var r=Render(state);var mask=r.masks["plain"];
            var fill=RegionCoverage.Select(N,N,mask,new bool[N*N],.7f);int count=mask.Count(x=>x);
            Equal(true,Math.Abs(fill.Count(x=>x)/(double)count-.7)<.001);Equal(false,r.materials.Any(x=>x!=null));
            var all=RegionCoverage.Select(N,N,mask,new bool[N*N],1f);Equal(false,fill.SequenceEqual(all));
        });
    }
}
