using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using MapGenAI.MapGen;
using MapGenAI.UI;

class Program
{
    static readonly List<object> reports = new();
    static string F(double n) => n.ToString("0.00000", CultureInfo.InvariantCulture);
    static ElevationShape Shape(string kind, int seed, string size, string gap, string direction) =>
        new() { id="verified_landscape", type="landform", landform=kind, variant=seed.ToString(), size=size, gap=gap, direction=direction };
    sealed class Measure
    {
        public string Kind, Seed, Size, Gap, Direction;
        public int Resolution, PassableCells, LowCells, LowComponents, MissedLowCells, PassableComponents;
        public bool LowAreaReachesEdge, Finite;
        public List<int> ComponentLowAreas = new();
    }
    static Measure Scan(ElevationShape shape, int n=80)
    {
        var sampler = new NaturalLandformGeometry(shape);
        var height = new float[n*n]; var passable = new bool[n*n]; var low = new bool[n*n];
        var m = new Measure { Kind=shape.landform,Seed=shape.variant,Size=shape.size,Gap=shape.gap,Direction=shape.direction,Resolution=n,Finite=true };
        // Independent criteria derived only from generated heights on neutral .2
        // ground. Neither the sampler's floor flag nor production flood-fill is used.
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            var c=sampler.Sample((x+.5f)/n,(z+.5f)/n); int i=z*n+x;
            height[i]=c.influence*c.elevation+(1-c.influence)*.2f;
            m.Finite &= float.IsFinite(height[i]);
            passable[i]=height[i]<.7f; low[i]=height[i]<.1f;
            if(passable[i])m.PassableCells++;if(low[i])m.LowCells++;
        }
        var labels=Enumerable.Repeat(-1,n*n).ToArray();var queue=new Queue<int>(); int largestLow=0;
        for(int start=0;start<labels.Length;start++)
        {
            if(!passable[start] || labels[start]>=0)continue;
            int component=m.PassableComponents++, countLow=0;bool edge=false;
            labels[start]=component;queue.Enqueue(start);
            while(queue.Count>0)
            {
                int i=queue.Dequeue(), x=i%n,z=i/n;
                if(low[i])countLow++;
                edge |= x==0 || z==0 || x==n-1 || z==n-1;
                void Visit(int next) { if(passable[next] && labels[next]<0){labels[next]=component;queue.Enqueue(next);} }
                if(x>0)Visit(i-1);if(x+1<n)Visit(i+1);if(z>0)Visit(i-n);if(z+1<n)Visit(i+n);
            }
            if(countLow>0){m.LowComponents++;m.ComponentLowAreas.Add(countLow);}
            if(countLow>largestLow){largestLow=countLow;m.LowAreaReachesEdge=edge;}
        }
        m.MissedLowCells=m.LowCells-largestLow;return m;
    }
    static TileMapState Edit(TileMapState state,string changes)=>MapStateEditor.Merge(state,MapParameterParser.Parse(SimpleJson.Parse("{\"shape_ops\":[{\"op\":\"update\",\"id\":\"verified_landscape\",\"changes\":"+changes+"}]}")));
    static void Need(bool ok,string label){if(!ok)throw new Exception(label);}
    static void OpeningChecks()
    {
        var shape=Shape("open_basin",38,"large","0.1","134");shape.opening="0.14";
        var state=new TileMapState{animalDensity=1.37f};state.elevationShapes.Add(shape);
        state.elevationShapes.Add(new ElevationShape{id="unrelated",type="bump",position="top_left"});
        string before=MapStateCodec.Serialize(state);
        foreach(string kind in new[]{"winding_valley","foothills"})
        {
            var next=Edit(state,"{\"landform\":\""+kind+"\"}");var s=next.elevationShapes[0];
            Need(s.id==shape.id && s.landform==kind && s.opening==null && s.variant==shape.variant && s.direction==shape.direction && s.size==shape.size && s.gap==shape.gap,"subtype preservation");
            Need(next.animalDensity==1.37f && SimpleJson.Serialize(next.elevationShapes[1])==SimpleJson.Serialize(state.elevationShapes[1]),"unrelated state preservation");
            Need(MapStateCodec.Serialize(state)==before,"source mutated");
            var round=MapStateCodec.Deserialize(MapStateCodec.Serialize(next));Need(round.elevationShapes[0].opening==null && round.elevationShapes[0].landform==kind,"subtype codec");
            reports.Add(new { test="opening subtype cleanup",kind,ok=true,geometry=Scan(s) });
        }
        var cleared=Edit(state,"{\"opening\":null}");Need(cleared.elevationShapes[0].opening==null && shape.opening=="0.14","opening null reset");
        var a=new NaturalLandformGeometry(shape);var b=new NaturalLandformGeometry(cleared.elevationShapes[0]);float delta=0;
        for(int z=0;z<80;z++)for(int x=0;x<80;x++)delta=Math.Max(delta,Math.Abs(a.Sample((x+.5f)/80,(z+.5f)/80).elevation-b.Sample((x+.5f)/80,(z+.5f)/80).elevation));
        Need(delta==0,"null opening differs from documented .14 default");
        foreach(string kind in new[]{"winding_valley","foothills"})
        {
            bool rejected=false;try{Edit(state,"{\"landform\":\""+kind+"\",\"opening\":0.14}");}catch(FormatException){rejected=true;}
            Need(rejected && before==MapStateCodec.Serialize(state),"explicit inapplicable opening must reject atomically");
        }
        reports.Add(new {test="opening null and explicit invalid opening",ok=true,maxElevationDifference=delta});
    }
    static int Main()
    {
        string assembly=typeof(NaturalLandformGeometry).Assembly.Location;
        Console.WriteLine("Loaded "+assembly+" SHA256="+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))).ToLowerInvariant());
        var failures=new List<Measure>();
        foreach(var s in new[]{Shape("foothills",38,"large","0.1","134"),Shape("foothills",95,"1","0.32","155")})
        foreach(int resolution in new[]{80,81,250})
        {
            var m=Scan(s,resolution);reports.Add(new{test="previous corner disconnection",measurement=m});
            Console.WriteLine(JsonSerializer.Serialize(m,new JsonSerializerOptions{IncludeFields=true}));
            if(!m.Finite || m.MissedLowCells!=0 || !m.LowAreaReachesEdge)failures.Add(m);
        }
        OpeningChecks();
        var random=new Random(9262307);int sampled=0;
        foreach(string kind in new[]{"open_basin","winding_valley","foothills"})
        for(int seed=1000;seed<1160;seed++)for(int mode=0;mode<2;mode++)
        {
            string size=mode==0?(seed%2==0?"0.35":"1"):F(.35+random.NextDouble()*.65);
            string gap=mode==0?(seed%3==0?"0.1":"0.32"):F(.1+random.NextDouble()*.22);
            string direction=F(random.NextDouble()*360);
            var s=Shape(kind,seed,size,gap,direction);
            if(kind=="open_basin")s.opening=mode==0?(seed%2==0?"0.08":"0.3"):F(.08+random.NextDouble()*.22);
            var m=Scan(s);sampled++;
            if(!m.Finite || m.LowCells==0 || m.MissedLowCells!=0 || !m.LowAreaReachesEdge)failures.Add(m);
        }
        reports.Add(new{test="fresh corpus",seedFirst=1000,seedsPerKind=160,layouts=sampled,failures=failures.Count});
        File.WriteAllText("result.json",JsonSerializer.Serialize(new{assembly,reports,failures},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("Fresh layouts="+sampled+"; total failures="+failures.Count);
        foreach(var failure in failures.Take(12))Console.WriteLine(JsonSerializer.Serialize(failure,new JsonSerializerOptions{IncludeFields=true}));
        return failures.Count==0?0:1;
    }
}
