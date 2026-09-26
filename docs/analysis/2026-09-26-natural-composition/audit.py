"""Read recorded real replies/states with independent dictionary-level invariants."""
import copy
import json
from pathlib import Path

root = Path(__file__).resolve().parent


def read(path):
    return json.loads((root / path).read_text(encoding='utf-8-sig'))


def state(path):
    return read(path)['state']


def toggle_only(before, after, details):
    expected = copy.deepcopy(before)
    found = False
    for shape in expected['elevationShapes']:
        if shape['type'] == 'landform':
            shape['details'] = details
            found = True
    return found and expected == after


def unrequested_soil(plan):
    for shape in plan['elevationShapes']:
        if shape['type'] == 'passage':
            continue
        fills = [shape.get('fill')] + [op.get('fill') for op in (shape.get('compositeOps') or [])]
        if any(value and (value.lower().startswith('soil') or value == 'rich_soil') for value in fills):
            return True
    return False


def run():
    checks = []

    def check(name, ok):
        checks.append({'name': name, 'pass': bool(ok)})

    on = state('provider-ko/composition-01-after.json')
    off = state('provider-ko/composition-02-after.json')
    again = state('provider-ko/composition-03-after.json')
    check('Real off response changes only existing landform details', toggle_only(on, off, 'none'))
    check('Real on response restores the entire original state', toggle_only(off, again, 'natural') and again == on)
    bad = copy.deepcopy(off)
    bad['elevationShapes'][0]['variant'] = '999'
    check('Same detector rejects changed mountain variant', not toggle_only(on, bad, 'none'))
    bad = copy.deepcopy(off)
    bad['fertilityOffset'] = 0.9
    check('Same detector rejects unrelated fertility bonus', not toggle_only(on, bad, 'none'))
    check('First whole-landscape request has no Soil/SoilRich repaint', not unrequested_soil(on))
    check('Same soil detector rejects observed first recommendation', unrequested_soil(state('provider-ko/composition-04-option-1-state.json')))
    for i in range(1, 4):
        check(f'Revised recommendation {i} has no unrequested soil repaint',
              not unrequested_soil(state(f'provider-refine/composition-01-option-{i}-state.json')))
    native = read('native-n2601/result.json')
    check('Both recorded states rendered through native MapPreview', native['ok'] and len(native['results']) == 2)
    a, b = [row['audit'] for row in native['results']]
    check('Both native previews contain water and usable floor',
          all(row['water'] > 0 and row['plannedFloor'] > 0 for row in (a, b)))
    check('Water and mountain counts are retained when details are disabled',
          a['water'] == b['water'] == 1046 and a['mountains'] == b['mountains'] == 7597)
    result = {'ok': all(row['pass'] for row in checks), 'checks': checks,
              'retainedQualityFailure': 'provider-ko option 1 proposed unsolicited 35% rich soil; corrected prompt tested separately',
              'limits': 'Native counts are not full-map cell identity; preview aesthetics and user grassland compatibility remain separate.'}
    (root / 'audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result['ok'] else 1


if __name__ == '__main__':
    raise SystemExit(run())
