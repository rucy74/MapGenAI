using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using MapGenAI.LLM;
using MapGenAI.UI;

namespace MapGenAI.ImageInput
{
    public sealed class ImageCandidate
    {
        public string title, notes;
        public ImageMapData map;
    }
    public static class ImageInterpretation
    {
        public const string Prompt = @"Interpret the supplied reference as a RimWorld terrain layout. Return one complete JSON object only.
Schema: {""view"":""top_down|oblique"",""candidates"":[{""title"":""short title"",""notes"":""uncertainties and substitutions"",""background"":""soil"",""regions"":[{""id"":""lake"",""label"":""water"",""vertices"":[[0.2,0.2],[0.8,0.2],[0.8,0.8],[0.2,0.8]]}]}]}.
Preserve a top-down map/sketch as closely as possible, producing ONE candidate. For an oblique photo, produce TWO distinct plausible top-down interpretations, explaining missing depth and assumptions. These are editable terrain plans, not exact reconstructions. Do not claim completion of a game map.
Coordinates x=0 left, x=1 right, z=0 BOTTOM, z=1 TOP. Vertices describe closed polygons; do NOT repeat the first vertex at the end. Later regions overwrite earlier regions. Put a soil island AFTER its containing water region. Preserve narrow passages. At most 32 regions per candidate, 3..64 vertices per polygon, each coordinate in [0,1]. Avoid self-intersections. Do not output character grids or repeated rows.
Labels: natural (leave existing generated terrain), mountain, water, shallow_water, soil, rich_soil, sand, marsh, mud, ice. Trees/forests and buildings are not direct terrain labels: use soil and disclose that vegetation comes from the biome and structures are not reproduced. Never invent missing geometry silently. Notes must explain interpretation limits and any omitted features, in the user's language. Include at least one nonempty region. Output only JSON.";

        public static string BuildPrompt(bool korean,string referenceNotes=null)
        {
            if(referenceNotes!=null && referenceNotes.Length>2000)throw new FormatException("Image reference notes are limited to 2000 characters");
            return Prompt+(korean?"\nWrite titles and notes in Korean.":"\nWrite titles and notes in English.")+
                (string.IsNullOrWhiteSpace(referenceNotes)?"":"\nUser's image legend / interpretation notes: "+referenceNotes.Trim());
        }

        public static List<ImageCandidate> Parse(string response,int width=128,int height=128)
        {
            if(width<1 || height<1 || width>ImageMapData.MaxSide || height>ImageMapData.MaxSide) throw new FormatException("Invalid output dimensions");
            var root=ProviderResponse.Command(response); string view=root.GetString("view");
            var candidates=root.GetObjectArray("candidates");
            if(view!="top_down" && view!="oblique") throw new FormatException("Image view must be top_down or oblique");
            if(candidates==null || candidates.Count!=(view=="top_down"?1:2)) throw new FormatException("Top-down images require one candidate; oblique images require two");
            var result=new List<ImageCandidate>();
            foreach(var candidate in candidates)
            {
                var regions=candidate.GetObjectArray("regions");
                if(regions==null || regions.Count<1 || regions.Count>32) throw new FormatException("Image interpretation requires 1..32 regions");
                string title=candidate.GetString("title"),notes=candidate.GetString("notes");
                if(string.IsNullOrWhiteSpace(title) || title.Length>120 || string.IsNullOrWhiteSpace(notes) || notes.Length>4096) throw new FormatException("Image interpretation requires a title and explanation");
                char background=ImageMapData.Label(candidate.GetString("background"));
                var cells=Enumerable.Repeat(background,width*height).ToArray();
                var ids=new HashSet<string>();
                var owners=Enumerable.Repeat(-1,cells.Length).ToArray();
                var regionNames=new List<string>();
                foreach(var region in regions)
                {
                    string id=region.GetString("id");
                    if(string.IsNullOrEmpty(id) || id.Length>64 || !ids.Add(id)) throw new FormatException("Missing or duplicate image region id");
                    char label=ImageMapData.Label(region.GetString("label"));
                    int owner=regionNames.Count; regionNames.Add(id);
                    var polygon=region.GetNestedFloatArray("vertices");
                    if(polygon==null || polygon.Length<3 || polygon.Length>64) throw new FormatException("Invalid region polygon");
                    // Share degenerate-coordinate/edge/area validation with terrain primitives.
                    ShapeValidation.Validate(new ElevationShape {type="composite",compositeShapes=new List<ShapePrimitive>{new ShapePrimitive{id="region",prim="poly",verts=polygon}},compositeOps=new List<ComposeOp>{new ComposeOp{op="add",s="region",e=1}}});
                    int count=0;
                    for(int z=0;z<height;z++) for(int x=0;x<width;x++)
                        if(Contains(polygon,(x+.5f)/width,(z+.5f)/height)) {cells[z*width+x]=label;owners[z*width+x]=owner;count++;}
                    if(count==0) throw new FormatException("Image region is smaller than a terrain cell: "+id);
                }
                for(int i=0;i<regionNames.Count;i++) if(!owners.Contains(i))
                    notes+="\n겹침으로 사라진 영역 / Fully covered region: "+regionNames[i];
                if(cells.Distinct().Count()==1) notes+="\n단일 지형으로 해석되었습니다. 원본과 비교한 뒤 적용하세요. / Only one terrain class; compare with the reference before applying.";
                var map=new ImageMapData {width=width,height=height,cells=new string(cells),note=notes}; map.Validate();
                result.Add(new ImageCandidate {title=title,notes=notes,map=map});
            }
            return result;
        }
        static bool Contains(float[][] vertices,float x,float z)
        {
            bool inside=false;
            for(int i=0,j=vertices.Length-1;i<vertices.Length;j=i++)
            {
                var a=vertices[i];var b=vertices[j];
                if((a[1]>z)!=(b[1]>z) && x<(b[0]-a[0])*(z-a[1])/(b[1]-a[1])+a[0]) inside=!inside;
            }
            return inside;
        }
    }
}
