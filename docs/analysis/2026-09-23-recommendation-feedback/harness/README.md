# Executed probe sources

`FeedbackProbe.cs` is the exact source used for native-headless-r5 (36 assertions). `BaselineProbe.cs` was used for native-headless-r6 (18 assertions). They replay the two adjacent recorded response files and intercept all provider client calls. They are not live-model evaluations or screenshot drivers.

The original build projects remain at `work/mapgenai-guided/native-feedback/FeedbackProbe.csproj` and `work/mapgenai-guided/native-baseline/BaselineProbe.csproj` in the task workspace. Both target net472, reference the game Managed assemblies and Harmony, and use assembly name `MapGenAI.FeedbackProbe`. The first references this build's product DLL; the second references the previous package DLL with SHA256 `2c1458bfcf9ac4f62b1719a7d7814f7a240723ca6b9ce58169337bc891919780`.

`run-headless.ps1` records the actual local run paths and accepts `-Run`, `-ProductDll`, and `-ProbeDll`. These paths are machine-specific. Prepare a physical disposable game copy (including MonoBleedingEdge), Harmony and Map Preview, use an isolated marked saved-data folder, and adjust paths before reusing elsewhere. Never point its mod destination at the user's normal installation. No game binaries are included in this repository.

The PNG artifacts are CPU-generated map preview textures, not GUI screenshots. NullGfxDevice produces graphics/atlas errors; compare images only with a baseline run under the same environment.
