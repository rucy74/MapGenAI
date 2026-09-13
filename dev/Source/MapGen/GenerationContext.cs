using System;
using System.Threading;
using Verse;

namespace MapGenAI.MapGen
{
    // Each generator reads a fixed snapshot for its own tile, including background previews.
    public static class GenerationContext
    {
        static readonly object GenerationLock = new object();
        [ThreadStatic] static Scope current;
        public static bool Active => current != null;
        public static TileMapState State => current?.State;
        public static int TileId => current?.TileId ?? -1;

        // Capture AFTER this mod's image and SDF layers; reapplying just the raw image would erase edits.
        public static void CaptureImageElevation(Map map,MapGenFloatGrid elevation)
        {
            var image=current?.State?.imageMap;if(image==null || !image.replaceElevation)return;
            current.ImageMap=map;current.ImageElevation=new float[map.Size.x*map.Size.z];
            current.ImageAuthored=new bool[current.ImageElevation.Length];
            foreach(var cell in CellRect.WholeMap(map))
            {
                int index=cell.z*map.Size.x+cell.x;
                current.ImageAuthored[index]=image.At(cell.x*image.width/map.Size.x,cell.z*image.height/map.Size.z)!='N';
                current.ImageElevation[index]=elevation[cell];
            }
        }
        public static void RestoreImageElevation(Map map,MapGenFloatGrid elevation)
        {
            if(current?.ImageMap!=map || current.ImageElevation==null)return;
            foreach(var cell in CellRect.WholeMap(map))
            {
                int index=cell.z*map.Size.x+cell.x;
                if(current.ImageAuthored[index])elevation[cell]=current.ImageElevation[index];
            }
        }

        public static IDisposable Enter(int tileId, TileMapState state)
        {
            var frozen = state?.Clone();
            Monitor.Enter(GenerationLock);
            var scope = new Scope { Previous = current, TileId = tileId, State = frozen };
            current = scope;
            return scope;
        }

        sealed class Scope : IDisposable
        {
            public Scope Previous;
            public int TileId;
            public TileMapState State;
            public Map ImageMap;
            public float[] ImageElevation;
            public bool[] ImageAuthored;
            bool disposed;
            public void Dispose()
            {
                if (disposed) return;
                if (current != this) throw new InvalidOperationException("Generation scopes must close in reverse order");
                disposed = true;
                current = Previous;
                Monitor.Exit(GenerationLock);
            }
        }
    }
}
