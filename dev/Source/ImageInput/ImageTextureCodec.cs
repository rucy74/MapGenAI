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
                var resized=Resize(source,limit,original);
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
        static Texture2D Resize(Texture2D source,int limit,Color32[] cached=null)
        {
            float ratio=Math.Min(1f,(float)limit/Math.Max(source.width,source.height));
            int w=Math.Max(1,(int)(source.width*ratio)),h=Math.Max(1,(int)(source.height*ratio));
            var old=cached??source.GetPixels32();var pixels=new Color32[w*h];
            for(int z=0;z<h;z++)for(int x=0;x<w;x++) pixels[z*w+x]=old[(z*source.height/h)*source.width+x*source.width/w];
            return Make(w,h,pixels);
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
                return new ImageMapData {width=small.width,height=small.height,cells=new string(cells),note="팔레트 색상 매칭 / Palette color matching. 인식되지 않은 픽셀은 자연 지형으로 유지 / Unmatched pixels use natural terrain: "+unknown+" / "+cells.Length};
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
