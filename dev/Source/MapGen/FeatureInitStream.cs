using System;
using System.Collections.Generic;
using Verse;

namespace MapGenAI.MapGen
{
    // RimWorld seeds one random stream per map and initializes every tile feature worker from it in genOrder
    // (MapGenerator.GenerateMap, and Map Preview's copy of that loop). A feature this mod added must not take
    // draws from that stream: the tile's own features, including its world river and shore, read them later.
    public static class FeatureInitStream
    {
        // world: the committed world tile, never a candidate substitute. No baseline means the tile was never edited.
        // worldConnection: river and shore features. A new one only replaces the tile's own connection at the same genOrder
        // (River -> RiverIsland, Coast -> Bay) and first draws what that connection drew, so it stays on the shared stream.
        // Returns null when the generated tile has no added feature, so natural generation stays untouched.
        public static FeatureInitPlan Plan(IEnumerable<string> generated, TileWorldSnapshot baseline, TileWorldSnapshot lastApplied, TileWorldSnapshot world,
            Func<string, bool> worldConnection = null)
        {
            var original = new HashSet<string>(baseline == null ? world.mutators : WorldTileEditor.Rebase(baseline, lastApplied, world).mutators);
            var added = new HashSet<string>();
            foreach (var name in generated) if (!original.Contains(name) && worldConnection?.Invoke(name) != true) added.Add(name);
            return added.Count == 0 ? null : new FeatureInitPlan(added, Peek());
        }

        // The next shared draw, without consuming it.
        public static int Peek()
        {
            Rand.PushState();
            try { return Rand.Int; }
            finally { Rand.PopState(); }
        }

        // Stable across processes (unlike string.GetHashCode), so previews and generated maps agree.
        public static int SeedFor(int streamSeed, string defName)
        {
            unchecked
            {
                int name = 23;
                foreach (char c in defName) name = name * 31 + c;
                uint s = (uint)streamSeed;
                return (int)(s ^ ((uint)name + 2654435769u + (s << 6) + (s >> 2)));
            }
        }
    }

    public sealed class FeatureInitPlan
    {
        readonly HashSet<string> added;
        readonly int streamSeed;
        public FeatureInitPlan(HashSet<string> added, int streamSeed) { this.added = added; this.streamSeed = streamSeed; }
        public bool Isolates(string defName) => added.Contains(defName);

        // An added feature runs from its own stream; the shared stream resumes exactly where it was.
        // phase names a later generation step of the feature, whose native stream is shared per GenStep.
        public void Run(string defName, Action action, string phase = null)
        {
            if (!Isolates(defName)) { action(); return; }
            Rand.PushState(FeatureInitStream.SeedFor(streamSeed, phase == null ? defName : defName + "/" + phase));
            try { action(); }
            finally { Rand.PopState(); }
        }
    }
}
