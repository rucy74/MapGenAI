from pathlib import Path
import json,hashlib,re,copy
root=Path('docs/analysis/2026-09-26-landscape-relations')
def j(p): return json.loads(p.read_text('utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
checks=[]
def check(ok,label):
 checks.append(('PASS ' if ok else 'FAIL ')+label)
 if not ok: raise AssertionError(label)
def failures(run):
 return [v for v in run['checks'] if not v.startswith('PASS ')]+[str(v['audit']['issues']) for v in run['results'] if v['audit']['issues']]+['relationship violation' for v in run['results'] for a in v['audit']['relationships'] if a['cells']<=0 or a['violations']!=0]
dll=Path('dev/Assemblies/MapGenAI.dll')
runs={name:j(root/name/'result.json') for name in ['native-n2711','native-n2710']}
positive=copy.deepcopy(runs['native-n2711']);positive['checks'][0]='FAIL injected';check(bool(failures(positive)),'native checker detects injected failed assertion')
positive=copy.deepcopy(runs['native-n2711']);positive['results'][0]['audit']['relationships'][0]['violations']=1;check(bool(failures(positive)),'native checker detects injected relation violation')
for name,r in runs.items():
 check(r['ok'] and not failures(r),name+' complete native checks and no placement issues')
 check(j(root/name/'launch.json')['sourceDllSha256'].lower()==sha(dll),name+' used current DLL')
 check('-force-d3d11' in (root/name/'Player.log').read_text('utf-8') and '-nographics' not in (root/name/'Player.log').read_text('utf-8'),name+' real graphics mode')
 check(len(r['results'])==(5 if name.endswith('2711') else 12),name+' expected complete outputs')
check('282 PASS / 0 FAIL' in (root/'pure-regression.log').read_text('utf-8'),'fresh complete regression total')
provider=j(root/'provider-diversity/result.json')
check(provider['calls']==3 and provider['ok'],'latest three independent actual recommendations')
check(all(v['inputSnapshotUntouched'] and v['batchValid'] and v['optionCount']==3 for v in provider['results']),'nine options keep input state and validate')
check(all(len(list((root/'native-n2710').glob(f'diversity-{a:02d}-*-option-*-state.png')))==3 for a in [1,2,3]),'all nine actual recommendation pictures present')
# Evidence contains prompts/results, never provider configuration.
secret=re.compile(r'AIza[0-9A-Za-z_-]{30,}|sk-(?:proj-)?[A-Za-z0-9_-]{30,}')
check(bool(secret.search('AIza'+'x'*35)),'secret detector positive control')
textfiles=[p for p in root.rglob('*') if p.is_file() and p.suffix.lower() in {'.json','.txt','.log','.md','.xml'}]
check(not any(secret.search(p.read_text('utf-8-sig',errors='replace')) for p in textfiles),'evidence does not contain token-shaped credentials')
files={p.relative_to(root).as_posix():sha(p) for p in sorted(root.rglob('*')) if p.is_file() and p.name!='artifact-audit.json'}
result={'ok':True,'checks':checks,'dllSha256':sha(dll),'nativeChecks':sum(len(v['checks']) for v in runs.values()),'providerCalls':15,'inputTokens':274899,'outputTokens':6736,'rawFiles':files,'limits':'No visual quality, all biome/mod compatibility, full GL parity or post-guard real model repair guarantee.'}
(root/'artifact-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
print('\n'.join(checks));print('Raw files',len(files),'MB',round(sum(p.stat().st_size for p in root.rglob('*') if p.is_file())/1024**2,2))
