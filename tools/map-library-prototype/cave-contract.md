# Observed cave geology contract (developer only)

The existing terrain observation remains schema 2. A separate actual capture
`<id>-geology.json` schema 1 records `width`, `height`, `row_order: south-first`,
`elevation`, `caves` (finite numeric arrays of width*height), `roof_table` (names,
index 0 is `None`), `roof_indices`, `walkable` (string of 0/1), and
`source_terrain_sha256`, actual `constructed_floor` and `nonrock_edifice` 0/1
masks (not inferred from material names). Capture occurs inside final genstep 99999 before native
working grids are cleared. Unsupported constructed roofs are recorded as truth,
never guessed to be natural roofs. Map Preview PNG is not evidence of actual roofs.

Explicit `cave_layer` schema 1, mode `source-geology`, has source dimensions,
south-first order, source_biome, `known` (string K/N), full `elevation`/`caves`
arrays, `roof_codes` (0 none, 1 RoofRockThin, 2 RoofRockThick), provenance hashes.
Unknown source roofs, constructed ground and nonrock source edifices
are preserved as N. Source fields and dimensions are validated before export.
Missing geology is an error for an explicit cave request; legacy detail/default
exports are unchanged. Images cannot supply cave geology.

Python main API: `cave.observed_cave(terrain_path, geology_path, palette=None)`
returns (layer, receipt); `cave.decode(layer)` returns 2D numpy fields `known`
(bool), `elevation`, `caves`, `roof` (0/1/2); `cave.resample(fields,width,height)`
uses floor(x*source_width/target_width), likewise z. Known numeric zero and no
roof are different from N. Full floating grids are not rounded to binary masks.

Runtime applies observed grids before native RocksFromGrid. RockGridPass must
retain known observed elevations/caves instead of flattening them to .71/.5.
Full cave replay uses four authoritative sidecars with empty product params;
approximate authored water polygons must not run again at 400 and flatten the
observed grids. Legacy rock/image/default product commands are unchanged.
The rock mask remains the authoritative final solid occupancy. Unsupported
current water/roads/buildings/floors/unknown and artificial roofs are protected.
Only known natural roofs are reconciled safely; no bulk roof remover, building
deletion, source saves/world configuration or source resource definitions.
Intermediate applications save invariant audits. Final capture saves actual
geology and a `<id>-cave-final-audit.json` with stage99999/source/target dimensions,
known/unknown/protected counts, grid and natural roof mismatches,
protected_conflicts, protected_changes, unknown_changes, and protection kinds.
`unsafe_roof_cells` is a required bounded nonnegative integer. Zero support
conflicts, not just a correctly shaped roof mask, are required for admission.
Final protected collisions reject candidates; an application PASS is not final
fidelity approval. Unsupported or unanchored source natural roofs reject safely.

Independent `cave_report.evaluate(folder,runs,write=True)` writes
`cave-evaluation.json`, with records run/id/biome/size/pass/reasons and separate
source cave mask/value, elevation, actual natural roof, walkable roofed passage,
four-connected source passage components and entrance/connectivity preservation.
It reads raw source observations directly, never exporter arrays as truth.
Non-empty real GL cave examples must be measured. An identical solid rock mask
with a broken cave/roof must fail. Final cave audit missing/invalid is fail closed.
Existing rock>=98%, original supported ground>=95%, water/protection/late native
structure conflict gates are retained. Same mask IoU alone cannot prove caves.

Reports show fresh native source / rock-only replay on the same target tile and
seed / new geology replay, plus actual roof and passage diagnostic overlays.
Overlays are labelled measurements, not game screenshots. All original raw
captures/failed profiles remain preserved; source provenance is explicit.
