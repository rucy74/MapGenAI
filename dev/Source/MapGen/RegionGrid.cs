using System.Collections.Generic;
using Verse;

namespace MapGenAI.MapGen
{
    // Filled by the existing rasterizers, once per generation scope. Never re-infer a mask from colors.
    public sealed class RegionGrid
    {
        public readonly Map Map;
        public readonly string[] Materials;
        public readonly bool[] Flatten;
        public readonly bool[] LocalRoadCells;
        public readonly byte[] NativeWater; // 1 shallow, 2 deep; only water_profile:native
        public readonly bool[] NativeShore; // same water field's dry-side band, details:natural
        public readonly Dictionary<string,bool[]> LandscapeReservations = new Dictionary<string,bool[]>();
        public float[] VegetationWeights; // optional, generated once; never retained outside this generation scope
        readonly Dictionary<string,bool[]> masks = new Dictionary<string,bool[]>();
        readonly System.Action<string,int,string> observeMaterial;
        internal readonly Dictionary<int,CoverageCell> CoverageOriginal = new Dictionary<int,CoverageCell>();
        public RegionGrid(Map map) : this(map,null) { }
        public RegionGrid(Map map, System.Action<string,int,string> observeMaterial) { Map = map; this.observeMaterial=observeMaterial; Materials = new string[map.Size.x * map.Size.z]; Flatten = new bool[Materials.Length]; LocalRoadCells = new bool[Materials.Length]; NativeWater=new byte[Materials.Length]; NativeShore=new bool[Materials.Length]; }
        public int Index(IntVec3 cell) => cell.z * Map.Size.x + cell.x;
        public Dictionary<int,float> CaptureFlattened(MapGenFloatGrid elevation)
        {
            var values=new Dictionary<int,float>();
            foreach(var cell in CellRect.WholeMap(Map))if(Flatten[Index(cell)])values[Index(cell)]=elevation[cell];
            return values;
        }
        public void PreserveLaterHeightEdit(MapGenFloatGrid elevation,Dictionary<int,float> before)
        {
            if(before==null)return;
            foreach(var entry in before)
                if(System.Math.Abs(elevation[new IntVec3(entry.Key%Map.Size.x,0,entry.Key/Map.Size.x)]-entry.Value)>.0001f)Flatten[entry.Key]=false;
        }
        public bool Contains(string id, int x, int z) => x >= 0 && z >= 0 && x < Map.Size.x && z < Map.Size.z &&
            masks.TryGetValue(id, out var mask) && mask[z * Map.Size.x + x];
        public void Record(string id, IntVec3 cell, bool inside, string fill, bool deep)
        {
            int i = Index(cell);
            if (inside && !string.IsNullOrEmpty(id))
            {
                if (!masks.TryGetValue(id, out var mask)) masks[id] = mask = new bool[Materials.Length];
                mask[i] = true;
            }
            if (!string.IsNullOrEmpty(fill))
            {
                NativeWater[i]=0;NativeShore[i]=false;
                Materials[i] = TerrainMaterials.DefName(fill, deep);
                if(inside)observeMaterial?.Invoke(id,i,Materials[i]);
            }
        }
        public bool[] Mask(string id) => masks.TryGetValue(id,out var mask)?(bool[])mask.Clone():new bool[Materials.Length];
        // The source mask precedes passage cuts, so an exit does not erase a ring's interior.
        public bool[] Mask(string id,string part)
        {
            var mask=Mask(id);
            return part=="enclosed"?RegionCoverage.Enclosed(Map.Size.x,Map.Size.z,mask):mask;
        }
        public void SetMask(string id,bool[] mask) { masks[id]=(bool[])mask.Clone(); }
        public static void Record(Map map, string id, IntVec3 cell, bool inside, string fill = null, bool deep = true)
        {
            GenerationContext.Regions(map)?.Record(id, cell, inside, fill, deep);
        }
    }
    internal sealed class CoverageCell
    {
        public TerrainDef original,painted;
        public float originalElevation,paintedElevation;
    }
}
