# Native generation reference and local water integration

User request: “그래 좀 그렇게 좀 해 봐. 기본 생성 코드 좀 참조해 봐.” Work remains DEV-only, with actual generated images supplied by the developer before asking the user to repeat tests.

## Reference and scope

The installed RimWorld Assembly-CSharp.dll was inspected with ILSpy. Existing engine elevation, fertility, rock, terrain patch, river/coast and plant generation remain the baseline. This change focuses on the common authored freshwater path, rather than adding a special basin recipe.

Native Lake constructs one displaced field for deep water, shallow water and shore. Its two displacement bands and .75/.5/.45 thresholds inform the adapter. The original whole-map worker is not invoked for a local edit: it also clears elevation, chooses a world-feature position and changes waterInfo.lakeCenter.

Public helpers reused: MapNoiseUtility.AddDisplacementNoise, MapGenUtility.DeepFreshWaterTerrainAt, ShallowFreshWaterTerrainAt, ShouldGenerateBeachSand and LakeshoreTerrainAt. The last helper reads dedicated biome lakeBeachTerrain and tile-feature overrides; a generic fertility material list is not an equivalent replacement. Native lake shores are not universally mud.

Native Terrain's patch makers are cleaned after terrain generation. Calling TerrainFrom/GetNaturalTerrainAt at our later surface stage would recreate noise and consume RNG. Existing native ground is therefore retained outside the local water/shore edit. Native plant species and terrain suitability rules still execute afterward.

## Compatibility contract

- A persisted composite water_profile selects native or legacy; absent values retain prior generation. Only new rough standing freshwater additions default to native. Exact shapes, old saved snapshots, updates and special waters do not silently upgrade.
- Native rough water uses one displaced signed field for the water footprint and dry shore, then measures actual raster distance to dry land for submerged shelves. This avoids treating compressed field distance as physical cell distance. Explicit WaterShallow stays shallow. details:none disables shore/vegetation effects without changing the selected water outline/depth.
- Exact geometry and explicit non-water material operations retain the old renderer. Per-operation contributing primitives set scale, so adding a small unrelated shape does not change a large lake.
- Water material resolution honors per-cell native biome helpers. Shore application is bounded, checks actual remaining owned freshwater, and preserves explicit material/coverage areas, roads, foundations, rocks, roofs, buildings and special ground.
- World river/ocean connections retain existing protection. This work does not add an LLM-visible native-water-reference operation or rewrite recommendations/whole mountain layouts.

## Verification plan

1. Pure state and existing regression suite: old snapshot, exact geometry, move/update, clone/preset and new default behavior.
2. Isolated RimWorld runs with frozen old and candidate DLLs: same seed/tile/authored input; profile is the declared intentional difference. Native Lake/Oasis images are style references with their own footprints, not coordinate-matched expected images.
3. Native noise property samples across 100 unfiltered variants and varied footprints, plus global RNG preservation. Actual native Scribe/palette checks remain distinct from pure shims.
4. Preview and completed-map comparisons: old/exact and saved legacy equality, explicit shallow/deep behavior, details-off geometry preservation, protected coverage/roads/special waters.
5. Display real Map Preview PNGs directly. Graphics must be enabled: the first nographics probes produced unusable gray previews despite valid terrain layers. Raw failures and intermediate images are retained; test passes do not establish aesthetic approval.

## Boundaries

No image-input reactivation, new provider/Fable calls, Steam publication, main merge or general distribution change. DLL installation only after the user's running game permits safe replacement. Rollback tag: dev-before-native-generation-2026-09-27 at 21e5614.
