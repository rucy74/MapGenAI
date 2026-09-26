# Native generation reference: bounded DEV recommendation

Read-only investigation, 2026-09-27. Source baseline: MapGenAI `dev` 21e5614. Installed `Assembly-CSharp.dll` inspected with ilspycmd; decompiled reference outputs remain outside product repository. No game/API/build/product mutation.

## What native generation actually does

- Native elevation is Perlin plus detail displacement, stretching/rotation and another warp; world hilliness scales it. Fertility is a separate Perlin field. This is not a hydrology/erosion simulation, so simply using native code cannot promise physically perfect drainage.
- Current native Lake uses one transformed radius field and two displacement stages: `.006, 40, 2` and `.015, 15, 4`. The same scalar value chooses deep water (`>.75`), shallow (`>.5`) and beach (`>.45`). Its post-elevation phase clears elevation over the beach-inclusive footprint before rocks spawn.
- Native terrain evaluates biome terrain patches, gravel/rock elevation bands, then fertility thresholds. At the end of terrain generation each patch maker is cleaned up.
- Native plants run after final ground and use WildPlantSpawner's species/density/placement rules. Preserve those rules rather than adding hand-placed vegetation.

## Public reuse APIs verified

```csharp
ModuleBase MapNoiseUtility.AddDisplacementNoise(ModuleBase baseShape, float frequency,
    float strength, int octaves = 4, int seed = -1)
ModuleBase MapNoiseUtility.CreateFalloffRadius(float radius, Vector2 offset,
    float exponent = 1f, bool invert = true)
TerrainDef MapGenUtility.DeepFreshWaterTerrainAt(IntVec3 cell, Map map)
TerrainDef MapGenUtility.ShallowFreshWaterTerrainAt(IntVec3 cell, Map map)
TerrainDef MapGenUtility.LakeshoreTerrainAt(IntVec3 cell, Map map)
TerrainDef MapGenUtility.RiverbankTerrainAt(IntVec3 cell, Map map)
TerrainDef MapGenUtility.MudTerrainAt(IntVec3 cell, Map map)
bool MapGenUtility.ShouldGenerateBeachSand(IntVec3 cell, Map map)
TerrainDef MapGenUtility.GetNaturalTerrainAt(IntVec3 cell, Map map)
TerrainDef MapGenUtility.TerrainFrom(IntVec3 c, Map map, float elevation,
    float fertility, bool preferRock)
```

The lake/mud/riverbank selectors honor tile mutator material overrides, then `map.BiomeAt(cell)` dedicated fields, then native fallbacks. The existing MapGenAI `NativeGround` general terrain-list membership is not equivalent. Native shore can legitimately be Sand in a temperate biome even if Sand is absent from the general ground threshold list. Native Lake itself does not surround every lake with Mud.

## Minimal safe implementation recommendation

1. Introduce a versioned opt-in natural-water profile for generic water, leaving exact shapes, explicit WaterDeep/WaterShallow materials and legacy state unchanged. Do not overload `details:natural` silently if old saved-map repeatability is part of the contract.
2. Use one local continuous shape field for deep/shallow/beach. Current SDF interior has `t=1` everywhere, so all interior is deep and shallow is only an exterior feather. Give natural water an actual inward shallow shelf; derive shoreline width from the same profile, not independent bank noise.
3. Use native water/shore material selectors per cell. Keep the native terrain as the surrounding transition's default; mud is a constrained optional wet-soil accent, not mandatory full ring. Keep special/saline/hot/icy waters outside generic freshwater scope.
4. For geometry evolution, a `ModuleBase` adapter around authored SDF can use `AddDisplacementNoise` in world-cell coordinates, bounded to authored shape scale; use explicit stable nonnegative seeds derived from persisted variant/tile so it consumes no global RNG. Do not stack full native 40-cell warp on a tiny pond or on top of current roughness without scale control. Exact geometry retains existing rasterization.
5. Apply local natural geometry during the existing elevation pass so rocks/terrain/vegetation see the same footprint. Retain late paint only for material reconciliation after native water workers and for permitted banks. Ensure ownership/protected masks are applied consistently to every pass, and preserve native river/ocean/world-road cells.
6. Never reconstruct a entire native Lake worker to generate a local pond: Init has Odyssey gating and changes `map.waterInfo.lakeCenter`; generation iterates the whole map and clears elevation. Such a worker is not a safe local API.

## Ordering and determinism hazards

Installed Core order: Elevation10 -> MutatorPostElevation20 -> Rocks200 -> Terrain210 -> MutatorPostTerrain220 -> RemoveTinyIslands240 -> Roads390 -> custom terrain400/roads410/blend420 -> structures500/700/750/800 -> Plants900 -> MutatorFinal1600 -> custom coverage1900.

- Calling `GetNaturalTerrainAt` / `TerrainFrom` from a late 400/420 pass is unsafe as an assumed replay of original native ground: TerrainPatchMaker was cleaned up after210, and the next TerrainAt lazily consumes Rand to initialize fresh noise. Preserve/capture original native ground instead, or explicitly scope a new field with clear semantics; do not silently re-roll.
- Native Lake's `.45` elevation clear happens before rocks. Late shape expansion must not paint water beneath existing rocks or erase roofs/buildings. Prefer early geometry plus existing late protection.
- Map Preview has a filtered generation list; both profile and material stage must run in preview and full map. Inspect actual active Harmony patches for overridden native methods before relying on base implementation in the user's large mod set; no exhaustive active-mod patch inventory was performed in this bounded read-only investigation.
- New generated shapes or shelves need semantic masks/coverage decisions; do not alter an explicitly counted 70% source area through a shore pass.

## Useful verification

- Same tile/seed/native baseline versus new natural profile; temperate/arid/boreal and native river/coast combinations. Display unchanged native preview captures directly to user.
- Assert exact/legacy outputs unchanged; explicit paints, world river/ocean/roads and structures remain unchanged; shallow shelf is inside water footprint and nonempty in eligible positive fixtures; no unrelated disconnected water changed.
- Record actual per-cell terrain names to confirm biome/mutator selectors are used, including an override fixture. Null fallback alone cannot prove integration.
- Compare native preview/full-generation depth/bank output and RNG determinism. Technical preservation checks do not establish visual appeal.

## Source locations

- Installed Core `Data/Core/Defs/MapGeneration/CommonMapGenerator.xml` (ordering).
- Fresh outputs in this directory: `RimWorld.MapNoiseUtility.cs`, `RimWorld.GenStep_ElevationFertility.cs`, `RimWorld.TerrainPatchMaker.cs`, `RimWorld.GenStep_Plants.cs`.
- Fresh Lake: adjacent `../mapgenai-native-reference/TileMutatorWorker_Lake.cs`.
- Cached locally decompiled `work/mapgenai-engine/RimWorld.MapGenUtility.cs` and `RimWorld.GenStep_Terrain.cs`.
- Product: `dev/Source/MapGen/SdfComposite.cs`, `LandscapeBlendGeneration.cs`, `RegionGrid.cs`, and `Patches/AuthoringGenerationPatch.cs`.
