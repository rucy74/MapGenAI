using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.BaseGen;
using Verse;

namespace MapGenAI.MapGen
{
    // Uses the game's native ancientTemple rule chain, including DLC/difficulty-aware interiors.
    public static class AncientDangerGeneration
    {
        public static StructurePlacement Generate(Map map,StructurePlan plan,PlannedRect rect,bool preview)
        {
            var result=new StructurePlacement{id=plan.id,kind=plan.kind,rect=rect};
            if(preview)
            {
                // A reservation outline only: native interiors create pawns, lords and signals and cannot run in Map Preview.
                for(int z=0;z<rect.height;z++)for(int x=0;x<rect.width;x++)
                    if(x==0 || z==0 || x==rect.width-1 || z==rect.height-1)result.wallCells.Add(new[]{rect.x+x,rect.z+z});
                result.walls=result.wallCells.Count;return result;
            }
            if(!BaseGen.symbolStack.Empty)throw new InvalidOperationException("Ancient danger generator cannot run inside another BaseGen operation");
            var previousSettings=BaseGen.globalSettings;var previousStack=BaseGen.symbolStack;
            var before=new HashSet<Thing>(map.listerThings.AllThings);var area=new CellRect(rect.x,rect.z,rect.width,rect.height);
            try
            {
                BaseGen.globalSettings=new GlobalSettings{map=map};BaseGen.symbolStack=new SymbolStack();
                var parameters=new ResolveParams{rect=area,disableSinglePawn=true,disableHives=true,makeWarningLetter=true};
                if(Find.Storyteller.difficulty.peacefulTemples)parameters.podContentsType=PodContentsType.AncientFriendly;
                BaseGen.symbolStack.Push("ancientTemple",parameters);BaseGen.Generate();
                var created=map.listerThings.AllThings.Where(t=>!before.Contains(t)).ToList();
                foreach(var thing in created)
                {
                    if(thing.def.category==ThingCategory.Building || thing.def.category==ThingCategory.Pawn || thing.def.category==ThingCategory.Item)
                        if(!thing.OccupiedRect().FullyContainedWithin(area))throw new InvalidOperationException("Native ancient danger exceeded its reserved footprint: "+plan.id);
                    if(thing.def==ThingDefOf.Wall){result.wallCells.Add(new[]{thing.Position.x,thing.Position.z});result.walls++;result.spawnedWalls++;}
                    if(thing.def==ThingDefOf.AncientCryptosleepCasket)
                    {
                        result.caskets++;
                        if(thing is IThingHolder holder)result.containedThings+=holder.GetDirectlyHeldThings().Count;
                    }
                    if(thing.def.category==ThingCategory.Item || thing.def.defName=="AncientHermeticCrate")result.lootThings++;
                    if(thing is Pawn pawn && pawn.HostileTo(Faction.OfPlayer))result.defenders++;
                    if(thing.def==ThingDefOf.RectTrigger || thing.def==ThingDefOf.SignalAction_Letter)result.warningThings++;
                }
                foreach(var cell in area)
                {
                    if(map.roofGrid.RoofAt(cell)!=null)result.roofCells++;
                    if(map.terrainGrid.TerrainAt(cell).IsFloor)result.floors++;
                }
                // BaseGen catches resolver exceptions internally. Check actual outputs rather than accepting a returned call.
                if(result.spawnedWalls==0 || result.roofCells==0 || result.floors==0 || result.lootThings==0 || result.warningThings<2)
                    throw new InvalidOperationException("Native ancient danger did not complete its shell, roof, loot and warning signals: "+plan.id);
                return result;
            }
            finally{BaseGen.globalSettings=previousSettings;BaseGen.symbolStack=previousStack;}
        }
    }
}
