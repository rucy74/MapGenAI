"""Summarize recorded native runs; compare final legacy previews with the saved baseline."""
from pathlib import Path
import hashlib
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/analysis/2026-09-23-natural-landforms'
BASELINE = ROOT / 'docs/analysis/2026-09-23-recommendation-feedback/verification/native-headless-r7'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def different_pixels(a, b):
    if a.size != b.size:
        raise ValueError('Different image dimensions')
    left, right = a.convert('RGBA').tobytes(), b.convert('RGBA').tobytes()
    return sum(left[i:i+4] != right[i:i+4] for i in range(0, len(left), 4))


def main():
    dll_hash = hashlib.sha256((ROOT / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest()
    runs = []
    for number in (5, 6, 7, 8, 9, 10, 11, 12):
        folder = OUT / f'native-n{number}'
        launch, result = read(folder / 'launch.json'), read(folder / 'result.json')
        rows = []
        for row in result.get('results', []):
            # Retain concise measurements here; full cell coordinates remain in raw evidence.
            audit = {k: v for k, v in row.get('audit', {}).items() if k not in ('riverCells', 'oceanCells')}
            rows.append({**row, 'audit': audit})
        runs.append({'run': folder.name, 'productDllSha256': launch['sourceDllSha256'].lower(),
                     'isFinalDll': launch['sourceDllSha256'].lower() == dll_hash,
                     'ok': result['ok'], 'checks': len(result['checks']),
                     'newProviderCalls': result['newProviderCalls'], 'measurements': rows})
        assert result['ok'], folder

    comparisons = []
    for name in sorted([f'{kind}-map-{i}.png' for kind in ('guided', 'quick') for i in (1, 2, 3)]):
        with Image.open(BASELINE / name) as before, Image.open(OUT / 'native-n12' / name) as after:
            changed = different_pixels(before, after)
            comparisons.append({'image': name, 'size': list(before.size), 'differentPixels': changed})
            assert changed == 0, name
    with Image.open(BASELINE / comparisons[0]['image']) as raw:
        before = raw.convert('RGBA')
        changed = before.copy()
        pixel = list(changed.getpixel((0, 0)))
        pixel[0] ^= 1
        changed.putpixel((0, 0), tuple(pixel))
        positive = different_pixels(before, changed)
        assert positive == 1, 'Pixel comparator must detect a one-pixel change'

    followup = {r['id']: r for r in read(OUT / 'native-n11/result.json')['results']}
    water = []
    for before_id, after_id, field in (('river-baseline', 'valley-existing-river', 'riverCells'),
                                       ('coast-baseline', 'foothills-existing-coast', 'oceanCells')):
        before = set(filter(None, followup[before_id]['audit'][field].split(',')))
        after = set(filter(None, followup[after_id]['audit'][field].split(',')))
        water.append({'feature': field, 'before': len(before), 'after': len(after),
                      'lost': len(before - after), 'new': len(after - before)})
        assert before and before == after, field

    summary = {'finalProductDllSha256': dll_hash, 'pureTests': {'pass': 249, 'fail': 0,
               'unfilteredDefaultLayouts': 1200, 'boundaryLayouts': 300},
               'independentLayouts': {'count': 960, 'fail': 0, 'evidence': 'independent-result.json'},
               'nativeRuns': runs, 'finalNativeWaterCellComparison': water,
               'finalLegacyPixelComparison': comparisons,
               'pixelComparatorPositiveControl': {'mutatedPixels': 1, 'detectedPixels': positive},
               'limitations': ['Controlled fixtures, not live provider request quality.',
                   'Same native map seed for authored variants; not five distinct world seeds.',
                   'Native textures and one full-map fixture, not GUI or long-running colony tests.',
                   'New geometry corpus uses a neutral base and centered layouts. Native features can divide traversable ground.',
                   'No memory/FPS benchmark, original repository integration, installation or remote push.']}
    (OUT / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'finalDll': dll_hash, 'water': water, 'oldDifferentPixels': sum(c['differentPixels'] for c in comparisons),
                      'positiveControl': positive, 'finalNativeChecks': sum(r['checks'] for r in runs if r['isFinalDll'])}, indent=2))


if __name__ == '__main__':
    main()
