"""Summarize final organic-layout evidence without hiding earlier trial DLLs."""
from pathlib import Path
import hashlib
import json
import re
from PIL import Image
from summarize import different_pixels

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/analysis/2026-09-23-landform-design'
OLD = ROOT / 'docs/analysis/2026-09-23-natural-landforms'
RECOMMENDATION_BASE = ROOT / 'docs/analysis/2026-09-23-recommendation-feedback/verification/native-headless-r7'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def main():
    dll = hashlib.sha256((ROOT / 'dev/Assemblies/MapGenAI.dll').read_bytes()).hexdigest()
    runs = []
    for number in range(13, 26):
        folder = OUT / f'native-n{number}'
        launch, result = read(folder/'launch.json'), read(folder/'result.json')
        assert result['ok'] and all(c.startswith('PASS ') for c in result['checks']), folder
        rows = []
        for row in result.get('results', []):
            audit = {k:v for k,v in row.get('audit',{}).items() if k not in ('riverCells','oceanCells')}
            rows.append({**row, 'audit': audit})
        final = launch['sourceDllSha256'].lower() == dll
        assert final == (number >= 20), 'Evidence DLL lineage changed'
        runs.append({'run':folder.name,'productDllSha256':launch['sourceDllSha256'].lower(),
                     'isFinalDll':final,'checks':len(result['checks']),
                     'newProviderCalls':result['newProviderCalls'],'measurements':rows})
    comparisons = []
    pairs = [(RECOMMENDATION_BASE/name,OUT/'native-n24'/name,'recorded-recommendation')
             for name in sorted(f'{kind}-map-{i}.png' for kind in ('quick','guided') for i in (1,2,3))]
    pairs += [(p,OUT/'native-n25'/p.name,'saved-classic-layout') for p in sorted((OLD/'native-n11').glob('*.png'))]
    for a,b,scope in pairs:
        with Image.open(a) as before, Image.open(b) as after:
            changed = different_pixels(before,after)
            comparisons.append({'image':a.name,'scope':scope,'pixels':before.width*before.height,'differentPixels':changed})
            assert changed == 0, (a,b,changed)
    with Image.open(pairs[0][0]) as raw:
        before=raw.convert('RGBA');changed=before.copy();p=list(changed.getpixel((0,0)));p[0]^=1
        changed.putpixel((0,0),tuple(p));positive=different_pixels(before,changed)
        assert positive==1,'A changed pixel must be detected'
    followup={r['id']:r for r in read(OUT/'native-n23/result.json')['results']}
    water=[]
    for old,new,key in [('river-baseline','valley-existing-river','riverCells'),('coast-baseline','foothills-existing-coast','oceanCells')]:
        a=set(filter(None,followup[old]['audit'][key].split(',')));b=set(filter(None,followup[new]['audit'][key].split(',')))
        water.append({'feature':key,'before':len(a),'after':len(b),'lost':len(a-b),'new':len(b-a)})
        assert a and a==b,key
    match=re.search(r'CoreRegressionTests: (\d+) PASS / (\d+) FAIL',(OUT/'pure-d4.log').read_text(encoding='utf-8-sig'))
    assert match and match[2]=='0'
    provider=read(OUT/'provider-d1/result.json')
    independent=read(OUT/'independent-d2-r1-summary.json')
    assert independent['ok'] and independent['organicFailures']==0 and independent['legacyDifferentCells']==0
    summary={'finalDllSha256':dll,'pureTests':{'pass':int(match[1]),'fail':int(match[2]),'defaultLayouts':1200,'boundaryLayouts':300},
             'independent':independent,
             'nativeRuns':runs,'finalNativeChecks':sum(r['checks'] for r in runs if r['isFinalDll']),
             'oldMapComparisons':comparisons,'pixelComparatorPositiveControl':{'changed':1,'detected':positive},
             'waterCellComparison':water,'providerAttempt':provider,
             'limitations':['Native fixtures are controlled parameters, not successful live provider output.',
               'Authored variants share one native map seed; no multi-biome or long-play guarantee.',
               'Connectivity corpus is centered on neutral ground; preserved world features may divide final paths.',
               'Visual composition was inspected, not a promise that every user likes every variant.',
               'Original integration, game installation and push are pending.']}
    (OUT/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'finalNativeChecks':summary['finalNativeChecks'],'legacyMaps':len(comparisons),
                     'legacyPixels':sum(c['pixels'] for c in comparisons),'changedPixels':sum(c['differentPixels'] for c in comparisons),
                     'water':water},indent=2))

if __name__=='__main__': main()
