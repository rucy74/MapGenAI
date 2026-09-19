"""Evaluate recorded real replies/maps; no model calls or map generation here."""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / 'docs/analysis/2026-09-19-contextual-recommendations'
DLL = 'f265b99feb95e86a289dedefed4a5aa518af7177db9fbde5f9b5c023dbc1eda2'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def run(name):
    folder = ROOT / name
    assert read(folder / 'launch.json')['sourceDllSha256'].lower() == DLL, name
    result = read(folder / 'suite-result.json')
    assert result['complete'] and not result['fatal'], name
    assert all(r['action'] != 'error' for r in result['results']), name
    log = (folder / 'Player.log').read_text(encoding='utf-8-sig')
    assert 'Underground cave generation exceeded' not in log, name
    return folder, result


assert hashlib.sha256((REPO / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest() == DLL
assert 'CoreRegressionTests: 150 PASS / 0 FAIL' in (ROOT / 'tests-final.log').read_text(encoding='utf-8-sig')
selections = 0
for lang in ('ko', 'en'):
    folder, data = run('selection-accepted-' + lang)
    for item in data['results']:
        if item['action'] != 'recommend':
            continue
        assert item['options'] == 3 and item['unchangedBeforeSelection']
        for selection in item['selections']:
            assert selection['appliedStoredCommand'] and selection['undo']
            selections += 1
assert selections == 18

maps = []
previews = 0
for lang in ('ko', 'en'):
    for option in (1, 2, 3):
        folder, data = run(f'accepted-maps-{lang}-{option}')
        for item in data['results']:
            assert item['generated'] and item['undo'] and item['preservedSourceShapes']
            assert not item['issues'], (folder.name, item)
            ident = item['id']
            assert (folder / (ident + '-generated-preview.png')).is_file()
            if ident == 'existing':
                before = read(folder / (ident + '-before.json'))['state']
                after = read(folder / (ident + '-after.json'))['state']
                for key in ('elevationShapes', 'structures'):
                    assert all(value in after[key] for value in before[key]), key
                fills = item['compoundAudit']['fills']
                assert len(fills) == 1 and abs(fills[0]['painted'] / fills[0]['eligible'] - .7) < .001
                assert fills[0]['waterInInterior'] == 0
            if ident == 'explicit-canyon':
                routes = item['compoundAudit']['routes']
                assert routes[0]['width'] == 12 and routes[0]['dryEndpointConnection']
            if ident == 'explicit-ring':
                after = read(folder / (ident + '-after.json'))['state']
                ring = next(s for s in after['elevationShapes'] if s['id'] == 'donut_mountain')
                assert not ring['edge_roughness']
                assert all(s['prim'] == 'circle' for s in ring['compositeShapes'])
                assert item['waterCells'] == 0 and item['centerReachesSouth']
            maps.append(dict(language=lang, option=option, id=ident, dryPercent=round(item['dryCells']/625, 2),
                             centerConnectedCells=item['centerGroundCells'], image=f'{folder.name}/{ident}-generated-preview.png'))
        preview_folder = ROOT / (f'previews-ko-{option}' if lang == 'ko' else folder.name)
        preview = read(preview_folder / 'preview-result.json')
        assert preview['ok'] and not preview['error']
        assert len(preview['results']) == len(data['results'])
        for item in preview['results']:
            assert not item['issues']
            assert (preview_folder / (item['id'] + '-background-preview.png')).is_file()
            previews += 1
assert len(maps) == previews == 20

source = (REPO / 'dev/Source/UI/L10n.cs').read_text(encoding='utf-8-sig')
pair = re.search(r'\{"MapGenAI_Welcome", \(\s*("(?:[^"\\]|\\.)*")\s*,\s*("(?:[^"\\]|\\.)*")\s*\)\}', source)
assert pair
welcome = []
for lang, raw in zip(('Korean', 'English'), pair.groups()):
    text = json.loads(raw)
    xml = ET.parse(REPO / f'dev/Languages/{lang}/Keyed/MapGenAI.xml').getroot().findtext('MapGenAI_Welcome').replace('\\n', '\n')
    assert text == xml and '3개' not in text and 'three' not in text.lower()
    welcome.append(dict(language=lang, codeXmlMatch=True, text=text))

result = dict(ok=True, dllSha256=DLL, offlineTests=150, newProviderCalls=17,
              acceptedReplySources=read(ROOT/'accepted-response-sources.json'), candidateSelections=selections,
              fullMaps=20, backgroundPreviews=previews, maps=maps, welcome=welcome,
              initialInvalidReplies=2, intermediateUndergroundCaveGenerationFailure=True,
              interruptedPreviewStages=6, unresolvedPriorNativeCrashes=1,
              limitations=['Prompt guidance, not a mechanical quality gate.',
                           'One deterministic world, selected tiles and Gemini 3.8 only.',
                           'Maximum options remains three; four not implemented.',
                           'Existing unrelated native crash remains unresolved.'])
(ROOT / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(json.dumps({key: result[key] for key in ('ok', 'offlineTests', 'candidateSelections', 'fullMaps', 'backgroundPreviews', 'newProviderCalls')}))
