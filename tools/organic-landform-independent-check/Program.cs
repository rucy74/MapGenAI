using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// No product sources or production flood-fill helpers are compiled into this checker.
// Both frozen assemblies are loaded from captured bytes into distinct load contexts.
static class Program
{
    const int N = 80;
    const float BaseHeight = .2f;
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    static readonly string[] Kinds = { "open_basin", "winding_valley", "foothills" };
    public readonly record struct Cell(float Influence, float Elevation, bool Floor);
    public record Parameters(string Kind, string Layout, int Variant, string Size, string Gap, string Direction, string Opening, string Position = "center");
    sealed class Sampler
    {
        public readonly string Path, Hash;
        readonly AssemblyLoadContext context;
        readonly Type shapeType;
        readonly ConstructorInfo constructor;
        readonly Dictionary<string, FieldInfo> fields;
        readonly Func<object, float, float, Cell> sample;
        public Sampler(string path, string name)
        {
            Path = System.IO.Path.GetFullPath(path);
            byte[] bytes = File.ReadAllBytes(Path); Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            context = new AssemblyLoadContext(name, true);
            using var stream = new MemoryStream(bytes);
            var assembly = context.LoadFromStream(stream);
            shapeType = assembly.GetType("MapGenAI.MapGen.ElevationShape", true);
            var geometry = assembly.GetType("MapGenAI.MapGen.NaturalLandformGeometry", true);
            constructor = geometry.GetConstructor(new[] { shapeType });
            fields = shapeType.GetFields().ToDictionary(f => f.Name);
            var method = geometry.GetMethod("Sample");
            var instance = Expression.Parameter(typeof(object)); var x = Expression.Parameter(typeof(float)); var z = Expression.Parameter(typeof(float));
            var cell = Expression.Variable(method.ReturnType);
            sample = Expression.Lambda<Func<object, float, float, Cell>>(Expression.Block(new[] { cell },
                Expression.Assign(cell, Expression.Call(Expression.Convert(instance, geometry), method, x, z)),
                Expression.New(typeof(Cell).GetConstructor(new[] { typeof(float), typeof(float), typeof(bool) }),
                    Expression.Field(cell, "influence"), Expression.Field(cell, "elevation"), Expression.Field(cell, "floor"))), instance, x, z).Compile();
        }
        public Cell[] Raster(Parameters p)
        {
            object shape = Activator.CreateInstance(shapeType);
            void Set(string key, string value) { if (fields.TryGetValue(key, out var field)) field.SetValue(shape, value); else if (value != null) throw new Exception("Missing field " + key); }
            Set("id", "independent_layout"); Set("type", "landform"); Set("landform", p.Kind); Set("layout", p.Layout);
            Set("variant", p.Variant.ToString(CultureInfo.InvariantCulture)); Set("size", p.Size); Set("gap", p.Gap);
            Set("direction", p.Direction); Set("opening", p.Opening); Set("position", p.Position);
            object geometry = constructor.Invoke(new[] { shape });
            var cells = new Cell[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++) cells[z * N + x] = sample(geometry, (x + .5f) / N, (z + .5f) / N);
            return cells;
        }
    }
    sealed class Measurement
    {
        public bool Finite = true, ValidInfluence = true, MainLowAreaReachesEdge;
        public int PassableCells, LowCells, LowComponents, MissingLowCells, PassableComponents, LargestLowSquare;
        public List<int> ComponentAreas = new(), ComponentLowAreas = new();
        public List<float> ComponentMinimumHeights = new();
        public bool Failed => !Finite || !ValidInfluence || LowCells == 0 || MissingLowCells != 0 || !MainLowAreaReachesEdge;
    }
    static float[] Heights(Cell[] cells) => cells.Select(c => c.Influence * c.Elevation + (1 - c.Influence) * BaseHeight).ToArray();
    static Measurement Measure(float[] heights)
    {
        var m = new Measurement(); var clear = heights.Select(h => h < .7f).ToArray(); var low = heights.Select(h => h < .1f).ToArray();
        m.Finite = heights.All(float.IsFinite); m.PassableCells = clear.Count(b => b); m.LowCells = low.Count(b => b);
        var seen = new bool[heights.Length]; var q = new Queue<int>(); int largestLow = 0;
        // Independent cardinal flood-fill over actual mixed heights. No Cell.Floor,
        // sampler distance, region mask or production connectivity helper is consulted.
        for (int start = 0; start < heights.Length; start++)
        {
            if (seen[start] || !clear[start]) continue;
            int area = 0, lowArea = 0; float minimum = float.MaxValue; bool edge = false;
            seen[start] = true; q.Enqueue(start); m.PassableComponents++;
            while (q.Count > 0)
            {
                int i = q.Dequeue(), x = i % N, z = i / N; area++; if (low[i]) lowArea++;
                minimum = Math.Min(minimum, heights[i]); edge |= x == 0 || z == 0 || x == N - 1 || z == N - 1;
                void Visit(int n) { if (clear[n] && !seen[n]) { seen[n] = true; q.Enqueue(n); } }
                if (x > 0) Visit(i - 1); if (x + 1 < N) Visit(i + 1); if (z > 0) Visit(i - N); if (z + 1 < N) Visit(i + N);
            }
            m.ComponentAreas.Add(area); m.ComponentLowAreas.Add(lowArea); m.ComponentMinimumHeights.Add(minimum);
            if (lowArea > 0) m.LowComponents++;
            if (lowArea > largestLow) { largestLow = lowArea; m.MainLowAreaReachesEdge = edge; }
        }
        m.MissingLowCells = m.LowCells - largestLow;
        var squares = new int[low.Length];
        for (int i = 0; i < low.Length; i++) if (low[i])
        {
            squares[i] = i < N || i % N == 0 ? 1 : 1 + Math.Min(squares[i - 1], Math.Min(squares[i - N], squares[i - N - 1]));
            m.LargestLowSquare = Math.Max(m.LargestLowSquare, squares[i]);
        }
        return m;
    }
    static string Float(double value) => value.ToString("0.00000", CultureInfo.InvariantCulture);
    static IEnumerable<Parameters> Corpus()
    {
        var random = new Random(9232026);
        foreach (string kind in Kinds) for (int seed = 156000; seed < 156160; seed++) for (int mode = 0; mode < 3; mode++)
        {
            string size, gap, opening, direction;
            if (mode == 0)
            {
                size = seed % 2 == 0 ? "0.35" : "1"; gap = seed % 3 == 0 ? "0.1" : "0.32";
                opening = seed % 2 == 0 ? "0.08" : "0.3"; direction = Float((seed * 47 + 13) % 360 + .013);
            }
            else if (mode == 1)
            {
                size = Float(.35 + random.NextDouble() * .65); gap = Float(.1 + random.NextDouble() * .22);
                opening = Float(.08 + random.NextDouble() * .22); direction = Float(random.NextDouble() * 360);
            }
            else
            {
                size = new[] { "small", "medium", "large" }[seed % 3]; gap = Float(.1 + random.NextDouble() * .22);
                opening = Float(.08 + random.NextDouble() * .22);
                direction = new[] { "0.01", "44.99", "90.01", "134.99", "180.01", "224.99", "270.01", "314.99", "359.99" }[seed % 9];
            }
            yield return new Parameters(kind, "organic", seed, size, gap, direction, kind == "open_basin" ? opening : null);
        }
    }
    static bool Same(Cell a, Cell b) => BitConverter.SingleToInt32Bits(a.Influence) == BitConverter.SingleToInt32Bits(b.Influence) &&
        BitConverter.SingleToInt32Bits(a.Elevation) == BitConverter.SingleToInt32Bits(b.Elevation) && a.Floor == b.Floor;
    static void WriteNew(string path, object value)
    { using var stream = new FileStream(path, FileMode.CreateNew); JsonSerializer.Serialize(stream, value, Json); }
    static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: Review <current-pure-dll> <frozen-pure-dll> <output-prefix>"); return 2; }
        string prefix = Path.GetFullPath(args[2]); Directory.CreateDirectory(Path.GetDirectoryName(prefix));
        using var corpusLog = new StreamWriter(new FileStream(prefix + "-corpus.jsonl", FileMode.CreateNew));
        var current = new Sampler(args[0], "current-organic"); var frozen = new Sampler(args[1], "frozen-classic");
        Console.WriteLine("Current " + current.Hash + "\nFrozen " + frozen.Hash);
        int organicFailures = 0, legacyDifferentCells = 0, organicSameAsClassic = 0, tested = 0;
        int classicComparedCells = 0; var failureFiles = new List<string>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var p in Corpus())
        {
            var organic = current.Raster(p); var height = Heights(organic); var measurement = Measure(height);
            measurement.ValidInfluence = organic.All(c => float.IsFinite(c.Influence) && float.IsFinite(c.Elevation) && c.Influence >= 0 && c.Influence <= 1);
            if (measurement.Failed)
            {
                organicFailures++; string name = prefix + "-failure-" + organicFailures.ToString("D4") + ".json"; failureFiles.Add(name);
                WriteNew(name, new { parameters = p, resolution = N, baseHeight = BaseHeight, current.Hash, measurement, mixedHeight = height,
                    cells = organic });
            }
            var old = frozen.Raster(p with { Layout = null });
            var missing = current.Raster(p with { Layout = null }); var classic = current.Raster(p with { Layout = "classic" });
            int difference = 0, organicDifference = 0;
            for (int i = 0; i < old.Length; i++)
            {
                if (!Same(old[i], missing[i]) || !Same(old[i], classic[i])) difference++;
                if (!Same(organic[i], classic[i])) organicDifference++;
            }
            legacyDifferentCells += difference; classicComparedCells += old.Length;
            if (organicDifference == 0) organicSameAsClassic++;
            if (difference > 0) WriteNew(prefix + "-legacy-difference-" + tested.ToString("D4") + ".json", new { parameters = p, old, missing, classic });
            corpusLog.WriteLine(JsonSerializer.Serialize(new { parameters = p, measurement, legacyDifferentCells = difference, organicDifferentCells = organicDifference }, Json));
            tested++;
            if (tested % 160 == 0) { corpusLog.Flush(); Console.WriteLine("Cases=" + tested + " organicFailures=" + organicFailures + " legacyDifferentCells=" + legacyDifferentCells); }
        }
        // A positive control for the checker: sever the low plain with a solid
        // cross-map barrier. The unchanged detector must report disconnected lowland.
        var controlShape = new Parameters("foothills", "organic", 23, "large", "0.25", "bottom", null);
        var control = Heights(current.Raster(controlShape)); var intact = Measure(control);
        for (int x = 0; x < N; x++) control[N / 2 * N + x] = 1;
        var severed = Measure(control); bool detectorControl = !intact.Failed && severed.MissingLowCells > 0 && severed.Failed;
        timer.Stop();
        bool ok = organicFailures == 0 && legacyDifferentCells == 0 && organicSameAsClassic == 0 && detectorControl;
        WriteNew(prefix + "-summary.json", new { ok, current = new { current.Path, current.Hash }, frozen = new { frozen.Path, frozen.Hash },
            resolution = N, baseHeight = BaseHeight, seedsPerKind = 160, kinds = Kinds, layouts = tested, organicFailures, failureFiles,
            classicComparedCells, comparisonFields = "bit-exact influence/elevation and exact floor for both current null and classic against frozen r7", legacyDifferentCells,
            organicSameAsClassic, detectorControl, intactControl = intact, severedControl = severed, seconds = timer.Elapsed.TotalSeconds,
            boundary = "Centered layouts on neutral .2 base. 4-neighbor passability at mixed height <.7; usable lowland at height <.1. No floor flags used for BFS. Tiny higher mountain-wall pockets are not required to join the settlement floor. No claim about moved/clipped maps, native water/structures, visuals or real model quality." });
        Console.WriteLine("Finished ok=" + ok + " layouts=" + tested + " organicFailures=" + organicFailures + " legacyDifferentCells=" + legacyDifferentCells + " detectorControl=" + detectorControl + " seconds=" + timer.Elapsed.TotalSeconds);
        return ok ? 0 : 1;
    }
}
