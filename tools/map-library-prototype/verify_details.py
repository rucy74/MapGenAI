"""Fresh verification receipts for developer rock and fine ground replay."""
import argparse, base64, datetime, hashlib, io, json, pathlib, re, subprocess
import numpy as np
from PIL import Image
from verify_ground import Document
from water_report import evaluate as water_evaluate, BASE
from rock import decode
from rock_report import evaluate

ROOT=pathlib.Path(__file__).resolve().parents[2]


def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest().upper()


def verify(folder,runs):
    def command(args,name):
        process=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace')
        (folder/name).write_text(process.stdout,encoding='utf-8')
        if process.returncode:raise ValueError('Failed fresh command: '+repr(args))
        return process.stdout
    output=command(['python','-X','utf8','-m','unittest','discover','-s','tools/map-library-prototype','-p','test_*.py','-v'],'unit-output.txt')
    units=int(re.search(r'Ran (\d+) tests',output).group(1))
    command(['dotnet','build','tools/map-library-prototype/PrototypeProbe.csproj','--nologo'],'build-output.txt')
    checks=captures=0
    for run in runs:
        result=read(run/'result.json')
        assert result['ok'] and all(c['ok'] for c in result['checks'])
        assert all(s['provider_calls']==0 for s in result['results'])
        checks+=len(result['checks']);captures+=len(list(run.glob('*-terrain.json')))
        assert read(run/'cleanup.json')['archived']
        for file in run.glob('*-map*.png'):
            with Image.open(file) as image:image.verify()
    rock=evaluate(folder,runs)
    rock=read(folder/'rock-evaluation.json')
    assert len(rock['guards'])==2 and all(c['pass'] for c in rock['guards'])
    assert len(rock['records'])==18 and rock['final_audit_missing']==0 and rock['final_audit_invalid']==0
    # A successful generator invocation is not a source-fidelity certificate.
    # Failed scenes must remain visible and cannot promote a catalog profile.
    assert rock['passed']+rock['failed']==18
    # The underlying observations are independent of the RLE writer/decoder.
    samples=[]
    for entry in read(folder/'catalog.json')['entries']:
        if not entry.get('requires_rock_sidecar'):continue
        source=read(folder/'source-native-final'/(entry['source']['reference_id']+'-terrain.json'))
        original=np.asarray(list(source['cells'])).reshape(source['height'],source['width'])
        requested=decode(read(folder/entry['command'])['rock_layer'])
        assert np.array_equal(requested==2,original=='M')
        assert not ((requested==0) & (original!='N')).any()
        samples.append({'id':entry['id'],'raw_source_rock_cells':int((original=='M').sum()),'all_source_rocks_retained':True})
    water,_=water_evaluate(folder,runs)
    assert water and all(r['pass'] for r in water)
    geometry=read(folder/'transfer-evaluation.json');ground=read(folder/'ground-evaluation.json')
    assert ground['applications']==ground['passing_applications']
    legacy=[]
    for run,name in zip(runs[:3],('transfer-a-native-r2','transfer-b-native-r2','transfer-c-native-r2')):
        previous=BASE/'water-v3'/name
        for ident in ('core-foothills','core-dry-clearing'):
            before=read(previous/(ident+'-terrain.json'));after=read(run/(ident+'-terrain.json'))
            assert before['cells']==after['cells'] and before['terrain_defs']==after['terrain_defs']
            assert digest(previous/(ident+'-map.png'))==digest(run/(ident+'-map.png'))
            legacy.append({'run':run.name,'id':ident,'whole_png_and_terrain_identical':True})
    catalog=read(folder/'catalog.json')
    for entry in catalog['entries']:
        for profile in entry['verified_profiles']:
            if entry.get('requires_rock_sidecar'):
                rows=[r for r in rock['records'] if r['id']==entry['id'] and r['biome']==profile['biome'] and r['size']==profile['map_size']]
                assert rows and all(r['pass'] for r in rows)
    reuse=read(folder/'source-reuse-receipt.json')
    for file in reuse['files']:
        assert hashlib.sha256((folder/'source-native-final'/file['name']).read_bytes()).hexdigest()==file['sha256']
    document=Document();document.feed((folder/'review.html').read_text(encoding='utf-8'))
    for src in document.images:
        assert src.startswith('data:image/png;base64,')
        with Image.open(io.BytesIO(base64.b64decode(src.split(',',1)[1],validate=True))) as image:image.verify()
    hashes={};dev='6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6';stable='9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5'
    for name,path,expected in [('sourceDev',ROOT/'dev/Assemblies/MapGenAI.dll',dev),('installedDev','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll',dev),
            ('dist',ROOT/'dist/Assemblies/MapGenAI.dll',stable),('installedGeneral','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/MapGenAI.dll',stable)]:
        hashes[name]=digest(path);assert hashes[name]==expected
    assert not command(['git','diff','--name-only','21a218f','--','dev','dist'],'product-diff.txt').strip()
    index=read(folder/'index/space.json');vectors=np.load(folder/'index/vectors.npy',allow_pickle=False)
    assert index['catalog_sha256']==hashlib.sha256((folder/'catalog.json').read_bytes()).hexdigest()
    assert index['entry_ids']==[e['id'] for e in catalog['entries']] and vectors.shape==(len(index['entry_ids']),384)
    assert np.isfinite(vectors).all() and np.allclose(np.linalg.norm(vectors,axis=1),1,atol=1e-5)
    receipt={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'unit_tests':units,'native_checks':checks,
        'fresh_generated_maps_including_baselines':captures,'reused_original_gl_maps':6,'source_reuse_files':len(reuse['files']),
        'raw_rock_source_masks':samples,'rock_evidence':'rock-evaluation.json','rock_controls':rock['guards'],
        'strict_detail_passed':rock['passed'],'strict_detail_cases':rock['checks'],
        'strict_detail_failed':rock['failed'],'full_source_ground_passed':rock['same_biome_ground_passed'],
        'full_source_ground_cases':rock['same_biome_ground_checks'],
        'final_capture_audits_present':rock['final_audit_checks'],
        'final_capture_protected_conflicts':rock['final_protected_conflicts'],
        'water_layouts':len(water),'passing_water_layouts':sum(r['pass'] for r in water),
        'ground_applications':ground['applications'],'geometry_passed':geometry['passed'],'geometry_cases':geometry['checks'],
        'legacy_controls':legacy,'available_catalog_entries':[e['id'] for e in catalog['entries'] if e['verified_profiles']],
        'quarantined_entries':[e['id'] for e in catalog['entries'] if not e['verified_profiles']],
        'local_index_shape':list(vectors.shape),'local_index_matches_catalog':True,'product_dll_hashes':hashes,
        'product_sources_unchanged':True,'paid_api_calls':0,'html_images':len(document.images),'html_rendered_in_browser':False,
        'archived_runs':[r.name for r in runs],
        'scope':'Observed complete inland topology only. Native stone/resources retained inside occupancy; roofs, caves, source ore layout, UI, installation and release are not copied.'}
    (folder/'verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--folder',type=pathlib.Path,required=True);parser.add_argument('--runs',nargs='+',required=True)
    args=parser.parse_args();verify(args.folder,[args.folder/name for name in args.runs])
