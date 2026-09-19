using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    public sealed class RoadPlacement
    {
        public string id,kind;
        public int length,paintedCells,protectedCells,blockedCells;
        public readonly List<int[]> path=new List<int[]>();
        public readonly List<int[]> footprint=new List<int[]>();
    }
}
