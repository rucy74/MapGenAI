"""Verify mountain-cut evidence and compare full maps without trusting model descriptions."""
from pathlib import Path
import argparse
import json


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def expand(layer):
    cells = []
    for run in layer['runs'].split(','):
        index, count = map(int, run.split(':'))
        cells.extend([layer['names'][index]] * count)
    assert len(cells) == 62500
    return cells


def evaluate(root):
    def suite(name, count):
        data = read(root / name / 'suite-result.json')
        assert data['complete'] and data['fatal'] is None, name
        assert len(data['results']) == count, name
        assert all(r.get('generated') and r.get('undo') and 'error' not in r for r in data['results']), name
        return {r['id']: r for r in data['results']}

    counts = {'final-ko': 7, 'final-ko-seed1': 7, 'final-en': 2}
    generated = 0
    for folder, count in counts.items():
        for id, result in suite(folder, count).items():
            audit = result['passageScopeAudit']
            assert audit['outsideChanges'] == audit['unclearedSelectedCells'] == 0, (folder, id)
            expected = 'full' if id.endswith(('full', 'restore')) else 'mountains'
            assert len(audit['passages']) == 1
            passage = audit['passages'][0]
            assert passage['scope'] == expected and passage['selectedCells'] > 0, (folder, id)
            assert (passage['selectedOpenGround'] == 0) == (expected == 'mountains'), (folder, id)
            assert result['dryConnectionWidth8'] and result.get('waypointsReachedWidth8', True), (folder, id)
            assert not result['issues'], (folder, id, result['issues'])
            before = read(root / folder / f'{id}-before.json')
            after = read(root / folder / f'{id}-after.json')
            # The user only requested a passage change: every non-passage shape must survive exactly.
            def terrain(state):
                return [s for s in state['state']['elevationShapes'] if s['type'] != 'passage']
            assert terrain(before) == terrain(after), (folder, id, 'source terrain')
            if id == 'ko-widen':
                assert [s['width'] for s in after['state']['elevationShapes'] if s['type'] == 'passage'] == [12]
            generated += 1

    ground = []
    suite('control', 7)
    for id in ['ko-cut', 'ko-short', 'ko-bent', 'ko-convert', 'ko-widen']:
        before = read(root / 'control' / f'{id}-cells.json')
        after = read(root / 'final-ko' / f'{id}-cells.json')
        a, b, edifices = map(expand, [before['terrain'], after['terrain'], before['edifice']])
        changes = [i for i in range(62500) if a[i] != b[i]]
        assert changes, id
        assert all(edifices[i] is not None for i in changes), (id, 'terrain changed on previously open ground')
        ground.append({'id': id, 'terrainChanges': len(changes), 'changedOriginallyOpenGround': 0})

    old, new = suite('legacy-old', 10), suite('legacy-new', 10)
    assert set(old) == set(new)
    for id in old:
        assert read(root / 'legacy-old' / f'{id}-cells.json') == read(root / 'legacy-new' / f'{id}-cells.json'), (id, 'legacy cells changed')

    diagnostics = suite('diagnostics', 3)
    overlap = diagnostics['overlap']['passageScopeAudit']['passages']
    assert overlap[0]['selectedCells'] == overlap[1]['selectedCells'] > 0
    mixed = diagnostics['mixed']['passageScopeAudit']['passages']
    assert mixed[0]['selectedCells'] > mixed[1]['selectedCells'] > 0 and mixed[1]['selectedOpenGround'] == 0
    empty = diagnostics['no-mountain']
    assert empty['passageScopeAudit']['passages'][0]['selectedCells'] == 0
    assert empty['passageScopeAudit']['outsideChanges'] == 0
    assert any('No mountain intersects' in s for s in empty['issues'])
    previews = 0
    for folder, count in [('final-ko', 3), ('final-en', 1)]:
        preview = read(root / folder / 'preview-result.json')
        assert preview['ok'] and len(preview['results']) == count
        for result in preview['results']:
            assert result['dryConnectionWidth8'] and result.get('waypointsReachedWidth8', True)
            assert not result['issues'] and result['passageScopeAudit']['outsideChanges'] == 0
            assert (root / folder / (result['id'] + '-background-preview.png')).is_file()
        previews += count
    for provider, count in [('provider-ko', 7), ('provider-en', 2)]:
        result = read(root / provider / 'result.json')
        assert result['transportAndEnvelopeOk'] and result['requestsCompleted'] == count
    for id, native in suite('native', 2).items():
        assert native['dryConnectionWidth8'] and native.get('waypointsReachedWidth8', True), id
        assert not native['issues'] and native['passageScopeAudit']['outsideChanges'] == 0, id
        assert native['passageScopeAudit']['passages'][0]['selectedOpenGround'] == 0, id
    result = dict(acceptedModelMaps=generated, freshModelRequests=9, scopeAndWidthChecksPassed=True,
                  originalGroundComparisons=ground, legacyMapsIdentical=10, legacyLayers=['terrain', 'edifice', 'roofs'],
                  controlledScope='Ambient Ancient*, ruins, shrines, road/cave debris and mechanoid remains excluded; authored structures included.',
                  overlappingAndEmptyRouteChecks=3, actualBackgroundPreviews=previews, fullNativeMaps=2)
    (root / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence', type=Path)
    evaluate(parser.parse_args().evidence)
