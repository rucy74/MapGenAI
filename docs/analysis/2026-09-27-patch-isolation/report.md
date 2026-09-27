# Missing world-map button: cause, fix, and evidence

2026-09-27/28. Follows the Workshop report "WHY IST there no button" (discussion 581683497192766249) and the earlier inconclusive audit in `../2026-09-27-button-audit/`.

## Cause

With Map Preview's default layout the AI Map Gen button was drawn but could not be seen. The mod never registers a Map Preview toolbar button; it draws its own button during `WorldInterface.WorldInterfaceOnGUI`, underneath every window. The placement rule was "right of the Map Preview toolbar, otherwise below it". Map Preview keeps the toolbar about 50 UI units from the right screen edge, so the right spot never fits, and the preview window sits directly below the toolbar, so the button always landed inside the preview window's rectangle.

In `normal-old-placement` the button rectangle was (980, 105, 110, 30) and the preview window (980, 105, 250, 250). The button was drawn on every frame, yet the capture shows no button.

The button code is unchanged since the August release, and Map Preview's 1.6 assembly is dated 2026-08-09, so this was not introduced by the 2026-09-27 update.

A second weakness made any patch failure fatal to the entry point: the mod called `Harmony.PatchAll()`, which stops at the first class that throws, and the button patch is the last of 35 patch classes in the assembly. RimWorld only logs the constructor exception, so the button and the mod settings page would both disappear.

## Fix

- `Core/EntryButtonPlacement.cs`: right of the toolbar, then left of it, then below it; the first spot that is on screen and clear of every open window. In the default layout this is left of the toolbar.
- `Core/PatchPlan.cs` and `MapGenAIMod.ApplyPatches`: the entry patch is applied first and every other class separately; a failing class is logged by name and only its feature is off. Classes are selected with Harmony's own `HasHarmonyAttribute`, the same test `PatchAll` uses.
- `-mapgenAISimulatePatchFailure=<class>` makes one class fail on purpose; it is inert unless the game is started with that argument.

## Evidence

Offline tests: 333 PASS / 0 FAIL. Four mutations were caught: removing failure isolation (2 FAIL), removing entry-first ordering (3 FAIL), ignoring windows when placing the button (1 FAIL), restoring the old placement rule (2 FAIL).

Isolated game, English UI, 1920x1080 at UI scale 1.5, Map Preview defaults, no model calls. Each run captures `00-world-entry.png` with tile 281 selected and records `entryAudit` in `result.json`.

| Run | DLL | Patch classes | Button patch | Settings page | Button |
|---|---|---|---|---|---|
| normal-old-placement | 9d5c50f2 | 35/35 | applied | registered | drawn under the preview window, not visible |
| normal | e836d637 (dev) | 35/35 | applied | registered | left of the toolbar, visible |
| simulated-failure | e836d637 (dev) | 34/35 | applied | registered | visible |
| hotfix-normal | f3f82a06 (release) | 35/35 | applied | registered | visible |
| hotfix-simulated-failure | f3f82a06 (release) | 34/35 | applied | registered | visible |

The release hotfix is the public 2a039cd source plus these changes only; the development prompt compaction is not included. It was uploaded to the Workshop on 2026-09-28 00:12 (Steam log: manifest 411976757874084671, "Upload finished ... : OK").

## Not verified

Clicking the button was not simulated. The click code is unchanged and the new spot is not covered by any window. Whether the reporter's case was this layout issue is not confirmed; they have not answered which button they meant.
