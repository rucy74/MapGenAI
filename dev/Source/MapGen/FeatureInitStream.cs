using System;
using System.Collections.Generic;
using System.Linq;
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
        // Unedited tiles take the unchanged native path.
        public static FeatureInitPlan Plan(IEnumerable<string> generated, TileWorldSnapshot baseline, TileWorldSnapshot lastApplied, TileWorldSnapshot world,
            Func<string, bool> worldConnection = null, Func<string, int> generationOrder = null, Action<string, string> reserveRemoved = null)
        {
            var originals = baseline == null ? world.mutators : WorldTileEditor.Rebase(baseline, lastApplied, world).mutators;
            var original = new HashSet<string>(originals);
            var names = generated.ToList();
            var added = new HashSet<string>();
            foreach (var name in names) if (!original.Contains(name) && worldConnection?.Invoke(name) != true) added.Add(name);
            // Water-category replacements are intentional changes to that connection. Reserve only removed non-water features.
            var removed = new HashSet<string>(original.Where(n => !names.Contains(n) && worldConnection?.Invoke(n) != true));
            if (removed.Count > 0 && reserveRemoved != null && generationOrder != null)
                return new FeatureInitPlan(added, Peek(), originals.OrderBy(generationOrder).ToList(), names, removed, reserveRemoved, generationOrder);
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
        readonly List<string> original, generated;
        readonly HashSet<string> removed;
        readonly Action<string, string> reserve;
        readonly Func<string, int> generationOrder;
        readonly Dictionary<string, int> cursors = new Dictionary<string, int>();
        public FeatureInitPlan(HashSet<string> added, int streamSeed) { this.added = added; this.streamSeed = streamSeed; }
        public FeatureInitPlan(HashSet<string> added, int streamSeed, List<string> original, List<string> generated,
            HashSet<string> removed, Action<string, string> reserve, Func<string, int> generationOrder) : this(added, streamSeed)
        { this.original = original; this.generated = generated; this.removed = removed; this.reserve = reserve; this.generationOrder = generationOrder; }
        public bool Isolates(string defName) => added.Contains(defName);

        // An added feature runs from its own stream; the shared stream resumes exactly where it was.
        // phase names a later generation step of the feature, whose native stream is shared per GenStep.
        public void Run(string defName, Action action, string phase = null)
        {
            string key = phase ?? "Init";
            if (original != null)
            {
                int index = original.IndexOf(defName);
                // A replacement connection (RiverIsland for River) has a different name but the same native order.
                int boundary = index >= 0 ? index : original.FindIndex(n => generationOrder(n) >= generationOrder(defName));
                if (boundary < 0) boundary = original.Count;
                Advance(boundary, key, phase);
                if (index >= 0) cursors[key] = Math.Max(cursors[key], index + 1);
            }
            if (!Isolates(defName)) action();
            else
            {
                Rand.PushState(FeatureInitStream.SeedFor(streamSeed, phase == null ? defName : defName + "/" + phase));
                try { action(); }
                finally { Rand.PopState(); }
            }
            if (original != null && defName == generated[generated.Count - 1]) Advance(original.Count, key, phase);
        }
        void Advance(int end, string key, string phase)
        {
            cursors.TryGetValue(key, out int cursor);
            for (int i = cursor; i < end; i++) if (removed.Contains(original[i])) reserve(original[i], phase);
            cursors[key] = Math.Max(cursor, end);
        }
    }
}
