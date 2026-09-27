"""Objective measurements for the basic checks, from the Map Preview cell dumps written by ShowcaseProbe
(NN-name-preview-cells.txt: header JSON, then one row per z, north first; '#' = solid rock colour, other
symbols = terrain defName from the preview map). Usage: python measure-previews.py <run-folder>"""
import json, math, os, sys
from collections import deque

def load(folder, name):
    path = os.path.join(folder, name + "-cells.txt")
    if not os.path.exists(path):
        return None
    lines = open(path, encoding="utf-8").read().split("\n")
    head = json.loads(lines[0])
    rows = lines[1:1 + head["height"]]
    legend = head["legend"]
    w, h = head["width"], head["height"]
    # grid[z][x] with z = 0 at the south edge
    grid = [[legend.get(rows[h - 1 - z][x], "?") for x in range(w)] for z in range(h)]
    return grid, w, h

def cells(grid, test):
    return [(x, z) for z, row in enumerate(grid) for x, v in enumerate(row) if test(v)]

def centroid(pts, w, h):
    if not pts:
        return None
    return (round(sum(p[0] for p in pts) / len(pts) / (w - 1), 3), round(sum(p[1] for p in pts) / len(pts) / (h - 1), 3))

def is_rock(v): return v.startswith("solid rock")
def is_river(v): return "Moving" in v            # WaterMovingShallow / WaterMovingChestDeep
def is_ocean(v): return "Ocean" in v
def is_still_water(v): return v in ("WaterDeep", "WaterShallow", "Marsh")

def ring(grid, w, h):
    """Rays from the map centre every 2 degrees (0 = east, 90 = north, 270 = south). A ray is 'clear' when it
    reaches the map edge without crossing solid rock; a ring with one opening leaves one run of clear angles."""
    cx, cz = (w - 1) / 2, (h - 1) / 2
    clear = []
    for deg in range(0, 360, 2):
        a = math.radians(deg)
        hit = False
        for step in range(int(0.06 * w), w):
            x, z = int(round(cx + step * math.cos(a))), int(round(cz + step * math.sin(a)))
            if not (0 <= x < w and 0 <= z < h):
                break
            if is_rock(grid[z][x]):
                hit = True
                break
        if not hit:
            clear.append(deg)
    runs = []
    for deg in clear:
        if runs and deg - runs[-1][1] == 2:
            runs[-1][1] = deg
        else:
            runs.append([deg, deg])
    if len(runs) > 1 and runs[0][0] == 0 and runs[-1][1] == 358:
        runs[0][0] = runs[-1][0] - 360
        runs.pop()
    centre = [grid[z][x] for z in range(h) for x in range(w) if math.hypot(x - cx, z - cz) <= 0.08 * w]
    rock = sum(is_rock(v) for row in grid for v in row)
    return {"clearRayRunsDeg": runs, "clearRays": len(clear), "rockCells": rock,
            "centreStillWaterFraction": round(sum(is_still_water(v) for v in centre) / len(centre), 3)}

def islands(grid, w, h):
    """Land (non-water, non-river) components that touch only still water, not the map edge."""
    seen = [[False] * w for _ in range(h)]
    found = []
    for z in range(h):
        for x in range(w):
            v = grid[z][x]
            if seen[z][x] or is_still_water(v) or is_river(v):
                continue
            comp, edge, border = [], False, set()
            q = deque([(x, z)]); seen[z][x] = True
            while q:
                a, b = q.popleft(); comp.append((a, b))
                if a in (0, w - 1) or b in (0, h - 1): edge = True
                for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    na, nb = a + da, b + db
                    if 0 <= na < w and 0 <= nb < h:
                        nv = grid[nb][na]
                        if is_still_water(nv) or is_river(nv):
                            border.add(nv)
                        elif not seen[nb][na]:
                            seen[nb][na] = True; q.append((na, nb))
            if not edge and border and all(is_still_water(b) for b in border) and len(comp) >= 4:
                found.append({"cells": len(comp), "centroid": centroid(comp, w, h)})
    return sorted(found, key=lambda i: -i["cells"])[:5]

def line_fit(pts):
    n = len(pts); mx = sum(p[0] for p in pts) / n; mz = sum(p[1] for p in pts) / n
    sxx = sum((p[0] - mx) ** 2 for p in pts) / n; szz = sum((p[1] - mz) ** 2 for p in pts) / n
    sxz = sum((p[0] - mx) * (p[1] - mz) for p in pts) / n
    tr, det = sxx + szz, sxx * szz - sxz * sxz
    small = tr / 2 - math.sqrt(max(tr * tr / 4 - det, 0))
    big = tr / 2 + math.sqrt(max(tr * tr / 4 - det, 0))
    angle = math.degrees(0.5 * math.atan2(2 * sxz, sxx - szz))
    return {"perpendicularRms": round(math.sqrt(max(small, 0)), 2), "lengthSd": round(math.sqrt(big), 1), "axisAngleDeg": round(angle, 1)}

def measure(folder, name):
    loaded = load(folder, name)
    if not loaded:
        return None
    grid, w, h = loaded
    river = cells(grid, is_river); ocean = cells(grid, is_ocean); water = cells(grid, is_still_water)
    counts = {}
    for row in grid:
        for v in row:
            counts[v] = counts.get(v, 0) + 1
    out = {"size": [w, h], "counts": dict(sorted(counts.items(), key=lambda kv: -kv[1])),
           "riverCells": len(river), "riverCentroid": centroid(river, w, h),
           "oceanCells": len(ocean), "oceanCentroid": centroid(ocean, w, h),
           "stillWaterCells": len(water), "stillWaterCentroid": centroid(water, w, h),
           "hotSpringCells": counts.get("HotSpring", 0), "ring": ring(grid, w, h), "islands": islands(grid, w, h)}
    if len(river) >= 30:
        out["riverLine"] = line_fit(river)
    return out

if __name__ == "__main__":
    folder = sys.argv[1]
    names = sorted(f[:-len("-cells.txt")] for f in os.listdir(folder) if f.endswith("-cells.txt"))
    result = {n: measure(folder, n) for n in names}
    json.dump(result, open(os.path.join(folder, "preview-measurements.json"), "w", encoding="utf-8"), indent=1)
    for n, m in result.items():
        print(n, "river", m["riverCells"], m["riverCentroid"], m.get("riverLine"), "ocean", m["oceanCentroid"], "hot", m["hotSpringCells"],
              "rock", m["ring"]["rockCells"], "clearRuns", m["ring"]["clearRayRunsDeg"][:6], "ctrWater", m["ring"]["centreStillWaterFraction"], "islands", m["islands"][:2])
