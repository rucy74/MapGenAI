using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MapGenAI.ImageInput;
using MapGenAI.MapGen;
using MapGenAI.UI;
using UnityEngine;
using Verse;
using static CoreRegressionTests;

static class ImageMapTests
{
    static string Polygon(string id,string label,string vertices)=>"{\"id\":\""+id+"\",\"label\":\""+label+"\",\"vertices\":"+vertices+"}";
    static string Candidate(string regions)=>"{\"title\":\"test\",\"notes\":\"test fixture assumptions\",\"background\":\"soil\",\"regions\":["+regions+"]}";
    static string Input(string regions)=>"{\"view\":\"top_down\",\"candidates\":["+Candidate(regions)+"]}";
    public static void RunAll()
    {
        Check("Observed top-level shape shorthand is normalized without accepting ambiguous edits",()=>
        {
            var command=MapGenAI.LLM.ProviderResponse.Command("{\"action\":\"generate\",\"shape_ops\":[{\"op\":\"add\",\"shape\":{\"type\":\"bump\"}}]}");
            Equal(1,MapStateEditor.Merge(null,MapParameterParser.Parse(command.GetObject("params"))).elevationShapes.Count);
            Throws(()=>MapGenAI.LLM.ProviderResponse.Command("{\"action\":\"generate\",\"params\":{},\"shape_ops\":[]}"));
            Throws(()=>MapGenAI.LLM.ProviderResponse.Command("{\"action\":\"generate\",\"danger_density\":2,\"shape_ops\":[]}"));
        });
        Check("Soil image labels preserve existing mountain elevation",()=>
        {
            var map=new Map{Size=new IntVec3(3,1,2)};var e=new MapGenFloatGrid();var f=new MapGenFloatGrid();
            foreach(var c in CellRect.WholeMap(map))e[c]=.9f;
            new ImageMapData{width=3,height=2,cells="GRBHDI"}.Apply(map,e,f);
            Equal(true,CellRect.WholeMap(map).All(c=>e[c]==.9f));Equal(-2085f,f[new IntVec3(0,0,0)]);
        });
        Check("Downsampling limitation is detected for subcell corridors",()=>
        {
            var cells=Enumerable.Repeat('G',256*256).ToArray();for(int z=0;z<256;z++)cells[z*256+127]='W';
            var data=new ImageMapData{width=256,height=256,cells=new string(cells)};
            Equal(true,data.SamplingWarning(100,100)!=null);Equal(true,data.SamplingWarning(256,256)==null);
            var map=new Map{Size=new IntVec3(100,1,100)};var e=new MapGenFloatGrid();var f=new MapGenFloatGrid();data.Apply(map,e,f);
            Equal(0,CellRect.WholeMap(map).Count(c=>f[c]==-2005)); // Documented limit; no automatic dilation invents geometry.
        });
        Check("Malformed named settings reject instead of silently becoming defaults",()=>
        {
            foreach(var input in new[]{"{\"hills\":\"banana\"}","{\"hill_size\":\"huge\"}","{\"river_direction\":\"banana\"}","{\"river_position\":\"banana\"}"})Throws(()=>MapParameterParser.Parse(SimpleJson.Parse(input)));
        });
        Check("Shape prompt examples are complete applicable response envelopes",()=>
        {
            foreach(bool korean in new[]{true,false})
                foreach(var line in ShapeEditPrompt.Rules(korean).Split('\n'))
                {
                    int start=line.IndexOf("{\"action\"",StringComparison.Ordinal);if(start<0)continue;
                    var command=MapGenAI.LLM.ProviderResponse.Command(line.Substring(start,line.LastIndexOf('}')-start+1));
                    var patch=MapParameterParser.Parse(command.GetObject("params"));Equal(true,patch.shape_ops.Count>0);
                }
        });
        Check("Corrupt saved image repairs explicitly while new presets stay strict",()=>
        {
            var data=new ImageMapData{width=2,height=2,cells="WQMG"};data.RepairLoadedData();Equal("WNMG",data.cells);Equal(true,data.note.Contains("repaired"));
            data=new ImageMapData{width=2,height=2,cells="W"};data.RepairLoadedData();Equal("N",data.cells);Equal(1,data.width);
            Throws(()=>MapStateCodec.Deserialize("{\"schema_version\":2,\"state\":{\"imageMap\":{\"width\":2,\"height\":2,\"cells\":\"WQMG\"}}}"));
        });
        Check("Shape difference identifies removals by stable ID",()=>
        {
            var before=new TileMapState{elevationShapes=new List<ElevationShape>{new ElevationShape{id="a",type="bump"},new ElevationShape{id="b",type="bump"}}};
            var after=before.Clone();after.elevationShapes.RemoveAt(0);
            string diff=MapStateDescription.Describe(before,after,false);Equal(true,diff.Contains("a: removed"));Equal(false,diff.Contains("b:"));
        });
        Check("Image polygons preserve north/south and island ordering",()=>
        {
            string lake=Polygon("lake","water","[[0,0.5],[1,0.5],[1,1],[0,1]]"),island=Polygon("island","soil","[[0.4,0.7],[0.6,0.7],[0.6,0.9],[0.4,0.9]]");
            var map=ImageInterpretation.Parse(Input(lake+","+island),20,20).Single().map;
            Equal('G',map.At(10,2));Equal('W',map.At(2,18));Equal('G',map.At(10,16));
            var covered=ImageInterpretation.Parse(Input(island+","+lake),20,20).Single();
            Equal(true,covered.notes.Contains("island"));Equal(covered.notes,covered.map.note);
        });
        Check("Image geometry rejects malformed, intersecting and subcell regions",()=>
        {
            foreach(var vertices in new[]{"[]","[[0,0],[1.2,0],[0,1]]","[[0,0],[1,0],[0.2,0.8],[0.8,0.8],[0,0.3]]","[[0,0],[0.001,0],[0,0.001]]"})
                Throws(()=>ImageInterpretation.Parse(Input(Polygon("a","water",vertices)),20,20));
            Throws(()=>ImageInterpretation.Parse(Input(string.Join(",",Enumerable.Range(0,33).Select(i=>Polygon("r"+i,"water","[[0,0],[1,0],[0,1]]"))))));
        });
        Check("Oblique interpretation requires two alternatives with explanations",()=>
        {
            string candidate=Candidate(Polygon("a","water","[[0,0],[1,0],[0,1]]"));
            Throws(()=>ImageInterpretation.Parse("{\"view\":\"oblique\",\"candidates\":["+candidate+"]}"));
            Equal(2,ImageInterpretation.Parse("{\"view\":\"oblique\",\"candidates\":["+candidate+","+candidate+"]}").Count);
        });
        Check("Region relabel affects only one connected component and undo is exact",()=>
        {
            var map=new ImageMapData{width=5,height=2,cells="WWGWWGGGGG"};
            var region=map.RegionAt(0,0);Equal(2,region.Count);
            var result=ImageRegionCorrection.Apply(map,region,"{\"action\":\"relabel\",\"label\":\"mountain\"}",out _);
            Equal("MMGWWGGGGG",result.cells);Equal("WWGWWGGGGG",map.cells);
            var unchanged=ImageRegionCorrection.Apply(map,region,"{\"action\":\"ask\",\"message\":\"Cannot move contours\"}",out _);Equal(map.cells,unchanged.cells);
            Throws(()=>ImageRegionCorrection.Apply(map,region,"{\"action\":\"move\"}",out _));
            var state=new TileMapState{imageMap=map};var undo=state.Clone();state.imageMap=result;
            Equal(map.cells,undo.imageMap.cells);
            Equal(true,MapStateDescription.Describe(undo,state,true).Contains("2칸"));
        });
        Check("Image presets retain label cells and interpretation notes",()=>
        {
            var state=new TileMapState{imageMap=new ImageMapData{width=3,height=2,cells="MWNSGI",note="해석 😀"}};
            Equal(MapStateCodec.Serialize(state),MapStateCodec.Serialize(MapStateCodec.Deserialize(MapStateCodec.Serialize(state))));
            Throws(()=>new ImageMapData{width=3,height=2,cells="MWN"}.Validate());
        });
        Check("Image grid applies exact footprints and leaves natural cells unchanged",()=>
        {
            var map=new Map{Size=new IntVec3(6,1,4)};var e=new MapGenFloatGrid();var f=new MapGenFloatGrid();
            foreach(var c in CellRect.WholeMap(map)){e[c]=.4f;f[c]=.6f;}
            new ImageMapData{width=3,height=2,cells="MWNSGI"}.Apply(map,e,f);
            foreach(var c in CellRect.WholeMap(map))
            {
                if(c.x<2 && c.z<2){Equal(.85f,e[c]);Equal(.6f,f[c]);}
                if(c.x>=4 && c.z<2){Equal(.4f,e[c]);Equal(.6f,f[c]);}
            }
            Equal(4,CellRect.WholeMap(map).Count(c=>f[c]==-2005));
            Equal(-2025f,f[new IntVec3(0,0,2)]);Equal(-2085f,f[new IntVec3(2,0,2)]);Equal(-2065f,f[new IntVec3(4,0,2)]);
        });
        Check("Image headers reject huge dimensions before texture decoding",()=>
        {
            var png=new byte[24];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(png,0);new byte[]{73,72,68,82}.CopyTo(png,12);png[19]=10;png[23]=20;ImageHeader.Validate(png);
            png[16]=127;Throws(()=>ImageHeader.Validate(png));Throws(()=>ImageHeader.Validate(new byte[24]));
            byte[] jpeg={255,216,255,192,0,8,8,0,20,0,30,0,255,217,0,0,0,0,0,0,0,0,0,0};ImageHeader.Validate(jpeg);
            jpeg[4]=127;Throws(()=>ImageHeader.Validate(jpeg));
        });
        Check("Composite rotation changes the sampled long axis",()=>
        {
            var build=typeof(SdfComposite).GetMethod("BuildSdfFunc",BindingFlags.NonPublic|BindingFlags.Static);
            var part=new ShapePrimitive{id="rect",prim="rect",center=new[]{.5f,.5f},w=.6f,h=.1f};
            var normal=(Func<Vector2,float>)build.Invoke(null,new object[]{part});part.rot=90;
            var rotated=(Func<Vector2,float>)build.Invoke(null,new object[]{part});
            Equal(true,normal(new Vector2(.7f,.5f))<0);Equal(true,rotated(new Vector2(.7f,.5f))>0);Equal(true,rotated(new Vector2(.5f,.7f))<0);
        });
    }
}
