"""Check actual road/bridge terrain and connectivity, not model descriptions."""
from pathlib import Path
import hashlib
import json
from PIL import Image, ImageChops

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/analysis/2026-09-22-road-bridges'
checks = []
maps = []

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def check(label, value):
    checks.append(dict(label=label, ok=bool(value)))

def load(run):
    full = read(root / run / 'suite-result.json')
    preview = read(root / run / 'preview-result.json')
    check(run + ': complete', full['complete'] and full['fatal'] is None and preview['ok'])
    return ({r['id']: r for r in full['results']}, {r['id']: r for r in preview['results']})

def preserved(label, row):
    check(label + ': generated', not row.get('error') and row.get('undo', True))
    audit = row['roadAudit']; immediate = audit['immediate']
    zero = ['outsideFootprintChanges', 'footprintMaskDifferences', 'elevationChanges',
            'protectedFloorChanges', 'protectedLayerChanges', 'existingBridgeChanges',
            'newBridgesMissingOriginalBase', 'bridgedWaterMissingOriginalWater', 'waterChangedWithoutBridge']
    check(label + ': preserved layers/world links', all(immediate[k] == 0 for k in zero)
          and immediate['worldRoadsUnchanged'] and immediate['worldRiversUnchanged']
          and audit['worldRoadsStillUnchanged'] and audit['worldRiversStillUnchanged'])
    return audit, immediate

def success(label, row, bridges):
    audit, immediate = preserved(label, row)
    check(label + ': success', not immediate['applyFailed'] and not row.get('issues') and audit['roads'])
    check(label + ': bridge count', (immediate['newBridgeCells'] > 0) == bridges)
    for road in audit['roads']:
        check(label + ': walkable continuous road', road['blockedPathCells'] == road['unwalkablePathCells'] == road['bridgeUnwalkableCells'] == 0
              and road['connectedWithinFootprint'])
        for crossing in road['crossings']:
            check(label + ': road-bridge-road', crossing['dryBanksPresent'] and crossing['entireWaterPathBridged']
                  and crossing['roadBridgeRoadConnected'] and crossing['separateDryBanksWithinFootprint'])

def failure(label, row):
    audit, immediate = preserved(label, row)
    check(label + ': rejected without painting', immediate['applyFailed'] and row['issues'] and not audit['roads']
          and immediate['failureLayersUnchanged'] and immediate['anyLayerChanges'] == 0
          and immediate['beforeLayerHash'] == immediate['afterLayerHash'])

def same_pixels(a, b):
    left = Image.open(a).convert('RGBA'); right = Image.open(b).convert('RGBA')
    return left.size == right.size and ImageChops.difference(left, right).convert('RGB').getbbox() is None

current, previews = load('native-r1')
baseline, baseline_previews = load('baseline-native')
check('19 paired fixture maps', len(current) == len(previews) == len(baseline) == len(baseline_previews) == 19)
rejected = {'deep-lake-rejected', 'lava-rejected', 'water-endpoint-rejected', 'batch-preflight-rejected'}
dry = {'dirt-path', 'dirt-road', 'stone-road', 'asphalt', 'highway', 'pond-dry-detour'}
unchanged = []
pixel_matches = 0
for ident, full in current.items():
    preview = previews[ident]
    for mode, row in [('full', full), ('preview', preview)]:
        label = ident + '/' + mode
        if ident in rejected:
            failure(label, row)
        else:
            success(label, row, ident not in dry)
    if ident not in dry | rejected | {'wet-ground'}:
        failure(ident + '/old-full', baseline[ident])
        failure(ident + '/old-preview', baseline_previews[ident])
    fa = full['roadAudit']; pa = preview['roadAudit']
    # Map Preview omits native SetTerrain under-layer bookkeeping for stone/asphalt.
    # Compare paths, visible terrain, and every actual bridge layer independently.
    check(ident + ': full-preview bridge layers', fa['immediate']['bridgeLayers']['hash'] == pa['immediate']['bridgeLayers']['hash'])
    check(ident + ': full-preview roads', [(r['pathHash'], r['terrainHash']) for r in fa['roads']] == [(r['pathHash'], r['terrainHash']) for r in pa['roads']])
    image = 'native-r1/' + ident + '-generated-preview.png'
    thumb = 'native-r1/' + ident + '-background-preview.png'
    if ident not in rejected:
        equal = same_pixels(root / image, root / thumb)
        check(ident + ': all 62500 preview pixels', equal)
        pixel_matches += int(equal)
    if ident in dry:
        for mode, row, old, suffix in [('full', full, baseline[ident], '-generated-preview.png'), ('preview', preview, baseline_previews[ident], '-background-preview.png')]:
            check(ident + '/' + mode + ': old map unchanged', row['roadAudit']['finalLayerHash'] == old['roadAudit']['finalLayerHash']
                  and row['roadAudit']['roads'][0]['pathHash'] == old['roadAudit']['roads'][0]['pathHash']
                  and same_pixels(root / 'native-r1' / (ident + suffix), root / 'baseline-native' / (ident + suffix)))
            unchanged.append(ident + '/' + mode)
    maps.append(dict(id=ident, image=image, preview=thumb, bridgeCells=fa['immediate']['newBridgeCells'], expectedFailure=ident in rejected))

actual_river = current['native-river']['roadAudit']['immediate']['bridgeLayers']['cells']
check('Native river shallow and deep base preserved', {'WaterMovingShallow', 'WaterMovingChestDeep'} <= {cell[3] for cell in actual_river})
fault, fault_preview = load('fault-native')
for mode, rows in [('full', fault), ('preview', fault_preview)]:
    for ident, row in rows.items():
        failure('injected-fault/' + mode, row)
        i = row['roadAudit']['immediate']
        check('injected-fault/' + mode + ': five real writes occurred', i['faultInjected'] and i['completedBridgeWritesBeforeFault'] == 5
              and i['faultPatchTarget'] == 'MapGenAI.MapGen.RoadBridges.Place'
              and i['faultPatchOwner'] in i['faultTargetPatchOwners'])

existing, existing_preview = load('existing-native-r2')
for mode, rows in [('full', existing), ('preview', existing_preview)]:
    for ident, row in rows.items():
        success('existing/' + mode, row, True)
        i = row['roadAudit']['immediate']
        check('existing/' + mode + ': fixture actually present', i['existingFixture'] and i['existingBridgeCells'] == 93 and i['protectedFloorCells'] >= 94)

provider_calls = []
for lang in ['ko', 'en']:
    model = read(root / ('provider-' + lang) / 'result.json')
    check(lang + ': fresh first response', model['transportAndEnvelopeOk'] and model['requestsCompleted'] == 1 and model['results'][0]['action'] == 'generate')
    provider_calls += model['results']
    full, preview = load('provider-native-' + lang)
    for mode, rows in [('full', full), ('preview', preview)]:
        for ident, row in rows.items():
            success('provider-' + lang + '/' + mode, row, True)
    ident = next(iter(full))
    run = 'provider-native-' + lang
    check(lang + ': first response preview pixels', same_pixels(root / run / (ident + '-generated-preview.png'), root / run / (ident + '-background-preview.png')))

dll = hashlib.sha256((repo / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest()
for run in ['native-r1', 'fault-native', 'existing-native-r2', 'provider-native-ko', 'provider-native-en']:
    check(run + ': current DLL', read(root / run / 'launch.json')['sourceDllSha256'].lower() == dll)
offline = (root / 'offline-tests-final.txt').read_text(encoding='utf-8-sig')
check('183 offline tests passed', '183 passed, 0 failed' in offline or '183 PASS' in offline or 'Passed: 183' in offline)
result = dict(ok=all(c['ok'] for c in checks), checks=checks, nativeChecks=len(checks), dllSha256=dll,
              offlineTests=183, currentFullMaps=23, currentPreviews=23, baselineFullMaps=19, baselinePreviews=19,
              unchangedDryMaps=unchanged, successfulFixturePixelMatches=pixel_matches, providerCalls=provider_calls,
              maps=maps, scope='Native RimWorld 1.6 controlled maps and two fresh model requests. No claim for all mod combinations or unrelated MG23 crashes.')
(root / 'evaluation.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for check in checks:
    if not check['ok']:
        print('FAIL', check['label'])
print(json.dumps({k: result[k] for k in ['ok', 'nativeChecks', 'offlineTests', 'currentFullMaps', 'currentPreviews', 'successfulFixturePixelMatches']}))
raise SystemExit(0 if result['ok'] else 1)
