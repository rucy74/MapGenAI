using System;
using System.Collections.Generic;
using System.Linq;
using MapGenAI.MapGen;
using Verse;
using static CoreRegressionTests;

// RimWorld 1.6 seeds one random stream per map (world seed, tile) and runs every tile feature's Init from it in genOrder.
// Worker draw patterns are the decompiled 1.6.4871 Init bodies. Expected centres come from the showcase-01 Player.log.
static class FeatureInitStreamTests
{
    const int ShowcaseMapSeed = 377549301; // HashCombineInt(StableStringHash("mapgenai-showcase-20260927"), 281 * 397)
    const int DrawsBeforeLoop = 1;         // the only offset reproducing both logged river centres (z 101 and z 140)

    static readonly Dictionary<string, int> GenOrder = new Dictionary<string, int> { { "Caves", -100 }, { "HotSprings", 0 }, { "River", 50 }, { "RiverIsland", 50 }, { "Coast", 100 } };
    static bool Connection(string name) => name == "River" || name == "RiverIsland" || name == "Coast"; // River or Coast category

    static int[] Draw(string feature)
    {
        switch (feature)
        {
            // GetRiverCenter x and z on a 250-cell map, then the bend, shallow, bank and width noise seeds.
            case "River": return new[] { (int)(Rand.Range(0.3f, 0.7f) * 250f), (int)(Rand.Range(0.3f, 0.7f) * 250f), Rand.Int, Rand.Int, Rand.Int, Rand.Int };
            case "HotSprings": return new[] { Rand.Int, Rand.Int }; // spring noise, displacement noise
            case "Coast": return new[] { BitConverter.SingleToInt32Bits(Rand.Range(0.1f, 0.2f)), Rand.Int, Rand.Int }; // shore offset, two displacement seeds
            case "Caves": return new[] { Rand.Int }; // direction noise
            case "RiverIsland": return Draw("River").Concat(new[] { Rand.Int, Rand.Int, Rand.Int }).ToArray(); // base.Init, then island shape
            default: throw new ArgumentException(feature);
        }
    }

    static TileWorldSnapshot Snap(IEnumerable<string> names) => new TileWorldSnapshot { mutators = names.ToList() };
    static string[] Ordered(IEnumerable<string> names) => names.Distinct().OrderBy(n => GenOrder[n]).ToArray();
    static string Text(int[] draws) => string.Join(",", draws);

    // One map's native Init loop. original = the world tile before this mod edited it; routed = this build's isolation.
    static Dictionary<string, int[]> Generate(int mapSeed, string[] original, string[] generated, bool routed)
    {
        var draws = new Dictionary<string, int[]>();
        Rand.PushState(mapSeed);
        try
        {
            for (int i = 0; i < DrawsBeforeLoop; i++) _ = Rand.Int;
            var plan = routed ? FeatureInitStream.Plan(generated, Snap(original), Snap(generated), Snap(generated), Connection, n => GenOrder[n], (n, p) => Draw(n)) : null;
            foreach (var name in generated)
            {
                if (plan == null) draws[name] = Draw(name);
                else plan.Run(name, () => draws[name] = Draw(name));
            }
        }
        finally { Rand.PopState(); }
        return draws;
    }

    public static void RunAll()
    {
        Check("Removed native Caves reserve their draws before retained or replacement world water", () =>
        {
            for (int i = 1; i <= 200; i++)
            {
                var original = new[] { "Caves", "HotSprings", "River", "Coast" };
                var reference = Generate(i, original, original, false);
                foreach (var generated in new[] { new[] { "River", "Coast" }, new[] { "HotSprings", "River", "Coast" }, new[] { "RiverIsland", "Coast" } })
                {
                    var actual = Generate(i, original, generated, true);
                    string river = generated.Contains("River") ? "River" : "RiverIsland";
                    Equal(Text(reference["River"]), Text(actual[river].Take(6).ToArray()));
                    if (river == "River") Equal(Text(reference["Coast"]), Text(actual["Coast"]));
                }
            }
        });
        Check("Removed feature reservations preserve the post-terrain bend and do not skip same-order features", () =>
        {
            var calls = new List<string>();
            var original = new[] { "Caves", "HotSprings", "River" };
            var generated = new[] { "Added", "RiverIsland" };
            var order = new Func<string, int>(n => n == "Added" ? 0 : GenOrder[n]);
            Rand.PushState(1);
            try
            {
                var plan = FeatureInitStream.Plan(generated, Snap(original), Snap(generated), Snap(generated), Connection, order,
                    (n, phase) => { calls.Add(n + "/" + phase); _ = Rand.Int; });
                plan.Run("Added", () => calls.Add("Added"));
                plan.Run("RiverIsland", () => calls.Add("RiverIsland"));
                Equal("Caves/,Added,HotSprings/,RiverIsland", string.Join(",", calls));
                calls.Clear();
                plan.Run("Added", () => calls.Add("Added"), "PostTerrain");
                plan.Run("RiverIsland", () => calls.Add("RiverIsland"), "PostTerrain");
                Equal("Caves/PostTerrain,Added,HotSprings/PostTerrain,RiverIsland", string.Join(",", calls));
            }
            finally { Rand.PopState(); }
        });
        Check("Showcase river draws: z 101 alone, z 140 when hot springs draw first from the shared stream", () =>
        {
            Equal(101, Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "River" }, false)["River"][1]);
            Equal(140, Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "HotSprings", "River" }, false)["River"][1]);
        });
        Check("Added hot springs leave every river draw of the showcase tile unchanged", () =>
        {
            var alone = Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "River" }, true)["River"];
            var added = Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "HotSprings", "River" }, true)["River"];
            Equal(Text(alone), Text(added));
            Equal(101, added[1]);
        });
        Check("200 map seeds: added features never shift the draws of the tile's own features", () =>
        {
            var originals = new[] { new[] { "River" }, new[] { "River", "Coast" }, new[] { "Caves", "River" }, new[] { "Coast" } };
            var additions = new[] { new[] { "HotSprings" }, new[] { "Caves" }, new[] { "HotSprings", "Caves" } };
            int compared = 0, routedShifts = 0, nativeShifts = 0;
            for (int i = 0; i < 200; i++)
            {
                int seed = unchecked((int)(2654435761u * (uint)(i + 1)));
                foreach (var original in originals.Select(Ordered))
                {
                    var reference = Generate(seed, original, original, false);
                    foreach (var add in additions)
                    {
                        var generated = Ordered(original.Concat(add));
                        if (generated.Length == original.Length) continue; // already on the tile: not an addition
                        var routed = Generate(seed, original, generated, true);
                        var native = Generate(seed, original, generated, false);
                        foreach (var name in original)
                        {
                            compared++;
                            if (!routed[name].SequenceEqual(reference[name])) routedShifts++;
                            if (!native[name].SequenceEqual(reference[name])) nativeShifts++;
                        }
                    }
                }
            }
            Console.WriteLine("  own-feature draw sets compared=" + compared + ", shifted with routing=" + routedShifts + ", shifted without routing=" + nativeShifts);
            Equal(0, routedShifts);
            if (nativeShifts == 0) throw new Exception("Detector check failed: unrouted additions never shifted a draw");
        });
        Check("A feature the tile already had is not isolated when requested again", () =>
        {
            var tile = new[] { "HotSprings", "River" };
            Equal(true, FeatureInitStream.Plan(tile, Snap(tile), Snap(tile), Snap(tile)) == null);
            var reference = Generate(ShowcaseMapSeed, tile, tile, false);
            var routed = Generate(ShowcaseMapSeed, tile, tile, true);
            foreach (var name in tile) Equal(Text(reference[name]), Text(routed[name]));
        });
        Check("Planning reads the shared stream without consuming it", () =>
        {
            Rand.PushState(ShowcaseMapSeed); int plain = Rand.Int; Rand.PopState();
            Rand.PushState(ShowcaseMapSeed);
            FeatureInitStream.Peek();
            var plan = FeatureInitStream.Plan(new[] { "HotSprings", "River" }, Snap(new[] { "River" }), Snap(new[] { "HotSprings", "River" }), Snap(new[] { "HotSprings", "River" }));
            int next = Rand.Int; Rand.PopState();
            Equal(true, plan != null);
            Equal(plain, next);
        });
        Check("Added features draw from distinct, repeatable streams", () =>
        {
            var original = new[] { "River" }; var generated = new[] { "Caves", "HotSprings", "River" };
            var first = Generate(ShowcaseMapSeed, original, generated, true);
            var second = Generate(ShowcaseMapSeed, original, generated, true);
            foreach (var name in generated) Equal(Text(first[name]), Text(second[name]));
            if (first["Caves"][0] == first["HotSprings"][0]) throw new Exception("Two added features drew the same first value");
        });
        Check("A river variant replacing the tile's river keeps drawing where the river drew (no isolation)", () =>
        {
            var river = Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "River" }, false)["River"];
            var island = Generate(ShowcaseMapSeed, new[] { "River" }, new[] { "HotSprings", "RiverIsland" }, true)["RiverIsland"];
            Equal(Text(river), Text(island.Take(6).ToArray()));
            var plan = FeatureInitStream.Plan(new[] { "HotSprings", "RiverIsland" }, Snap(new[] { "River" }), Snap(new[] { "HotSprings", "RiverIsland" }),
                Snap(new[] { "HotSprings", "RiverIsland" }), Connection);
            Equal(true, plan != null && plan.Isolates("HotSprings") && !plan.Isolates("RiverIsland"));
        });
        Check("Post-terrain step: an added feature leaves the river's bend seed where it was", () =>
        {
            // GenStep_MutatorPostTerrain (SeedPart 562343345): Caves draws two values, then the river builds its node (RiverNode.seed = Rand.Int).
            int Bend(string[] original, string[] generated, bool routed)
            {
                FeatureInitPlan plan = null;
                Rand.PushState(ShowcaseMapSeed);
                try { plan = routed ? FeatureInitStream.Plan(generated, Snap(original), Snap(generated), Snap(generated), Connection) : null; }
                finally { Rand.PopState(); }
                int seed = 0;
                Rand.PushState(562343345);
                try
                {
                    foreach (var name in generated)
                    {
                        Action step = name == "Caves" ? (Action)(() => { _ = Rand.Int; _ = Rand.Int; }) : () => seed = Rand.Int;
                        if (plan == null) step(); else plan.Run(name, step, "PostTerrain");
                    }
                }
                finally { Rand.PopState(); }
                return seed;
            }
            int alone = Bend(new[] { "River" }, new[] { "River" }, false);
            Equal(alone, Bend(new[] { "River" }, new[] { "Caves", "River" }, true));
            if (Bend(new[] { "River" }, new[] { "Caves", "River" }, false) == alone) throw new Exception("Detector check failed: unrouted Caves kept the bend seed");
        });
        Check("Added set is relative to the tile before editing, keeping external changes as original", () =>
        {
            var edited = FeatureInitStream.Plan(new[] { "HotSprings", "River", "External" }, Snap(new[] { "River" }),
                Snap(new[] { "HotSprings", "River" }), Snap(new[] { "HotSprings", "River", "External" }));
            Equal(true, edited != null && edited.Isolates("HotSprings") && !edited.Isolates("River") && !edited.Isolates("External"));
            Equal(true, FeatureInitStream.Plan(new[] { "River" }, null, null, Snap(new[] { "River" })) == null);
            var candidate = FeatureInitStream.Plan(new[] { "HotSprings", "River" }, null, null, Snap(new[] { "River" }));
            Equal(true, candidate != null && candidate.Isolates("HotSprings") && !candidate.Isolates("River"));
            Equal(true, FeatureInitStream.Plan(new[] { "River" }, Snap(new[] { "Caves", "River" }), Snap(new[] { "River" }), Snap(new[] { "River" })) == null);
        });
    }
}
