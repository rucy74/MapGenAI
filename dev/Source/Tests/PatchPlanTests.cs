using System;
using System.Collections.Generic;
using MapGenAI;
using static CoreRegressionTests;

// One failing game patch must not take the world-map button or the other patches with it.
static class PatchPlanTests
{
    static PatchPlan.Result Run(string[] classes, string entry, params string[] failing)
    {
        var failSet = new HashSet<string>(failing);
        return PatchPlan.Apply(classes, c => c == entry, c => c, c =>
        {
            if (failSet.Contains(c)) throw new Exception("Patching exception", new MissingMethodException("target method not found: " + c));
        });
    }

    public static void RunAll()
    {
        // The release assembly lists the button patch last (35th); a shortened list with the same shape.
        var classes = new[] { "Patch_AuthoredGeneration", "Patch_CandidatePreviewRequest", "Patch_StructurePreview", "Patch_TerrainFrom", "WorldInterface_Patch" };

        Check("Entry patch is applied first, the others keep their order", () =>
        {
            Equal("WorldInterface_Patch,Patch_AuthoredGeneration,Patch_CandidatePreviewRequest,Patch_StructurePreview,Patch_TerrainFrom",
                string.Join(",", PatchPlan.Order(classes, c => c == "WorldInterface_Patch")));
        });

        Check("A failing patch turns off only itself; later patches and the button still apply", () =>
        {
            var result = Run(classes, "WorldInterface_Patch", "Patch_CandidatePreviewRequest");
            Equal("WorldInterface_Patch,Patch_AuthoredGeneration,Patch_StructurePreview,Patch_TerrainFrom", string.Join(",", result.Applied));
            Equal(1, result.Failed.Count);
            Equal("Patch_CandidatePreviewRequest: MissingMethodException: target method not found: Patch_CandidatePreviewRequest", result.Failed[0]);
        });

        Check("A failure right after the button, and the last patch failing, still leave everything else applied", () =>
        {
            var result = Run(classes, "WorldInterface_Patch", "Patch_AuthoredGeneration", "Patch_TerrainFrom");
            Equal("WorldInterface_Patch,Patch_CandidatePreviewRequest,Patch_StructurePreview", string.Join(",", result.Applied));
            Equal("Patch_AuthoredGeneration,Patch_TerrainFrom", string.Join(",", result.Failed.ConvertAll(f => f.Substring(0, f.IndexOf(':')))));
        });

        Check("With no failures every patch applies exactly once", () =>
        {
            var result = Run(classes, "WorldInterface_Patch");
            Equal(5, result.Applied.Count);
            Equal(0, result.Failed.Count);
        });
    }
}
