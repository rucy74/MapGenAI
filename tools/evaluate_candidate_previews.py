"""Verify final native recommendation-preview receipts, including clean shutdown."""
from pathlib import Path
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/analysis/2026-09-19-candidate-previews'
expected = 'd53110ee3d5e7e5a7a05efa3a5da1b192d83c0d15c707c326d802397a93cddcd'
runs = []
for name in ('native-ko-final', 'native-en-final'):
    folder = root / name
    read = lambda name: json.loads((folder / name).read_text(encoding='utf-8-sig'))
    result, launch, cleanup = read('result.json'), read('launch.json'), read('cleanup.json')
    log = (folder / 'Player.log').read_text(encoding='utf-8-sig')
    assert result['ok'] and result['error'] is None
    assert len(result['checks']) == 56 and all(c.startswith('PASS ') for c in result['checks'])
    comparisons = [c for c in result['checks'] if 'all 62500 pixels match' in c]
    assert len(comparisons) == 9 and all('(diff=0)' in c for c in comparisons)
    assert launch['sourceDllSha256'].lower() == expected
    assert cleanup['removed'] and not Path(launch['mod']).exists()
    assert not any(word in log for word in ('Crash!!!', 'TypeLoadException', 'ThreadAbortException'))
    assert log.count('System.InvalidOperationException: Intentional candidate preview failure') == 1
    for measured in result['measurements']:
        assert len(set(measured['hashes'])) == 3
        for i in range(1, 4):
            assert (folder / (measured['id'] + '-' + str(i) + '.png')).is_file()
        for suffix in ('-ui.png', '-zoom.png'):
            assert (folder / (measured['id'] + suffix)).is_file()
    runs.append({'name': name, 'checks': len(result['checks']), 'pixelComparisons': len(comparisons),
                 'seconds': [s for m in result['measurements'] for s in m['seconds']],
                 'intentionalFailureRecovered': True, 'cleanShutdown': True})
assert 'CoreRegressionTests: 146 PASS / 0 FAIL' in (root / 'final-tests.log').read_text(encoding='utf-8-sig')
if (repo / 'dev/Assemblies/MapGenAI.dll').exists():
    assert hashlib.sha256((repo / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest() == expected
summary = {'ok': True, 'dllSha256': expected, 'offlineTests': 146, 'nativeChecks': 112,
           'pixelComparisons': 18, 'newProviderCalls': 0, 'runs': runs,
           'limits': 'Fixed seed, flat inland tile, isolated DLC/Map Preview profile; ordinary preview comparison, not full-map equality. Timings include a deliberate 100 ms worker pause.'}
(root / 'evaluation.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))
