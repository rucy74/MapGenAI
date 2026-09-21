using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace MapGenAI.MapGen
{
    // A bridge is a foundation in RimWorld 1.6, not a replacement for the water.
    public static class RoadBridges
    {
        static readonly FieldInfo Foundations=typeof(TerrainGrid).GetField("foundationGrid",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        static readonly FieldInfo Under=typeof(TerrainGrid).GetField("underGrid",BindingFlags.Instance|BindingFlags.NonPublic);

        public static bool Supports(Map map,IntVec3 cell,TerrainDef bridge)
        {
            var grid=map.terrainGrid;var terrain=grid.TerrainAt(cell);
            if(terrain.dangerous || terrain.bridge || terrain.isFoundation || grid.FoundationAt(cell)!=null ||
                grid.UnderTerrainAt(cell)!=null || grid.TempTerrainAt(cell)!=null)return false;
            // Bridgeable alone also admits empty space. Roads only bridge water or wet ground.
            if(!terrain.IsWater && !terrain.IsRiver && terrain.defName!="Mud" && terrain.defName!="MarshyTerrain")return false;
            return bridge!=null && bridge.bridge && bridge.isFoundation && bridge.terrainAffordanceNeeded!=null &&
                terrain.changeable && terrain.affordances.Contains(bridge.terrainAffordanceNeeded);
        }

        public static void Place(Map map,IntVec3 cell,TerrainDef bridge)
        {
            if(AuthoringGeneration.Current?.preview==true)
            {
                // Map Preview's SetTerrain shortcut overwrites water, while the
                // full SetFoundation needs components omitted by a minimal map.
                FoundationGrid(map)[map.cellIndices.CellToIndex(cell)]=bridge;
            }
            else map.terrainGrid.SetFoundation(cell,bridge);
        }
        static TerrainDef[] FoundationGrid(Map map)=>(TerrainDef[])(Foundations?.GetValue(map.terrainGrid)
            ??throw new InvalidOperationException("Unable to access preview bridge foundations"));

        public sealed class Snapshot
        {
            readonly TerrainDef top,under,foundation;
            readonly ColorDef color;
            public Snapshot(Map map,IntVec3 cell)
            {
                var grid=map.terrainGrid;
                top=grid.TopTerrainAt(cell);under=grid.UnderTerrainAt(cell);foundation=grid.FoundationAt(cell);color=grid.ColorAt(cell);
            }
            public void Restore(Map map,IntVec3 cell)
            {
                var grid=map.terrainGrid;
                if(AuthoringGeneration.Current?.preview==true)
                {
                    int index=map.cellIndices.CellToIndex(cell);
                    FoundationGrid(map)[index]=foundation;
                    grid.topGrid[index]=top;((TerrainDef[])Under.GetValue(grid))[index]=under;grid.colorGrid[index]=color;
                    return;
                }
                if(grid.FoundationAt(cell)!=foundation)
                {
                    if(grid.FoundationAt(cell)!=null)grid.RemoveFoundation(cell,false);
                    if(foundation!=null)grid.SetFoundation(cell,foundation);
                }
                grid.SetTerrain(cell,top);
                if(under!=null)grid.SetUnderTerrain(cell,under);
                else if(grid.UnderTerrainAt(cell)!=null)((TerrainDef[])Under.GetValue(grid))[map.cellIndices.CellToIndex(cell)]=null;
                grid.SetTerrainColor(cell,color);
            }
        }
    }
}
