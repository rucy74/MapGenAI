# Independent review and fixes

Two Codex roles reviewed/tested this change under the project completion workflow. No Claude/Fable
consultation or external provider call was made during this implementation.

The adversarial reviewer found and reproduced:

1. Foothill spurs could clip diagonal map corners and leave planned floor disconnected. Variant 38,
   size=large, gap=.1, direction=134 and variant 95, size=1, gap=.32, direction=155 reproduced it.
   The generator now tapers the outer spurs near the footprint ends. Regression coverage includes
   300 unfiltered boundary cases; independent scans repeat both cases at 80, 81 and 250 resolution.
2. An inherited basin `opening` prevented changing its kind to valley/foothills; `opening:null`
   also failed. Subtype edits now clear an inherited inapplicable opening, null restores the default,
   while explicitly supplying an opening to the wrong kind remains an atomic validation error.
3. The English package description could imply follow-up edits had no API cost. It now states that
   edits use normal chat requests and only the local geometry stage adds no further model call.

The independent tester loaded the final r7 pure-test assembly and used a separate four-neighbor BFS
on actual blended heights, without using the production floor mask or production flood-fill. Both
corner cases pass at all three resolutions. New variants 1000..1159 × three kinds × two sets of
boundary/random parameters yield 960 layouts and zero disconnected low-floor cases. This does not
claim that every low bank fragment belongs to one component; the intended floor is the invariant.

Evidence: `independent-result.json`, `independent-run.log` and
`tools/natural-landform-independent-check/Program.cs`. From a folder where output files may be written:

```powershell
dotnet run --project <repo>/tools/natural-landform-independent-check/Review.csproj -p:NaturalTestsDll=<absolute-path-to-final-TextToMap.Tests.dll>
```

The recorded native n5/n9 evidence used the earlier C576EC62 product DLL. Final native n10/n11/n12
re-ran foothills, full-map integration and legacy recommendation preservation on the 73011EE2 DLL.
Do not merge earlier-build and final-build results without keeping this lineage.
