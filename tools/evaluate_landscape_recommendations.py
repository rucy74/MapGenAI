"""Export real stored options for full-map replay; independently check recorded outcomes."""
import argparse
import copy
import json
from pathlib import Path


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def export(root):
    for language, ids in [('ko', ['plain', 'coast', 'existing']), ('en', ['plain', 'existing'])]:
        output = root / ('maps-input-' + language)
        output.mkdir(exist_ok=True)
        cases = []
        for short in ids:
            key = 'recommend-' + short
            source = root / ('provider-final-' + language)
            reply = read(source / (key + '-response.json'))
            for index, option in enumerate(reply['options'], 1):
                key_out = short + '-' + str(index)
                write(output / (key_out + '-response.json'), {'action': 'generate', 'params': option['params']})
                write(output / (key_out + '-before.json'), read(source / (key + '-before.json')))
                cases.append({'id': key_out, 'kind': 'recommendation', 'tile': 'coast' if short == 'coast' else 'inland',
                              'beforeFile': key_out + '-before.json', 'request': 'Replay stored option ' + key_out,
                              'omitAmbientRuins': True})
        write(output / 'cases.json', {'cases': cases})
        # Each alternative belongs to the SAME selected tile. A separate game run
        # for each option keeps the fixture tile order identical to prompt capture.
        for index in range(1, 4):
            write(output / ('cases-' + str(index) + '.json'), {'cases': [c for c in cases if c['id'].endswith('-' + str(index))]})


def preserve(before, after):
    """Removing ONLY additions must reproduce the authored baseline, including all scalars."""
    reduced = copy.deepcopy(after)
    for key in ['elevationShapes', 'structures']:
        ids = {item['id'] for item in before[key]}
        reduced[key] = [item for item in after[key] if item['id'] in ids]
    reduced['mutators'] = [item for item in after['mutators'] if item in before['mutators']]
    return before == reduced


def evaluate(root):
    results = []
    def check(ok, message):
        results.append({'pass': bool(ok), 'check': message})
    for language in ['ko', 'en']:
        native = root / ('native-final-' + language)
        suite = read(native / 'suite-result.json')
        check(suite['complete'] and not suite['fatal'], language + ': native run complete')
        expected = {'recommend-plain': 3, 'recommend-existing': 3}
        if language == 'ko':
            expected.update({'recommend-coast': 3, 'recommend-scoped': 3, 'recommend-two': 2})
        cases = {case['id']: case for case in suite['results']}
        for key, count in expected.items():
            case = cases[key]
            check(case.get('options') == count and case.get('unchangedBeforeSelection'), language + '/' + key + ': expected choices, no immediate edit')
            check(len(case.get('selections', [])) == count and all(s['appliedStoredCommand'] and s['undo'] for s in case.get('selections', [])), language + '/' + key + ': every selection and Undo')
            source = root / ('provider-final-' + language)
            before = read(source / (key + '-before.json'))['state']
            states = [read(native / (key + '-option-' + str(i) + '-after.json'))['state'] for i in range(1, count + 1)]
            check(len({json.dumps(state, sort_keys=True) for state in states}) == count, language + '/' + key + ': distinct states')
            if key == 'recommend-existing':
                check(all(preserve(before, state) for state in states), language + ': existing geometry, floor, exit, 70% soil, two ruins and scalars preserved')
                mutated = copy.deepcopy(states[0]); mutated['elevationShapes'][0]['compositeOps'][0]['e'] = .09
                check(not preserve(before, mutated), language + ': preservation detector rejects an unintended floor change')
            if key == 'recommend-scoped':
                check(all(all(shape in state['elevationShapes'] for shape in before['elevationShapes']) for state in states), 'scoped soil options retain the left mountain')
        if language == 'ko':
            direct = cases['direct-edit']
            check(direct.get('generated') and not direct.get('issues') and direct['waterCells'] > 100 and direct['mountainCells'] > 1000, 'direct request still generates mountain and lake without choices')
        runs = [read(root / ('maps-same-tile-final-' + language + '-' + str(i)) / 'suite-result.json') for i in range(1, 4)]
        check(all(run['complete'] and not run['fatal'] for run in runs), language + ': full-map replay completed')
        map_results = [c for run in runs for c in run['results']]
        check(len(map_results) == (9 if language == 'ko' else 6), language + ': all selected terrain options generated')
        source_tiles = {t['id']: t['tile'] for t in suite['tiles']}
        check(all(t['tile'] == source_tiles['recommend-' + t['id'].rsplit('-', 1)[0]] for run in runs for t in run['tiles']), language + ': each option generated on its original prompt tile')
        for case in map_results:
            key = language + '/' + case['id']
            check(case.get('generated') and case.get('undo') and not case.get('issues'), key + ': generated with no authoring issues and reversible state')
            if case['id'].startswith('existing-'):
                fills = case['compoundAudit']['fills']
                check(len(fills) == 1 and abs(fills[0]['painted'] - fills[0]['eligible'] * .7) <= .501, key + ': actual 70% usable interior soil')
                check(sum(p['rect'] is not None for p in case['placements']) == 2, key + ': two original ruins placed')
            for route in case['compoundAudit']['routes']:
                check(route['cutCells'] > 0 and route['blockedCutCells'] == 0, key + ': mountain passage actually cuts dry cells')
    result = {'ok': all(r['pass'] for r in results), 'checks': results,
              'limits': 'First responses on fixed tiles; state preservation is not pixel equality. Rendering diversity and naturalness also require visual review. Whole-route edge connectivity is reported separately.'}
    write(root / 'evaluation.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('root', type=Path)
    parser.add_argument('--export', action='store_true')
    args = parser.parse_args()
    if args.export:
        export(args.root)
    else:
        raise SystemExit(evaluate(args.root))
