"""Compare real generator observations with an unchanged DLL control. No model or game calls."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageChops


def evaluate(baseline: Path, fixed: Path):
    read = lambda p: json.loads(p.read_text(encoding="utf-8-sig"))
    b, f = read(baseline / "result.json"), read(fixed / "result.json")
    bd, fd = ({r["id"]: r for r in d["records"]} for d in (b, f))
    checks = []
    def check(name, condition):
        checks.append({"name": name, "ok": bool(condition)})
    check("Both isolated runs completed", b["ok"] and f["ok"])
    check("No model calls in the replay scripts", b["apiCalls"] == f["apiCalls"] == 0)
    for kind in ("preview", "map"):
        original = fd["river-caves"][kind]
        check(f"{kind}: baseline detects river shift after native removal", bd["river-caves"][kind]["riverCentre"] != bd["river-removed"][kind]["riverCentre"])
        for edit in ("removed", "replaced"):
            actual = fd[f"river-{edit}"][kind]
            check(f"{kind}: {edit} preserves native river centre and whole course", all(original[k] == actual[k] for k in ("riverCentre", "courseSha256")))
        original = fd["coast-caves"][kind]
        check(f"{kind}: baseline detects coast field shift after native removal", bd["coast-caves"][kind]["coastFieldSha256"] != bd["coast-removed"][kind]["coastFieldSha256"])
        for edit in ("removed", "replaced"):
            check(f"{kind}: {edit} preserves all 62500 coast field values", original["coastFieldSha256"] == fd[f"coast-{edit}"][kind]["coastFieldSha256"])
        for case in ("river-plain", "coast-plain", "river-added-springs", "coast-added-springs"):
            check(f"{kind}: {case} keeps previous whole terrain output", bd[case][kind]["terrainSha256"] == fd[case][kind]["terrainSha256"])
        before, after = (fd[f"north-{s}"][kind]["riverCentre"] for s in ("before", "after"))
        check(f"{kind}: original north reply moves Z to 212 and keeps X", before[0] == after[0] and after[1] == 212 and before[1] != after[1])
        road = fd["road-after"][kind]["authoring"]
        check(f"{kind}: original road reply produces one usable road", len(road["roads"]) == 1 and road["roads"][0]["blockedCells"] == 0 and not road["issues"])
        check(f"{kind}: baseline detects the same road reply failure", bool(bd["road-after"][kind]["authoring"]["issues"]) and not bd["road-after"][kind]["authoring"]["roads"])
    check("Original north reply control changes X instead of Z", bd["north-after"]["preview"]["riverCentre"][0] != bd["north-before"]["preview"]["riverCentre"][0] and bd["north-after"]["preview"]["riverCentre"][1] == bd["north-before"]["preview"]["riverCentre"][1])
    check("Other-tile control detects editor state leak", bd["other-before"]["preview"]["terrainSha256"] != bd["other-editor-active"]["preview"]["terrainSha256"])
    check("Other tile keeps all terrain while editor A is active", fd["other-before"]["preview"]["terrainSha256"] == fd["other-editor-active"]["preview"]["terrainSha256"] == bd["other-before"]["preview"]["terrainSha256"])
    check("Baseline candidate uses different native order", b["candidateOrder"] != b["appliedOrder"])
    check("Candidate and applied native order match", f["candidateOrder"] == f["appliedOrder"])
    check("Candidate and applied whole terrain match", fd["candidate-unapplied"]["preview"]["terrainSha256"] == fd["candidate-applied"]["preview"]["terrainSha256"])
    images = [Image.open(fixed / "captures" / f"candidate-{name}-preview.png").convert("RGBA") for name in ("unapplied", "applied")]
    difference = ImageChops.difference(*images)
    changed = sum(any(pixel) for pixel in difference.getdata())
    check("Candidate and applied 62500 preview pixels match", images[0].size == images[1].size == (250,250) and changed == 0)
    for image in images: image.close()
    return {"ok": all(c["ok"] for c in checks), "passed": sum(c["ok"] for c in checks), "failed": sum(not c["ok"] for c in checks), "candidateChangedPixels": changed, "checks": checks}


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("baseline", type=Path)
    p.add_argument("fixed", type=Path)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args()
    result = evaluate(args.baseline, args.fixed)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Edit stability: {result['passed']} PASS / {result['failed']} FAIL; candidate changed pixels={result['candidateChangedPixels']}")
    for check in result["checks"]:
        if not check["ok"]: print("FAIL:", check["name"])
    raise SystemExit(0 if result["ok"] else 1)
