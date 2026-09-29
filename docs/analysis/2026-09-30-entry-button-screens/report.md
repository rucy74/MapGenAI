# World-map button: starting-site screen and Map Preview's toolbar turned off

2026-09-30. Follows `../2026-09-27-patch-isolation/report.md` (v1.7.1). Two questions were left open by that release: whether the button works on the new-colony starting-site screen, where most players use it, and the limitation noted in `../2026-09-27-button-audit/`: the button is not drawn at all when Map Preview's toolbar is turned off in Map Preview's settings.

## What the probe does now

Isolated game, English UI, 1920x1080 at UI scale 1.5 (1280x720 UI units), Map Preview 1.6 defaults unless noted, dry run (no model calls). Scenario flags in this folder:

- `startingSite`: builds the same fixed-seed world the quick test uses, then opens the new-colony starting-site screen (`Page_SelectStartingSite`) instead of starting a game.
- `mapPreviewToolbarOff`: turns off Map Preview's own toolbar switches, for the starting-site screen and for play (`EnableToolbar`, `EnableToolbarInPlay`).
- `clickEntry`: clicks the drawn button. A mouse-down and, on a later frame, a mouse-up at the button's centre are handed to the button's own `GUI.Button` call, both in repaint passes; then checks that the map dialog opened.
- `entryAudit` now also records which window, if any, is over the button's centre and which windows overlap it.

## Results

| Run | Build | Screen | Map Preview toolbar | Button drawn at | Window over the button | Click opens the dialog |
|---|---|---|---|---|---|---|
| v171-site-on-click | v1.7.1 (f3f82a06) | starting site | on | (865,60) | none | yes |
| v171-play-on-click | v1.7.1 | world map in play | on | (865,60) | none | yes |
| v171-site-off | v1.7.1 | starting site | off | not drawn (0 draws) | - | - |
| rc-site-on | v1.7.2 candidate (9a792fad) | starting site | on | (865,60) | none | yes |
| rc-site-off | v1.7.2 candidate | starting site | off | (865,105) | none | yes |
| rc-play-on | v1.7.2 candidate | world map in play | on | (865,60) | none | yes |
| rc-play-off | v1.7.2 candidate | world map in play | off | (865,105) | none | yes |
| dev-site-off | dev (87a77f4a) | starting site | off | (865,105) | none | yes |
| dev-play-off | dev (87a77f4a) | world map in play | off | (865,105) | none | yes |

On both screens Map Preview's toolbar was at (980,50,250,50) and its preview window at (980,105,250,250). No run logged an exception; the same log scan finds the known error in `../2026-09-27-patch-isolation/simulated-failure`.

`v171-site-on` and `v171-play-on` were the first attempts. The button was in the same place, but their clicks did not register: the probe sent the mouse-down in a layout pass and the mouse-up in a repaint pass, and IMGUI gives the button a different control id in each. That was a probe defect, fixed before the `-click` runs. Those two failed attempts also show that the dialog does not open without the click.

## Fix (v1.7.2)

`Core/EntryButtonPlacement.Anchor` chooses what the button is placed beside. When Map Preview's toolbar is open, it is the toolbar. When the toolbar is turned off, it is the top edge of the preview window. When neither is open, it is the top-right corner. `WorldInterface_Patch.Postfix` no longer requires the toolbar.

Offline tests: 337 PASS / 0 FAIL, including 4 new tests. Four mutations were each caught:

| Mutation | Result |
|---|---|
| Preview window ignored | 2 FAIL |
| Whole preview window used as the anchor | 2 FAIL |
| No top-right corner fallback | 1 FAIL |
| Preview window preferred over the toolbar | 1 FAIL |

The v1.7.2 package is the v1.7.1 files with only the DLL and `build.json` replaced. It uses the DLL tested above (9a792fad).

## Not verified

- A real mouse click. The probe hands the events to the button's own GUI call, so window focus and other mods' input handling are not exercised.
- Map Preview windows at non-default positions or sizes, and other UI scales or resolutions. Unit tests cover a dragged preview window and the no-preview case.
- The top-right corner fallback in the game. It is covered by a unit test only.
- Whether the reporter's case was either of these; they have not answered.
