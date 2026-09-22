"""Prepare/review recorded MG31 responses against full-map and Map Preview runs."""
import argparse
import json
import shutil
from pathlib import Path
from PIL import Image, ImageChops


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def prepare(root):
    suite = root / "suite-input"
    suite.mkdir(exist_ok=True)
    cases = []
    for lang in ("ko", "en"):
        source = root / f"provider-{lang}"
        assert read(source / "result.json")["ok"]
        for case in read(source / "cases.json")["cases"]:
            case["generateUnchanged"] = True
            cases.append(case)
            for suffix in ("-before.json", "-response.json"):
                shutil.copy2(source / (case["id"] + suffix), suite / (case["id"] + suffix))
    # Replay the real corrected command across a native world river, preserving
    # both existing road IDs and their prior state. This is a fixture, not a new API sample.
    river = dict(cases[1], id="centered-cross-river", worldRiver=True,
                 beforeFile="centered-cross-river-before.json")
    for suffix in ("-before.json", "-response.json"):
        shutil.copy2(suite / (cases[1]["id"] + suffix), suite / (river["id"] + suffix))
    cases.append(river)
    (suite / "cases.json").write_text(json.dumps({"cases": cases}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(suite / "cases.json")


def controlled(root):
    suite = root / "controlled-input-r2"
    suite.mkdir(exist_ok=True)
    # A declared dry-floor fixture isolates road geometry from preserved natural
    # outcrops. Native river generation still happens after this floor operation.
    old = root.parent / "2026-09-22-road-bridges/native-r1/native-river-before.json"
    base = read(old)
    roads = read(root / "provider-ko/memory-ko-2-before.json")["state"]["localRoads"]
    cases = []
    for river in (False, True):
        ident = "cross-river" if river else "cross-dry"
        before = json.loads(json.dumps(base))
        before["state"]["localRoads"] = roads
        before["state"]["straightRiver"] = river
        before["state"]["riverXPosition"] = .25 if river else .5
        if not river:
            before["state"]["hasRiver"] = False
            before["state"]["riverDirectionAngle"] = -1
        (suite / f"{ident}-before.json").write_text(json.dumps(before), encoding="utf-8")
        shutil.copy2(root / "provider-ko/memory-ko-2-response.json", suite / f"{ident}-response.json")
        cases.append(dict(id=ident, kind="local-roads", beforeFile=f"{ident}-before.json",
                          worldRiver=river, generateUnchanged=True, omitAmbientRuins=True,
                          preview=True, request="Controlled dry-floor fixture: replay the actual centered-cross correction. Preserve native river water."))
    (suite / "cases.json").write_text(json.dumps({"cases": cases}, indent=2), encoding="utf-8")
    print(suite / "cases.json")


def evaluate(root):
    checks = []
    def check(name, ok):
        checks.append(dict(name=name, ok=bool(ok)))
    def rows(folder, preview=False):
        data = read(root / folder / ("preview-result.json" if preview else "suite-result.json"))
        check(folder + (" preview complete" if preview else " maps complete"),
              data.get("ok") if preview else data.get("complete") and not data.get("fatal"))
        return {r["id"]: r for r in data["results"]}
    def pixels(a, b):
        with Image.open(a) as x, Image.open(b) as y:
            return x.size == y.size and ImageChops.difference(x.convert("RGB"), y.convert("RGB")).getbbox() is None
    for lang in ("ko", "en"):
        native = read(root / f"native-{'final-r2-' if lang == 'ko' else ''}{lang}" / "result.json")
        check(lang + " native dialog checks", native["ok"] and native["unexpectedProviderCalls"] == 0)
        provider = read(root / f"provider-{lang}" / "result.json")
        check(lang + " fresh multi-turn and summary", provider["ok"] and len(provider["results"]) == 5)
        memory = read(root / f"provider-{lang}/summary.json")
        check(lang + " actual summary reduction", memory["InputTokens"] < memory["testBudget"] * .6
              and memory["rawMessages"] > memory["sentMessages"] and memory["SummaryCalls"] > 0)
    placement_failures = []
    for preview in (False, True):
        current, old = rows("maps-dev", preview), rows("maps-baseline-r2", preview)
        check("same replay IDs", current.keys() == old.keys() and len(current) == 9)
        suffix = "-background-preview.png" if preview else "-generated-preview.png"
        for ident, row in current.items():
            before = old[ident]
            audit = row["roadAudit"]
            imm = audit["immediate"]
            check(ident + suffix + " terrain/road layers unchanged from released DLL",
                  audit["finalLayerHash"] == before["roadAudit"]["finalLayerHash"])
            check(ident + suffix + " image unchanged from released DLL",
                  pixels(root / "maps-dev" / (ident + suffix), root / "maps-baseline-r2" / (ident + suffix)))
            check(ident + suffix + " preserves world links", audit["worldRoadsStillUnchanged"] and audit["worldRiversStillUnchanged"])
            if imm["applyFailed"]:
                check(ident + suffix + " protected obstacle rejection is atomic", imm["failureLayersUnchanged"])
                placement_failures.append(dict(id=ident, preview=preview, issues=row.get("issues")))
    for preview in (False, True):
        data = rows("controlled-native-r2", preview)
        for ident, row in data.items():
            audit = row["roadAudit"]
            roads = audit["roads"]
            check(ident + str(preview) + " two successful roads", len(roads) == 2 and not audit["immediate"]["applyFailed"])
            check(ident + str(preview) + " both orthogonal center axes", len(roads) == 2
                  and any(all(p[1] == 125 for p in r["path"]) for r in roads)
                  and any(all(p[0] == 125 for p in r["path"]) for r in roads))
            check(ident + str(preview) + " walkable and connected", len(roads) == 2 and all(
                r["connectedWithinFootprint"] and r["blockedPathCells"] == 0 and r["unwalkablePathCells"] == 0 for r in roads))
            check(ident + str(preview) + " no lost water or world links", audit["worldRoadsStillUnchanged"]
                  and audit["worldRiversStillUnchanged"] and audit["immediate"]["waterChangedWithoutBridge"] == 0
                  and all(r["bridgedWaterMissingOriginalWater"] == 0 for r in roads))
            if ident == "cross-river":
                check("native river gets walkable bridges " + str(preview), sum(r["bridgedWaterCells"] for r in roads) > 0
                      and all(r["bridgeUnwalkableCells"] == 0 for r in roads))
    report = dict(ok=all(c["ok"] for c in checks), checks=checks,
                  placementFailures=placement_failures,
                  scope="Intent and history repaired; raw natural obstructions remain protected. Placement failure is not a successful cross.")
    (root / "evaluation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(dict(ok=report["ok"], checks=len(checks), failed=[c for c in checks if not c["ok"]],
                         protectedPlacementFailures=len(placement_failures)), ensure_ascii=False))
    return 0 if report["ok"] else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--controlled", action="store_true")
    parser.add_argument("--evaluate", action="store_true")
    args = parser.parse_args()
    if args.prepare:
        prepare(args.root)
    if args.controlled:
        controlled(args.root)
    if args.evaluate:
        raise SystemExit(evaluate(args.root))
