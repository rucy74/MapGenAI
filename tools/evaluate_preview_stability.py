"""Compare captured native runs, including the expected pre-fix queue-loss failure."""
from pathlib import Path
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/analysis/2026-09-19-preview-stability'
read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
old = '53fa0d7f4574a555d654f433e7aaa0a5542d5b27a87265fabc5ba589ee8d04ae'
new = '4010d0decee7a5d485c944bbf1877421a48020a7800693e837cbd02de23018ef'
checks = []
for language in ('ko', 'en'):
    baseline = read(root / f'baseline-{language}/result.json')
    final = read(root / f'recovered-{language}-r2/result.json')
    for prefix, result, sha in [('baseline-', baseline, old), ('recovered-', final, new)]:
        name = prefix + language + ('-r2' if prefix == 'recovered-' else '')
        assert result['ok'] and result['error'] is None
        assert all(c.startswith('PASS ') for c in result['checks'])
        assert len(result['checks']) == (185 if name == 'recovered-ko-r2' else 176)
        assert read(root / name / 'launch.json')['sourceDllSha256'].lower() == sha
        assert sum('refined candidate matches all 62500' in c for c in result['checks']) == 6
        log = (root / name / 'Player.log').read_text(encoding='utf-8-sig')
        assert not any(word in log for word in ('Crash!!!', 'TypeLoadException', 'ThreadAbortException'))
        assert 'Intentional candidate preview failure' in log
        checks.append({'name': name, 'checks': len(result['checks']), 'refinementRounds': 6})
    assert len(baseline['measurements']) == len(final['measurements']) == 3
    for a, b in zip(baseline['measurements'], final['measurements']):
        assert a['id'] == b['id'] and len(a['hashes']) == 3 and a['hashes'] == b['hashes']

failure = read(root / 'queue-baseline-r2/result.json')
assert not failure['ok']
expected_failure = 'dropped request reaches a bounded error instead of waiting forever'
assert failure['checks'][-1] == 'FAIL ' + expected_failure
assert expected_failure in failure['error']
assert read(root / 'queue-baseline-r2/launch.json')['sourceDllSha256'].lower() == old
ko = read(root / 'recovered-ko-r2/result.json')
for label in (expected_failure,
              'late timed-out result cannot publish a stale texture',
              'replacement renders while other candidate textures stay unchanged',
              'queue recovery selection remains one-step undoable'):
    assert 'PASS ' + label in ko['checks']

for name in ('queue-baseline', 'recovered-ko', 'recovered-en'):
    assert read(root / name / 'interrupted.json')['reason'].startswith('Harness optional-assembly')
    assert not (root / name / 'result.json').exists()
for path in root.glob('*/launch.json'):
    assert read(path.with_name('cleanup.json'))['removed']
    assert not Path(read(path)['mod']).exists()
assert 'CoreRegressionTests: 150 PASS / 0 FAIL' in (root / 'tests.log').read_text(encoding='utf-8-sig')
assert hashlib.sha256((repo / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest() == new
prior = root.parent / '2026-09-19-candidate-refinement/native-final-ko'
assert 'Crash!!!' in (prior / 'Player.log').read_text(encoding='utf-8-sig')
assert not (prior / 'result.json').exists()
summary = dict(ok=True, scope='Queue-loss recovery and recorded recommendation regression; prior native crash remains unresolved.',
               dllSha256=new, baselineDllSha256=old, offlineTests=150, runs=checks,
               finalNativeChecks=361, baselineNativeChecks=352, unchangedBaselineImages=18,
               finalNormalPreviewPixelComparisons=30, newProviderCalls=0, newFullMaps=0,
               expectedBaselineFailure=expected_failure, interruptedHarnessRuns=3, unresolvedPriorNativeCrashes=1)
(root / 'evaluation.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))
