"""Review recorded first replies and independently measured maps; no API calls."""
from pathlib import Path
import argparse
import copy
import json


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def state(path):
    return read(path)["state"]


def norm(value):
    if isinstance(value, float):
        return round(value, 5)
    if isinstance(value, dict):
        return {k: norm(v) for k, v in value.items()}
    if isinstance(value, list):
        return [norm(v) for v in value]
    return value


def same(actual, expected, reason):
    assert norm(actual) == norm(expected), reason


def shapes(s):
    return {v["id"]: v for v in s["elevationShapes"]}


def one(s, kind):
    found = [v for v in s["elevationShapes"] if v["type"] == kind]
    assert len(found) == 1, (kind, len(found))
    return found[0]


def basin_contract(before, after, turn):
    clean = copy.deepcopy(after)
    old, new = shapes(before), shapes(clean)
    fill = one(after, "region_fill")
    ring = shapes(after)[fill["region"]]
    floor = next(v for v in after["elevationShapes"] if v["type"] == "composite" and v["id"] != ring["id"])
    assert fill["region_part"] == "enclosed" and float(fill["coverage"]) == (.7 if turn < 6 else .5)
    assert sum(v["count"] for v in after["structures"]) == 2
    assert all(v["region"] == ring["id"] and v["region_part"] == "enclosed" for v in after["structures"])
    assert after["elevationShapes"].index(floor) < after["elevationShapes"].index(ring)
    assert any(v["fill"] == "Soil" and abs(v["e"] - .05) < 1e-6 for v in floor["compositeOps"])
    if turn < 7:
        route = one(after, "passage")
        assert route["scope"] == "mountains" and route["width"] == (8 if turn < 5 else 12)
    if turn == 1:
        clean["elevationShapes"] = before["elevationShapes"]
        clean["structures"] = before["structures"]
    elif turn == 2:
        # Only the two inner/floor radii may grow. The outer boundary and all
        # other scalar/geometry/structure fields must remain identical.
        ring_before = old[ring["id"]]
        subtraction = next(v for v in ring["compositeOps"] if v["op"] == "sub")
        inner_id = subtraction["a"]
        for part in new[ring["id"]]["compositeShapes"]:
            previous = next(v for v in ring_before["compositeShapes"] if v["id"] == part["id"])
            if part["id"] == inner_id:
                assert part["r"] > previous["r"]
                part["r"] = previous["r"]
        ground = new[floor["id"]]["compositeShapes"][0]
        previous = old[floor["id"]]["compositeShapes"][0]
        assert ground["r"] > previous["r"]
        ground["r"] = previous["r"]
    elif turn == 3:
        for item in clean["elevationShapes"]:
            previous = old[item["id"]]
            if item["type"] == "composite":
                for part, prior in zip(item["compositeShapes"], previous["compositeShapes"]):
                    same(part["center"], [prior["center"][0] + .08, prior["center"][1]], "Compound move omitted a component")
                    part["center"] = prior["center"]
            elif item["type"] == "passage":
                same(item["points"], [[p[0] + .08, p[1]] for p in previous["points"]], "Exit did not follow move")
                item["points"] = previous["points"]
    elif turn == 4:
        same(route["points"], [[.58, .5], [1, .5]], "Wrong exit direction")
        new[route["id"]]["points"] = old[route["id"]]["points"]
    elif turn == 5:
        new[route["id"]]["width"] = old[route["id"]]["width"]
    elif turn == 6:
        new[fill["id"]]["coverage"] = old[fill["id"]]["coverage"]
    elif turn == 7:
        previous = one(before, "passage")
        assert not any(v["type"] == "passage" for v in after["elevationShapes"])
        clean["elevationShapes"].insert(before["elevationShapes"].index(previous), previous)
    same(clean, before, "Unrequested basin state changed at turn " + str(turn))


def basic_contract(before, after, turn):
    clean = copy.deepcopy(after)
    old, new = shapes(before), shapes(clean)
    if turn == 1:
        assert len(new) == 2
        mountain = next(v for v in new.values() if v["type"] == "ridge")
        lake = one(clean, "composite")
        assert mountain["direction"] == "left"
        assert lake["compositeShapes"][0]["prim"] == "circle"
        assert lake["compositeShapes"][0]["center"][0] > .5 and lake["compositeShapes"][0]["center"][1] < .5
        assert any(v["fill"] == "water" for v in lake["compositeOps"])
        clean["elevationShapes"] = before["elevationShapes"]
    elif turn == 2:
        changed = [key for key in old if norm(old[key]) != norm(new[key])]
        assert len(changed) == 1 and new[changed[0]]["fill"] == "LavaDeep"
        new[changed[0]]["fill"] = old[changed[0]]["fill"]
    elif turn == 3:
        added = set(new) - set(old)
        assert len(added) == 1
        star = new[next(iter(added))]
        assert star["edge_roughness"] in (None, "none", "0")
        assert any(p["prim"] == "star" for p in star["compositeShapes"])
        clean["elevationShapes"] = [s for s in clean["elevationShapes"] if s["id"] not in added]
    elif turn == 4:
        changed = [key for key in old if norm(old[key]) != norm(new[key])]
        assert len(changed) == 1 and new[changed[0]]["edge_roughness"] == "medium"
        new[changed[0]]["edge_roughness"] = old[changed[0]]["edge_roughness"]
    else:
        removed = set(old) - set(new)
        assert len(removed) == 1
        removed_shape = old[next(iter(removed))]
        if turn == 5:
            assert any(p["prim"] == "star" for p in removed_shape["compositeShapes"])
        else:
            assert removed_shape["fill"] == "LavaDeep"
        clean["elevationShapes"].insert(before["elevationShapes"].index(removed_shape), removed_shape)
    same(clean, before, "Unrequested basic state changed at turn " + str(turn))


def two_contract(before, after, turn, first):
    groups = {}
    for ring in first["elevationShapes"]:
        if ring["type"] == "composite" and any(v["op"] == "sub" for v in ring["compositeOps"]):
            groups["left" if ring["compositeShapes"][0]["center"][0] < .5 else "right"] = ring["id"]
    assert set(groups) == {"left", "right"}
    if turn == 1:
        assert len(first["structures"]) == 2 and all(p["count"] == 1 and p["region_part"] == "enclosed" for p in first["structures"])
        assert len([s for s in first["elevationShapes"] if s["type"] == "passage" and s["width"] == 8 and s["scope"] == "mountains"]) == 2
        assert len([s for s in first["elevationShapes"] if s["type"] == "region_fill" and float(s["coverage"]) == .7]) == 2
    elif turn == 2:
        clean = copy.deepcopy(after)
        fill = next(s for s in clean["elevationShapes"] if s["type"] == "region_fill" and s["region"] == groups["right"])
        assert float(fill["coverage"]) == .3
        fill["coverage"] = shapes(before)[fill["id"]]["coverage"]
        same(clean, before, "Other basin changed with right coverage")
    else:
        expected = copy.deepcopy(before)
        def left(s):
            if s["region"]:
                return s["region"] == groups["left"]
            if s["type"] == "passage":
                return s["points"][0][0] < .5
            return s["compositeShapes"][0]["center"][0] < .5
        expected["elevationShapes"] = [s for s in expected["elevationShapes"] if left(s)]
        expected["structures"] = [p for p in expected["structures"] if p["region"] == groups["left"]]
        same(after, expected, "Deleting right basin damaged left or left orphaned right components")


def map_contract(result, planned):
    assert result.get("generated") and result["undo"], result.get("error", result.get("message"))
    assert not result["issues"], result["issues"]
    audit = result["passageScopeAudit"]
    assert audit["outsideChanges"] == audit["unclearedSelectedCells"] == 0
    for cut in audit["passages"]:
        assert cut["selectedCells"] > 0 and cut["selectedOpenGround"] == 0
    geometry = result["compoundAudit"]
    for route in geometry["routes"]:
        assert route["cutCells"] > 0 and route["blockedCutCells"] == 0
    for enclosure in geometry["enclosures"]:
        assert enclosure["waterCells"] == 0
        assert enclosure["dryWalkableCells"] > enclosure["cells"] * .7
    for measured in geometry["fills"]:
        fill = shapes(planned)[measured["id"]]
        assert measured["eligible"] > 100
        # Compare the requested fraction independently. Either neighbouring cell
        # is valid at a half-cell tie; production stores the fraction as float32.
        assert abs(measured["painted"] - measured["eligible"] * float(fill["coverage"])) <= .501
        assert measured["waterInInterior"] == 0
    for measured in geometry["structures"]:
        assert measured["outsideRegionCells"] == measured["routeOverlapCells"] == 0
    assert len(result["placements"]) == sum(p["count"] for p in planned["structures"])
    assert all(p["spawnedWalls"] > 0 for p in result["placements"])


def suite(folder):
    raw = read(folder / "suite-result.json")
    assert raw["complete"] and raw["fatal"] is None
    return raw["results"]


def evaluate(root):
    observations = []
    edits = 0
    for folder, prefix, count, contract in [("ko", "basin", 7, basin_contract), ("basic", "basic", 6, basic_contract)]:
        provider = root / ("provider-r1-" + folder)
        for turn in range(1, count + 1):
            name = f"{prefix}-{turn:02}"
            contract(state(provider / (name + "-before.json")), state(provider / (name + "-after.json")), turn)
            edits += 1
    provider = root / "provider-r1-two"
    first = state(provider / "two-01-after.json")
    for turn in range(1, 4):
        name = f"two-{turn:02}"
        two_contract(state(provider / (name + "-before.json")), state(provider / (name + "-after.json")), turn, first)
        edits += 1
    for folder in ["native-r1-shapes", "native-r1-en", "native-r1-ko", "native-r1-basic", "native-r1-two", "native-seed1-ko", "native-features", "native-normal"]:
        current = root / folder
        for result in suite(current):
            planned = state(current / (result["id"] + "-after.json"))
            map_contract(result, planned)
            if result["kind"] == "caldera" or result["id"] == "basin-07":
                assert not result["centerReachesAnyEdge"], "Closed caldera leaks"
            if result["kind"] in ("valley-exit", "straight-canyon", "bent-canyon"):
                assert result["dryConnectionWidth8"], "Requested dry connection absent"
            if result["kind"] == "bent-canyon":
                assert result["waypointsReachedWidth8"]
            observations.append({"run": folder, "id": result["id"], "enclosures": result["compoundAudit"]["enclosures"], "routes": result["compoundAudit"]["routes"]})
        preview = current / "preview-result.json"
        if preview.exists():
            raw = read(preview)
            assert raw["ok"] and raw["results"]
            for result in raw["results"]:
                assert not result["issues"]
                assert all(e["waterCells"] == 0 for e in result["compoundAudit"]["enclosures"])
    for name in ["features-add", "features-remove"]:
        current = root / "native-features"
        before, after = [state(current / (name + suffix)) for suffix in ["-before.json", "-after.json"]]
        expected = copy.deepcopy(before)
        if name == "features-add":
            expected["mutators"] = ["HotSprings", "SunnyMutator"]
        else:
            expected["mutators"] = ["SunnyMutator"]
            expected["removeMutators"] = ["HotSprings"]
        same(after, expected, "Feature edit damaged unrelated settings")
        terrain = read(current / (name + "-cells.json"))["terrain"]
        assert ("HotSpring" in terrain["names"]) == (name == "features-add")
    identical = []
    old_root = root.parent / "2026-09-19-compound-plan"
    for old, new in [("legacy-new", "legacy-new"), ("final-ko", "compound-new")]:
        previous = {r["id"]: r for r in suite(old_root / old)}
        for result in suite(root / new):
            name = result["id"]
            assert result["generated"] and result["cellHash"] == previous[name]["cellHash"], (new, name, "Frozen map changed")
            same(read(root / new / (name + "-cells.json")), read(old_root / old / (name + "-cells.json")), "Terrain/edifice/roof cells changed")
            identical.append(new + "/" + name)
    # Deliberately broken states/maps must be rejected by the same public checks.
    caught = []
    p = root / "provider-r1-ko"
    before = state(p / "basin-02-before.json")
    bad = state(p / "basin-02-after.json")
    bad["elevationShapes"][1]["compositeShapes"][0]["r"] += .05
    try:
        basin_contract(before, bad, 2)
    except AssertionError:
        caught.append("outer ring changed during interior resize")
    result = copy.deepcopy(suite(root / "native-r1-ko")[0])
    result["compoundAudit"]["fills"][0]["painted"] += 100
    try:
        map_contract(result, state(p / "basin-01-after.json"))
    except AssertionError:
        caught.append("wrong coverage by 100 cells")
    result = copy.deepcopy(suite(root / "native-r1-ko")[0])
    result["compoundAudit"]["routes"][0]["blockedCutCells"] = 1
    try:
        map_contract(result, state(p / "basin-01-after.json"))
    except AssertionError:
        caught.append("one blocked passage cell")
    assert len(caught) == 3
    return {"ok": True, "reviewedSequentialEdits": edits, "newFullMaps": len(observations), "frozenIdenticalMaps": identical, "detectorPositiveControls": caught, "observations": observations,
            "limits": "RimWorld1.6, recorded Gemini first replies and fixed inland tiles/seeds. No GL generator integration, all-language/model guarantee, or thick-roof cave/coast support. Mountain-only passages preserve outside terrain; endpoint connectivity is reported separately."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    args = parser.parse_args()
    report = evaluate(args.root)
    (args.root / "evaluation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "observations"}, ensure_ascii=False))
