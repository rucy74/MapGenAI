using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MapGenAI.MapGen;
using Verse;

namespace MapGenAI.Patches
{
    // Add after terrain/mutators/roads, and after native structures but before the player start.
    // Injecting into this entry also covers Map Preview's filtered list of gensteps.
    [HarmonyPatch(typeof(MapGenerator),nameof(MapGenerator.GenerateContentsIntoMap))]
    static class Patch_AuthoredGeneration
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix(ref IEnumerable<GenStepWithParams> genStepDefs)
        {
            var steps=genStepDefs.ToList();genStepDefs=steps;
            AuthoringGeneration.Begin(steps.Any(s=>s.def.genStep.GetType().FullName=="MapPreview.MapPreviewGenerator+PreviewTextureGenStep"));
            var state=GenerationContext.State;
            if(state==null || (state.elevationShapes.Count==0 && state.structures.Count==0 && state.localRoads.Count==0))return;
            genStepDefs=genStepDefs.Concat(new [] {
                new GenStepWithParams(new GenStepDef {defName="MapGenAI_AuthoredTerrain",order=400,genStep=new AuthoredTerrainStep()},default),
                new GenStepWithParams(new GenStepDef {defName="MapGenAI_RegionCoverage",order=790,genStep=new RegionCoverageStep()},default),
                new GenStepWithParams(new GenStepDef {defName="MapGenAI_FinalRegionCoverage",order=1900,genStep=new RegionCoverageStep()},default),
                new GenStepWithParams(new GenStepDef {defName="MapGenAI_PositionedStructures",order=800,genStep=new PositionedStructureStep()},default)
            }).ToList();
            if(state.localRoads.Count>0)genStepDefs=genStepDefs.Concat(new[]{new GenStepWithParams(new GenStepDef {defName="MapGenAI_LocalRoads",order=410,genStep=new LocalRoadStep()},default)}).ToList();
        }
        static void Postfix(Map map)
        {
            try {PassageGeneration.Check(map);}catch(Exception e){AuthoringGeneration.Fail(e);}
            try {LocalRoadGeneration.Check(map);}catch(Exception e){AuthoringGeneration.RoadFailure(e);}
            // Candidate-only observation; does not alter generation or direct map edits.
            try {CandidatePreviewContext.Current?.InspectWater(map);}catch(Exception e){Log.Warning("[MapGenAI] Candidate water observation unavailable: "+e.Message);}
            AuthoringGeneration.Finish((int)map.Tile);
        }
    }
    sealed class AuthoredTerrainStep : GenStep
    {
        public override int SeedPart => 214536710;
        public override void Generate(Map map,GenStepParams parms)
        {try {AuthoringGeneration.ApplyTerrain(map);PassageGeneration.Reserve(map);}catch(Exception e){AuthoringGeneration.Fail(e);}}
    }
    sealed class LocalRoadStep : GenStep
    {
        public override int SeedPart=>214536713;
        public override void Generate(Map map,GenStepParams parms)
        {try{LocalRoadGeneration.Apply(map);}catch(Exception e){AuthoringGeneration.RoadFailure(e);}}
    }
    sealed class PositionedStructureStep : GenStep
    {
        public override int SeedPart => 214536711;
        public override void Generate(Map map,GenStepParams parms)
        {try {AuthoringGeneration.PlaceStructures(map);}catch(Exception e){AuthoringGeneration.Fail(e);}}
    }
    // First count after native ruins750, then reconcile after late DLC structures/MutatorFinal1600.
    sealed class RegionCoverageStep : GenStep
    {
        public override int SeedPart => 214536712;
        public override void Generate(Map map,GenStepParams parms)
        {try {AuthoringGeneration.ApplyCoverage(map);}catch(Exception e){AuthoringGeneration.Fail(e);}}
    }
}
