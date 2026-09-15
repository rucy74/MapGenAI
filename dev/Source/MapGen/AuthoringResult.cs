using System.Collections.Generic;

namespace MapGenAI.MapGen
{
    public sealed class AuthoringResult
    {
        public string state;
        public readonly List<string> issues = new List<string>();
        public readonly List<StructurePlacement> placements = new List<StructurePlacement>();
        public readonly List<CoverageResult> coverage = new List<CoverageResult>();
        public int terrainCells, protectedCells;
        public bool preview;
    }
    public sealed class CoverageResult
    {
        public string id;
        public int eligible, selected, existing;
    }
    public sealed class StructurePlacement
    {
        public string id,kind="ruin";
        public PlannedRect rect;
        public int walls, floors, spawnedWalls;
        public int roofCells,caskets,containedThings,lootThings,defenders,warningThings;
        public readonly List<int[]> wallCells = new List<int[]>();
    }
}
