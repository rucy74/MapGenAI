using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.ImageInput;
using MapGenAI.MapGen;
using MapGenAI.UI;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MapGenAI.RuntimeProbe
{
    // Uses the real production Unity decoder/preprocessor, and the installed Map Preview renderer.
    public static class RealImageProbe
    {
        public static void Prepare(string manifest,string output)
        {
            var entries=SimpleJson.Parse(File.ReadAllText(manifest)).GetObjectArray("inputs");
            var prepared=new List<object>();
            foreach(var entry in entries)
            {
                string id=SafeId(entry.GetString("id"));
                string path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("path")));
                var image=ImageTextureCodec.Load(path);
                try
                {
                    var payload=ImageTextureCodec.ForVision(image);
                    string file=id+(payload.mimeType=="image/png"?".png":".jpg");
                    File.WriteAllBytes(Path.Combine(output,file),payload.bytes);
                    var colors=ImageTextureCodec.AnalyzeColors(image);
                    var sheet=ImageTextureCodec.ForColorVision(image,colors);
                    string sheetName=id+"-color-sheet"+(sheet.mimeType=="image/png"?".png":".jpg");
                    File.WriteAllBytes(Path.Combine(output,sheetName),sheet.bytes);
                    File.WriteAllText(Path.Combine(output,id+"-color-prompt.txt"),colors.BuildPrompt(true));
                    File.WriteAllText(Path.Combine(output,id+"-color-plan.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"width",colors.Width},{"height",colors.Height},{"groups",colors.Groups.Select(v=>(int)v).ToArray()},{"colors",colors.Colors.Select(c=>c.Select(v=>(int)v).ToArray()).ToArray()}}));
                    int width=128,height=Math.Max(1,(int)(128f*image.height/image.width));
                    if(height>128){height=128;width=Math.Max(1,(int)(128f*image.width/image.height));}
                    var raw=image.GetPixels32();var rgb=new byte[width*height*3];
                    for(int z=0;z<height;z++)for(int x=0;x<width;x++)
                    {
                        var pixel=raw[(z*image.height/height)*image.width+x*image.width/width];int i=(z*width+x)*3;
                        rgb[i]=pixel.r;rgb[i+1]=pixel.g;rgb[i+2]=pixel.b;
                    }
                    string rgbName=id+"-pixels.json";
                    File.WriteAllText(Path.Combine(output,rgbName),SimpleJson.Serialize(new Dictionary<string,object>{{"width",width},{"height",height},{"rgb",Convert.ToBase64String(rgb)},{"order","south row first"}}));
                    prepared.Add(new Dictionary<string,object>{{"id",id},{"image",file},{"mime",payload.mimeType},{"width",width},{"height",height},{"pixels",rgbName},{"colorImage",sheetName},{"colorMime",sheet.mimeType},{"colorPlan",id+"-color-plan.json"},{"colorPrompt",id+"-color-prompt.txt"},{"originalWidth",image.width},{"originalHeight",image.height},{"sentWidth",payload.width},{"sentHeight",payload.height},{"bytes",payload.bytes.Length}});
                }
                finally{UnityEngine.Object.Destroy(image);}
            }
            File.WriteAllText(Path.Combine(output,"prepared.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"inputs",prepared}}));
            File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",true},{"preparedCount",prepared.Count},{"scope","Production Unity image preparation only; no map generation or AI quality claim"}}));
        }

        public static void Generate(string manifest,string output)
        {
            var entries=SimpleJson.Parse(File.ReadAllText(manifest)).GetObjectArray("states");
            var results=new List<object>();
            int center=Find.CurrentMap.Tile;
            var nearby=new List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(center,nearby);
            var terrainTile=nearby.First(t=>Find.WorldGrid[t].PrimaryBiome?.canBuildBase==true && !Find.WorldGrid[t].WaterCovered && !Find.WorldObjects.AnyMapParentAt(t));
            var parent=(MapParent)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            parent.Tile=terrainTile;parent.SetFaction(Faction.OfPlayer);Find.WorldObjects.Add(parent);
            // Generate each sample in a separate process: avoids reusing a partially cleaned map.
            if(entries.Count!=1)throw new InvalidOperationException("Use one image state per isolated run");
            var entry=entries[0];string id=SafeId(entry.GetString("id"));
            string hills=entry.GetString("forceHilliness");if(!string.IsNullOrWhiteSpace(hills))Find.WorldGrid[terrainTile].hilliness=(Hilliness)Enum.Parse(typeof(Hilliness),hills);
            if(entry.ContainsKey("forceMutators"))
            {
                var tile=Find.WorldGrid[terrainTile];foreach(var m in tile.Mutators.ToList())tile.RemoveMutator(m);
                foreach(string name in entry.GetArray("forceMutators"))tile.AddMutator(DefDatabase<TileMutatorDef>.GetNamed(name));
            }
            var state=MapStateCodec.Deserialize(File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest),entry.GetString("path")))));
            MapGenParams.RestoreSnapshot(state,terrainTile);
            var original=DefDatabase<MapGeneratorDef>.GetNamed("Base_Player");
            var generator=(MapGeneratorDef)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(original,null);
            generator.genSteps=new List<GenStepDef>(original.genSteps);
            var trace=new List<object>();
            foreach(var step in original.genSteps)generator.genSteps.Add(new GenStepDef{defName="MapGenAI_Trace_"+step.defName,order=step.order+.001f,genStep=new TraceStep{output=output,stage=step.defName,wanted=state.imageMap,trace=trace}});
            File.WriteAllText(Path.Combine(output,"generation-config.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"imageBase",state.imageMap.replaceElevation},{"mutators",Find.WorldGrid[terrainTile].Mutators.Select(m=>m.defName).ToArray()},{"steps",original.genSteps.Select(s=>new Dictionary<string,object>{{"def",s.defName},{"order",s.order}}).ToArray()}}));
            generator.genSteps.Add(new GenStepDef{defName="MapGenAI_ImageProbeCapture",order=99999,genStep=new CaptureStep{output=output,id=id}});
            var map=MapGenerator.GenerateMap(new IntVec3(128,1,128),parent,generator);
            var wanted=state.imageMap;int waterMatches=0,mountainMatches=0,waterWanted=0,mountainWanted=0,waterExtra=0,mountainExtra=0,groundWanted=0,groundMatched=0;
            foreach(var cell in CellRect.WholeMap(map))
            {
                char label=wanted.At(Math.Min(wanted.width-1,cell.x*wanted.width/map.Size.x),Math.Min(wanted.height-1,cell.z*wanted.height/map.Size.z));
                bool water=map.terrainGrid.TerrainAt(cell).IsWater;
                var rock=cell.GetEdifice(map);bool mountain=rock!=null && rock.def.building?.isNaturalRock==true;
                if(label=='W' || label=='S'){waterWanted++;if(water)waterMatches++;}
                if(label=='M'){mountainWanted++;if(mountain)mountainMatches++;}
                if(label!='N')
                {
                    if(label!='W' && label!='S' && water)waterExtra++;
                    if(label!='M' && mountain)mountainExtra++;
                    if(label!='W' && label!='S' && label!='M'){groundWanted++;if(!water && !mountain)groundMatched++;}
                }
            }
            results.Add(new Dictionary<string,object>{{"id",id},{"tile",(int)terrainTile},{"biome",map.Biome.defName},{"hilliness",map.TileInfo.hilliness.ToString()},{"waterWanted",waterWanted},{"waterMatched",waterMatches},{"mountainWanted",mountainWanted},{"mountainMatched",mountainMatches},{"waterExtra",waterExtra},{"mountainExtra",mountainExtra},{"groundWanted",groundWanted},{"groundMatched",groundMatched}});
            File.WriteAllText(Path.Combine(output,"result.json"),SimpleJson.Serialize(new Dictionary<string,object>{{"ok",true},{"results",results},{"scope","Actual full map generation and native Map Preview colors; success flag means execution, not image fidelity"}}));
        }
        static string SafeId(string value)
        {
            if(string.IsNullOrEmpty(value)||value.Length>80||value.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))throw new FormatException("Invalid fixture ID");return value;
        }
        sealed class CaptureStep:GenStep
        {
            public string output,id;
            public override int SeedPart=>13717;
            public override void Generate(Map map,GenStepParams parms)
            {
                var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="MapPreview");
                var requestType=assembly.GetType("MapPreview.MapPreviewRequest");
                var request=Activator.CreateInstance(requestType,new object[]{Find.World.info.seedString,(int)map.Tile,new IntVec2(map.Size.x,map.Size.z)});
                var result=Activator.CreateInstance(assembly.GetType("MapPreview.MapPreviewResult"),new[]{request});
                var generatorType=assembly.GetType("MapPreview.MapPreviewGenerator");
                var nativeStep=(GenStep)Activator.CreateInstance(generatorType.GetNestedType("PreviewTextureGenStep",BindingFlags.NonPublic),new object[]{result,true});
                nativeStep.Generate(map,parms);
                generatorType.GetMethod("AddBevelToSolidStone",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{result});
                var pixels=(Color[])result.GetType().GetField("Pixels").GetValue(result);
                var texture=new Texture2D(map.Size.x,map.Size.z,TextureFormat.RGBA32,false);
                try {texture.SetPixels(pixels);texture.Apply();File.WriteAllBytes(Path.Combine(output,id+"-generated-preview.png"),ImageConversion.EncodeToPNG(texture));}
                finally{UnityEngine.Object.Destroy(texture);}
                var labels=new char[map.Size.x*map.Size.z];
                foreach(var cell in CellRect.WholeMap(map))
                {
                    var terrain=map.terrainGrid.TerrainAt(cell);
                    labels[cell.z*map.Size.x+cell.x]=MapGenerator.Elevation[cell]>=.7f && !terrain.IsRiver && MapGenerator.Caves[cell]<=0?'M':terrain.IsWater?'W':'G';
                }
                File.WriteAllText(Path.Combine(output,id+"-generated-state.json"),MapStateCodec.Serialize(new TileMapState{imageMap=new ImageMapData{width=map.Size.x,height=map.Size.z,cells=new string(labels),note="Sampled actual generated elevation/caves/terrain; non-water/non-rock grouped as soil for comparison"}}));
            }
        }
        sealed class TraceStep:GenStep
        {
            public string output,stage;public ImageMapData wanted;public List<object> trace;
            public override int SeedPart=>117;
            public override void Generate(Map map,GenStepParams parms)
            {
                int extra=0,missing=0;var elevation=MapGenerator.Elevation;
                foreach(var c in map.AllCells)
                {
                    char label=wanted.At(c.x*wanted.width/map.Size.x,c.z*wanted.height/map.Size.z);
                    if(label!='M' && label!='N' && elevation[c]>=.7f)extra++;
                    if(label=='M' && elevation[c]<.7f)missing++;
                }
                trace.Add(new Dictionary<string,object>{{"after",stage},{"extraElevationMountains",extra},{"missingElevationMountains",missing}});
                File.WriteAllText(Path.Combine(output,"generation-trace.json"),SimpleJson.Serialize(trace));
            }
        }
    }
}
