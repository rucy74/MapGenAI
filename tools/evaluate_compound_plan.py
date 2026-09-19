"""Evaluate recorded edits and independent full-map measurements; no provider calls."""
from pathlib import Path
import argparse
import copy
import json
import math


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def normalized(value):
    if isinstance(value, float):
        return round(value, 5)
    if isinstance(value, dict):
        return {k: normalized(v) for k, v in value.items()}
    if isinstance(value, list):
        return [normalized(v) for v in value]
    return value


def edit_contract(before, after, lang, turn):
    shapes = after['elevationShapes']
    passage = next(s for s in shapes if s['type'] == 'passage')
    fill = next(s for s in shapes if s['type'] == 'region_fill')
    count = (2 if turn < 4 else 3) if lang == 'ko' else 2
    if lang == 'ko' and turn == 7:
        count = 0
    assert sum(p['count'] for p in after['structures']) == count
    assert passage['width'] == (8 if turn == 1 else 12)
    assert passage['scope'] == 'mountains'
    assert float(fill['coverage']) == (.7 if turn <= (2 if lang == 'ko' else 1) else .5)
    assert fill['region_part'] == 'enclosed'
    for p in after['structures']:
        assert p['region'] == fill['region'] and p['region_part'] == 'enclosed'
        assert (p['width'], p['height']) == (9, 7)
    ring = next(s for s in shapes if s['id'] == fill['region'])
    ground = next(s for s in shapes if s['type'] == 'composite' and s['id'] != ring['id'])
    assert shapes.index(ground) < shapes.index(ring)
    assert any(o['fill'] == 'Soil' and abs(o['e'] - .05) < .00001 for o in ground['compositeOps'])
    if turn == 1:
        clean = copy.deepcopy(after)
        clean['elevationShapes'] = before['elevationShapes']
        clean['structures'] = before['structures']
        assert normalized(clean) == normalized(before), 'Unrequested globals changed'
        return
    clean = copy.deepcopy(after)
    old = {s['id']: s for s in before['elevationShapes']}
    if turn == 2:
        next(s for s in clean['elevationShapes'] if s['type'] == 'passage')['width'] = 8
        if lang == 'en':
            next(s for s in clean['elevationShapes'] if s['type'] == 'region_fill')['coverage'] = '0.7'
    elif lang == 'ko' and turn == 3:
        next(s for s in clean['elevationShapes'] if s['type'] == 'region_fill')['coverage'] = '0.7'
    elif lang == 'ko' and turn == 4:
        clean['structures'][0]['count'] = 2
    elif (lang == 'ko' and turn == 5) or (lang == 'en' and turn == 3):
        for s in clean['elevationShapes']:
            previous = old[s['id']]
            if s['type'] == 'composite':
                for prim, prior in zip(s['compositeShapes'], previous['compositeShapes']):
                    assert abs(prim['center'][0] - prior['center'][0] - .1) < .00001
                    assert prim['center'][1] == prior['center'][1]
                    prim['center'] = prior['center']
                s['position'] = previous['position']
            elif s['type'] == 'passage':
                for point, prior in zip(s['points'], previous['points']):
                    assert abs(point[0] - prior[0] - .1) < .00001 and point[1] == prior[1]
                s['points'] = previous['points']
    elif lang == 'ko' and turn == 6:
        assert normalized(passage['points']) == [[.6, .5], [1, .5]]
        clean['elevationShapes'] = [s for s in clean['elevationShapes'] if s['type'] != 'passage']
        clean['elevationShapes'].append(next(s for s in before['elevationShapes'] if s['type'] == 'passage'))
    elif lang == 'ko' and turn == 7:
        clean['structures'] = before['structures']
    assert normalized(clean) == normalized(before), (lang, turn, 'Unrequested state changed')


def map_contract(result, state, require_cut=False, preview=False):
    assert not result['issues'], result['issues']
    audit = result['passageScopeAudit']
    assert audit['outsideChanges'] == audit['unclearedSelectedCells'] == 0
    assert all(p['selectedCells'] > 0 and p['selectedOpenGround'] == 0 for p in audit['passages'])
    contract = result['compoundAudit']
    for measured in contract['fills']:
        fill = next(s for s in state['elevationShapes'] if s['id'] == measured['id'])
        assert measured['eligible'] > 1000
        assert measured['painted'] == math.floor(measured['eligible'] * float(fill['coverage']) + .5)
        assert measured['waterInInterior'] == 0
    for p in contract['structures']:
        assert p['outsideRegionCells'] == p['routeOverlapCells'] == 0
    for route in contract['routes']:
        if require_cut:
            assert route['cutCells'] > 0 and route['blockedCutCells'] == 0
    if not preview:
        assert result['generated'] and result['undo'] and 'error' not in result
        assert len(result['placements']) == sum(p['count'] for p in state['structures'])
        assert all(p['walls'] == p['spawnedWalls'] > 0 for p in result['placements'])


def evaluate(root):
    def suite(folder, count):
        data = read(root / folder / 'suite-result.json')
        assert data['complete'] and data['fatal'] is None and len(data['results']) == count, folder
        return data['results']
    edit_count = 0
    for lang, count in [('ko', 7), ('en', 3)]:
        folder = root / f'final-provider-{lang}'
        metadata = read(folder / 'sequence-result.json')
        assert metadata['ok'] and len(metadata['results']) == count
        for turn in range(1, count + 1):
            id = ('compound' if lang == 'ko' else 'english') + f'-{turn:02}'
            before = read(folder / f'{id}-before.json')['state']
            after = read(folder / f'{id}-after.json')['state']
            edit_contract(before, after, lang, turn)
            edit_count += 1
    observations = []
    for folder, count in [('final-ko', 7), ('final-ko-seed1', 7), ('final-en', 3), ('native', 2)]:
        for result in suite(folder, count):
            state = read(root / folder / (result['id'] + '-after.json'))['state']
            map_contract(result, state, require_cut=folder in ('final-ko-seed1', 'native'))
            observations.append({'folder': folder, 'id': result['id'], 'fills': result['compoundAudit']['fills'], 'routes': result['compoundAudit']['routes']})
    previews = 0
    for folder, count in [('final-ko', 2), ('final-en', 1), ('final-ko-seed1', 2)]:
        data = read(root / folder / 'preview-result.json')
        assert data['ok'] and len(data['results']) == count
        for result in data['results']:
            state = read(root / folder / (result['id'] + '-after.json'))['state']
            map_contract(result, state, require_cut=folder == 'final-ko-seed1', preview=True)
            previews += 1
    old, new = suite('legacy-old', 10), suite('legacy-new', 10)
    assert [r['id'] for r in old] == [r['id'] for r in new]
    for a, b in zip(old, new):
        assert a['generated'] and b['generated'] and not a['issues'] and not b['issues']
        assert read(root / 'legacy-old' / (a['id'] + '-cells.json')) == read(root / 'legacy-new' / (b['id'] + '-cells.json'))
    prior = suite('route-old', 1)[0]['compoundAudit']
    corrected = suite('route-new', 1)[0]['compoundAudit']
    assert prior['structures'][0]['routeOverlapCells'] == 72 and not prior['routes'][0]['dryEndpointConnection']
    assert corrected['structures'][0]['routeOverlapCells'] == 0 and corrected['routes'][0]['dryEndpointConnection']
    assert 'Structure placement failed' in suite('baseline-native', 1)[0]['issues'][0]
    # Exercise the detectors with positive failures, not just all-zero success records.
    example = suite('final-ko', 7)[0]
    state = read(root / 'final-ko/compound-01-after.json')['state']
    detected = []
    for name in ['wrong-percentage', 'ruin-in-passage']:
        broken = copy.deepcopy(example)
        if name == 'wrong-percentage':
            broken['compoundAudit']['fills'][0]['painted'] += 100
        else:
            broken['compoundAudit']['structures'][0]['routeOverlapCells'] = 1
        try:
            map_contract(broken, state)
        except AssertionError:
            detected.append(name)
    assert len(detected) == 2
    return {'ok': True, 'sequentialEdits': edit_count, 'finalMaps': len(observations), 'actualPreviews': previews, 'legacyIdenticalMaps': 10, 'detectorPositiveControls': detected, 'observations': observations,
            'limits': 'Mountain-only scope preserves open ground, so full-width connectivity to the map edge is observational, not guaranteed. Natural mountain slopes remain around the usable interior. Final edits are replayed on separate fixed tiles, with seed variation; exact unrelated settings preservation is tested in the sequential states.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    args = parser.parse_args()
    result = evaluate(args.root)
    (args.root / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'observations'}, ensure_ascii=False))
