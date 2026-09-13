using System;
using System.Threading;

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
