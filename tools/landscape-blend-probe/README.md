# Native landscape blend audit

This is a disposable verification mod, not part of the distributed mod. The runner only adds a fresh profile and probe mod inside the marked `work/mapgenai-headless-runtime` copy. Every run must use a new `blend-*` name. It never installs to the original game, calls providers, or advances colony simulation.

```powershell
dotnet build tools/landscape-blend-probe/LandscapeBlendProbe.csproj --no-restore
& tools/landscape-blend-probe/run.ps1 -Run blend-a-none-s4-r3 -ProductDll dev/Assemblies/MapGenAI.dll -Case A -Details none -Graphics
# Wait for that owned process to finish before starting another.
& tools/landscape-blend-probe/run.ps1 -Run blend-a-natural-s4-r3 -ProductDll dev/Assemblies/MapGenAI.dll -Case A -Details natural -Graphics
python tools/landscape-blend-probe/compare.py docs/analysis/2026-09-23-landscape-blending/native-blend-a-none-s4-r3 docs/analysis/2026-09-23-landscape-blending/native-blend-a-natural-s4-r3 --out docs/analysis/2026-09-23-landscape-blending/native-compare-a-s4-r3.json
```

Cases use the first matching tile in the fixed world seed, variant `23`, explicit `layout:organic`, map size 250. Selection does not filter for pleasing visual results:

| Case | Fixture |
| --- | --- |
| A | Flat temperate forest, open basin, explicitly authored shallow pond |
| B | Temperate forest tile with an existing world river, winding valley |
| C | Flat temperate forest, open basin, explicit `HotSprings` addition through supported state |
| D | Flat desert, foothills; native biome does not care about local fertility |
| protected | Temperate forest foothills, explicit 70% rich-soil fill, local dirt path |
| road | Same foothills and local dirt path, without whole-area fill protection |

For compatibility, run the same probe with `-Details null` once against the frozen pre-blend DLL and once against the current DLL, then compare with `--legacy`. New `details`, vegetation weights and report fields use reflection. The probe does not reference new product types directly. The baseline must still contain organic landforms and local roads.

Final r5+ runs additionally fix QuickTest's setup RNG to `902323`, `ticksGame=0` and `gameStartAbsTick=3600000`; fixture metadata records these inputs and mean tile temperature. Final map metadata records actual outdoor temperature. Raw r3/r4 runs are retained as diagnostics and must not be mixed into an asserted deterministic comparison. Native ruins still varied even with setup/date fixed; strict equality failures remain recorded. `-QuietStructures` explicitly requests `ruinDensity=0` and `dangerDensity=0` in both full-map state and preview command for a separate diagnostic legacy comparison, never silently modifying the ordinary cases.

## Artifacts and boundaries

- `result.json`: native assertions, blend counters, actual Plants900 scope weighting and authoring reports. Exceptions and failed checks are retained.
- `fixture.json`, `state.json`, `preview-command.json`, `launch.json`: exact fixture, state, DLL hashes and launch metadata.
- `preview.png`: actual MapPreview terrain texture. MapPreview does not show grass or trees.
- `full-plants.json`: full-map plant coordinates, species, growth, surface, biome, fertility, native minimum, membership of native wild-plant lists, independent water/rock distance.
- `full-plants900.json` and `full-plants-complete.json`: immediately after native initial plant generation and after complete map generation.
- `full-vegetation-plan.json`: every dry, non-rock planned floor coordinate with terrain, weight and independent distance. This is a candidate cell audit, not a plant occupancy prediction.
- `*-spatial.json`: candidate and actual plant occupancy counts in water/rock distance bins. Counts are descriptive; no claim that every biome must grow more plants.
- `*-terrain.json`: lossless RLE of visible/top/under/foundation/temp terrain and exact elevation/cave hashes.
- `*-before-blend.json`, `*-after-blend.json`, `*-blend-changes.json`: direct around-stage mutation audit; all water, rock, special material, old gravel, existing foundation, authored fill source area, local road footprint and outside-landform floor are protected.
- `scribe-{null,none,natural}.xml`: native Scribe roundtrips preserve details and other authored settings; baseline lacks the new field and checks null only.

Independent distance uses a separable squared Euclidean transform, checked against a literal 3-4-5 triangle. Comparison has synthetic mutations through its actual verdict functions. Current and old null cases require exact terrain layers and plant coordinate/species/growth rows, including after generation scope closes. Off/natural comparisons require unchanged heights, caves, special/water materials and ground outside the planned region; plant changes remain measured outcomes. Desert scope requires zero active density weights, but native plants may respond to changed ground.

`compare.py` sends actual synthetic water-to-soil, outside-floor soil, plant species and plant coordinate mutations through the same verdict functions used for real artifacts; it also proves a permitted decorative change passes the normal verdict while failing strict legacy equality. Starting with r7, active native soil cells also call the patched `GetDesiredPlantsCountAt` and compare its output with the native density/fertility formula multiplied by the planned weight. A result matching the unpatched formula fails that test. This distinguishes active prefix behavior from merely storing a plan.

The harness executes real game and MapPreview generators in a controlled official-DLC configuration. It does not establish every biome/mod combination, visual appeal, native road shoulder coverage on every road type, or live model interpretation. Initial r1/r2 instrumentation failures must be retained as such; they are not product failures.

Large completed JSON files are stored as `.json.gz`. `compare.py` and `summarize.py` transparently read either form. `compact-evidence.py` checks every resolved path remains inside this new evidence root, verifies decompressed bytes and SHA256 against the original, then replaces only that owned uncompressed copy. `native-compression.json` is the immutable receipt. Small result/fixture/launch files and PNGs remain directly readable. The compression receipt must not be overwritten on subsequent runs.
