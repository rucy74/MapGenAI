# Native generation visual comparison harness

Prepared 2026-09-27. Individual `runs/*/result.json` and their actual PNGs are the execution evidence; scaffold compilation alone is not a game result.

## Provenance and scope

`prepare.py` derives `Probe.cs`, `NativeVisualProbe.csproj`, and `run.ps1` from the canonical `tools/shoreline-probe` at baseline `21e56141d4ec05d4e02599cee4aac2f1d70fda6a`. Original source hashes are in `provenance.json`. Regenerating these files overwrites this task-specific scaffold only. It does not launch a game or alter product code.

Frozen old product: `../baseline-21e5614.dll`, SHA256 `b75da4efe80ce1333b75538a669ea0811dc4c81b98976bb94a15beba1cb5e084`. The probe compiles against that assembly. New optional `water_profile` uses reflection so the same probe DLL can run both products. Asking an old DLL for a profile fails explicitly. Omit this flag for old and legacy-preservation checks.

No model API is involved. The native process patches provider factory creation to throw and counts attempted calls. Every preview is the real Map Preview output; the harness does not draw a substitute map. Completed-map terrain snapshots are recorded separately. A thumbnail is not a screenshot of the complete game UI or proof of final plant quality.

## Execution

Run from this directory, serially, with fresh names:

```powershell
dotnet build ./NativeVisualProbe.csproj --no-restore -v minimal
$old = '../baseline-21e5614.dll'
$new = 'F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll'
./run.ps1 -Run native-temperate-ground-old-01 -ProductDll $old -Case native-ground -Biome temperate -Graphics
# Wait for this run's result.json and its owned process to exit before the next command.
./run.ps1 -Run native-temperate-feature-old-01 -ProductDll $old -Case native-feature -Biome temperate -Graphics
./run.ps1 -Run native-temperate-pool-old-01 -ProductDll $old -Case pool -Biome temperate -WaterFill water -Graphics
./run.ps1 -Run native-temperate-pool-new-01 -ProductDll $new -Case pool -Biome temperate -WaterFill water -WaterProfile native -Graphics
```

`-WaterProfile native` is conditional on the production field/token implemented by the main worker; do not call it before that field exists. If its name changes, update the reflected field in `prepare.py` and regenerate/build. No natural-looking result is fabricated if the field is missing.

Repeat ground, feature, old pool, new profile pool for `-Biome desert` using distinct run names. Temperate `native-feature` uses the real Lake feature. Desert uses the real Oasis feature because the game's Lake and Pond whitelist excludes Desert. These native feature references have native position/area; they are not claimed to match the authored pool footprint. Both still use the same world seed, tile selection, map size and time. Features are added through normal product state commit/feature validation rather than forcing illegal feature state.

`native-ground` loads MapGenAI with no authored geometry, no added features and ruin/danger density zero. It is an unmodified-terrain baseline within this mod set, not a claim that the MapGenAI assembly is absent. Harmony interceptions are recorded so this distinction is auditable.

World seed is fixed to `mapgenai-guided-live-20260923`, setup RNG `902323`, map 250 x 250, absolute time 3600000. Tile selection is first matching flat/inland/no-river/no-feature/no-road tile in world enumeration, independent of rendered appearance. All Desert cases additionally require the native Oasis `averageTemperatureRange` (currently 20 to 60), so the authored and native feature references use one legal warm tile. The original Desert feature run 01 failed this condition before this chooser adjustment; preserve that failure. Inspect `fixture.json` to ensure old/new pairs actually have the same tile and climate.

Use `-Graphics` for deliverable PNGs. The first two `-nographics` runs produced gray previews despite valid terrain JSON, so they are retained only as nonvisual terrain evidence. `-WaterFill water` asks for generic freshwater with depth; the default `WaterShallow` intentionally has no deep center. `-FieldChecks` runs 100 unfiltered field variants across six fixtures after the complete map and image have been saved; a property failure marks the run failed without discarding its diagnostic visual. Independent `-IntegrationChecks` invokes native Scribe/profile persistence and temporary palette helper/restoration checks; these helper sentinels are never painted into the saved map.

## Legacy and protection checks

- Run `-Case exact -Details none` under both DLLs; compare all final terrain layers, elevation/cave phase hashes and preview PNGs byte for byte. Exact shape profile remains absent.
- Run ordinary `-Case pool -Details natural` under both DLLs with no profile. This checks saved/legacy natural water remains unchanged if new behavior is explicit opt-in.
- Repeat `-Case pool -Details none` and `-BypassBlend` as separate pairs; bypass skips only the LandscapeBlendGeneration stage, not new native water geometry.
- `-Case hotspring` under both DLLs checks special water preservation; do not set the freshwater native profile on it without a separately specified rejection test.
- `-Case protected` adds a 70% rich soil source and a real local dirt road. This scaffold checks their placement reports, but does not inject controlled foundations or assert old-bank distances. For bridge/foundation/under-layer regression reuse canonical `tools/shoreline-probe` on the new DLL without native profile. Its previous exact-bank positive controls are not valid aesthetic tests of the new geometry.
- `-StateFile PATH` replays a stored TileMapState unchanged and takes precedence over generated case/profile values. Preview plan resolution must exactly match that recorded state. This allows parent-prepared schema fixtures after state format is finalized.

## Evidence

Results go to `../runs/<run-name>/`:

- `launch.json`: owned PID, profile, product and probe SHA256; product is copied into this unique disposable mod before launch.
- `state.json`, `preview-command.json`, `fixture.json`: exact authored state and matching seed/tile/biome data.
- `harmony-patches.json`: actual prefix/postfix/transpiler/finalizer owners for elevation, terrain, Lake/Oasis, plants and the native water/shore/mud/riverbank helper methods. An inherited Oasis method records its resolved declaring type.
- `preview.png`: actual terrain image returned by Map Preview.
- `preview/full-native-terrain.json`, `preview/full-native-lake.json` where exercised: native-stage terrain layers/counts and elevation/cave hashes.
- `preview/full-before-blend.json`, `preview/full-after-blend.json` where exercised: custom surface stage snapshots.
- `full-complete-terrain.json`: completed map terrain layers, without released generation-only elevation/cave grids.
- `full-native-palette.json`, `full-actual-features.json`: helper results at the map center and actual feature worker types.
- `result.json`: render failures/rejections, exceptions, native worker observations, provider attempts. Check `ok` and inspect `Player.log`; file existence alone is not success.

For visual delivery, assemble unchanged `preview.png` files into a labeled sheet, preserve per-source SHA256, and display via `view_image` with its returned `image_url` emitted through `image(...)`. Label native Lake/Oasis as style references with different footprints. Label exact/protected fixtures as controlled regressions rather than recommended map designs. The visual judgment must inspect the final PNGs instead of being inferred from PASS counts.

## Launch and cleanup boundary

Runner only uses `work/mapgenai-headless-runtime`, requires `MAPGENAI_HEADLESS_OWNED`, rejects junction/symlink runtime and Mods parents, refuses another process using that same executable, uses `Start-Process -WindowStyle Hidden`, and creates fresh unique profile/output/mod directories. For the default probe DLL it also refuses any top-level C# source newer than the assembly. This prevents an unsuccessful build being followed by a launch of stale probe code. Explicit `-ProbeDll` bypasses that timestamp check for intentionally frozen harness assemblies, so its SHA remains essential. It never edits installed `G:/SteamLibrary/.../RimWorld/Mods`, user saves or user ModsConfig. It never stops another process. A process belonging to another track or the user's normal RimWorld can remain running.

After results complete and the exact launch PID/executable is gone, archive only this task's `Mods/NativeVisualProbe-<run>` folders outside that runtime's Mods directory after resolving and checking absolute ownership paths. `archive-probes.ps1` derives every exact source from this task's launch receipts and checks both resolved roots, reparse points, PIDs and SHA256. The executed cleanup archived 27 task-owned probe folders to the owned runtime's `ArchivedProbes/native-generation-2026-09-27`; `../probe-cleanup.json` records every source, destination and file hash. It is not intended to be rerun against already-archived sources.

## Final frozen candidate and matrix

Candidate 06 SHA256: `4908863336befac9c141ba35cb15bbfb47617ed87a9236cd21f7b3449abde019`.
Frozen compiled probe SHA256: `1b686be3068be35dda32663d38aed9fe35622769525cee8e08147a37ca431e79`.

`matrix.py` generated 13 fixtures before intentionally stopping at the first raw river check failure. `finish-matrix.py` generated the two remaining fixtures after the failure was diagnosed as native seasonal ice. With reused candidate-06 temperate full-map run this is 16 fixtures, all with real Map Preview and complete native maps. The native Lake and Oasis references are two additional reference runs with different geometry. No failed run was overwritten or filtered out.

`audit.py` reads the raw saved evidence; final result `../final-audit.json` is **160 PASS / 0 FAIL**. Its independent checks establish:

- Exact geometry, legacy profile-absent state and special HotSpring: old/new final terrain layers, phase elevation/cave hashes and PNG bytes equal.
- Details on/off: all 3,483 water cells and their deep/shallow layers equal; 429 dry shoreline cells differ.
- Explicit WaterShallow introduces no WaterDeep.
- Both protected fixtures retain the 4,050-cell explicit source region through preview/full blending, real road footprints through full blending/completion, and 70% actual eligible-region fill.
- CSG ring center remains a 2,024-cell enclosed dry island in preview and completed map after native cleanup.
- Actual river-cell permanent layers survive every custom surface stage. All before/after-blend river layers are unchanged.

The two raw river `result.json` files deliberately remain **FAIL**: at absolute tick 3600000 the native complete generator adds ThinIce to the surface/temp columns of 7,256 old or 7,302 new river cells. The complete-map top/under/foundation columns remain unchanged; the independent audit requires every changed column to be exactly this native ice overlay. Preview has no such overlay.

The first independent audit, preserved as `../final-audit-rejected-river-identity.json`, had 152 PASS / 2 FAIL because it expected identical river-type masks across changed pond shapes. The actual native RiverTerrainAt worker skips existing nonriver water. All 246 different river labels are explained exactly by that native pre-river guard in both preview/full maps: 100 old-only river cells are new stage-210 WaterShallow; 146 new-only river cells are old stage-210 WaterDeep (116) or WaterShallow (30). All 7,156 shared river layers and the entire river outside the changed authored water are identical. The final audit checks those strict conditions. It does **not** claim unchanged river-type masks inside changed pond footprints.

The candidate-06 production field/raster tests are **706 PASS / 0 FAIL**, 100 unfiltered variants across six fixtures (9,830,400 sampled cells), no resampling/retries. Native Scribe and temporary helper palette/restoration integration checks are **13 PASS / 0 FAIL**. These are separate test scopes, not 600 full maps or full custom-biome render proof.

`render.cjs` created three phone-readable sheets in `../screenshots/`; every embedded PNG is unchanged. `manifest.json` records source PNG SHA256 and product/probe launch identities. All three rendered sheets were opened with `view_image` and visually inspected for correct images/labels/no clipping. This is no assertion that aesthetic quality is solved. Original Lake/Oasis are clearly labeled different-footprint style references.

## Limits and next useful checks

An actual river comparison is included in the final matrix. No coast or rendered custom-biome fixture was generated. Palette override checks are independent of ordinary map appearance and restore every mutated definition field. Helper sentinels never enter actual map screenshots. These fixed authored shapes do not test LLM interpretation, recommendation variety, complete forest aesthetics, or all other generation systems.

The first valid shallow old/new graphics pair has equal seed/tile/time and equal state except `water_profile`; 717 completed-map terrain cells differ. This is implementation evidence, not aesthetic approval. Candidate 03's generic-water preview/full map rendered, but its 600 unfiltered field variants failed 416 of 706 checks (290 passed), with disconnected water and unwanted dry holes among the failures. Keep `native-temperate-water-new03-graphics-01` failed and preserve its raw output; do not present that candidate as ready.

Candidate 04's `native-temperate-water-new04-graphics-01` remains failed: 674 field checks passed, 32 failed. Two circle fixtures and the ring passed all 100 variants each; elongated ellipse had three disconnected-water cases and four unbuffered-deep cases, thin path had 24 unbuffered-deep cases, and CSG union had one unwanted hole. Its 13 native Scribe/palette integration checks passed. This records the improvement without converting the remaining field failures into success.

Candidate 05 remains failed at 700 PASS / 6 FAIL, using the new actual-raster EDT depth checks. Every connectivity/hole/depth-buffer check passed, but six circle variants had no deep cells. Raw transformed-field depth observations are retained as diagnostics because the production depth painter now uses the separately tested raster EDT. Candidate 06 adjusts shelf depth relative to actual inradius and passes all 706 checks without retrying or filtering seeds.

`native-temperate-water-old-graphics-01` is explicitly INVALID: a concurrent helper file caused a failed build and PowerShell initially continued to launch the prior probe, which did not understand `WaterFill`. The raw run and `INVALID-RUN.txt` are preserved. Subsequent builds and launches are separate operations with inspected exit codes, and the stale-source launch check was added afterward.
