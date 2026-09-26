using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MapGenAI.LLM;
using MapGenAI.MapGen;
using MapGenAI.UI;
using UnityEngine;
using Verse;

namespace MapGenAI.NativeVisualProbe
{
    // Bounded native-noise property checks, using the loaded product implementation.
    // No candidate gate/retry, map generation, provider calls, or product mutation.
    public static class NativeWaterChecks
    {
        const int Size=128, Variants=100;
        sealed class Fixture
        {
            public string name;public Func<Vector2,float> sdf;public List<ShapePrimitive> parts;
            public int left,right,bottom,top,holes;
        }
        sealed class Field
        {
            public Func<Vector2,float> sample;public float shelf,shore;
        }
        sealed class Component
        {
            public int area,x0=Size,z0=Size,x1,z1;public bool touchesEdge;
            public Dictionary<string,object> Describe()=>new Dictionary<string,object>{{"area",area},{"x0",x0},{"z0",z0},{"x1",x1},{"z1",z1},{"touchesMapEdge",touchesEdge}};
        }
        public static bool? Run(Map map,string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);
            string path=Path.Combine(outputDirectory,"native-water-checks.json");
            var type=typeof(SdfComposite).Assembly.GetType("MapGenAI.MapGen.NativeWaterField",false);
            var raster=typeof(SdfComposite).Assembly.GetType("MapGenAI.MapGen.NativeWaterRaster",false)?.GetMethod("DeepMask",BindingFlags.Public|BindingFlags.Static);
            if(type==null||raster==null)
            {
                Save(path,new Dictionary<string,object>{{"schemaVersion",2},{"supported",false},{"skipped",true},{"reason","Loaded product predates NativeWaterField or NativeWaterRaster.DeepMask; no raster property checks executed."}});
                return null;
            }
            var watch=Stopwatch.StartNew();var rows=new List<object>();var checks=new List<object>();
            int failed=0,totalSamples=0;var fixtures=Fixtures();
            Action<bool,string,object> check=(ok,name,evidence)=>{checks.Add(new Dictionary<string,object>{{"ok",ok},{"name",name},{"evidence",evidence}});if(!ok)failed++;};
            try
            {
                foreach(var f in fixtures)
                {
                    var hashes=new HashSet<string>();int connectedFailures=0,holeFailures=0,deepBoundaryFailures=0,rawDeepBoundaryDiagnostics=0;
                    for(int variant=0;variant<Variants;variant++)
                    {
                        string identity="native-property/"+f.name+"/"+variant;
                        var a=Create(type,f,identity);var b=Create(type,f,identity);
                        var water=new bool[Size*Size];var rawDeep=new bool[water.Length];
                        int waterCount=0,rawDeepCount=0,shoreCount=0;
                        bool finite=true,repeat=true,translation=true;uint hash=2166136261u;
                        // Full unfiltered raster, including far cells: a folded displacement
                        // field may create disconnected water beyond the nominal footprint.
                        for(int z=0;z<Size;z++)for(int x=0;x<Size;x++)
                        {
                            var p=new Vector2(x/(float)Size,z/(float)Size);float value=a.sample(p);totalSamples++;
                            finite&=!float.IsNaN(value)&&!float.IsInfinity(value);
                            int i=z*Size+x;water[i]=value<=0;rawDeep[i]=value<=-a.shelf;
                            if(water[i])waterCount++;
                            if(rawDeep[i])rawDeepCount++;
                            if(value>0&&value<=a.shore)shoreCount++;
                            if(x%13==0&&z%13==0)
                            {
                                repeat&=value==b.sample(p);
                                // Same algebra as anchored placement/render. Integer map-cell
                                // translation must preserve the field, without reseeding it.
                                var moved=new Vector2((x+7)/(float)Size,(z-5)/(float)Size)-new Vector2(7f/Size,-5f/Size);
                                translation&=Math.Abs(value-a.sample(moved))<=1e-6f;
                            }
                        }
                        // The product now uses the final raster's actual distance-to-dry
                        // for its submerged shelf. Call that production code directly.
                        var deep=(bool[])raster.Invoke(null,new object[]{Size,Size,water,a.shelf*Size});
                        var repeatedDeep=(bool[])raster.Invoke(null,new object[]{Size,Size,(bool[])water.Clone(),a.shelf*Size});
                        bool depthRepeat=deep.SequenceEqual(repeatedDeep);
                        int deepCount=deep.Count(v=>v),shallowCount=waterCount-deepCount;
                        int outsideWater=deep.Where((v,i)=>v&&!water[i]).Count();
                        for(int i=0;i<water.Length;i++)unchecked{hash=(hash^(uint)(deep[i]?2:water[i]?1:0))*16777619u;}
                        var waterComponents=Components(water,true);var dryHoles=Components(water,false).Where(c=>!c.touchesEdge).ToList();
                        int components=waterComponents.Count,holes=dryHoles.Count;
                        int unbuffered=0,rawUnbuffered=0;
                        for(int z=1;z<Size-1;z++)for(int x=1;x<Size-1;x++)
                        {
                            int i=z*Size+x;
                            if(rawDeep[i]&&(!water[i-1]||!water[i+1]||!water[i-Size]||!water[i+Size]))rawUnbuffered++;
                            if(!deep[i])continue;
                            bool missing=false;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)missing|=!water[(z+dz)*Size+x+dx];
                            if(missing)unbuffered++;
                        }
                        connectedFailures+=components!=1?1:0;holeFailures+=holes!=f.holes?1:0;deepBoundaryFailures+=unbuffered>0?1:0;
                        rawDeepBoundaryDiagnostics+=rawUnbuffered>0?1:0;
                        hashes.Add(hash.ToString("x8"));
                        bool sampled=waterCount>0&&deepCount>0&&shallowCount>0&&shoreCount>0;
                        bool ok=sampled&&finite&&repeat&&depthRepeat&&translation&&components==1&&holes==f.holes&&unbuffered==0&&outsideWater==0;
                        check(ok,f.name+" variant "+variant,new Dictionary<string,object>{{"water",waterCount},{"deep",deepCount},{"shallow",shallowCount},{"shore",shoreCount},{"components",components},{"waterComponents",waterComponents.OrderByDescending(c=>c.area).Select(c=>c.Describe()).ToArray()},{"dryHoles",holes},{"dryHoleComponents",dryHoles.OrderByDescending(c=>c.area).Select(c=>c.Describe()).ToArray()},{"expectedDryHoles",f.holes},{"rasterDeepNextToDry8",unbuffered},{"rasterDeepOutsideWater",outsideWater},{"rawFieldDeepCountDiagnostic",rawDeepCount},{"rawFieldDeepNextToDry4Diagnostic",rawUnbuffered},{"finite",finite},{"repeatExact",repeat},{"rasterDepthRepeatExact",depthRepeat},{"anchorTranslation",translation}});
                    }
                    check(hashes.Count>=20,f.name+" varies across 100 unfiltered seeds",new Dictionary<string,object>{{"distinctMasks",hashes.Count},{"variants",Variants}});
                    rows.Add(new Dictionary<string,object>{{"fixture",f.name},{"variants",Variants},{"distinctMasks",hashes.Count},{"connectedFailures",connectedFailures},{"holeFailures",holeFailures},{"rasterUnbufferedDeepFailures",deepBoundaryFailures},{"rawFieldUnbufferedDeepVariantDiagnostics",rawDeepBoundaryDiagnostics}});
                }
                for(int variant=0;variant<Variants;variant++)
                {
                    int expected,actual;
                    Rand.PushState(842301+variant);try{expected=Rand.Int;}finally{Rand.PopState();}
                    Rand.PushState(842301+variant);
                    try{var a=Create(type,fixtures[variant%fixtures.Count],"rng/"+variant);a.sample(new Vector2(.51f,.47f));actual=Rand.Int;}
                    finally{Rand.PopState();}
                    check(actual==expected,"Native field preserves Rand state "+variant,new Dictionary<string,object>{{"expected",expected},{"actual",actual}});
                }
            }
            catch(Exception e){check(false,"Native field property runner completed",e.ToString());}
            watch.Stop();
            Save(path,new Dictionary<string,object>{{"schemaVersion",2},{"supported",true},{"skipped",false},{"productAssembly",typeof(SdfComposite).Assembly.Location},{"mapArgumentUsed",false},{"fieldSize",Size},{"variantsPerFixture",Variants},{"unfiltered",true},{"resamplingOrRetry",false},{"depthImplementation","Production NativeWaterRaster.DeepMask"},{"rawFieldDepthDiagnosticsAffectPass",false},{"totalSamples",totalSamples},{"elapsedMs",watch.ElapsedMilliseconds},{"passed",checks.Count-failed},{"failed",failed},{"fixtures",rows},{"checks",checks},{"scope","Production NativeWaterField, native displacement modules, and NativeWaterRaster.DeepMask. Raw transformed-SDF depth is retained separately as a diagnostic. Does not execute full map/SdfComposite integration, details toggles, material helpers, legacy replay, Scribe, or prove aesthetic quality; those need separate integration evidence."}});
            return failed==0;
        }
        static Field Create(Type type,Fixture f,string identity)
        {
            var instance=Activator.CreateInstance(type,new object[]{f.sdf,f.parts,(float)Size,(float)Size,1f,identity});
            return new Field{sample=(Func<Vector2,float>)Delegate.CreateDelegate(typeof(Func<Vector2,float>),instance,type.GetMethod("Sample")),shelf=(float)type.GetField("Shelf").GetValue(instance),shore=(float)type.GetField("Shore").GetValue(instance)};
        }
        static List<Fixture> Fixtures()
        {
            var center=new Vector2(.5f,.5f);
            ShapePrimitive Circle(string id,float x,float y,float r)=>new ShapePrimitive{id=id,prim="circle",center=new[]{x,y},r=r};
            var pathParts=new List<ShapePrimitive>{new ShapePrimitive{id="path",prim="path",w=.065f,verts=new[]{new[]{.28f,.43f},new[]{.5f,.55f},new[]{.72f,.45f}}}};
            var path=new LandscapePath(pathParts[0].GetVerts(),pathParts[0].w,0,"property-path");
            return new List<Fixture>
            {
                new Fixture{name="small-circle",sdf=p=>SdfComposite.SdfCircle(p,center,.065f),parts=new List<ShapePrimitive>{Circle("c",.5f,.5f,.065f)},left=42,right=86,bottom=42,top=86},
                new Fixture{name="large-circle",sdf=p=>SdfComposite.SdfCircle(p,center,.19f),parts=new List<ShapePrimitive>{Circle("c",.5f,.5f,.19f)},left=27,right=101,bottom=27,top=101},
                new Fixture{name="elongated-ellipse",sdf=p=>SdfComposite.SdfEllipse(p,center,.32f,.065f),parts=new List<ShapePrimitive>{new ShapePrimitive{id="e",prim="ellipse",center=new[]{.5f,.5f},w=.64f,h=.13f}},left=12,right=116,bottom=42,top=86},
                new Fixture{name="thin-path",sdf=path.Sample,parts=pathParts,left=23,right=105,bottom=42,top=84},
                new Fixture{name="csg-union",sdf=p=>SdfComposite.OpUnion(SdfComposite.SdfCircle(p,new Vector2(.425f,.5f),.12f),SdfComposite.SdfCircle(p,new Vector2(.575f,.5f),.12f)),parts=new List<ShapePrimitive>{Circle("a",.425f,.5f,.12f),Circle("b",.575f,.5f,.12f)},left=26,right=102,bottom=36,top=92},
                new Fixture{name="csg-ring",sdf=p=>SdfComposite.OpSubtract(SdfComposite.SdfCircle(p,center,.10f),SdfComposite.SdfCircle(p,center,.22f)),parts=new List<ShapePrimitive>{Circle("outer",.5f,.5f,.22f),Circle("inner",.5f,.5f,.10f)},left=23,right=105,bottom=23,top=105,holes=1}
            };
        }
        static List<Component> Components(bool[] mask,bool value)
        {
            var seen=new bool[mask.Length];var queue=new int[mask.Length];var result=new List<Component>();
            for(int start=0;start<mask.Length;start++)
            {
                if(seen[start]||mask[start]!=value)continue;var component=new Component();result.Add(component);int head=0,tail=0;queue[tail++]=start;seen[start]=true;
                while(head<tail)
                {
                    int i=queue[head++],x=i%Size,z=i/Size;
                    component.area++;component.x0=Math.Min(component.x0,x);component.x1=Math.Max(component.x1,x);component.z0=Math.Min(component.z0,z);component.z1=Math.Max(component.z1,z);component.touchesEdge|=x==0||z==0||x==Size-1||z==Size-1;
                    void Visit(int n){if(!seen[n]&&mask[n]==value){seen[n]=true;queue[tail++]=n;}}
                    if(x>0)Visit(i-1);if(x<Size-1)Visit(i+1);if(z>0)Visit(i-Size);if(z<Size-1)Visit(i+Size);
                }
            }
            return result;
        }
        static void Save(string path,object data)=>File.WriteAllText(path,SimpleJson.Serialize(data));
    }
}
