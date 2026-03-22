using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// 타일의 현재 맵 생성 상태. WorldComponent에 영구 저장됨.
    /// 이건 "이전 생성 이력"이 아니라 "타일이 현재 어떤 상태인지"를 나타냄.
    /// 바닐라 tile.hilliness와 동일한 역할 — 방향/위치 등 세밀한 정보 포함.
    /// </summary>
    public class TileMapState : IExposable
    {
        // 지형
        public string hills = "none";
        public float hillAmount = 1f;
        public List<ElevationShape> elevationShapes = new List<ElevationShape>();
        public float vegetationDensity = 1f;
        public float fertilityOffset = 0f;
        public float animalDensity = 1f;

        // 수계
        public bool hasRiver = false;
        public float riverDirectionAngle = -1f;
        public float riverXPosition = 0.5f;
        public float riverZPosition = 0.5f;
        public bool straightRiver = false;

        // 지물
        public bool hasRoads = false;
        public bool hasCaves = false;
        public bool cavesExplicitlySet = false;
        public int geyserCount = -1;
        public bool hasRockChunks = true;
        public float hillSize = 0.021f;
        public float hillSmoothness = 2.0f;

        // TileMutator
        public List<string> mutators = new List<string>();
        public List<string> removeMutators = new List<string>();

        // 해안/석재/광석/폐허
        public string coastDirection = "auto";
        public int rockCount = -1;
        public float oreDensity = 1f;
        public List<string> rockTypes = new List<string>();
        public float ruinDensity = 1f;
        public float dangerDensity = 1f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref hills, "hills", "none");
            Scribe_Values.Look(ref hillAmount, "hillAmount", 1f);
            Scribe_Values.Look(ref vegetationDensity, "vegetationDensity", 1f);
            Scribe_Values.Look(ref fertilityOffset, "fertilityOffset", 0f);
            Scribe_Values.Look(ref animalDensity, "animalDensity", 1f);

            Scribe_Values.Look(ref hasRiver, "hasRiver", false);
            Scribe_Values.Look(ref riverDirectionAngle, "riverDirectionAngle", -1f);
            Scribe_Values.Look(ref riverXPosition, "riverXPosition", 0.5f);
            Scribe_Values.Look(ref riverZPosition, "riverZPosition", 0.5f);
            Scribe_Values.Look(ref straightRiver, "straightRiver", false);

            Scribe_Values.Look(ref hasRoads, "hasRoads", false);
            Scribe_Values.Look(ref hasCaves, "hasCaves", false);
            Scribe_Values.Look(ref cavesExplicitlySet, "cavesExplicitlySet", false);
            Scribe_Values.Look(ref geyserCount, "geyserCount", -1);
            Scribe_Values.Look(ref hasRockChunks, "hasRockChunks", true);
            Scribe_Values.Look(ref hillSize, "hillSize", 0.021f);
            Scribe_Values.Look(ref hillSmoothness, "hillSmoothness", 2.0f);

            Scribe_Values.Look(ref coastDirection, "coastDirection", "auto");
            Scribe_Values.Look(ref rockCount, "rockCount", -1);
            Scribe_Values.Look(ref oreDensity, "oreDensity", 1f);
            Scribe_Values.Look(ref ruinDensity, "ruinDensity", 1f);
            Scribe_Values.Look(ref dangerDensity, "dangerDensity", 1f);

            Scribe_Collections.Look(ref elevationShapes, "elevationShapes", LookMode.Deep);
            Scribe_Collections.Look(ref mutators, "mutators", LookMode.Value);
            Scribe_Collections.Look(ref removeMutators, "removeMutators", LookMode.Value);
            Scribe_Collections.Look(ref rockTypes, "rockTypes", LookMode.Value);

            // 로드 시 null 방지
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (elevationShapes == null) elevationShapes = new List<ElevationShape>();
                if (mutators == null) mutators = new List<string>();
                if (removeMutators == null) removeMutators = new List<string>();
                if (rockTypes == null) rockTypes = new List<string>();
            }
        }

        /// <summary>깊은 복사 (undo 스냅샷용).</summary>
        public TileMapState Clone()
        {
            return new TileMapState
            {
                hills = hills,
                hillAmount = hillAmount,
                vegetationDensity = vegetationDensity,
                fertilityOffset = fertilityOffset,
                animalDensity = animalDensity,
                hasRiver = hasRiver,
                riverDirectionAngle = riverDirectionAngle,
                riverXPosition = riverXPosition,
                riverZPosition = riverZPosition,
                straightRiver = straightRiver,
                hasRoads = hasRoads,
                hasCaves = hasCaves,
                cavesExplicitlySet = cavesExplicitlySet,
                geyserCount = geyserCount,
                hasRockChunks = hasRockChunks,
                hillSize = hillSize,
                hillSmoothness = hillSmoothness,
                mutators = new List<string>(mutators),
                removeMutators = new List<string>(removeMutators),
                coastDirection = coastDirection,
                rockCount = rockCount,
                oreDensity = oreDensity,
                rockTypes = new List<string>(rockTypes),
                ruinDensity = ruinDensity,
                dangerDensity = dangerDensity,
                elevationShapes = elevationShapes.Select(s => s.Clone()).ToList()
            };
        }

        /// <summary>기본값(빈 상태)인지 확인.</summary>
        public bool IsDefault()
        {
            return hills == "none" && hillAmount == 1f && elevationShapes.Count == 0
                && vegetationDensity == 1f && animalDensity == 1f && fertilityOffset == 0f
                && !hasRiver && !hasCaves && !hasRoads
                && geyserCount == -1 && hasRockChunks
                && mutators.Count == 0 && removeMutators.Count == 0
                && coastDirection == "auto" && rockCount == -1
                && oreDensity == 1f && rockTypes.Count == 0
                && ruinDensity == 1f && dangerDensity == 1f;
        }
    }
}
