"""Judge the river-stability probe runs without launching the game.

fixed   = this build (features MapGen AI adds initialize from their own random stream)
control = the release candidate from before the fix (installed DEV package DLL, read only)
Each run replays saved showcase states and plain feature additions on tiles of the showcase world and records,
for Map Preview maps and real 250x250 maps: the river centre (TileMutatorWorker_River.riverCenter), the river
course (cells the river worker gives depth > 0), the river water cells (IsRiver) and the ocean cells.

"The river did not move" = same centre and same course. River water cells can still differ where an added
feature changes other water the river worker skips; that is reported, not judged.
Usage: python tools/evaluate_river_stability.py [fixed-run-folder] [control-run-folder]
"""
from pathlib import Path
import hashlib
import json
import math
import re
import sys

REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / 'docs/analysis/2026-09-27-river-stability'
CONTROL_DLL = '4908863336befac9c141ba35cb15bbfb47617ed87a9236cd21f7b3449abde019'
checks, facts = [], {}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def check(ok, label):
    checks.append(('PASS ' if ok else 'FAIL ') + label)


class Run:
    def __init__(self, name):
        self.name, self.folder = name, ROOT / name
        self.result = read(self.folder / 'result.json')
        self.launch = read(self.folder / 'launch.json')
        self.records = {r['id']: r for r in self.result['records']}
        self.log = (self.folder / 'Player.log').read_text(encoding='utf-8', errors='replace')

    def capture(self, rid, kind='preview'):
        record = self.records.get(rid)
        if record is None or 'applyError' in record or kind not in record or record[kind].get('missing'):
            return None
        data = read(self.folder / record[kind]['file'])
        assert data['tile'] == record['tile'], (self.name, rid, kind, data['tile'], record['tile'])
        return data

    def groups(self, letter):
        ids = {rid.split('-')[0] for rid in self.records if re.fullmatch(letter + r'\d*', rid.split('-')[0])}
        return sorted(ids)


def centre(c):
    return None if c is None else c.get('riverCentre')


def kept_course(a, b):
    return a is not None and b is not None and centre(a) == centre(b) and a.get('course') is not None and a.get('course') == b.get('course')


def axis(cap):
    """Principal axis of the river water cells: (degrees from the x axis, RMS distance from the fitted line)."""
    w = cap['width']
    pts = [(i % w, i // w) for i in cap['river']]
    n = len(pts)
    mx, mz = sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n
    sxx = sum((p[0] - mx) ** 2 for p in pts) / n
    szz = sum((p[1] - mz) ** 2 for p in pts) / n
    sxz = sum((p[0] - mx) * (p[1] - mz) for p in pts) / n
    theta = 0.5 * math.atan2(2 * sxz, sxx - szz)
    minor = (sxx + szz) / 2 - math.sqrt(((sxx - szz) / 2) ** 2 + sxz ** 2)
    return math.degrees(theta) % 180, math.sqrt(max(minor, 0.0))


def additions(run, groups, key):
    """Compare every variant of a tile with its base. Pure additions keep all original features; the rest are replacements."""
    pure, moved, water, replaced = 0, [], [], []
    for tid in groups:
        base, base_rec = run.capture(tid + '-base'), run.records.get(tid + '-base')
        for rid, rec in run.records.items():
            if not rid.startswith(tid + '-') or rid == tid + '-base':
                continue
            other = run.capture(rid)
            if base is None or other is None:
                continue
            if not set(base_rec['features']) <= set(rec['features']):
                replaced.append({'id': rid, 'features': [base_rec['features'], rec['features']], 'centre': [centre(base), centre(other)],
                                 'sameCourse': kept_course(base, other)})
                continue
            pure += 1
            if key == 'river':
                if not kept_course(base, other):
                    moved.append(rid)
                elif base['river'] != other['river']:
                    water.append([rid, len(base['river']), len(other['river'])])
            elif base['ocean'] != other['ocean']:
                moved.append(rid)
    return pure, moved, water, replaced


def judge(run, fixed):
    tag, r = run.name, run.result
    check(r.get('ok') is True and r.get('worldSeed') == 'mapgenai-showcase-20260927', f'{tag}: run completed on the showcase world')
    tiles = r['tiles']
    check(tiles['A']['tile'] == 281 and tiles['A']['features'] == ['River'] and tiles['A']['biome'] == 'TemperateForest',
          f'{tag}: tile 281 is the showcase river tile (TemperateForest, River only)')
    check(tiles['B']['tile'] == 83 and tiles['B']['features'] == ['Coast'], f'{tag}: tile 83 is the showcase coast tile')
    a07, a08, m07, m08 = run.capture('A-07'), run.capture('A-08'), run.capture('A-07', 'map'), run.capture('A-08', 'map')
    # Water samples must be non-empty, or equal water proves nothing (a frozen map once read 0 == 0).
    same_07_08 = (kept_course(a07, a08) and a07['river'] == a08['river'] and kept_course(m07, m08) and m07['river'] == m08['river']
                  and min(len(a07['river']), len(m07['river'])) > 0)
    dry, lake = r.get('A-dryFeature'), r.get('A-lakeFeature')
    d, dm, lk, isl = run.capture('A-07-dry'), run.capture('A-07-dry', 'map'), run.capture('A-07-lake'), run.capture('A-07-island')
    a05, a05s = run.capture('A-05'), run.capture('A-05-springs')
    rivers, coasts = run.groups('R'), ['B'] + run.groups('C')
    rp, rmoved, rwater, rreplaced = additions(run, rivers, 'river')
    cp, cmoved, _, creplaced = additions(run, coasts, 'ocean')
    facts[tag] = {
        'A-07 / A-08 centre (preview, map)': [centre(a07), centre(a08), centre(m07), centre(m08)],
        'A-07 / A-08 river water cells (preview, map)': [len(a07['river']), len(a08['river']), len(m07['river']), len(m08['river'])],
        'A-08 hot spring cells (preview, map)': [a08['hotSpringCells'], m08['hotSpringCells']],
        'A-07 map cells covered by temporary terrain (ice)': m07.get('coveredCells'),
        'dry feature / centre / course kept (preview, map) / water cells': [dry, centre(d), kept_course(a07, d), kept_course(m07, dm), len(d['river']) if d else None],
        'lake feature / centre / course kept / water cells': [lake, centre(lk), kept_course(a07, lk), len(lk['river']) if lk else None],
        'island: apply error / centre / course kept': [run.records.get('A-07-island', {}).get('applyError'), centre(isl), kept_course(a07, isl)],
        'A-05 / with springs centre': [centre(a05), centre(a05s)],
        'river tiles, pure additions, moved': [rivers, rp, rmoved], 'river water cells changed with course kept': rwater,
        'coast tiles, pure additions, moved': [coasts, cp, cmoved],
        'replacements (not pure additions)': rreplaced + creplaced,
        'isolated inits A-08 (preview, map)': [run.records['A-08'].get('isolatedPreview'), run.records['A-08'].get('isolatedMap')]}
    if fixed:
        check(r['routing'] == {'generateMap': True, 'mapPreview': True, 'postTerrain': True} and r.get('isolatedPostTerrainTotal', 0) > 0,
              f'{tag}: both native Init call sites and the post-terrain loop routed; isolated post-terrain steps {r.get("isolatedPostTerrainTotal")}')
        check(same_07_08, f'{tag}: 07 vs 08 (hot springs added): same centre, course and river water cells in Map Preview and real map')
        check(a08['hotSpringCells'] > 0 and m08['hotSpringCells'] > 0, f'{tag}: the added hot springs were generated (preview and map)')
        check(run.records['A-08'].get('isolatedPreview', 0) >= 1 and run.records['A-08'].get('isolatedMap', 0) >= 1,
              f'{tag}: the added feature took the isolated path in both generators')
        check(run.records['A-07'].get('isolatedPreview') == 0 and run.records['A-07'].get('isolatedMap') == 0,
              f'{tag}: a state without added features takes the unchanged path')
        check(dry is not None and kept_course(a07, d) and kept_course(m07, dm), f'{tag}: 07 + {dry} (dry feature added): same centre and course (preview and map)')
        check(lake is not None and kept_course(a07, lk), f'{tag}: 07 + {lake}: same centre and course (the added lake does not pull the river)')
        check(kept_course(a05, a05s) and a05['river'] == a05s['river'], f'{tag}: 05 (default river position) + hot springs: x and z of the centre, course and water kept')
        check(len(rivers) >= 6 and rp >= 12 and not rmoved, f'{tag}: {len(rivers)} more river tiles, {rp} pure additions: no river moved')
        check(len(coasts) >= 2 and cp >= 2 and not cmoved, f'{tag}: {len(coasts)} ocean shore tiles, {cp} pure additions: no ocean cell moved')
    else:
        check(r['routing'] == 'absent', f'{tag}: release candidate has no routing (control)')
        check(centre(a08) == [50, 140] and len(a08['river']) == 1553 and a08['hotSpringCells'] == 843,
              f'{tag} (positive control): reproduces showcase-01 step 08 (centre 50,140; 1553 river and 843 hot spring cells)')
        check(not same_07_08 and len(rmoved) > 0 and len(cmoved) > 0,
              f'{tag} (positive control): the same comparisons flag the moved river on 281, {len(rmoved)} river additions and {len(cmoved)} shore additions')
    # Existing river options (both builds).
    check(centre(a07) is not None and centre(a07)[0] == 50 and centre(m07)[0] == 50, f'{tag}: river_position left still pins x = 50')
    straight_angle, straight_rms = axis(a07)
    curvy_angle, curvy_rms = axis(a05)
    north = run.capture('A-07-north')
    north_angle, north_rms = axis(north)
    facts[tag].update({'07 axis deg / rms': [round(straight_angle, 1), round(straight_rms, 2)], '05 axis deg / rms': [round(curvy_angle, 1), round(curvy_rms, 2)],
                       'north axis deg / rms': [round(north_angle, 1), round(north_rms, 2)]})
    check(straight_rms <= 2.0 < curvy_rms, f'{tag}: straight_river still straight (rms {straight_rms:.2f} vs {curvy_rms:.2f} without it)')
    check(north['river'] != a07['river'] and abs(north_angle - 90) <= 20, f'{tag}: river_direction north still turns the river (axis {north_angle:.0f} deg)')
    # Gates.
    routing_noise = [line for line in run.log.splitlines() if 'Feature initialization' in line]
    check(not routing_noise, f'{tag}: no feature-init routing warning in Player.log')
    failures = [line for line in run.log.splitlines() if re.search(r'Exception|\[MapGenAI\].*(fail|실패|Error)', line)]
    facts[tag]['exception or MapGenAI failure lines'] = failures[:20]
    check(not failures, f'{tag}: no exception or MapGen AI failure line in Player.log')
    return isl


fixed, control = Run(sys.argv[1] if len(sys.argv) > 1 else 'fixed'), Run(sys.argv[2] if len(sys.argv) > 2 else 'control')
built = hashlib.sha256((REPO / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest()
check(fixed.launch['sourceDllSha256'].lower() == built, f'fixed run loaded the current build {built[:10]}')
check(control.launch['sourceDllSha256'].lower() == CONTROL_DLL, 'control run loaded the release candidate 4908863336')
fixed_island = judge(fixed, True)
control_island = judge(control, False)
# A river variant replacing the river (River -> RiverIsland) is not isolated: same centre as before the fix.
check(fixed_island is not None and centre(fixed_island) == centre(control_island) == centre(fixed.capture('A-07')),
      f'07 + RiverIsland (replaces River): centre {centre(fixed_island)} in both builds, as without the island')
# Unchanged behaviour across builds, compared tile by tile: each run may pick different sweep tiles, because the
# quick-test start settlement lands on a random tile and the picker skips tiles with a world object.
def paired(rid):
    return rid in fixed.records and rid in control.records and fixed.records[rid]['tile'] == control.records[rid]['tile']


stateless = [n for n in fixed.groups('N') if paired(n + '-none')]
same_plain = [n for n in stateless if fixed.capture(n + '-none') and control.capture(n + '-none')
              and fixed.capture(n + '-none')['terrainSha256'] == control.capture(n + '-none')['terrainSha256']]
check(len(stateless) >= 2 and len(same_plain) == len(stateless) and not any(fixed.result['tiles'][n + '-hasState'] for n in stateless),
      f'tiles without MapGen AI state: whole-map terrain identical across builds ({len(same_plain)}/{len(stateless)} same-tile pairs)')
candidates = ['A-07', 'A-05'] + [t + '-base' for t in fixed.groups('R')] + ['B-base'] + [t + '-base' for t in fixed.groups('C')]
original = [rid for rid in candidates if paired(rid)]


def same_across(rid, kind='preview'):
    a, b = fixed.capture(rid, kind), control.capture(rid, kind)
    return a is not None and b is not None and a['river'] == b['river'] and a['ocean'] == b['ocean'] and centre(a) == centre(b) and a.get('course') == b.get('course')


kept = [rid for rid in original if same_across(rid)]
maps = [rid for rid in ['A-07', 'B-base'] if same_across(rid, 'map')]
check(len(original) >= 9 and len(kept) == len(original) and len(maps) == 2,
      f'states without added features: centre, course, river and shore identical across builds ({len(kept)}/{len(original)} same-tile previews, {len(maps)}/2 maps)')
facts['cross-build'] = {'stateless same-tile pairs': stateless, 'identical': same_plain, 'original same-tile pairs': len(original), 'identical previews': len(kept),
                        'identical maps': maps, 'not paired (different tile chosen)': [rid for rid in candidates + [n + '-none' for n in fixed.groups('N')] if not paired(rid)]}
print(json.dumps(facts, ensure_ascii=False, indent=1))
print('\n'.join(checks))
print(f"{sum(c.startswith('PASS') for c in checks)} PASS / {sum(c.startswith('FAIL') for c in checks)} FAIL")
(ROOT / f'evaluation-{fixed.name}-vs-{control.name}.json').write_text(json.dumps({'fixed': fixed.name, 'control': control.name, 'checks': checks, 'facts': facts}, ensure_ascii=False, indent=1), encoding='utf-8')
sys.exit(0 if all(c.startswith('PASS') for c in checks) else 1)
