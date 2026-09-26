# Native shoreline audit

This isolated harness renders a water-only authored composite without a separate dry-floor shape, then generates a full 250×250 map. It measures the actual `LandscapeBlendGeneration.Apply` stage. No provider is allowed. Product code is not patched except for optional bypass of this one stage, startup inputs and disabling colony ticks inside the marked private runtime.

Build after the product DLL is ready:

```powershell
dotnet build tools/shoreline-probe/ShorelineProbe.csproj -p:ProductDll=F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll
```

Run from the canonical repository, using a fresh run name each time:

```powershell
powershell -File tools/shoreline-probe/run.ps1 -Run shore-pool-natural-01 -ProductDll F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll
powershell -File tools/shoreline-probe/run.ps1 -Run shore-pool-none-01 -ProductDll F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll -Details none
powershell -File tools/shoreline-probe/run.ps1 -Run shore-pool-bypass-01 -ProductDll F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll -BypassBlend
```

`-Biome temperate|desert|cold|boreal|arid` selects an inland flat tile without existing features, rivers or roads. Cold selects Tundra at mean temperature ≤0, falling back to IceSheet ≤0; boreal selects the first matching BorealForest tile without imposing a temperature. Arid selects the first matching AridShrubland tile, allowing the same interaction geometry to be tested in a biome whose native palette includes Sand; it does not choose a tile based on its rendered appearance or observed blend result. The actual tile and climate are recorded. Use `-Case hotspring` for special HotSpring water, where no ordinary-water external bank is expected. `-Case protected` adds a 70% rich-soil region and a real local road. It also injects an existing bridge foundation, gravel and rich soil just before the measured stage; those controlled inputs are individually recorded, not described as naturally generated evidence.

Run one owned game at a time. The runner only creates fresh profile/mod directories inside `MAPGENAI_HEADLESS_OWNED`, refuses linked runtime/Mods parents, uses a separate mod list and save folder, and does not change the installed RimWorld Mods directory. `-Graphics` can be used if this environment needs a graphics device for PNG encoding. No cleanup/deletion is performed.

New outputs default to `docs/analysis/2026-09-26-shoreline-review/native-<run>`. `-AnalysisGroup YYYY-MM-DD-label` selects another fresh dated analysis group. Historical September 26 blending evidence stays in its original folder; the runner never overwrites existing runs.

- `launch.json`, `fixture.json`, `state.json`: DLL hashes, world/setup seeds, selected biome/climate, exact authored state and bypass/details mode. The fixture records the pool variant and native soil, plant-fertility and terrain-patch Mud conditions, including each patch threshold's terrain name.
- `preview.png`: actual MapPreview terrain, not a drawing from this harness.
- `preview/full-before-blend.json`, `*-after-blend.json`: terrain layer RLE, terrain/elevation/cave hashes, terrain names/counts and protection counts.
- `preview/full-changes.json`: each changed cell, original/new terrain, independently computed water distance, protection flags and violations.
- `preview/full-phase-end-terrain.json`, `full-complete-terrain.json`: later terrain snapshot/hash to separate the measured stage from subsequent generators. The completed map has already released generation-only elevation/cave grids, so its last file marks them unavailable; before/after-stage and phase-end files retain their hashes.
- `preview/full-injected-controls.json`: controlled protection inputs for the protected case.
- `result.json`: checks, failures and exceptions. Preserve failed runs.

The detector uses a six-cell bounded brute-force Euclidean search independent of product distance/noise helpers. Literal positive/negative controls exercise the same violation function used for observed cells: permitted shore change, water mutation, dry/cold mud introduction, distant change and bypass mutation. Natural temperate pool requires at least one external eligible dry-shore change. Off/bypass and special-water cases require no stage mutation. Desert/cold changes may not introduce Soil/Mud, and the stage may never introduce Soil from Sand. Every changed cell must have started as Soil/Sand, be within six cells of supported freshwater, and preserve water, rock, existing gravel/special material, road, foundations, roofs/occupied cells, the complete 70% source region and height/cave grids.

The assertions cover stage behavior, not aesthetic approval, exact final plant identity, every biome/mod combination or the user's current selected package. Compare matching off/bypass phase-end hashes separately; native later generators can add differences not caused by the measured stage. No successful execution is claimed merely by adding these files.

The warm wet fixture also requires actual Mud accents; distance must follow the generated feathered water edge rather than only the inner authored mask. Explicit `none` schedules no blend stage, so its complete preview/full terrain is compared to bypass instead of requiring stage callbacks. `evaluate.py` checks the final seven recorded cases, lossless cell changes, local fixture bounds, heights, late-stage equality and native DLL hashes. See the dated report for retained diagnostic failures and limits.

## Paired nearby-water controls

The new interaction cases find the same unobstructed Soil/Sand strip beside the actual generated pond, outside its authored mask. They preserve all existing water, including the feather beyond that mask, and record every controlled cell before changing it. If no suitable strip exists, the probe fails visibly rather than silently replacing native geometry.

All five cases first set that 7×7 dry strip to Soil. These are controlled injected inputs, not a claim about natural world generation:

- `nearby-reference`: the strip remains dry.
- `nearby-water`: a 3×3 WaterShallow patch with three dry cells separating it from the original pond edge.
- `connected-water`: the same patch plus a three-cell WaterShallow connector to the pond.
- `explicit-water`: the same connected water geometry, but the patch and connector also receive a recorded `details=none` region mask and runtime shape. This tests protected water as a source/traversal barrier; it does not test the product's rasterizer for that extra shape.
- `special-water`: the disconnected 3×3 patch uses HotSpring instead of ordinary freshwater.

Each runs actual preview and full-map generation. `*-injected-interaction.json` records the anchor, direction and all 49 controlled cells. `*-interaction-water.json` records independently calculated source, connected-water and excluded masks. The independent implementation uses four-neighbor BFS and brute-force Euclidean distance, with no product connectivity/distance helpers. A standalone disconnected/connected/explicit-barrier positive/negative control checks that detector in every run.

The exact pre-stage difference from `nearby-reference` must be only the nine injected water cells, or those cells plus the three-cell connector. Elevation and cave hashes must remain identical. The audit compares every common dry Soil/Sand cell, including the unchanged part of the injected strip: disconnected ordinary water, explicit water and special water must not alter their results; connected ordinary water must change at least one dry-cell result. This makes the paired check capable of detecting the original unrelated-water material-distance bug rather than merely counting successful generator calls. A second synthetic control exercises the same Python comparison function with a one-cell bad mutation.

`-InteractionLayout cardinal` broadens this same controlled geometry without choosing a noise seed or a visually favorable result. It surveys east, west, north and south in that fixed order and uses the first valid row-major anchor for each direction, skipping strips that overlap an earlier selected strip. The original 49-cell geometry and all water, rock, authored-mask, explicit-material, infrastructure and source-distance constraints remain in force. Every feasible direction is included; unavailable directions record the attempted candidate count and rejection reasons in `*-interaction-selection.json`. At least three disjoint strips are required, otherwise the run fails before injecting any cells. The normal `single` default preserves the earlier first-anchor selection. Earlier single-anchor runs whose material output stayed unchanged remain evidence of an uninformative positive control, not successful connected-water material verification.

Cardinal receipts keep the original first `anchor`/`direction` fields and add an `anchors` array, fixed selection rule and direction survey. Every selected strip contributes its nine patch cells and optional three connector cells. The auditor compares the exact combined sets and every anchor across paired runs, verifies all four direction surveys and the minimum disjoint-strip count, and retains the same material-effect positive/negative assertions. No direction is selected by reading blend output. For example, add `-Biome arid -InteractionLayout cardinal` to each of the five serial run commands; the separate output run names should identify this new layout.

Example invocation after the product and probe have been built, using fresh run names for each case:

```powershell
powershell -File tools/shoreline-probe/run.ps1 -Run shore-nearby-reference-01 -Case nearby-reference -ProductDll F:/Projects/Rimworld/active/mapgen_ai/dev/Assemblies/MapGenAI.dll
# Repeat serially for nearby-water, connected-water, explicit-water and special-water.
python tools/shoreline-probe/evaluate.py --root docs/analysis/2026-09-26-shoreline-review --runs shore-nearby-reference-01 shore-nearby-water-01 shore-connected-water-01 shore-explicit-water-01 shore-special-water-01 --interactions
```

Use `--dll PATH` if auditing a retained DLL; by default the audit requires hashes to match the current product DLL. `--natural RUN --bypass RUN --off RUN` enables the existing paired on/off/bypass comparison for custom run lists. With no arguments the auditor still reads the historical seven-run list; it does not turn that historical evidence into a fresh product test. The interaction controls verify local policy behavior, not aesthetics, real user river placement, full-world topology or late plant identity.
