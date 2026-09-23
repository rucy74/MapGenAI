# Default landscape design — DEV checkpoint

## User decision

2026-09-23: “그래 해봐. 음 근데 사람이 아주 디테일하게 얘기하지 않아도 디자인 측면에서 이쁘개 되는 방향이 좋아”.
Improve the visual composition and default usability of the existing three natural landforms. The user
approved natural contours, real recommendation/follow-up validation, and preservation of working features.
Image input and Fable stay off. No extra model stage, general release update, or forced decorative features.

## Starting point and scope

Local branch `dev-natural-landforms`, base `b75900d8a6e3b0a59cf4cd944d64a3d3b5629cec`, rollback tag
`dev-before-landform-design-2026-09-23`. Original repo/game/shared records remain outside writable roots.
Resume sources: preceding natural-landforms report and existing pending mapgenai track update.

1. Add an organic layout profile with unequal rounded shoulders, side gullies, varying wall thickness,
   and related detail at several scales. Keep a generous connected floor and preserve native materials.
2. Newly added natural landforms default to this profile. Persist it in saves, presets, Undo and edits.
   Missing profile in an OLD state keeps the original sampler. Exact primitives/composites are untouched.
3. Give the existing model concise composition guidance for short requests. Favor one readable main
   landform, open settlement space and restrained accents suited to the tile. Do not always return the
   same three types or add water/resources without a reason.
4. Compare actual native previews before/after, test >=100 unfiltered variants and boundary cases,
   legacy equality, interior edits and 70% fill/ruins. Validate actual provider responses if the current
   permitted connection works; keep network refusal separate from recorded/offline success.
5. Build, scoped native verification, independent review/test, commit, package and pending handoff.

Visual beauty is a design judgment and user preference, not a guaranteed metric. The mechanical tests
cover geometry, connectivity, preservation and edit behavior; native pictures support visual review.
No retry-filtered corpus, second AI call for geometry, or installation claim from a local package alone.

## Session start

2026-09-23 22:30 +09:00: local HEAD/clean state and rollback tag checked. Read original track state,
latest log and TODO; original installation is still the older DEV. The previous 249-test/75-native
results are historical evidence, to be rerun as relevant after changes.
