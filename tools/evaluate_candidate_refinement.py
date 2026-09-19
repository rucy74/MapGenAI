"""Check measured refinement receipts; retain the one unresolved native crash."""
from pathlib import Path
import copy
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/analysis/2026-09-19-candidate-refinement'
baseline = root.parent / '2026-09-19-candidate-previews'
expected = '83ce44a7f12991c930d47a593ddb62ed353fcad4895e749b31eb19d46419dffe'
read = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
runs = []
for name, language in [('native-repeat-ko', 'ko'), ('native-final-en', 'en'), ('native-repeat2-ko', 'ko')]:
    folder = root / name
    result, launch, cleanup = [read(folder / (n + '.json')) for n in ('result', 'launch', 'cleanup')]
    log = (folder / 'Player.log').read_text(encoding='utf-8-sig')
    assert result['ok'] and result['error'] is None
    assert len(result['checks']) == 76 and all(c.startswith('PASS ') for c in result['checks'])
    assert launch['sourceDllSha256'].lower() == expected
    assert cleanup['removed'] and not Path(launch['mod']).exists()
    assert not any(word in log for word in ('Crash!!!', 'TypeLoadException', 'ThreadAbortException'))
    assert log.count('System.InvalidOperationException: Intentional candidate preview failure') == 1
    comparisons = [c for c in result['checks'] if '62500' in c]
    assert len(comparisons) == 10
    old = read(baseline / ('native-' + language + '-final/result.json'))
    assert len(result['measurements']) == len(old['measurements']) == 3
    for now, previous in zip(result['measurements'], old['measurements']):
        assert now['id'] == previous['id'] and now['hashes'] == previous['hashes']
    for suffix in ('precise.png', 'natural.png', 'natural-ui.png'):
        assert (folder / ('refine-' + suffix)).is_file()
    natural, precise = [read(folder / ('refine-' + n + '-after.json')) for n in ('natural', 'precise')]
    adjusted = copy.deepcopy(natural)
    for a, b in zip(adjusted['state']['elevationShapes'], precise['state']['elevationShapes']):
        if a['type'] == 'passage':
            assert a['edge_roughness'] == 'medium' and b['edge_roughness'] == 'none'
            a['edge_roughness'] = b['edge_roughness']
    assert adjusted == precise
    assert natural == read(root / ('provider-' + language) / 'refine-natural-after.json')
    runs.append({'name': name, 'checks': 76, 'normalPreviewPixelComparisons': 10,
                 'unchangedBaselineImages': 9, 'cleanShutdown': True})

providers = []
for language in ('ko', 'en'):
    folder = root / ('provider-' + language)
    results = read(folder / 'results.json')
    assert len(results) == 3
    for result, option in zip(results, (3, 3, 1)):
        assert result['option'] == option and result['preservedOtherState']
        response = read(folder / (result['id'] + '-response.json'))
        assert response['action'] == 'revise' and response['option'] == option
        assert set(response['params']) == ({'fertility_offset'} if option == 1 else {'shape_ops'})
        if option == 3:
            assert len(response['params']['shape_ops']) == 1
            assert set(response['params']['shape_ops'][0]['changes']) == {'edge_roughness'}
    providers.extend(results)

maps = read(root / 'full-maps/suite-result.json')
assert maps['complete'] and maps['fatal'] is None and len(maps['results']) == 4
assert read(root / 'full-maps/launch.json')['sourceDllSha256'].lower() == expected
for result in maps['results']:
    assert result['generated'] and result['undo'] and not result['issues']
    audit = result['passageScopeAudit']
    assert audit['outsideChanges'] == audit['unclearedSelectedCells'] == 0
    assert all(p['selectedCells'] > 0 and p['selectedOpenGround'] == 0 for p in audit['passages'])
    assert all(r['dryEndpointConnection'] and r['blockedCutCells'] == 0 for r in result['compoundAudit']['routes'])

failed = root / 'native-final-ko'
assert not (failed / 'result.json').exists()
assert 'Crash!!!' in (failed / 'Player.log').read_text(encoding='utf-8-sig')
assert read(failed / 'launch.json')['sourceDllSha256'].lower() == expected
for launch_path in root.glob('*/launch.json'):
    launch = read(launch_path)
    assert read(launch_path.with_name('cleanup.json'))['removed'] and not Path(launch['mod']).exists()
assert 'CoreRegressionTests: 150 PASS / 0 FAIL' in (root / 'final-tests.log').read_text(encoding='utf-8-sig')
if (repo / 'dev/Assemblies/MapGenAI.dll').exists():
    assert hashlib.sha256((repo / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest() == expected
summary = {
    'ok': True, 'scope': 'Acceptance assertions passed; not a claim that every native run succeeded.',
    'dllSha256': expected, 'offlineTests': 150, 'pairedNativeChecks': 152, 'repeatNativeChecks': 76,
    'pairedPixelComparisons': 20, 'pairedUnchangedBaselineImages': 18,
    'newProviderCalls': 6, 'fableCalls': 0, 'fullMaps': 4, 'runs': runs,
    'knownFailures': [{'run': 'native-final-ko', 'kind': 'native Mono crash during rapid in-flight replacement',
                       'resolved': False, 'sameBuildKoreanRepeatsPassed': 2}],
    'limits': 'Fixed seed and isolated DLC/Map Preview profile; four controlled full maps omit ambient ruins. '
              'Six live provider calls are a sample, not a general natural-language guarantee. '
              'Native crash root cause is undetermined and not declared fixed.'}
(root / 'evaluation.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary))
