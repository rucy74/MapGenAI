"""Fresh tests, native evidence, image decode, preservation and release hashes."""
import base64, datetime, hashlib, io, json, pathlib, re, subprocess
from html.parser import HTMLParser
from PIL import Image

ROOT=pathlib.Path(__file__).resolve().parents[2]
FOLDER=ROOT/'docs/analysis/2026-10-01-map-library-prototype/ground-v2'
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def digest(path):return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest().upper()
def command(args,output):
    p=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace')
    (FOLDER/output).write_text(p.stdout,encoding='utf-8')
    if p.returncode:raise ValueError('Failed command: '+repr(args)+'; '+output)
    return p.stdout
class Document(HTMLParser):
    def __init__(self):super().__init__();self.images=[]
    def handle_starttag(self,tag,attrs):
        if tag=='img':self.images.append(dict(attrs)['src'])
def verify():
    tests=command(['python','-X','utf8','-m','unittest','discover','-s','tools/map-library-prototype','-p','test_*.py','-v'],'unit-output.txt')
    unit_count=int(re.search(r'Ran (\d+) tests',tests).group(1))
    command(['dotnet','build','tools/map-library-prototype/PrototypeProbe.csproj','--nologo'],'build-output.txt')
    runs=['source-native-final','transfer-a-native-final','transfer-b-native-final','transfer-c-native-final','extra-native-r3']
    native_checks=0;captures=0
    for name in runs:
        folder=FOLDER/name;result=read(folder/'result.json')
        assert result['ok'] and all(c['ok'] for c in result['checks']);native_checks+=len(result['checks'])
        assert all(r['provider_calls']==0 for r in result['results'])
        for path in folder.glob('*-map*.png'):
            with Image.open(path) as image:image.verify()
        captures+=len(list(folder.glob('*-terrain.json')))
        assert read(folder/'cleanup.json')['archived']
    guard=read(FOLDER/'ground-evaluation.json');assert guard['passing_applications']==guard['applications']
    positive=next(r for r in guard['application_checks'] if r['id']=='ground-guard')
    assert positive['changed']>0
    assert all(n>0 for n in positive['protection_kinds'].values())
    assert any(r['unmapped']>0 for r in guard['application_checks'])
    desert=next(r for r in guard['application_checks'] if r['id']=='desert-lakeside');assert desert['adapted']>0
    for ident in ('missing-ground','unsafe-ground'):
        r=next(r for r in guard['application_checks'] if r['id']==ident)
        assert r['positive_unresolved_cells']>0 and r['changed']==0 and r['final_cells_changed_from_native']==0
    catalog=read(FOLDER/'catalog.json');oasis=next(e for e in catalog['entries'] if e['id']=='gl-oasis')
    assert oasis['status']=='prototype-quarantined' and not oasis['verified_profiles']
    exercised=set();declared=set()
    for e in catalog['entries']:
        layer=read(FOLDER/e['command']).get('ground_layer')
        if layer:declared.update(m['def'] for m in layer['materials'])
    for name in runs:
        for file in (FOLDER/name).glob('*-ground-application.json'):
            exercised.update(d for d,n in read(file)['applied_counts'].items() if n>0)
    assert not declared-exercised, 'Declared ground materials never applied: '+repr(declared-exercised)
    controls=[]
    for run in ('transfer-a','transfer-b','transfer-c'):
        for ident in ('core-foothills','core-dry-clearing'):
            before=read(FOLDER.parent/(run+'-native')/(ident+'-terrain.json'))
            after=read(FOLDER/(run+'-native-final')/(ident+'-terrain.json'))
            assert before['cells']==after['cells'] and before['terrain_defs']==after['terrain_defs']
            assert digest(FOLDER.parent/(run+'-native')/(ident+'-map.png'))==digest(FOLDER/(run+'-native-final')/(ident+'-map.png'))
            controls.append({'run':run,'id':ident,'all_topology_cells_identical':True,'all_named_terrain_counts_identical':True,'complete_preview_png_identical':True})
    doc=Document();doc.feed((FOLDER/'review.html').read_text(encoding='utf-8'))
    for src in doc.images:
        assert src.startswith('data:image/png;base64,')
        with Image.open(io.BytesIO(base64.b64decode(src.split(',',1)[1],validate=True))) as image:image.verify()
    hashes={};dev='6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6';release='9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5'
    for name,path,expected in [('sourceDev',ROOT/'dev/Assemblies/MapGenAI.dll',dev),('installedDev','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll',dev),
        ('dist',ROOT/'dist/Assemblies/MapGenAI.dll',release),('installedGeneral','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/MapGenAI.dll',release)]:
        hashes[name]=digest(path);assert hashes[name]==expected
    diff=subprocess.run(['git','diff','--name-only','a0bc390','--','dev','dist'],cwd=ROOT,stdout=subprocess.PIPE,encoding='utf-8',check=True)
    assert not diff.stdout.strip()
    receipt={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'unit_tests':unit_count,'native_checks':native_checks,'captured_native_maps_including_baselines':captures,
             'passing_ground_applications':guard['passing_applications'],'same_biome_fidelity_checks':guard['same_biome_fidelity_checks'],
             'same_biome_fidelity_range':[min(r['source_named_ground_agreement'] for r in guard['records'] if r['same_biome']),max(r['source_named_ground_agreement'] for r in guard['records'] if r['same_biome'])],
             'exercised_source_ground_materials':sorted(declared),'html_embedded_images':len(doc.images),'html_rendered_in_browser':False,
             'unchanged_legacy_controls':controls,'product_dll_hashes':hashes,'product_sources_unchanged':True,'paid_api_calls':0,
             'archived_final_runs':runs,'scope':'Ground prototype only. Existing headless Plant.Print cleanup warnings and oasis geometry failures are not claimed fixed.'}
    (FOLDER/'verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False))
if __name__=='__main__':verify()
