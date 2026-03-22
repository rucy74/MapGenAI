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
        public int SelectedTile => -1;
    }

    /// <summary>WorldGrid 스텁. 인덱서는 항상 null 반환.</summary>
    public class WorldGrid
    {
        public RimWorld.Planet.Tile this[int index] => null;
    }

    public static class Find
    {
        public static WorldSelector WorldSelector => null;
        public static WorldGrid WorldGrid => null;
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
        public static T GetNamedSilentFail(string defName) => null;
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
    }

    public class Tile
    {
        public List<TileMutatorDef> Mutators { get; set; } = new List<TileMutatorDef>();
        public bool IsCoastal => false;

        public void AddMutator(TileMutatorDef def) => Mutators.Add(def);
        public void RemoveMutator(TileMutatorDef def) => Mutators.Remove(def);
    }

    public class SurfaceTile : Tile
    {
        public List<object> Rivers { get; set; } = new List<object>();
    }
}

// ============================================================
// RimWorld 네임스페이스 (using RimWorld; 해소용)
// ============================================================
namespace RimWorld
{
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
