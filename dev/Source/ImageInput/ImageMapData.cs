using System;
using System.Collections.Generic;
using Verse;

namespace MapGenAI.ImageInput
{
    // Row zero is south/bottom, matching RimWorld z and Unity's GetPixels32 order.
    public sealed class ImageMapData : IExposable
    {
        public int width, height;
        public string cells;
        public string note;
        public const int MaxSide = 256;
        static readonly int[][] Neighbors = {new[]{-1,0},new[]{1,0},new[]{0,-1},new[]{0,1}};
        public static readonly Dictionary<string,char> Names = new Dictionary<string,char> {
            {"natural",'N'},{"mountain",'M'},{"water",'W'},{"shallow_water",'S'},
            {"soil",'G'},{"rich_soil",'R'},{"sand",'B'},{"marsh",'H'},{"mud",'D'},{"ice",'I'}
        };
        public void Validate()
        {
            if (width < 1 || height < 1 || width > MaxSide || height > MaxSide || cells == null || cells.Length != width * height)
                throw new FormatException("Invalid image terrain dimensions");
            foreach(char label in cells) if (!Names.ContainsValue(label)) throw new FormatException("Unknown image terrain label");
            if (note != null && note.Length > 4096) throw new FormatException("Image interpretation note is too long");
        }
        public ImageMapData Clone() => new ImageMapData {width=width,height=height,cells=cells,note=note};
        public void ExposeData()
        {
            Scribe_Values.Look(ref width,"width",0); Scribe_Values.Look(ref height,"height",0);
            Scribe_Values.Look(ref cells,"cells"); Scribe_Values.Look(ref note,"note");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) RepairLoadedData();
        }
        public void RepairLoadedData()
        {
            try {Validate();}
            catch(FormatException)
            {
                const string warning="저장된 이미지 지형이 손상되어 복구했습니다. 확인 후 다시 적용하세요. / Invalid saved image terrain was repaired; review before applying.";
                if(width<1 || height<1 || width>MaxSide || height>MaxSide || cells==null || cells.Length!=width*height)
                {width=height=1;cells="N";}
                else
                {
                    var repaired=cells.ToCharArray();for(int i=0;i<repaired.Length;i++)if(!Names.ContainsValue(repaired[i]))repaired[i]='N';cells=new string(repaired);
                }
                note=warning;Log.Warning("[MapGenAI] "+warning);
            }
        }
        public char At(int x,int z) => cells[z*width+x];
        public static char Label(string name)
        {
            if (name == null || !Names.TryGetValue(name,out var result)) throw new FormatException("Unknown terrain label: " + name);
            return result;
        }

        public List<int> RegionAt(int x,int z)
        {
            Validate();
            if (x<0 || z<0 || x>=width || z>=height) throw new ArgumentOutOfRangeException();
            char label=At(x,z); var seen=new bool[cells.Length]; var queue=new Queue<int>(); var region=new List<int>();
            int start=z*width+x; seen[start]=true; queue.Enqueue(start);
            while(queue.Count>0)
            {
                int current=queue.Dequeue(); region.Add(current);
                int cx=current%width,cz=current/width;
                foreach(var offset in Neighbors)
                {
                    int nx=cx+offset[0],nz=cz+offset[1];
                    if(nx<0 || nz<0 || nx>=width || nz>=height) continue;
                    int next=nz*width+nx;
                    if(!seen[next] && cells[next]==label) {seen[next]=true;queue.Enqueue(next);}
                }
            }
            return region;
        }
        public ImageMapData Relabel(IEnumerable<int> region,char label)
        {
            if(!Names.ContainsValue(label)) throw new FormatException("Unknown terrain label");
            var copy=cells.ToCharArray();
            foreach(int index in region) {if(index<0 || index>=copy.Length) throw new FormatException("Invalid region cell");copy[index]=label;}
            var result=Clone(); result.cells=new string(copy); return result;
        }

        // Reuses the archived GridBuilder's label/elevation contract and nearest-neighbor scaling.
        // No boundary dilation. Downsampling cannot retain features smaller than a target cell.
        public void Apply(Map map,MapGenFloatGrid elevation,MapGenFloatGrid fertility)
        {
            Validate();
            string warning=SamplingWarning(map.Size.x,map.Size.z);
            if(warning!=null)Log.Warning("[MapGenAI] "+warning);
            foreach(var cell in CellRect.WholeMap(map))
            {
                int x=Math.Min(width-1,cell.x*width/map.Size.x),z=Math.Min(height-1,cell.z*height/map.Size.z);
                char label=At(x,z);
                if(label=='N') continue;
                if(label=='M')elevation[cell]=.85f;
                else if(label=='W' || label=='S')elevation[cell]=.2f;
                switch(label)
                {
                    case 'W': fertility[cell]=-2005; break;
                    case 'S': fertility[cell]=-2025; break;
                    case 'G': fertility[cell]=-2085; break;
                    case 'R': fertility[cell]=-2095; break;
                    case 'B': fertility[cell]=-2075; break;
                    case 'H': fertility[cell]=-2045; break;
                    case 'D': fertility[cell]=-2055; break;
                    case 'I': fertility[cell]=-2065; break;
                }
            }
        }
        public string SamplingWarning(int targetWidth,int targetHeight) => width>targetWidth || height>targetHeight
            ? "이미지 지형을 작은 맵으로 축소하므로 가는 통로나 작은 섬이 사라질 수 있습니다. / Downsampling image terrain may lose thin passages or small islands." : null;
    }
}
