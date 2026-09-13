using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.LLM;
using MapGenAI.UI;

namespace MapGenAI.ImageInput
{
    // Geometry comes from image samples. The model supplies a bounded label for each color group.
    // No smoothing or small-component removal: those can erase narrow rivers and islands.
    public sealed class ColorTerrainPlan
    {
        public readonly int Width,Height;
        public readonly byte[] Groups;
        public readonly byte[][] Colors;
        public readonly int[] Counts;
        ColorTerrainPlan(int width,int height,byte[] groups,byte[][] colors,int[] counts)
        {Width=width;Height=height;Groups=groups;Colors=colors;Counts=counts;}

        public static ColorTerrainPlan Create(byte[] rgb,int width,int height,int maxColors=16)
        {
            if(width<1||height<1||width>256||height>256||rgb==null||rgb.Length!=width*height*3||maxColors<2||maxColors>24)
                throw new FormatException("Invalid image color analysis dimensions");
            var bins=new SortedDictionary<int,Bin>();
            for(int i=0;i<rgb.Length;i+=3)
            {
                int key=(rgb[i]>>3)*1024+(rgb[i+1]>>3)*32+(rgb[i+2]>>3);
                if(!bins.TryGetValue(key,out var bin)){bin=new Bin();bins[key]=bin;}
                bin.Count++;for(int c=0;c<3;c++)bin.Sum[c]+=rgb[i+c];
            }
            var samples=bins.Values.ToList();
            foreach(var sample in samples)sample.Color=sample.Sum.Select(v=>v/sample.Count).ToArray();
            int k=Math.Min(maxColors,samples.Count);
            var centers=new List<double[]>{samples.OrderByDescending(s=>s.Count).First().Color.ToArray()};
            while(centers.Count<k)
            {
                var next=samples.OrderByDescending(s=>centers.Min(c=>Distance(s.Color,c))*Math.Pow(s.Count,.35)).First();
                centers.Add(next.Color.ToArray());
            }
            for(int pass=0;pass<12;pass++)
            {
                var sum=new double[k][];var weight=new int[k];for(int i=0;i<k;i++)sum[i]=new double[3];
                foreach(var sample in samples)
                {
                    int group=Nearest(sample.Color,centers);weight[group]+=sample.Count;
                    for(int c=0;c<3;c++)sum[group][c]+=sample.Color[c]*sample.Count;
                }
                double movement=0;
                for(int i=0;i<k;i++)if(weight[i]>0)
                {var next=sum[i].Select(v=>v/weight[i]).ToArray();movement+=Distance(next,centers[i]);centers[i]=next;}
                if(movement<.01)break;
            }
            var groups=new byte[width*height];var counts=new int[k];
            for(int i=0;i<groups.Length;i++)
            {int group=Nearest(new[]{(double)rgb[i*3],rgb[i*3+1],rgb[i*3+2]},centers);groups[i]=(byte)group;counts[group]++;}
            var used=Enumerable.Range(0,k).Where(i=>counts[i]>0).OrderByDescending(i=>counts[i]).ToArray();
            var remap=new int[k];for(int i=0;i<used.Length;i++)remap[used[i]]=i;
            for(int i=0;i<groups.Length;i++)groups[i]=(byte)remap[groups[i]];
            return new ColorTerrainPlan(width,height,groups,used.Select(i=>centers[i].Select(v=>(byte)Math.Round(v)).ToArray()).ToArray(),used.Select(i=>counts[i]).ToArray());
        }
        public string BuildPrompt(bool korean,string notes=null)
        {
            if(notes!=null && notes.Length>2000)throw new FormatException("Image notes exceed 2000 characters");
            var clusters=new List<object>();
            for(int group=0;group<Colors.Length;group++)
            {
                var indices=Enumerable.Range(0,Groups.Length).Where(i=>Groups[i]==group).ToArray();
                var positions=new List<object>();
                foreach(int index in new[]{indices[indices.Length/4],indices[indices.Length/2],indices[indices.Length*3/4]})
                    positions.Add(new[]{Math.Round((index%Width+.5)/Width,3),Math.Round((index/Width+.5)/Height,3)});
                clusters.Add(new Dictionary<string,object>{{"id",group},{"rgb",Colors[group].Select(c=>(int)c).ToArray()},{"fraction",Math.Round((double)Counts[group]/Groups.Length,4)},{"example_x_z",positions}});
            }
            return @"Read this reasonably clear top-down map reference for RimWorld terrain. If the supplied image is a comparison sheet, the full reference is on the LEFT and numbered WHITE masks on the RIGHT show each color group's exact footprint. Code has already grouped its source pixels by color, retaining their positions. Assign ONE terrain label to EVERY listed color group. Do not redraw geometry or output coordinates. Use the reference image and each group's mask/sample positions to infer its meaning, not generic color associations alone.
Schema: {""title"":""short title"",""notes"":""uncertainties and substitutions"",""clusters"":[{""id"":0,""label"":""soil""}]}.
Allowed labels: natural, mountain, water, shallow_water, soil, rich_soil, sand, marsh, mud, ice.
Coordinates in examples: x=0 left, x=1 right, z=0 BOTTOM, z=1 TOP. In a RimWorld Map Preview solid raised stone and its edge shading mean mountain; flat gravel/stone floors do not. Water may be muted blue, grey or teal, not saturated blue. Tree foliage usually means ordinary soil, not rich soil unless the ground is visibly fertile. Do not turn all dark shadows into mountains. For textured illustrations, use the dominant terrain meaning of a color group and disclose ambiguity. Ordinary ground means soil; natural means deliberately retaining generated terrain, not a substitute for uncertainty. Text/interface/insets are not terrain; disclose if a color group mixes these with terrain. Return every id exactly once, with no extra groups. Return JSON only.
For recognizable native Map Preview images, solid mountain interiors use RGB(54,39,28), with bevel colors (76,52,38) and (28,19,14). Those brown interiors are mountain, not rich soil. Grey patches around mountain walls can be flat rough stone or gravel; do not label them mountain simply because they are rock colored. This palette guidance applies to Map Preview, not every illustration. Preserve the filled mountain interior, not only its outline.
"+(korean?"Write title and notes in Korean.\n":"Write title and notes in English.\n")+"Source color groups: "+SimpleJson.Serialize(clusters)+(string.IsNullOrWhiteSpace(notes)?"":"\nOptional user description: "+notes);
        }
        public ImageCandidate Interpret(string response)
        {
            var root=ProviderResponse.Command(response);var entries=root.GetObjectArray("clusters");
            string title=root.GetString("title"),notes=root.GetString("notes");
            if(string.IsNullOrWhiteSpace(title)||title.Length>120||string.IsNullOrWhiteSpace(notes)||notes.Length>3500||entries==null||entries.Count!=Colors.Length)
                throw new FormatException("Color analysis requires a title, explanation and one label per group");
            var labels=new char[Colors.Length];
            foreach(var entry in entries)
            {
                if(!entry.ContainsKey("id"))throw new FormatException("Missing color group ID");
                int id=entry.GetInt("id");if(id<0||id>=labels.Length||labels[id]!='\0')throw new FormatException("Unknown or duplicate color group ID");
                labels[id]=ImageMapData.Label(entry.GetString("label"));
            }
            var cells=Groups.Select(g=>labels[g]).ToArray();
            if(cells.Distinct().Count()==1)notes+="\n단일 지형 해석입니다. 원본을 확인하세요. / Single terrain class; review the reference.";
            notes+="\n색이 비슷한 지형은 같은 종류로 묶일 수 있습니다. / Similar terrain colors can share a label.";
            var map=new ImageMapData{width=Width,height=Height,cells=new string(cells),note=notes,replaceElevation=true};map.Validate();
            return new ImageCandidate{title=title,notes=notes,map=map};
        }
        static int Nearest(double[] color,List<double[]> centers)
        {int best=0;double distance=double.MaxValue;for(int i=0;i<centers.Count;i++){double d=Distance(color,centers[i]);if(d<distance){best=i;distance=d;}}return best;}
        // Weighted RGB distance is intentionally deterministic and dependency-free; benchmark its limits.
        static double Distance(double[] a,double[] b)=>.3*(a[0]-b[0])*(a[0]-b[0])+.59*(a[1]-b[1])*(a[1]-b[1])+.11*(a[2]-b[2])*(a[2]-b[2]);
        sealed class Bin {public int Count;public double[] Sum=new double[3],Color;}
    }
}
