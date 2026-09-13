using System;

namespace MapGenAI.ImageInput
{
    // Inspect dimensions before asking Unity to allocate decoded pixels.
    public static class ImageHeader
    {
        public static void Validate(byte[] data)
        {
            if(data==null || data.Length<24 || data.Length>12*1024*1024) throw new FormatException("PNG/JPEG must be at most 12 MiB");
            int w=0,h=0;
            if(data[0]==137 && data[1]==80 && data[2]==78 && data[3]==71 && data[4]==13 && data[5]==10 && data[6]==26 && data[7]==10)
            {
                if(data[12]!=73 || data[13]!=72 || data[14]!=68 || data[15]!=82) throw new FormatException("Missing PNG IHDR");
                w=Big32(data,16); h=Big32(data,20);
            }
            else if(data[0]==255 && data[1]==216)
            {
                int p=2;
                while(p<data.Length)
                {
                    if(data[p++]!=255) throw new FormatException("Invalid JPEG marker");
                    while(p<data.Length && data[p]==255) p++;
                    if(p>=data.Length) break;
                    int marker=data[p++];
                    if(marker==217 || marker==218) break;
                    if(marker==1 || (marker>=208 && marker<=215)) continue;
                    if(p+2>data.Length) break;
                    int length=Big16(data,p);
                    if(length<2 || p+length>data.Length) throw new FormatException("Truncated JPEG segment");
                    if((marker>=192 && marker<=195) || (marker>=197 && marker<=199) || (marker>=201 && marker<=203) || (marker>=205 && marker<=207))
                    {
                        if(length<8) throw new FormatException("Invalid JPEG size segment");
                        h=Big16(data,p+3);w=Big16(data,p+5);break;
                    }
                    p+=length;
                }
            }
            if(w<1 || h<1 || w>8192 || h>8192 || (long)w*h>16000000) throw new FormatException("Use a PNG/JPEG up to 8192 per side and 16 million pixels");
        }
        static int Big16(byte[] data,int p) => (data[p]<<8)|data[p+1];
        public static int ExifOrientation(byte[] data)
        {
            if(data.Length<4 || data[0]!=255 || data[1]!=216)return 1;
            int p=2;
            while(p+4<=data.Length)
            {
                if(data[p++]!=255)return 1;while(p<data.Length && data[p]==255)p++;
                if(p+3>data.Length)return 1;int marker=data[p++];if(marker==218 || marker==217)return 1;
                if(marker==1 || (marker>=208 && marker<=215))continue;
                int length=Big16(data,p),end=p+length;if(length<2 || end>data.Length)return 1;
                if(marker==225 && length>=16 && data[p+2]==69 && data[p+3]==120 && data[p+4]==105 && data[p+5]==102 && data[p+6]==0 && data[p+7]==0)
                {
                    int start=p+8;bool little=data[start]==73 && data[start+1]==73;
                    if(!little && !(data[start]==77 && data[start+1]==77))return 1;
                    Func<int,int> u16=i=>little?data[i]|(data[i+1]<<8):Big16(data,i);
                    Func<int,long> u32=i=>little?(long)data[i]|((long)data[i+1]<<8)|((long)data[i+2]<<16)|((long)data[i+3]<<24):(uint)Big32(data,i);
                    if(u16(start+2)!=42)return 1;
                    long offset=start+u32(start+4);if(offset<start+8 || offset+2>end)return 1;
                    int entry=(int)offset+2,count=u16((int)offset);
                    for(int n=0;n<count && entry+12<=end;n++,entry+=12)
                        if(u16(entry)==274 && u16(entry+2)==3 && u32(entry+4)==1)
                        {int value=u16(entry+8);return value>=1 && value<=8?value:1;}
                }
                p=end;
            }
            return 1;
        }
        static int Big32(byte[] data,int p) => (data[p]<<24)|(data[p+1]<<16)|(data[p+2]<<8)|data[p+3];
    }
}
