"""Fresh verification for the developer whole-composition water sidecar."""
import argparse, base64, datetime, hashlib, io, json, pathlib, re, subprocess
import numpy as np
from PIL import Image
from verify_ground import Document
from water_report import evaluate, BASE
from water import decode

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
    unit_count=int(re.search(r'Ran (\d+) tests',output).group(1))
    command(['dotnet','build','tools/map-library-prototype/PrototypeProbe.csproj','--nologo'],'build-output.txt')
    checks=0;captures=0
    for run in runs:
        result=read(run/'result.json');assert result['ok'] and all(c['ok'] for c in result['checks'])
        assert all(r['provider_calls']==0 for r in result['results']);checks+=len(result['checks'])
        captures+=len(list(run.glob('*-terrain.json')))
        assert read(run/'cleanup.json')['archived']
        for file in run.glob('*-map*.png'):
            with Image.open(file) as image:image.verify()
    rows,controls=evaluate(folder,runs)
    assert len(controls)==2 and all(c['pass'] for c in controls)
    assert any(r['cleared_ordinary_pond_cells']>0 for r in rows)
    assert all(r['protected_changes']==r['unknown_changes']==r['outside_water_height_changes']==r['known_source_mismatches']==0 for r in rows)
    guard=next(c for c in controls if c['id']=='water-guard')
    assert all(n>0 for n in guard['protection_kinds'].values()) and guard['protected_conflicts']>0 and guard['unknown_fixture_preserved']
    catalog=read(folder/'catalog.json');geometry=read(folder/'transfer-evaluation.json');ground=read(folder/'ground-evaluation.json')
    index=read(folder/'index/space.json');vectors=np.load(folder/'index/vectors.npy',allow_pickle=False)
    assert index['catalog_sha256']==hashlib.sha256((folder/'catalog.json').read_bytes()).hexdigest()
    assert index['entry_ids']==[e['id'] for e in catalog['entries']] and vectors.shape==(len(index['entry_ids']),384)
    assert np.isfinite(vectors).all() and np.allclose(np.linalg.norm(vectors,axis=1),1,atol=1e-5)
    assert ground['applications']==ground['passing_applications']
    for entry in catalog['entries']:
        if entry.get('requires_water_sidecar'):
            for profile in entry['verified_profiles']:
                matching=[r for r in rows if r['id']==entry['id'] and r['biome']==profile['biome'] and r['size']==profile['map_size']]
                assert matching and all(r['pass'] for r in matching)
    # Re-read the independently captured source, not the converter's polygons.
    observed_small=[]
    from skimage.measure import label, regionprops
    for ident in ('gl-valley','gl-cliff','gl-lone-mountain'):
        source=read(folder/'source-native-final'/(ident+'-terrain.json'))
        truth=np.asarray(list(source['cells'])).reshape(source['height'],source['width']);wet=np.isin(truth,['W','S'])
        requested=decode(read(folder/'recipes'/(ident+'.json'))['water_layer'])>=2
        assert np.array_equal(requested,wet)
        small=sum(int(r.area) for r in regionprops(label(wet)) if r.area<max(40,int(wet.size*.0015)))
        assert small>0
        observed_small.append({'id':ident,'previously_omitted_small_pool_cells':small,'all_source_pool_cells_in_water_layer':True})
    legacy=[]
    for run in runs[:3]:
        previous=BASE/'ground-v2'/re.sub(r'-r\d+$','-final',run.name)
        for ident in ('core-foothills','core-dry-clearing'):
            before=read(previous/(ident+'-terrain.json'));after=read(run/(ident+'-terrain.json'))
            assert before['cells']==after['cells'] and before['terrain_defs']==after['terrain_defs']
            assert digest(previous/(ident+'-map.png'))==digest(run/(ident+'-map.png'))
            legacy.append({'run':run.name,'id':ident,'whole_png_and_terrain_identical':True})
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
    diff=command(['git','diff','--name-only','5cada7f','--','dev','dist'],'product-diff.txt');assert not diff.strip()
    receipt={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'unit_tests':unit_count,'native_checks':checks,'fresh_generated_maps_including_baselines':captures,
        'reused_original_gl_maps':6,'source_reuse_files':len(reuse['files']),'water_layouts':len(rows),'passing_water_layouts':sum(r['pass'] for r in rows),
        'local_index_shape':list(vectors.shape),'local_index_matches_catalog':True,
        'positive_controls':controls,'small_pool_examples':observed_small,'ground_applications':ground['applications'],'geometry_passed':geometry['passed'],'geometry_cases':geometry['checks'],
        'available_catalog_entries':[e['id'] for e in catalog['entries'] if e['verified_profiles']],
        'quarantined_entries':[e['id'] for e in catalog['entries'] if not e['verified_profiles']],
        'passing_water_iou_range':[min(r['metrics']['wet']['iou'] for r in rows if r['pass'] and r['metrics']['wet']),max(r['metrics']['wet']['iou'] for r in rows if r['pass'] and r['metrics']['wet'])],
        'legacy_controls':legacy,'product_dll_hashes':hashes,'product_sources_unchanged':True,'paid_api_calls':0,'html_images':len(document.images),'html_rendered_in_browser':False,
        'archived_runs':[r.name for r in runs],'scope':'Developer complete inland composition; native protected conflicts cause quarantine. Not procedural GL conversion, UI integration, release or aesthetic approval.'}
    (folder/'verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--folder',type=pathlib.Path,required=True);parser.add_argument('--runs',nargs='+',required=True);args=parser.parse_args()
    verify(args.folder,[args.folder/name for name in args.runs])
