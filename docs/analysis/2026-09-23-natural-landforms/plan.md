# Natural landforms — DEV implementation checkpoint

Base: `15fad6f514fa319a42d895cfd5698e18490c5588`, annotated rollback tag
`dev-before-natural-landforms-2026-09-23`. Work branch: `dev-natural-landforms`.

User approved proceeding after the rollback checkpoint and prioritizes preserving working features.
Implement three optional layouts: open basin, winding broad valley, and foothills beside a plain.
The existing provider chooses parameters; deterministic C# produces related walls and usable ground.
No additional model call. Existing exact shapes, narrow passages and their saved states retain their path.

The new `landform` shape owns one editable ID. Its `inside` region is the planned floor, including the
basin opening; `enclosed` is rejected for this open geometry. Soil coverage and structures reference
this ID. A persisted variant preserves the layout during moves, rotations and resizing. Native water
and protected features remain subject to existing placement rules; usable area is not a guarantee of
successful building placement on every tile.

Validation targets before reporting completion:
- Existing pure regression suite; parser/preset/edit/Undo/region integration.
- At least 100 unfiltered variants per kind across directions, connected floor and nontrivial walls.
- Native MapPreview renders for three kinds × five variants, actual game-layer inspection.
- Native follow-up changes, 70% eligible soil coverage, structure placement and old replay comparison.
- Runtime timing and memory observation; no unmeasured speed claims.
- Independent review, local commit/package and pending original-track handoff.

Original repo/shared records/game installation are outside the writable roots. This work is local DEV
only; do not claim it is installed or pushed. Image input and Fable remain disabled. Geological
Landforms equivalence and live-provider natural-language quality require separate evidence.
