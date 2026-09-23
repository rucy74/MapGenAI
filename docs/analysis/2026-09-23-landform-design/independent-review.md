# Independent organic geometry verification

Result: **PASS**, using captured DLL bytes rather than product source compiled into the checker.
No product files, protected original repository, installation or API/game runtime were changed.

## Tested builds

- Current: `mapgenai-design-provider-d2/TextToMap.Tests.dll`, SHA256
  `e1b206c70412e11347cc02e36425cb4bbd55125faeb5290bbbd20e087339c718`.
- Frozen prior sampler: `mapgenai-natural-tests-r7/TextToMap.Tests.dll`, SHA256
  `804d98d62c5e879dd870a2b1d240f36ff76dca3ed77a4fb94ba150ed7cb44c85`.

Both assemblies were loaded from byte snapshots into separate assembly contexts. The recorded
hashes, not an output directory's informal version label, identify the tested code.

## Evidence

- `independent-d2-r1-summary.json`: hashes, totals, detector control and boundary statement.
- `independent-d2-r1-corpus.jsonl`: all 1,440 input parameter sets and measurements, without seed filtering.
- `independent-d2-r1.log`: execution output. Completed in about 27 seconds; this is harness time,
  not a native map-generation performance result.
- `tools/organic-landform-independent-check`: standalone reproducer and method description.

New seeds **156000–156159**, three kinds and three parameter combinations per seed produced
**1,440 organic layouts** at 80×80. Every shape explicitly sets `layout:organic`. The corpus includes
minimum/maximum and random intermediate scale/gap/opening values, arbitrary angles and angles
immediately beside cardinal/diagonal directions. Positions stay centered.

The checker evaluates mixed height `influence * elevation + (1-influence) * 0.2`. An independent
four-neighbor BFS uses height `<0.7` as passable and height `<0.1` as usable lowland. All lowland must
share a component reaching a map edge. The sampler's `floor` flag and product connectivity helpers
are not used for this judgment. **Zero layouts failed.** A deliberately severed organic foothill
plain failed the same detector, so a disconnected floor is observable by this checker.

| Kind | Layouts | Minimum lowland cells | Minimum solid lowland square | Disconnected lowland cells |
|---|---:|---:|---:|---:|
| Open basin | 480 | 59 | 4×4 | 0 |
| Winding valley | 480 | 337 | 7×7 | 0 |
| Foothills | 480 | 466 | 9×9 | 0 |

Smallest supported size/width intentionally allows smaller usable areas. These measurements do not
claim every boundary configuration is a spacious settlement or visually pleasing.

For every corpus entry, the frozen sampler was separately compared with both current `layout:null`
and current `layout:classic`. Across **9,216,000 coordinates**—**18,432,000 pairwise cell comparisons**—
float influence/elevation were identical bit-for-bit and the floor flag was exactly equal.
**Zero differing cells.** Organic differed from classic in every layout, confirming that the new
branch was exercised rather than accidentally rerunning the classic sampler.

## Reproduction

From the repository root, use a fresh output prefix:

```powershell
dotnet run --project tools/organic-landform-independent-check/Review.csproj -- C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/mapgenai-design-provider-d2/TextToMap.Tests.dll C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/mapgenai-natural-tests-r7/TextToMap.Tests.dll docs/analysis/2026-09-23-landform-design/independent-d2-r2
```

The checker writes each failed case with raw sample/mixed-height arrays and never silently rerolls
or overwrites previous evidence. This run generated no failure files.

## Limits

This establishes pure geometry connectivity and legacy sampler preservation for centered layouts on
neutral 0.2 ground. It does not establish all clipped/moved layouts, native water or building behavior,
save integration, actual visuals, provider quality or final product-DLL runtime behavior. Small higher
pockets inside mountain walls are reported but are not required to join the low settlement floor.
The parent task's native tests and visual review cover separate boundaries.
