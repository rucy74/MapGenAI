# Natural landscape surfaces and vegetation — DEV

## User decision and start

2026-09-23: user asked “근데 물이나 풀이나 다른 지형 특징 그런 건 자연스럽게 조화시키는 건 다음 단계야?”
and approved continuing with “그래 계속해서 진행해봐”. Their previous design preference remains:
“사람이 아주 디테일하게 얘기하지 않아도 디자인 측면에서 이쁘개 되는 방향이 좋아”.

Resume: existing pending mapgenai track update, §2026-09-23 23:12, and previous landform-design report.
Local `dev-natural-landforms` starts clean at `d84ea4bfd319ec36708d66aaf0148cae8601eb71`.
Rollback tag: `dev-before-landscape-blending-2026-09-23`. Image input/Fable remain OFF.
Original repo, shared records and game installation are outside writable roots; no general release changes.

## Implementation target

- New natural landforms can blend their usable ground with actual water and mountain boundaries.
  Work from the generated terrain, including rivers, ponds and native hot springs; preserve those bodies.
- Irregular, limited shores and rocky foothills must leave useful open ground. Ordinary biome soil and
  sand/gravel may transition; explicit fills, counted coverage, roads, buildings and special mod terrain win.
- Shape local initial vegetation density while retaining the biome's actual plant species and placement rules.
  Do not turn every dry map into a lush forest or apply a global biome mutation for this effect.
- Persist an opt-in details value on each landform. Default only newly added landforms; old null/off states
  keep exactly the prior terrain and vegetation behavior. Normal partial edits preserve the setting.
- Existing water/feature creation tools remain responsible for which features are present. This step
  integrates their surroundings; it does not invent a world river/coast or relocate native landmark workers.
- No extra model stage. Extend existing guidance for short requests and explain the resulting changes in KO/EN.

## Verification

Pure field tests with >=100 unfiltered seeds, material/protection boundaries and detector positive controls;
native previews/full maps with water, dry biome, hot spring and explicit fills/roads; legacy exact comparisons.
Independently review/test before packaging. Native foliage counts/layout, terrain colors and aesthetic judgment
are distinct evidence. Preserve raw failures and identify the exact product DLL for every native run.

## Session start

Read current local HEAD/state and shared mapgenai decisions, game generation order and source hooks.
Work only in the existing writable development copy. Reuse original state/TODO/log via the existing pending
handoff because their actual paths are read-only; do not create a competing Codex ledger.
