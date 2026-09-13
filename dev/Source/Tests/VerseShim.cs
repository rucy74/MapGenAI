// VerseShim.cs — RimWorld/Verse 타입 스텁 (테스트 전용)
// MapGenParams.cs가 참조하는 Verse/RimWorld 타입만 최소 구현.
// ApplyMutatorsToWorldTile()는 Find.WorldSelector==null로 early return되므로
// Tile/TileMutatorDef는 컴파일만 되면 됨.

using System;
using System.Collections.Generic;
using System.Linq;

// ============================================================
// Verse 네임스페이스
// ============================================================
namespace Verse
{
    public static class Log
    {
        public static void Message(string text)
        {
            Console.WriteLine($"[LOG] {text}");
        }

        public static void Warning(string text)
        {
            Console.WriteLine($"[WRN] {text}");
        }

        public static void Error(string text)
        {
            Console.WriteLine($"[ERR] {text}");
        }
    }

    /// <summary>WorldSelector 스텁. SelectedTile은 항상 -1 반환.</summary>
    public class WorldSelector
    {
        public int SelectedTile { get; set; } = -1;
    }

    /// <summary>WorldGrid 스텁. 인덱서는 항상 null 반환.</summary>
    public class WorldGrid
    {
        public Dictionary<int, RimWorld.Planet.Tile> Tiles = new Dictionary<int, RimWorld.Planet.Tile>();
        public Dictionary<int, List<RimWorld.Planet.PlanetTile>> Neighbors = new Dictionary<int, List<RimWorld.Planet.PlanetTile>>();
        public RimWorld.Planet.Tile this[int index] { get { if (!Tiles.TryGetValue(index, out var tile)) return null; tile.tile=index; return tile; } }
        public void GetTileNeighbors(RimWorld.Planet.PlanetTile tile, List<RimWorld.Planet.PlanetTile> result)
        { if(Neighbors.TryGetValue(tile, out var list)) result.AddRange(list); }
    }

    public static class Find
    {
        public static WorldSelector WorldSelector { get; set; }
        public static WorldGrid WorldGrid { get; set; }
        public static RimWorld.Planet.World World { get; set; }
    }

    public static class Rand
    {
        private static readonly Random _rng = new Random(42);
        public static float Value => (float)_rng.NextDouble();
        public static float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
        public static int Range(int min, int max) => _rng.Next(min, max);
    }

    public static class DefDatabase<T> where T : class
    {
        public static Dictionary<string, T> Definitions = new Dictionary<string, T>();
        public static List<T> AllDefsListForReading => Definitions.Values.ToList();
        public static T GetNamedSilentFail(string defName) => Definitions.TryGetValue(defName, out var def) ? def : null;
    }

    public static class ModsConfig
    {
        public static bool OdysseyActive => false;
    }
}

// ============================================================
// RimWorld.Planet 네임스페이스
// ============================================================
namespace RimWorld.Planet
{
    public class TileMutatorDef
    {
        public string defName;
        public string label;
        public List<string> categories = new List<string>();
        public List<string> overrideCategories = new List<string>();
        public int priority;
        public List<RimWorld.BiomeDef> biomeWhitelist, biomeBlacklist;
        public Verse.FloatRange animalDensityRange = new Verse.FloatRange(0, float.MaxValue), plantDensityRange = new Verse.FloatRange(0, float.MaxValue),
            pollutionRange = new Verse.FloatRange(0,float.MaxValue), averageTemperatureRange = new Verse.FloatRange(float.MinValue,float.MaxValue);
        public Verse.IntRange coastSidesRange = Verse.IntRange.Invalid;
        public RimWorld.Hilliness minHilliness, maxHilliness;
        public bool canSpawnOnRiver=true, canSpawnOnRoad=true;
        public TileMutatorWorker Worker;
        public bool everValid=true;
        public bool EverValid() => everValid;
    }

    public class TileMutatorWorker
    {
        public Func<PlanetTile, bool> Eligibility = _ => true;
        public bool IsValidTile(PlanetTile tile, Verse.WorldGrid layer) => Eligibility(tile);
    }
    public struct PlanetTile
    {
        public int tileId;
        public Tile Tile => Verse.Find.WorldGrid[tileId];
        public Verse.WorldGrid Layer => Verse.Find.WorldGrid;
        public static implicit operator int(PlanetTile value) => value.tileId;
        public static implicit operator PlanetTile(int value) => new PlanetTile{tileId=value};
    }

    public class Tile
    {
        public Hilliness hilliness;
        public PlanetTile tile;
        public RimWorld.BiomeDef PrimaryBiome = new RimWorld.BiomeDef{defName="TemperateForest"};
        public float pollution, temperature=20;
        public bool WaterCovered;
        public List<TileMutatorDef> Mutators { get; set; } = new List<TileMutatorDef>();
        public bool IsCoastal => false;
        public Action<TileMutatorDef> BeforeAdd;

        public void AddMutator(TileMutatorDef def)
        {
            BeforeAdd?.Invoke(def);
            foreach(var old in Mutators.ToList())
                if ((old.categories.Any(def.categories.Contains) && def.priority >= old.priority) || old.categories.Any(def.overrideCategories.Contains)) Mutators.Remove(old);
            Mutators.Add(def);
        }
        public void RemoveMutator(TileMutatorDef def) => Mutators.Remove(def);
    }

    public class SurfaceTile : Tile
    {
        public struct RiverLink { public PlanetTile neighbor; }
        public List<RiverLink> Rivers { get; set; } = new List<RiverLink>();
        public List<object> Roads { get; set; } = new List<object>();
    }
}

// ============================================================
// RimWorld 네임스페이스 (using RimWorld; 해소용)
// ============================================================
namespace RimWorld
{
    public class BiomeDef { public string defName, label; public float animalDensity=1, plantDensity=1; }
    public static class BiomeDefOf
    {
        public static BiomeDef Ocean = new BiomeDef{defName="Ocean"}, Lake = new BiomeDef{defName="Lake"};
    }
    public enum Hilliness { Undefined, Flat, SmallHills, LargeHills, Mountainous, Impassable }
}

// Serialization calls are compile-only here. Actual save/load is checked in the game probe.
namespace Verse
{
    public struct FloatRange
    {
        public float min,max; public FloatRange(float min,float max){this.min=min;this.max=max;}
        public bool Includes(float value) => value >= min && value <= max;
    }
    public struct IntRange
    {
        public int min,max; public IntRange(int min,int max){this.min=min;this.max=max;}
        public static IntRange Invalid => new IntRange(int.MinValue,int.MinValue);
        public static bool operator ==(IntRange a,IntRange b)=>a.min==b.min&&a.max==b.max;
        public static bool operator !=(IntRange a,IntRange b)=>!(a==b);
        public override bool Equals(object obj)=>obj is IntRange other&&this==other;
        public override int GetHashCode()=>min^max;
    }
    public interface IExposable { void ExposeData(); }
    public struct IntVec3 { public int x, y, z; public IntVec3(int x, int y, int z) { this.x=x; this.y=y; this.z=z; } }
    public class Map { public IntVec3 Size; }
    public class MapGenFloatGrid
    {
        private readonly Dictionary<(int,int), float> cells = new Dictionary<(int,int), float>();
        public float this[IntVec3 cell] { get => cells.TryGetValue((cell.x,cell.z), out var value) ? value : 0f; set => cells[(cell.x,cell.z)] = value; }
    }
    public static class MapGenerator { public static MapGenFloatGrid Fertility; }
    public static class CellRect
    {
        public static IEnumerable<IntVec3> WholeMap(Map map)
        {
            for (int z=0; z<map.Size.z; z++) for (int x=0; x<map.Size.x; x++) yield return new IntVec3(x,0,z);
        }
    }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, ResolvingCrossRefs, PostLoadInit }
    public enum LookMode { Undefined, Value, Deep }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string label, T defaultValue = default(T), bool forceSave = false) { }
    }
    public static class Scribe_Deep { public static void Look<T>(ref T value,string label,params object[] args) { } }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> values, string label, LookMode mode) { }
        public static void Look<K,V>(ref Dictionary<K,V> values, string label, LookMode keyMode, LookMode valueMode) { }
    }
}

namespace RimWorld.Planet
{
    public class WorldComponent
    {
        public WorldComponent(World world) { }
        public virtual void ExposeData() { }
    }
    public class World
    {
        private readonly Dictionary<Type, object> components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : WorldComponent
        {
            if (!components.TryGetValue(typeof(T), out var value))
                components[typeof(T)] = value = Activator.CreateInstance(typeof(T), this);
            return (T)value;
        }
    }
}

// ============================================================
// MapGenAI.LLM 네임스페이스 (using MapGenAI.LLM; 해소용)
// ============================================================
namespace MapGenAI.LLM
{
}

// ============================================================
// MapPreview 네임스페이스 (RefreshMapPreview에서 사용)
// ============================================================
namespace MapPreview
{
    public static class WorldInterfaceManager
    {
        public static void RefreshPreview()
        {
            // 테스트에서는 no-op
        }
    }
}

// ============================================================
// Verse.Noise 네임스페이스 (혹시 참조될 경우를 대비한 스텁)
// ============================================================
namespace Verse.Noise
{
    public enum QualityMode { Low, Medium, High }

    public abstract class ModuleBase
    {
        public virtual double GetValue(double x, double y, double z) => 0.0;
    }

    public class Perlin : ModuleBase
    {
        public double Frequency { get; set; } = 1.0;
        public double Lacunarity { get; set; } = 2.0;
        public double Persistence { get; set; } = 0.5;
        public int OctaveCount { get; set; } = 6;
        public int Seed { get; set; } = 0;
        public QualityMode Quality { get; set; } = QualityMode.Medium;

        public override double GetValue(double x, double y, double z) => 0.0;
    }
}
