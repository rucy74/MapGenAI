using System.Linq;
using RimWorld.Planet;
using Verse;

namespace MapGenAI.MapGen
{
    public static class NativeRiverDirection
    {
        // Same link ordering and heading as TileMutatorWorker_River.GenerateRiverGraph (RW 1.6).
        // Read-only: no worker.Init, random draws, map allocation or change to the user's direction setting.
        public static float Angle(int tileId)
        {
            var tile = tileId < 0 ? null : Find.WorldGrid?[tileId] as SurfaceTile;
            if (tile?.Rivers == null || tile.Rivers.Count == 0) return -1f;
            var links = tile.Rivers.OrderBy(r => -((SurfaceTile)r.neighbor.Tile).riverDist).ToList();
            return Find.WorldGrid.GetHeadingFromTo(links[0].neighbor.Tile.tile, links[links.Count-1].neighbor.Tile.tile);
        }
    }
}
