# MapGen AI v1.7.2 — installation and Workshop verification

2026-09-30, Codex. Continues the button fix and the checks in [the earlier report](../2026-09-30-entry-button-screens/report.md). User requested “이어서 작업 해 줘” after the resume briefing specified installation, publication, subscription verification and the version tag.

## Result

The existing Workshop item **3685385453** was updated to the prepared v1.7.2 build. This fixes the world-map entry button when Map Preview's toolbar is disabled. The publication uses the release-only build, without DEV prompt compaction.

- Release source: `hotfix/entry-button`, commit `48d0d82372b70b095e918b4b209b41634eaa2cf5` (code fix `3383e39`).
- Annotated release tag: `v1.7.2` at that release commit. DEV's distribution synchronization commit is `0beedc1`; the release tag points to the release source rather than the DEV prompt-compaction branch.
- DLL SHA256: `9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5`.
- Upload completed at 21:05:45 KST. [Callback receipt](steam-upload-receipt.json): `k_EResultOK`, correct item, no I/O failure or outstanding legal agreement; [client upload log](steam-upload.log).
- [Independent public API](public-api-receipt.json): updated timestamp `1790769943`, 911,208 bytes, content manifest `4012711468297338724`.
- [Public change notes](https://steamcommunity.com/sharedfiles/filedetails/changelog/3685385453) include the v1.7.2 button fix. Publication changes content and change notes; title, full-description hash, tags, visibility and preview size match before and after.
- Installed normal mod, Steam subscription and repository `dist` match the nine-file ZIP payload, excluding the existing `PublishedFileId.txt` on installed/subscribed copies. See [comparison receipt](release-verification.json).

## Installation

The existing `install_release.py` backed up the previous normal mod and preserved its Workshop ID. Its [installation receipt](installation-receipt.json) records all four checks as true: payload match, unchanged Workshop ID, unchanged installed DEV and unchanged user configuration. No RimWorld process was running immediately before installation.

Workspace records: `agents/main/log/2026-09-30-2057-mapgenai-evidence/`. The installer, one-release Steam SDK publisher and full previous-folder backup are in `work/mapgenai-packages/2026-09-30-entry-button-v1.7.2/`. These workspace files are local, outside this product repository.

## Fresh runtime check

The installed normal DLL was used in an isolated, disposable game with the previous `site-toolbar-off.json` dry-run scenario. The real starting-site screen drew the button at UI coordinates (865,105), beside the preview, with no window over it. The probe delivered mouse-down/up events to the button's own `GUI.Button`, and the map dialog opened. No model calls were made. The game exited and its owned probe mod was archived out of `Mods`.

Sources: [result](installed-site-off/result.json), [launch](installed-site-off/launch.json), [cleanup](installed-site-off/cleanup.json), [entry capture](installed-site-off/00-world-entry.png), [dialog capture](installed-site-off/01-after-click.png). The earlier 337-test suite and four release-screen runs are earlier evidence; they were not rerun for this file-identical installation.

The [raw game log](installed-site-off/Player.log.zip) is stored byte-for-byte in a ZIP. Unity emitted mixed CRLF/CRCRLF and trailing spaces; the local original is kept unmodified and excluded from Git rather than being reformatted as evidence.

## Publication method

An independent console helper uses RimWorld's bundled Steamworks.NET/native API and the already logged-in Steam session. Its read-only preflight checks app 294100, ownership of the existing item, and all installed payload hashes against the installation receipt. Its publish mode calls `StartItemUpdate`, `SetItemContent`, and `SubmitItemUpdate` with the reviewed [change-note file](change-notes.txt). It does not create an item or set presentation metadata. This follows the [official Workshop update API](https://partner.steamgames.com/doc/features/workshop/implementation#Uploading_a_Workshop_Item), also used by RimWorld's built-in uploader. The receipt, Steam upload log, separate public API response, public changelog and actual refreshed subscribed files provide distinct evidence. The exact helper source is archived in [Program.cs](publisher-source/Program.cs); its explicit `inspect`, `publish`, and `download` modes are separate. Build output and bundled Steam libraries are not included in this repository.

## Remaining limits

- A physical mouse click, alternate UI scales/resolutions and non-default preview-window positions/sizes remain untested in this session.
- The reporter has not identified which button they meant; these fixes address independently reproduced entry-button problems.
- Installed DEV still has its previous prompt-compaction build (`45646072…`); source DEV has the button fixes. This publication does not install that DEV build.
- River relocation/feature replacement and the older unfinished terrain work remain separate tasks.

Read-only checks: `py -3 -X utf8 docs/analysis/2026-09-30-v1.7.2-release/verify_release.py`. The equality check rejects a deliberately changed DLL; the API-key scan detects two synthetic positive controls before scanning the release evidence.
