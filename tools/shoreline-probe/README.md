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

`-Biome temperate|desert|cold` selects an inland flat tile without existing features, rivers or roads. Cold selects Tundra at mean temperature ≤0, falling back to IceSheet ≤0; the actual tile and climate are recorded. Use `-Case hotspring` for special HotSpring water, where no ordinary-water external bank is expected. `-Case protected` adds a 70% rich-soil region and a real local road. It also injects an existing bridge foundation, gravel and rich soil just before the measured stage; those controlled inputs are individually recorded, not described as naturally generated evidence.

Run one owned game at a time. The runner only creates fresh profile/mod directories inside `MAPGENAI_HEADLESS_OWNED`, refuses linked runtime/Mods parents, uses a separate mod list and save folder, and does not change the installed RimWorld Mods directory. `-Graphics` can be used if this environment needs a graphics device for PNG encoding. No cleanup/deletion is performed.

Outputs live under `docs/analysis/2026-09-26-shoreline-blending/native-<run>`:

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
