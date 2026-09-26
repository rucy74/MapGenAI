"""Read actual native visual runs; do not turn coverage checks into an aesthetic verdict."""
from pathlib import Path
import argparse, hashlib, json

def sha(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

def read(p):
    return json.loads(p.read_text(encoding='utf-8-sig'))

def without_profile(value):
    if isinstance(value,dict):
        return {k:without_profile(v) for k,v in value.items() if k!='water_profile'}
    if isinstance(value,list):
        return [without_profile(v) for v in value]
    return value

def profile_values(state):
    found=[]
    def visit(value,path):
        if isinstance(value,dict):
            for k,v in value.items():
                if k=='water_profile':found.append({'path':path+'.'+k,'value':v})
                else:visit(v,path+'.'+k)
        elif isinstance(value,list):
            for i,v in enumerate(value):visit(v,path+f'[{i}]')
    visit(state,'$')
    return found

def layers(row):
    return [v for count,v in row['layers'] for _ in range(count)]

def main():
    args=argparse.ArgumentParser()
    args.add_argument('old',type=Path);args.add_argument('new',type=Path)
    args.add_argument('--exact',action='store_true')
    args.add_argument('--out',type=Path,required=True)
    a=args.parse_args()
    old,new=a.old.resolve(),a.new.resolve()
    old_result,new_result=read(old/'result.json'),read(new/'result.json')
    os,ns=read(old/'state.json'),read(new/'state.json')
    of,nf=read(old/'fixture.json'),read(new/'fixture.json')
    ot,nt=read(old/'full-complete-terrain.json'),read(new/'full-complete-terrain.json')
    checks={
        'old_native_run_passed':old_result['ok'],
        'new_native_run_passed':new_result['ok'],
        'no_provider_attempts':old_result['blockedProviderFactoryCalls']==new_result['blockedProviderFactoryCalls']==0,
        'same_fixture_context':all(of[k]==nf[k] for k in ['tile','worldSeed','setupRandSeed','mapSize','biome','temperature','rainfall','ticksGame','ticksAbs']),
        'same_state_except_profile':without_profile(os)==without_profile(ns),
        'actual_preview_pngs':all((d/'preview.png').read_bytes()[:8]==b'\x89PNG\r\n\x1a\n' for d in (old,new)),
        'complete_map_layer_count':len(layers(ot))==len(layers(nt))==250*250,
    }
    if a.exact:
        checks['exact_same_state']=os==ns or without_profile(os)==without_profile(ns) and all(v['value'] is None for v in profile_values(ns))
        checks['exact_preview_bytes']=sha(old/'preview.png')==sha(new/'preview.png')
        checks['exact_complete_map_layers']=layers(ot)==layers(nt)
    rows={}
    for name in ('preview-native-terrain.json','full-native-terrain.json','preview-phase-end-terrain.json','full-phase-end-terrain.json'):
        if (old/name).exists() and (new/name).exists():
            b,c=read(old/name),read(new/name)
            rows[name]={'oldTerrainHash':b['terrainHash'],'newTerrainHash':c['terrainHash'],'sameElevation':b['elevationHash']==c['elevationHash'],'sameCaves':b['cavesHash']==c['cavesHash']}
            if a.exact:checks['exact_'+name]=rows[name]['oldTerrainHash']==rows[name]['newTerrainHash'] and rows[name]['sameElevation'] and rows[name]['sameCaves']
    report={'ok':all(checks.values()),'checks':checks,'old':str(old),'new':str(new),
        'profileOnlyIntentionalDifference':{'old':profile_values(os),'new':profile_values(ns)},
        'oldLaunch':read(old/'launch.json'),'newLaunch':read(new/'launch.json'),
        'fullMapChangedCells':sum(x!=y for x,y in zip(layers(ot),layers(nt))),
        'oldGroundCounts':ot['groundCounts'],'newGroundCounts':nt['groundCounts'],'phaseComparisons':rows,
        'files':{str(p):sha(p) for d in (old,new) for p in sorted(d.glob('*')) if p.is_file()},
        'note':'Actual captured evidence and input equality only; no aesthetic score. Profile difference intentionally changes natural geometry; exact mode requires byte equality.'}
    a.out.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'ok':report['ok'],'checks':checks,'fullMapChangedCells':report['fullMapChangedCells'],'out':str(a.out)},ensure_ascii=False,indent=2))
    raise SystemExit(0 if report['ok'] else 1)

if __name__=='__main__':main()
