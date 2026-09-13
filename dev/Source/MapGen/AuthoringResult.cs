using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    public sealed class AuthoringResult
    {
        public string state;
        public readonly List<string> issues = new List<string>();
        public readonly List<StructurePlacement> placements = new List<StructurePlacement>();
        public int terrainCells, protectedCells;
        public bool preview;
    }
    public sealed class StructurePlacement
    {
        public string id;
        public PlannedRect rect;
        public int walls, floors, spawnedWalls;
        public readonly List<int[]> wallCells = new List<int[]>();
    }
}
