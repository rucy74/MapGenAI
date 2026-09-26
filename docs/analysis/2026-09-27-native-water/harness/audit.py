"""Independent audit of the frozen 16-fixture native-map matrix; never edits raw evidence."""
from pathlib import Path
import collections, hashlib, json, subprocess, sys
from compare import read, sha, layers, without_profile
root=Path(__file__).parent.parent
runs=root/'runs';gate=runs/'native-temperate-water-new06-graphics-01'
oldsha='b75da4efe80ce1333b75538a669ea0811dc4c81b98976bb94a15beba1cb5e084'
newsha='4908863336befac9c141ba35cb15bbfb47617ed87a9236cd21f7b3449abde019'
progress=read(root/'matrix-progress.json');completion=read(root/'matrix-completion.json')
selected={v['id']:runs/v['run'] for v in progress['jobs']+completion['jobs']};selected['temp-new']=gate
checks=[];observations={};comparisons={}
def check(name,ok,details=None):checks.append(dict(name=name,ok=bool(ok),details=details))
def snap(d,n):return layers(read(d/n))
def ground(v):return v.split('|')[1]
def water(v):return ground(v).startswith('Water')
def same_at(a,b,indices):return all(a[i]==b[i] for i in indices)
def clean(value,keys):
    if isinstance(value,dict):return {k:clean(v,keys) for k,v in value.items() if k not in keys}
    if isinstance(value,list):return [clean(v,keys) for v in value]
    return value
def island(mask,w,h,index):
    if mask[index]:return {'area':0,'touchesEdge':False,'bounds':None}
    seen={index};q=collections.deque([index]);edge=False
    while q:
        i=q.popleft();x,z=i%w,i//w
        edge|=x in (0,w-1) or z in (0,h-1)
        for xx,zz in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
            j=zz*w+xx
            if 0<=xx<w and 0<=zz<h and j not in seen and not mask[j]:seen.add(j);q.append(j)
    return {'area':len(seen),'touchesEdge':edge,'bounds':[min(i%w for i in seen),min(i//w for i in seen),max(i%w for i in seen),max(i//w for i in seen)]}

check('detector control: layer mutation rejected',not same_at(['a','b'],['a','c'],[0,1]))
check('detector control: water-mask mutation rejected',[water('WaterDeep|WaterDeep|||')]!=[water('Soil|Soil|||')])
toy=[True]*25;toy[12]=False
check('detector control: enclosed island recognized',island(toy,5,5,12)=={'area':1,'touchesEdge':False,'bounds':[2,2,2,2]})
check('detector control: open dry plane rejected',island([False]*25,5,5,12)['touchesEdge'])
check('matrix contains exactly 16 unique fixtures',len(selected)==16 and len(set(selected.values()))==16)
for key,d in selected.items():
    result=read(d/'result.json');launch=read(d/'launch.json');final=snap(d,'full-complete-terrain.json')
    expected=oldsha if key.endswith('-old') else newsha
    check(key+': expected frozen product SHA',launch['sourceDllSha256'].lower()==expected)
    check(key+': frozen probe SHA',launch['probeDllSha256'].lower()==progress['probeSha256'])
    check(key+': graphics enabled with PNG',launch['graphicsEnabled'] and (d/'preview.png').read_bytes()[:8]==b'\x89PNG\r\n\x1a\n')
    check(key+': no provider calls',result['blockedProviderFactoryCalls']==0)
    check(key+': complete map dimensions',len(final)==62500)
    if not key.startswith('river-'):check(key+': raw run passed',result['ok'] and not result['error'])
    observations[key]={'rawRunPassed':result['ok'],'rawChecks':result['checks'],'launchTime':launch['started'],'groundCounts':read(d/'full-complete-terrain.json')['groundCounts']}

for name,a,b,exact in [('temperate','temp-old','temp-new',False),('desert','desert-old','desert-new',False),('exact','exact-old','exact-new',True),('legacy','temp-old','legacy-new',True),('hotspring','hotspring-old','hotspring-new',True)]:
    path=root/(name+'-comparison.json')
    command=[sys.executable,str(root/'harness/compare.py'),str(selected[a]),str(selected[b]),'--out',str(path)]+(['--exact'] if exact else [])
    process=subprocess.run(command,capture_output=True,text=True,encoding='utf-8');comparison=read(path)
    check(name+': independently compared pair',process.returncode==0 and comparison['ok'],comparison['checks'])
    comparisons[name]={'path':str(path),'changedCells':comparison['fullMapChangedCells'],'ok':comparison['ok']}

on=selected['temp-new'];off=selected['off-new'];a=snap(on,'full-complete-terrain.json');b=snap(off,'full-complete-terrain.json')
check('details off: same state except details',clean(read(on/'state.json'),{'details'})==clean(read(off/'state.json'),{'details'}))
check('details off: all water masks identical',[water(v) for v in a]==[water(v) for v in b])
check('details off: deep/shallow water materials identical',all(x==y for x,y in zip(a,b) if water(x) or water(y)))
observations['detailsToggle']={'changedCells':sum(x!=y for x,y in zip(a,b)),'waterCells':sum(water(x) for x in a)}
shallow=snap(selected['shallow-new'],'full-complete-terrain.json')
check('explicit shallow: no deep water inserted',sum(ground(x)=='WaterDeep' for x in shallow)==0 and sum(ground(x)=='WaterShallow' for x in shallow)>0)

for key in ['protected-old','protected-new']:
    d=selected[key];result=read(d/'result.json');report=result['results']['fullReport']
    coverage=next(v for v in report['coverage'] if v['id']=='soil70')
    check(key+': actual 70 percent coverage',coverage['eligible']>0 and abs(coverage['selected']-.7*coverage['eligible'])<=1,coverage)
    roads=[z*250+x for road in report['roads'] for x,z in road['footprint']]
    explicit=[z*250+x for z in range(88,163) for x in range(146,200)]
    check(key+': real road footprint exists',len(roads)>0)
    for phase in ['preview','full']:
        before=snap(d,phase+'-before-blend.json');after=snap(d,phase+'-after-blend.json')
        check(key+': '+phase+' 4050-cell explicit region preserved through blend',len(explicit)==4050 and same_at(before,after,explicit))
        # Road footprint reported by full generation: the preview can legally choose a different route.
        if phase=='full':
            check(key+': full road layers preserved through blend',same_at(before,after,roads))
            check(key+': full road layers survive completed map',same_at(before,snap(d,'full-complete-terrain.json'),roads))
    observations[key]['protection']={'sourceRegionCells':len(explicit),'roadFootprintCells':len(set(roads)),'coverage':coverage}

for key in ['river-old','river-new']:
    d=selected[key];result=read(d/'result.json');ice=[]
    permitted=['full-phase-end: native river terrain layers preserved','full-complete: native river terrain layers preserved']
    check(key+': only diagnosed raw overlay checks failed',not result['error'] and all(c['ok'] or c['name'] in permitted for c in result['checks']))
    for phase in ['preview','full']:
        native=read(d/(phase+'-native-river.json'));cells=native['cells'];indices=[c['index'] for c in cells]
        check(key+': '+phase+' native river actually exists',len(cells)>0)
        before=snap(d,phase+'-before-blend.json');after=snap(d,phase+'-after-blend.json')
        check(key+': '+phase+' river all layers unchanged before blend',all(before[c['index']]==c['layers'] for c in cells))
        check(key+': '+phase+' river all layers preserved through blend',same_at(before,after,indices))
        names=[phase+'-phase-end-terrain.json']+(['full-complete-terrain.json'] if phase=='full' else [])
        for name in names:
            final=snap(d,name);changed=[(c['layers'],final[c['index']]) for c in cells if c['layers']!=final[c['index']]]
            exact_ice=all(y.split('|')[0]=='ThinIce' and y.split('|')[4]=='ThinIce' and x.split('|')[1:4]==y.split('|')[1:4] and x.split('|')[4]=='' for x,y in changed)
            check(key+': '+name+' permanent river layers preserved',all(c['layers'].split('|')[1:4]==final[c['index']].split('|')[1:4] for c in cells))
            check(key+': '+name+' changed layers exactly native ThinIce overlay',exact_ice)
            ice.append({'phase':name,'observed':len(cells),'changedSurfaceAndTemp':len(changed),'permanentChanges':sum(x.split('|')[1:4]!=y.split('|')[1:4] for x,y in changed),'changes':dict(collections.Counter(x+' -> '+y for x,y in changed))})
    observations[key]['seasonalIce']=ice
oldriver,newriver=selected['river-old'],selected['river-new']
check('river pair: identical fixture context',all(read(oldriver/'fixture.json')[k]==read(newriver/'fixture.json')[k] for k in ['tile','worldSeed','setupRandSeed','temperature','rainfall','ticksAbs','ticksGame']))
check('river pair: state differs only profile',without_profile(read(oldriver/'state.json'))==without_profile(read(newriver/'state.json')))
observations['riverPairNativeSkip']={}
for phase in ['preview','full']:
    aa={c['index']:c['layers'] for c in read(oldriver/(phase+'-native-river.json'))['cells']}
    bb={c['index']:c['layers'] for c in read(newriver/(phase+'-native-river.json'))['cells']}
    ot=snap(oldriver,phase+'-native-terrain.json');nt=snap(newriver,phase+'-native-terrain.json')
    onlyold=aa.keys()-bb.keys();onlynew=bb.keys()-aa.keys();shared=aa.keys()&bb.keys()
    # The native RiverTerrainAt worker deliberately returns null for existing non-river
    # water at terrain stage 210. Changed authored ponds therefore legitimately change
    # native river-type cell labels at stage 220, without moving the river field.
    fresh=lambda v:ground(v) in ('WaterDeep','WaterShallow')
    check('river pair: '+phase+' old-only river exactly excluded by new pre-river freshwater',len(onlyold)==100 and all(fresh(nt[i]) and not fresh(ot[i]) for i in onlyold))
    check('river pair: '+phase+' new-only river exactly excluded by old pre-river freshwater',len(onlynew)==146 and all(fresh(ot[i]) and not fresh(nt[i]) for i in onlynew))
    check('river pair: '+phase+' all shared native river layers identical',len(shared)>0 and all(aa[i]==bb[i] for i in shared))
    changedwater={i for i in range(62500) if fresh(ot[i])!=fresh(nt[i])}
    check('river pair: '+phase+' identical river outside changed authored water',all(aa.get(i)==bb.get(i) for i in (aa.keys()|bb.keys())-changedwater))
    observations['riverPairNativeSkip'][phase]={'oldRiverCells':len(aa),'newRiverCells':len(bb),'oldOnlyRiver':len(onlyold),'newOnlyRiver':len(onlynew),'sharedRiver':len(shared),'oldOnlyOpposite210Terrain':dict(collections.Counter(nt[i] for i in onlyold)),'newOnlyOpposite210Terrain':dict(collections.Counter(ot[i] for i in onlynew)),'initialIdenticalRiverMaskExpectation':False,'explanation':'Actual native RiverTerrainAt excludes existing nonriver water. All 246 changed labels coincide exactly with changed pre-river authored freshwater. No unexplained shared river material or outside-mask change.'}

ring=selected['ring-new'];observations['ringIsland']={}
for name in ['preview-phase-end-terrain.json','full-complete-terrain.json']:
    a=snap(ring,name);region=island([water(v) for v in a],250,250,125*250+125)
    check('ring: center dry island survived '+name,region['area']>0 and not region['touchesEdge'],region)
    observations['ringIsland'][name]=region
field=read(gate/'native-water-checks.json');integration=read(gate/'native-integration-checks.json')
check('unfiltered production field/raster checks',field['passed']==706 and field['failed']==0 and field['unfiltered'] and not field['resamplingOrRetry'])
check('actual Scribe and helper integration checks',integration['passed']==13 and integration['failed']==0)
manifest={str(p):sha(p) for d in selected.values() for p in sorted(d.iterdir()) if p.is_file()}
report={'ok':all(c['ok'] for c in checks),'passed':sum(c['ok'] for c in checks),'failed':sum(not c['ok'] for c in checks),'checks':checks,'observations':observations,'comparisons':comparisons,'files':manifest,
        'limits':['Raw river runs remain failed because native seasonal ThinIce changes surface/temp; separate permanent-layer and exact overlay audit is reported without rewriting those results.','Initial audit 152 PASS / 2 FAIL requiring identical native river-type masks is preserved as final-audit-rejected-river-identity.json. Native river worker skips changed authored nonriver water; the replacement bounded checks demand exact evidence for all 246 labels. Do not claim identical river-type masks across changed pond shapes.','Native map checks validate this fixed set of inputs and seeds, not all possible recommendations or aesthetic quality.','The 600 field cases are synthetic field/raster properties, not 600 generated full maps.','Helper palette sentinels are not full rendered custom biome tests.','StateFile input is authoritative; fixture CLI details field may say natural for the explicit off state.']}
(root/'final-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'ok':report['ok'],'passed':report['passed'],'failed':report['failed'],'failures':[c for c in checks if not c['ok']],'comparisons':comparisons,'detailsToggle':observations['detailsToggle'],'ringIsland':observations['ringIsland']},indent=2))
raise SystemExit(0 if report['ok'] else 1)
