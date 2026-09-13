using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MapGenAI.ImageInput
{
    public sealed class VisionImage
    {
        public byte[] bytes;
        public string mimeType;
        public int width,height;
    }
    public static class ImageTextureCodec
    {
        public static readonly Dictionary<char,Color32> Palette = new Dictionary<char,Color32> {
            {'N',new Color32(150,150,150,255)}, {'M',new Color32(80,70,60,255)},
            {'W',new Color32(30,90,210,255)}, {'S',new Color32(70,170,235,255)},
            {'G',new Color32(125,170,80,255)}, {'R',new Color32(65,105,45,255)},
            {'B',new Color32(230,205,125,255)}, {'H',new Color32(85,140,130,255)},
            {'D',new Color32(120,85,60,255)}, {'I',new Color32(220,245,255,255)}
        };
        public static Texture2D Load(string path)
        {
            var info=new FileInfo(path.Trim().Trim('"'));
            if(!info.Exists || info.Length>12*1024*1024) throw new FormatException("Image file missing or larger than 12 MiB");
            var data=File.ReadAllBytes(info.FullName); ImageHeader.Validate(data);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if(!ImageConversion.LoadImage(texture,data,false)) throw new FormatException("Could not decode PNG/JPEG");
                texture.filterMode=FilterMode.Point;
                int orientation=ImageHeader.ExifOrientation(data);
                if(orientation!=1)
                {
                    var upright=Orient(texture,orientation);UnityEngine.Object.Destroy(texture);return upright;
                }
                return texture;
            }
            catch {UnityEngine.Object.Destroy(texture);throw;}
        }
        public static VisionImage ForVision(Texture2D source)
        {
            var original=source.GetPixels32();
            for(int limit=768;limit>=192;limit/=2)
            {
                var resized=Resize(source,limit,original,true);
                try
                {
                    var data=ImageConversion.EncodeToPNG(resized);string mime="image/png";
                    if(data.Length>1024*1024){data=ImageConversion.EncodeToJPG(resized,85);mime="image/jpeg";}
                    if(data.Length<=1024*1024)return new VisionImage{bytes=data,mimeType=mime,width=resized.width,height=resized.height};
                }
                finally {UnityEngine.Object.Destroy(resized);}
            }
            throw new FormatException("Could not reduce image to 1 MiB");
        }
        public static Texture2D Orient(Texture2D source,int orientation)
        {
            if(orientation<1 || orientation>8)throw new FormatException("Invalid EXIF orientation");
            int w=source.width,h=source.height,dw=orientation>=5?h:w,dh=orientation>=5?w:h;
            var original=source.GetPixels32();var pixels=new Color32[original.Length];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int dx=x,dy=y;
                switch(orientation)
                {
                    case 2:dx=w-1-x;break;case 3:dx=w-1-x;dy=h-1-y;break;case 4:dy=h-1-y;break;
                    case 5:dx=y;dy=x;break;case 6:dx=h-1-y;dy=x;break;
                    case 7:dx=h-1-y;dy=w-1-x;break;case 8:dx=y;dy=w-1-x;break;
                }
                pixels[(dh-1-dy)*dw+dx]=original[(h-1-y)*w+x];
            }
            return Make(dw,dh,pixels);
        }
        static Texture2D Resize(Texture2D source,int limit,Color32[] cached=null,bool average=false)
        {
            float ratio=Math.Min(1f,(float)limit/Math.Max(source.width,source.height));
            int w=Math.Max(1,(int)(source.width*ratio)),h=Math.Max(1,(int)(source.height*ratio));
            var old=cached??source.GetPixels32();var pixels=new Color32[w*h];
            for(int z=0;z<h;z++)for(int x=0;x<w;x++)
            {
                if(!average){pixels[z*w+x]=old[(z*source.height/h)*source.width+x*source.width/w];continue;}
                int x0=x*source.width/w,x1=Math.Max(x0+1,(x+1)*source.width/w),z0=z*source.height/h,z1=Math.Max(z0+1,(z+1)*source.height/h);
                long r=0,g=0,b=0,a=0;int count=0;
                for(int sz=z0;sz<z1;sz++)for(int sx=x0;sx<x1;sx++){var c=old[sz*source.width+sx];r+=c.r;g+=c.g;b+=c.b;a+=c.a;count++;}
                pixels[z*w+x]=new Color32((byte)(r/count),(byte)(g/count),(byte)(b/count),(byte)(a/count));
            }
            return Make(w,h,pixels);
        }
        public static ColorTerrainPlan AnalyzeColors(Texture2D source,int maxSide=128)
        {
            var original=source.GetPixels32();var counts=new Dictionary<int,int>();
            int step=Math.Max(1,(original.Length+16383)/16384),sampleCount=0;
            for(int i=0;i<original.Length;i+=step){var c=original[i];int key=c.r*65536+c.g*256+c.b;if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;sampleCount++;}
            // Flat-color previews keep point samples; illustrations average texture within each cell.
            bool flat=System.Linq.Enumerable.Sum(System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(counts.Values,c=>c),12))>=sampleCount*.7;
            var small=Resize(source,Math.Max(1,Math.Min(256,maxSide)),original,!flat);
            try
            {
                var pixels=small.GetPixels32();var rgb=new byte[pixels.Length*3];
                for(int i=0;i<pixels.Length;i++){rgb[i*3]=pixels[i].r;rgb[i*3+1]=pixels[i].g;rgb[i*3+2]=pixels[i].b;}
                return ColorTerrainPlan.Create(rgb,small.width,small.height);
            }
            finally{UnityEngine.Object.Destroy(small);}
        }
        public static VisionImage ForColorVision(Texture2D source,ColorTerrainPlan plan)
        {
            const int width=1024;int height=Math.Max(560,((plan.Colors.Length+3)/4)*128+32);var pixels=new Color32[width*height];
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(24,28,34,255);
            var small=Resize(source,512,null,true);
            try
            {
                var rgb=small.GetPixels32();int ox=(512-small.width)/2,oz=(height-small.height)/2;
                for(int z=0;z<small.height;z++)for(int x=0;x<small.width;x++)pixels[(oz+z)*width+ox+x]=rgb[z*small.width+x];
            }
            finally{UnityEngine.Object.Destroy(small);}
            for(int group=0;group<plan.Colors.Length;group++)
            {
                int left=512+(group%4)*128,bottom=height-((group/4)+1)*128+10;
                float scale=96f/Math.Max(plan.Width,plan.Height);int mw=Math.Max(1,(int)(plan.Width*scale)),mh=Math.Max(1,(int)(plan.Height*scale));
                for(int z=0;z<mh;z++)for(int x=0;x<mw;x++)
                {
                    int sx=x*plan.Width/mw,sz=z*plan.Height/mh;
                    if(plan.Groups[sz*plan.Width+sx]==group)pixels[(bottom+(96-mh)/2+z)*width+left+8+(96-mw)/2+x]=new Color32(245,245,245,255);
                }
                Digits(pixels,width,left+8,bottom+100,group);
                var c=plan.Colors[group];for(int z=0;z<10;z++)for(int x=0;x<32;x++)pixels[(bottom+102+z)*width+left+68+x]=new Color32(c[0],c[1],c[2],255);
            }
            var sheet=Make(width,height,pixels);try{return ForVision(sheet);}finally{UnityEngine.Object.Destroy(sheet);}
        }
        static void Digits(Color32[] pixels,int width,int x,int y,int number)
        {
            string[] glyphs={"111101101101111","010110010010111","111001111100111","111001111001111","101101111001001","111100111001111","111100111101111","111001001001001","111101111101111","111101111001111"};
            foreach(char ch in number.ToString())
            {
                string glyph=glyphs[ch-'0'];for(int row=0;row<5;row++)for(int col=0;col<3;col++)if(glyph[row*3+col]=='1')
                    for(int dy=0;dy<3;dy++)for(int dx=0;dx<3;dx++)pixels[(y+(4-row)*3+dy)*width+x+col*3+dx]=new Color32(255,225,100,255);
                x+=12;
            }
        }
        public static ImageMapData FromPalette(Texture2D source,int maxSide=128)
        {
            var small=Resize(source,Math.Max(1,Math.Min(ImageMapData.MaxSide,maxSide)));
            try
            {
                var pixels=small.GetPixels32();var cells=new char[pixels.Length];int unknown=0;
                for(int i=0;i<pixels.Length;i++)
                {
                    int best=int.MaxValue;char label='N';
                    foreach(var entry in Palette)
                    {
                        var color=entry.Value;var pixel=pixels[i];
                        int distance=(pixel.r-color.r)*(pixel.r-color.r)+(pixel.g-color.g)*(pixel.g-color.g)+(pixel.b-color.b)*(pixel.b-color.b);
                        if(distance<best){best=distance;label=entry.Key;}
                    }
                    if(pixels[i].a<128 || best>3600) {label='N';unknown++;}
                    cells[i]=label;
                }
                return new ImageMapData {width=small.width,height=small.height,cells=new string(cells),replaceElevation=true,note="팔레트 색상 매칭 / Palette color matching. 인식되지 않은 픽셀은 자연 지형으로 유지 / Unmatched pixels use natural terrain: "+unknown+" / "+cells.Length};
            }
            finally {UnityEngine.Object.Destroy(small);}
        }
        public static Texture2D Preview(ImageMapData map,IEnumerable<int> selection=null)
        {
            map.Validate();var pixels=new Color32[map.cells.Length];
            for(int i=0;i<pixels.Length;i++)pixels[i]=Palette[map.cells[i]];
            if(selection!=null)foreach(int i in selection)
            {
                var c=pixels[i];pixels[i]=new Color32((byte)((c.r+255)/2),(byte)((c.g+235)/2),(byte)(c.b/2),255);
            }
            return Make(map.width,map.height,pixels);
        }
        static Texture2D Make(int width,int height,Color32[] pixels)
        {
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            texture.SetPixels32(pixels);texture.Apply();return texture;
        }
    }
}
