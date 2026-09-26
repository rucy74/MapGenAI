"""Audit final native shoreline artifacts, including off/bypass and late-stage comparisons."""
import collections
import hashlib
import json
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
root = repo / 'docs/analysis/2026-09-26-shoreline-blending'
runs = ['shore-temperate-natural-05', 'shore-desert-natural-01', 'shore-cold-natural-01',
        'shore-protected-natural-01', 'shore-hotspring-natural-01', 'shore-temperate-none-02',
        'shore-temperate-bypass-02']
checks, summaries = [], []

def read(folder, name):
    return json.loads((folder / name).read_text(encoding='utf-8-sig'))

def check(name, value):
    checks.append({'name': name, 'ok': bool(value)})

def expand(snapshot):
    return [value for count, value in snapshot['layers'] for _ in range(count)]

dll_hash = hashlib.sha256((repo / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest()
for run in runs:
    folder = root / ('native-' + run)
    result, launch = read(folder, 'result.json'), read(folder, 'launch.json')
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
            and (r['to'] != 'Mud' or r['waterDistance'] <= 2.4) for r in delta['rows']))
        check(run + '/' + phase + ': unchanged elevation and caves',
              before['elevationHash'] == after['elevationHash'] and before['cavesHash'] == after['cavesHash'])
        palette[phase] = dict(collections.Counter(r['to'] for r in delta['rows']))
    summaries.append({'run': run, 'nativeChecks': len(result['checks']), 'changedTo': palette})

natural = root / 'native-shore-temperate-natural-05'
bypass = root / 'native-shore-temperate-bypass-02'
off = root / 'native-shore-temperate-none-02'
for phase in ('preview', 'full'):
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
    check(phase + ': explicit off equals bypass', read(off, phase + '-phase-end-terrain.json')['terrainHash']
          == read(bypass, phase + '-phase-end-terrain.json')['terrainHash'])

files = {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
         for run in runs for p in sorted((root / ('native-' + run)).rglob('*')) if p.is_file()}
result = {'ok': all(c['ok'] for c in checks), 'checks': checks, 'dllSha256': dll_hash,
          'summaries': summaries, 'rawFileSha256': files,
          'limits': 'Seven controlled cases. No new model calls, user-mod matrix, plant identity or visual-quality guarantee.'}
(root / 'artifact-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'ok': result['ok'], 'checks': len(checks), 'rawFiles': len(files), 'summaries': summaries,
                  'failed': [c for c in checks if not c['ok']]}, ensure_ascii=False))
raise SystemExit(0 if result['ok'] else 1)
