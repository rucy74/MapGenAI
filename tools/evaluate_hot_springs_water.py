"""Check saved hot-spring regression runs without a model or game launch."""
from pathlib import Path
import copy
import hashlib
import json

REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / 'docs/analysis/2026-09-20-hot-springs-water'
DLL = '47fa300bd24e0211559d4acdef338b2eee7a8897506bd48f0e7301f83cdb3ffb'
OLD = '50caa50cf25b1f6fc820b30f78271f4e5943ab15b8db194df04845e425ecba2a'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(name, expected=DLL):
    folder = ROOT / name
    assert read(folder / 'launch.json')['sourceDllSha256'].lower() == expected
    data = read(folder / 'suite-result.json')
    assert data['complete'] and not data['fatal'], name
    return data


def check_water(audit):
    assert audit['hotSpringAfterStep'] > 0 and audit['hotSpringFinal'] > 0
    assert audit['waterChangesAfterStep'] == 0
    assert audit['worldLinksUnchanged'] and audit['worldLinksStillUnchanged']
    assert audit['mutatorOrderUnchanged']
    assert audit['riverComponentsBefore'] == audit['riverComponentsAfter']


assert sha(REPO / 'dev/Assemblies/MapGenAI.dll') == DLL
assert 'CoreRegressionTests: 174 PASS / 0 FAIL' in (ROOT / 'tests-r1.log').read_text(encoding='utf-8-sig')
baseline = run('baseline', OLD)
assert len(baseline['results']) == 8
assert baseline['results'][0]['generated']
assert all(not item['generated'] for item in baseline['results'][1:])
final = run('final-r2')
preview = read(ROOT / 'final-r2/preview-result.json')
assert preview['ok'] and not preview['error']
assert len(final['results']) == len(preview['results']) == 8
maps = []
restored = []
positive_controls = 0
for full, small in zip(final['results'], preview['results']):
    ident = full['id']
    assert ident == small['id']
    assert full['generated'] and full['undo'] and full['preservedSourceShapes']
    assert not full['issues'] and not small['issues'], ident
    state = read(ROOT / 'final-r2' / (ident + '-after.json'))['state']
    assert 'HotSprings' in state['mutators']
    for source, result in [('full', full), ('preview', small)]:
        audit = result['hotSpringAudit']
        check_water(audit)
        if ident != 'inland':
            assert audit['waterBefore'] > 0
        if ident.startswith(('river-', 'mouth-')):
            assert audit['riverBefore'] > 0 and audit['overwrittenBeforeRestore'] > 0
            restored.append(dict(id=ident, source=source, restoredCells=audit['overwrittenBeforeRestore']))
        if ident == 'river-0' and source == 'full':
            for key, value in [('waterChangesAfterStep', 1), ('worldLinksUnchanged', False), ('mutatorOrderUnchanged', False)]:
                bad = copy.deepcopy(audit)
                bad[key] = value
                try:
                    check_water(bad)
                except AssertionError:
                    positive_controls += 1
                else:
                    raise AssertionError('Detector accepted corrupted audit: ' + key)
    maps.append(dict(id=ident, image='final-r2/' + ident + '-generated-preview.png',
                     preview='final-r2/' + ident + '-background-preview.png',
                     springs=full['hotSpringAudit']['hotSpringFinal']))
assert positive_controls == 3 and len(restored) == 10

# Actual model replies, copied byte-for-byte into two accepted fixture positions.
provider = read(ROOT / 'provider/result.json')
assert provider['transportAndEnvelopeOk'] and provider['requestsCompleted'] == 2
for ident in ['coast-0', 'mouth-0']:
    response = read(ROOT / 'provider' / (ident + '-response.json'))
    assert response['action'] == 'generate' and response['params']['mutators'] == ['HotSprings']
    assert sha(ROOT / 'provider' / (ident + '-response.json')) == sha(ROOT / 'accepted-replies' / (ident + '-response.json'))

same = []
for current, previous in [
    ('unchanged-baseline', REPO / 'docs/analysis/2026-09-19-local-roads/unchanged-baseline'),
    ('roads-unchanged', REPO / 'docs/analysis/2026-09-19-local-roads/fixtures-final-r2')]:
    data = run(current)
    small = read(ROOT / current / 'preview-result.json')
    assert small['ok'] and not small['error']
    for item in data['results']:
        assert item['generated'] and item['undo']
        ident = item['id']
        for suffix in ['-generated-preview.png', '-background-preview.png']:
            filename = ident + suffix
            assert sha(ROOT / current / filename) == sha(previous / filename), (current, filename)
            same.append(current + '/' + filename)
    assert len(data['results']) == (6 if current == 'unchanged-baseline' else 14)
for current, previous, ident in [('final-r2', 'baseline', 'inland')]:
    filename = ident + '-generated-preview.png'
    assert sha(ROOT / current / filename) == sha(ROOT / previous / filename), filename
    same.append(filename)
assert len(same) == 41
natural = []
for folder, dll in [('natural-baseline-r2', OLD), ('natural-final-r2', DLL)]:
    data = run(folder, dll)
    item = data['results'][0]
    assert item['generated'] and not item['issues']
    assert not read(ROOT / folder / 'natural-after.json')['state']['mutators']
    check_water(item['hotSpringAudit'])
    natural.append(item['hotSpringAudit'])
assert natural[0]['terrainAfterStepHash'] == natural[1]['terrainAfterStepHash']
assert natural[0]['hotSpringFinal'] == natural[1]['hotSpringFinal'] == 695
old_natural_varies = sha(ROOT / 'natural-baseline/natural-generated-preview.png') != sha(ROOT / 'natural-baseline-r2/natural-generated-preview.png')
assert old_natural_varies, 'The same old-DLL control must demonstrate the final-map variation'
result = dict(ok=True, dllSha256=DLL, offlineTests=174, newProviderCalls=2,
              rejectedBefore=7, hotSpringFullMaps=8, hotSpringPreviews=8,
              unchangedImages=41, naturalPostTerrainIdentical=True, oldDllNaturalFinalMapVaries=old_natural_varies,
              restoredWater=restored, auditPositiveControls=3,
              maps=maps, limitations=['Fixed world and isolated profile, not the user entire mod set.',
                                     'Road bridges remain unsupported.',
                                     'Exceptional worker exit restoration was not separately fault-injected.'])
(ROOT / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ('maps', 'restoredWater', 'limitations')}, ensure_ascii=False))
