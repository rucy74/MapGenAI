"""Validate saved road runs without calling a model or starting RimWorld."""
from pathlib import Path
import copy
import hashlib
import json
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / 'docs/analysis/2026-09-19-local-roads'
DLL = '50caa50cf25b1f6fc820b30f78271f4e5943ab15b8db194df04845e425ecba2a'
FAILURES = {'blocked-direct', 'blocked-water', 'failed-road-keeps-ruins', 'batch-failure'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_audit(audit):
    immediate = audit['immediate']
    assert immediate['outsideFootprintChanges'] == 0
    assert immediate['elevationChanges'] == 0
    assert immediate['protectedFloorChanges'] == 0
    assert immediate['worldRoadsUnchanged'] and audit['worldRoadsStillUnchanged']
    for road in audit['roads']:
        assert road['paintedCells'] > 0 and road['blockedPathCells'] == 0
        assert road['connectedWithinFootprint']


assert sha(REPO / 'dev/Assemblies/MapGenAI.dll') == DLL
assert 'CoreRegressionTests: 173 PASS / 0 FAIL' in (ROOT / 'tests-final.log').read_text(encoding='utf-8-sig')
maps = []
matching_roads = 0
positive_controls = 0
for name, count in [('fixtures-final-r2', 14), ('provider-native-final-ko', 4), ('provider-native-final-en', 2)]:
    folder = ROOT / name
    assert read(folder / 'launch.json')['sourceDllSha256'].lower() == DLL
    full = read(folder / 'suite-result.json')
    preview = read(folder / 'preview-result.json')
    assert full['complete'] and not full['fatal']
    assert preview['ok'] and not preview['error']
    assert len(full['results']) == len(preview['results']) == count
    by_id = {item['id']: item for item in preview['results']}
    for item in full['results']:
        ident = item['id']
        peer = by_id[ident]
        assert item['generated'] and item['undo'] and item['preservedSourceShapes'], ident
        assert (folder / (ident + '-generated-preview.png')).is_file()
        assert (folder / (ident + '-background-preview.png')).is_file()
        # The native suite also rejects a changed Scribe/preset roundtrip.
        xml = ET.parse(folder / (ident + '-scribe.xml'))
        assert xml.getroot() is not None
        state = read(folder / (ident + '-after.json'))['state']
        saved_roads = [json.loads(p.text) for p in xml.findall('./fixture/state/localRoads/li/plan')]
        assert state.get('localRoads', []) == saved_roads, ident
        audit = item.get('roadAudit')
        if ident in ('remove-road', 'ko-remove'):
            assert not state.get('localRoads') and audit is None
            assert not item['issues'] and not peer['issues']
        else:
            check_audit(audit)
            check_audit(peer['roadAudit'])
            if ident in FAILURES:
                assert item['issues'] and peer['issues']
                assert not audit['roads'] and audit['immediate']['changedCells'] == 0
                assert not peer['roadAudit']['roads'] and peer['roadAudit']['immediate']['changedCells'] == 0
            else:
                assert not item['issues'] and not peer['issues'], ident
                assert audit['roads'] and audit['immediate']['changedCells'] > 0
                assert len(audit['roads']) == len(peer['roadAudit']['roads'])
                for road, preview_road in zip(audit['roads'], peer['roadAudit']['roads']):
                    assert road['kind'] == preview_road['kind']
                    assert road['pathHash'] == preview_road['pathHash']
                    if ident != 'existing-world-road':
                        assert road['terrainHash'] == preview_road['terrainHash'], ident
                        matching_roads += 1
            if ident == 'dirt-road':
                # Prove each zero-tolerance assertion rejects a corrupted receipt.
                for key in ('outsideFootprintChanges', 'elevationChanges', 'protectedFloorChanges'):
                    bad = copy.deepcopy(audit)
                    bad['immediate'][key] = 1
                    try:
                        check_audit(bad)
                    except AssertionError:
                        positive_controls += 1
                    else:
                        raise AssertionError('Detector failed: ' + key)
            if ident == 'existing-world-road':
                assert audit['immediate']['worldRoadLinks']
                assert audit['immediate']['protectedFloorCells'] > 0
                assert audit['roads'][0]['protectedCells'] > 0
        if ident in ('preserve-basin', 'failed-road-keeps-ruins'):
            assert len(item['placements']) == 2
            fill = item['compoundAudit']['fills'][0]
            assert abs(fill['painted'] / fill['eligible'] - .7) < .001
            assert item['compoundAudit']['routes'][0]['dryEndpointConnection']
        maps.append(dict(run=name, id=ident, expectedFailure=ident in FAILURES,
                         image=f'{name}/{ident}-generated-preview.png',
                         preview=f'{name}/{ident}-background-preview.png'))
assert positive_controls == 3 and matching_roads == 13

# Same old responses, seed, tiles, and generation with the new DLL but no roads.
old = REPO / 'docs/analysis/2026-09-19-contextual-recommendations'
baseline = ROOT / 'unchanged-baseline'
assert read(baseline / 'launch.json')['sourceDllSha256'].lower() == DLL
data = read(baseline / 'suite-result.json')
preview = read(baseline / 'preview-result.json')
assert data['complete'] and not data['fatal'] and preview['ok'] and not preview['error']
assert len(data['results']) == len(preview['results']) == 6
comparisons = []
for item in data['results']:
    ident = item['id']
    assert item['generated'] and item['undo'] and not item['issues']
    for suffix, previous in [('-generated-preview.png', 'accepted-maps-ko-1'),
                             ('-background-preview.png', 'previews-ko-1')]:
        filename = ident + suffix
        assert sha(baseline / filename) == sha(old / previous / filename), filename
        comparisons.append(filename)
result = dict(ok=True, dllSha256=DLL, offlineTests=173, newProviderCalls=6,
              roadFullMaps=20, roadBackgroundPreviews=20, expectedBlockedCases=4,
              matchedRoadFootprints=matching_roads, baselineImagesIdentical=len(comparisons),
              auditPositiveControls=positive_controls, maps=maps,
              limitations=['Native world-road overlaps can differ in preview.',
                           'Road surfaces only; no bridges, tunnels or roadside buildings.',
                           'One fixed test world and isolated mod profile.',
                           'Prior MG23 native crash remains unresolved.'])
(ROOT / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ('maps', 'limitations')}, ensure_ascii=False))
