"""Summarize recorded native maps; transport completion is not semantic success."""
import argparse
import hashlib
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def cells(layer):
    return [layer['names'][int(run.split(':')[0])]
            for run in layer['runs'].split(',') for _ in range(int(run.split(':')[1]))]


def evaluate(root):
    summary = {'scope': 'Three-seed native comparisons, explicitly separated from semantic/visual limits.'}
    for label, prefix in [('baseline', 'baseline-r2-s'), ('final', 'final-r2-s')]:
        paths = []; warnings = []; maps = 0
        for sample in range(3):
            result = read(root / f'{prefix}{sample}/suite-result.json')
            assert result['complete'] and result['fatal'] is None
            assert len(result['results']) == 20
            for case in result['results']:
                assert 'error' not in case, case
                if case.get('generated'):
                    maps += 1
                    assert case['undo'] and case['preservedSourceShapes'], case['id']
                if 'dryConnectionWidth8' in case:
                    paths.append({'sample': sample, 'id': case['id'],
                                  'connectedAtWidth8': case['dryConnectionWidth8'],
                                  'waypointsAtWidth8': case.get('waypointsReachedWidth8', True)})
                if case.get('issues'):
                    warnings.append({'sample': sample, 'id': case['id'], 'issues': case['issues']})
        summary[label] = {'maps': maps, 'paths': paths, 'passageCases': len(paths),
                          'passageConnectionsAndWaypointsPassed': sum(p['connectedAtWidth8'] and p['waypointsAtWidth8'] for p in paths),
                          'generationWarnings': warnings}
    assert summary['final']['passageConnectionsAndWaypointsPassed'] == 18
    before = read(root / 'controlled-r2-baseline/suite-result.json')
    after = read(root / 'controlled-r2-final/suite-result.json')
    assert before['complete'] and after['complete'] and before['tiles'] == after['tiles']
    comparisons = []
    for a, b in zip(before['results'], after['results']):
        assert a['id'] == b['id'] and a['cellHash'] == b['cellHash'] and b['undo'] and not b['issues']
        for side in ['baseline', 'final']:
            blob = root / f"controlled-r2-{side}/{a['id']}-cells.json"
            assert hashlib.sha256(blob.read_bytes()).hexdigest() == a['cellHash']
        comparisons.append(a['id'])
    assert len(comparisons) == 8
    summary['legacyCellsUnchanged'] = {'cases': comparisons, 'cellsPerMap': 62500,
        'layers': ['terrain', 'edifice', 'roofs'],
        'scope': 'Only this controlled fixture omits ambient Ancient*, ScatterRuinsSimple, ScatterShrines, ScatterRoadDebris, ScatterCaveDebris and MechanoidRemains. Authored structures and native terrain remain enabled. Full native runs are retained separately.'}
    previews = []
    for language in ['ko', 'en']:
        result = read(root / f'previews-{language}-final/preview-result.json')
        assert result['ok'] and len(result['results']) == 2
        for c in result['results']:
            assert c['mountainCells'] > 1000 and c['dryConnectionWidth8'] and c.get('waypointsReachedWidth8', True) and not c['issues'], c
        previews += [{'language': language, 'id': c['id']} for c in result['results']]
    summary['backgroundPreviews'] = previews
    coverage = read(root / 'coverage-final/result.json')
    assert coverage['ok'] and all(c.startswith('PASS:') for c in coverage['checks'])
    summary['legacyCoverageChecks'] = len(coverage['checks'])
    counts = [read(root / folder / 'result.json') for folder in ['provider-baseline', 'provider-focused-r1', 'provider-english']]
    assert all(r['transportAndEnvelopeOk'] for r in counts)
    summary['freshGeminiRequests'] = sum(r['requestsCompleted'] for r in counts)
    coast_before = read(root / 'coast-baseline-native/world-coast-cells.json')
    coast_after = read(root / 'coast-passage-native/world-coast-cells.json')
    ta, tb = cells(coast_before['terrain']), cells(coast_after['terrain'])
    ocean = [i for i, name in enumerate(ta) if 'Ocean' in name]
    assert ocean and all(ta[i] == tb[i] for i in ocean)
    blocked = read(root / 'coast-passage-native/suite-result.json')['results'][0]
    assert blocked['issues']
    summary['worldCoastProtection'] = {'oceanCellsUnchanged': len(ocean), 'obstructionReported': True}
    (root / 'evaluation.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return summary


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('evidence', type=Path)
    report = evaluate(parser.parse_args().evidence)
    print(json.dumps({k: v for k, v in report.items() if k not in ['baseline', 'final']}, ensure_ascii=True))
    print('Passage connections/waypoints:', report['baseline']['passageConnectionsAndWaypointsPassed'], '->', report['final']['passageConnectionsAndWaypointsPassed'], '/18')
