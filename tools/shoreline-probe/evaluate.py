"""Audit final native shoreline artifacts, including off/bypass and late-stage comparisons."""
import argparse
import collections
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root', type=Path, default=repo / 'docs/analysis/2026-09-26-shoreline-blending')
parser.add_argument('--runs', nargs='+')
parser.add_argument('--dll', type=Path, default=repo / 'dev/Assemblies/MapGenAI.dll')
parser.add_argument('--natural', help='Run name for paired effect comparison')
parser.add_argument('--bypass', help='Run name for paired effect comparison')
parser.add_argument('--off', help='Run name for paired off comparison')
parser.add_argument('--interactions', action='store_true', help='Require all five interaction cases and compare their common dry cells')
args = parser.parse_args()
root = args.root.resolve()
runs = args.runs or ['shore-temperate-natural-05', 'shore-desert-natural-01', 'shore-cold-natural-01',
        'shore-protected-natural-01', 'shore-hotspring-natural-01', 'shore-temperate-none-02',
        'shore-temperate-bypass-02']
checks, summaries, cases = [], [], {}

def read(folder, name):
    return json.loads((folder / name).read_text(encoding='utf-8-sig'))

def check(name, value):
    checks.append({'name': name, 'ok': bool(value)})

def expand(snapshot):
    return [value for count, value in snapshot['layers'] for _ in range(count)]

def common_dry_influence(old, reference_old, new, reference_new):
    common = {i for i, pair in enumerate(zip(old, reference_old)) if pair[0]==pair[1]
              and pair[0].split('|')[0] in ('Soil', 'Sand')}
    return common, sorted(i for i in common if new[i] != reference_new[i])


control = ['Soil|Soil|||', 'Soil|Soil|||']
water_control = ['WaterShallow|WaterShallow|||', control[1]]
mutated_control = [water_control[0], 'Mud|Mud|||']
check('Paired detector ignores only the actually changed water input',
      common_dry_influence(water_control, control, water_control, control)==({1}, []))
check('Paired detector catches one dry shore mutation beside changed water input',
      common_dry_influence(water_control, control, mutated_control, control)==({1}, [1]))


dll_hash = hashlib.sha256(args.dll.read_bytes()).hexdigest()
for run in runs:
    folder = root / ('native-' + run)
    result, launch = read(folder, 'result.json'), read(folder, 'launch.json')
    if not result['bypass'] and result['details']=='natural':
        cases[result['fixture']] = folder
    check(run + ': native checks and expected scheduling', result['ok'] and result['blendCalls'] == (0 if result['details']=='none' else 2)
          and result['blockedProviderFactoryCalls'] == 0 and all(c['ok'] for c in result['checks']))
    check(run + ': actual DLL', launch['sourceDllSha256'].lower() == dll_hash)
    palette = {}
    for phase in ('preview', 'full'):
        if result['details']=='none':
            check(run + '/' + phase + ': off still generated a complete native map', len(expand(read(folder, phase + '-phase-end-terrain.json'))) == 62500)
            continue
        delta = read(folder, phase + '-changes.json')
        before, after = read(folder, phase + '-before-blend.json'), read(folder, phase + '-after-blend.json')
        old, new = expand(before), expand(after)
        changed = {i for i, pair in enumerate(zip(old, new)) if pair[0] != pair[1]}
        measured = {r['z'] * 250 + r['x'] for r in delta['rows']}
        check(run + '/' + phase + ': complete lossless change receipt', len(old) == len(new) == 62500
              and changed == measured and delta['changed'] == len(measured))
        # Independent conservative bounds around this fixture's known central ellipse.
        # A whole-river/global-water expansion cannot pass by being near unrelated water.
        check(run + '/' + phase + ': local to opted-in pond; narrow mud accents', all(
            70 <= r['x'] <= 180 and 80 <= r['z'] <= 170
            and (r['to'] != 'Mud' or 0 <= r.get('connectedWaterDistance', r['waterDistance']) <= 2.4)
            and (0 <= r.get('sourceDistance', 0) <= 6) for r in delta['rows']))
        check(run + '/' + phase + ': unchanged elevation and caves',
              before['elevationHash'] == after['elevationHash'] and before['cavesHash'] == after['cavesHash'])
        palette[phase] = dict(collections.Counter(r['to'] for r in delta['rows']))
    summaries.append({'run': run, 'nativeChecks': len(result['checks']), 'changedTo': palette})

natural_run = args.natural or (None if args.runs else 'shore-temperate-natural-05')
bypass_run = args.bypass or (None if args.runs else 'shore-temperate-bypass-02')
off_run = args.off or (None if args.runs else 'shore-temperate-none-02')
if bool(natural_run) != bool(bypass_run) or off_run and not bypass_run:
    parser.error('--natural and --bypass are paired; --off also requires them')
for phase in ('preview', 'full') if natural_run else ():
    natural = root / ('native-' + natural_run)
    bypass = root / ('native-' + bypass_run)
    before = read(natural, phase + '-before-blend.json')
    control = read(bypass, phase + '-before-blend.json')
    check(phase + ': identical input for effect comparison', all(before[k] == control[k]
          for k in ('terrainHash', 'elevationHash', 'cavesHash')))
    delta = read(natural, phase + '-changes.json')
    measured = {r['z'] * 250 + r['x'] for r in delta['rows']}
    final = expand(read(natural, phase + '-phase-end-terrain.json'))
    unchanged = expand(read(bypass, phase + '-phase-end-terrain.json'))
    check(phase + ': late generators preserve precisely the measured surface changes',
          {i for i, pair in enumerate(zip(final, unchanged)) if pair[0] != pair[1]} == measured)
    if off_run:
        off = root / ('native-' + off_run)
        check(phase + ': explicit off equals bypass', read(off, phase + '-phase-end-terrain.json')['terrainHash']
              == read(bypass, phase + '-phase-end-terrain.json')['terrainHash'])

interaction_summary = []
if args.interactions:
    needed = {'nearby-reference', 'nearby-water', 'connected-water', 'explicit-water', 'special-water'}
    check('All five interaction controls were exercised', needed <= cases.keys())
    if needed <= cases.keys():
        reference = cases['nearby-reference']
        for case in sorted(needed - {'nearby-reference'}):
            folder = cases[case]
            for phase in ('preview', 'full'):
                prefix = case + '/' + phase
                control = read(folder, phase + '-injected-interaction.json')
                reference_control = read(reference, phase + '-injected-interaction.json')
                anchors = control.get('anchors', [{'anchor': control['anchor'], 'direction': control['direction']}])
                reference_anchors = reference_control.get('anchors', [{'anchor': reference_control['anchor'], 'direction': reference_control['direction']}])
                layout = control.get('interactionLayout', 'single')
                check(prefix + ': paired geometry-selection layout and all anchors match',
                      layout == reference_control.get('interactionLayout', 'single') and anchors == reference_anchors
                      and control.get('survey', []) == reference_control.get('survey', []))
                if layout == 'cardinal':
                    survey = control.get('survey', [])
                    directions = {tuple(s['direction']) for s in survey}
                    indices = [r['index'] for r in control['rows']]
                    check(prefix + ': all cardinal directions surveyed; at least three disjoint 49-cell strips',
                          directions == {(1, 0), (-1, 0), (0, 1), (0, -1)} and len(survey)==4
                          and sum(s['selected'] for s in survey)==len(anchors) and 3 <= len(anchors) <= 4
                          and len(indices)==len(set(indices))==49*len(anchors)
                          and all(s['attempted']>0 and (s['selected'] or s['rejections']) for s in survey))
                before = read(folder, phase + '-before-blend.json')
                reference_before = read(reference, phase + '-before-blend.json')
                old, ref_old = expand(before), expand(reference_before)
                new = expand(read(folder, phase + '-after-blend.json'))
                ref_new = expand(read(reference, phase + '-after-blend.json'))
                allowed = set(control['patchCells'])
                if case in ('connected-water', 'explicit-water'):
                    allowed.update(control['connectorCells'])
                input_changes = {i for i, pair in enumerate(zip(old, ref_old)) if pair[0] != pair[1]}
                check(prefix + ': identical native input outside precisely recorded water injection',
                      input_changes == allowed and before['elevationHash']==reference_before['elevationHash']
                      and before['cavesHash']==reference_before['cavesHash']
                      and control['anchor']==reference_control['anchor']
                      and control['direction']==reference_control['direction'])
                common_dry, influenced = common_dry_influence(old, ref_old, new, ref_new)
                check(prefix + ': measured common dry cells exist', len(common_dry)>0)
                check(prefix + (': connected water changes at least one common dry shore cell' if case=='connected-water'
                                else ': unrelated or explicit water cannot influence common dry shore cells'),
                      bool(influenced) if case=='connected-water' else not influenced)
                reach = read(folder, phase + '-interaction-water.json')
                patch, connected = set(reach['controlledWaterCells']), set(reach['connectedCells'])
                check(prefix + ': independently recorded connection polarity', bool(patch & connected)
                      if case=='connected-water' else not patch & connected)
                interaction_summary.append({'case': case, 'phase': phase, 'layout': layout, 'anchors': anchors, 'commonDryCells': len(common_dry),
                                            'influencedCells': influenced})

files = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
         for run in runs for p in sorted((root / ('native-' + run)).rglob('*')) if p.is_file()}
result = {'ok': all(c['ok'] for c in checks), 'checks': checks, 'dllSha256': dll_hash,
          'summaries': summaries, 'interactionSummary': interaction_summary, 'rawFileSha256': files,
          'limits': 'Only the listed controlled cases; injected interaction/protection fixtures are labeled. No model calls, user-mod matrix, plant identity or visual-quality guarantee.'}
(root / 'artifact-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'ok': result['ok'], 'checks': len(checks), 'rawFiles': len(files), 'summaries': summaries,
                  'failed': [c for c in checks if not c['ok']]}, ensure_ascii=False))
raise SystemExit(0 if result['ok'] else 1)
