# Independent organic sampler check

This standalone .NET 10 harness loads the specified current and frozen pure-test DLLs
from captured bytes in separate assembly contexts. It does not compile product source,
call a provider, install a mod or run RimWorld.

```powershell
dotnet run --project tools/organic-landform-independent-check/Review.csproj -- <current-dll> <frozen-r7-dll> <fresh-output-prefix>
```

The unfiltered corpus has 160 new seeds per kind, three parameter combinations per seed,
and all three landforms: **1,440 layouts**. `layout:organic` is explicit. Parameters cover
minimum/maximum and intermediate sizes, gaps, arbitrary and near-cardinal angles, and
basin openings. Positions are centered, on neutral height 0.2.

Cardinal BFS reads actual mixed heights: passability below 0.7 and usable lowland below 0.1.
It requires all lowland to share a component reaching the map boundary. It does not consult
the sampler's `floor` field or the product's flood-fill code. A deliberately severed plain
must fail as a detector control. Small higher pockets inside mountains are separately
reported and are not interpreted as settlement space. This is a usability check, not a
beauty metric or a guarantee over moved/clipped maps, native water or structures.

Every case also compares current `null` and `classic` profiles against the frozen DLL:
float influence/elevation are compared bit-for-bit, and `floor` exactly. The output records
DLL hashes, every case and all failure height grids; failures are never silently filtered
or regenerated. Output files use CreateNew and cannot overwrite an earlier run.
