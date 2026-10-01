"""Fresh tool/build/artifact receipts, with known quality failures kept visible."""
import base64, datetime, hashlib, io, json, pathlib, re, subprocess
from html.parser import HTMLParser
from PIL import Image
from retrieve import read_index

ROOT=pathlib.Path(__file__).resolve().parents[2]
FOLDER=ROOT/'docs/analysis/2026-10-01-map-library-prototype'
EXPECTED={'DEV':'6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6',
          'release':'9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5'}
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def command(args,receipt):
    process=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace')
    (FOLDER/receipt).write_text(process.stdout,encoding='utf-8')
    if process.returncode:raise ValueError('Command failed: '+repr(args))
    return process.stdout

class Document(HTMLParser):
    def __init__(self):super().__init__();self.ids=set();self.anchors=[];self.images=[]
    def handle_starttag(self,tag,attrs):
        attrs=dict(attrs)
        if 'id' in attrs:self.ids.add(attrs['id'])
        if tag=='a' and attrs.get('href','').startswith('#'):self.anchors.append(attrs['href'][1:])
        if tag=='img':self.images.append(attrs['src'])

def main():
    tests=command(['python','-X','utf8','-m','unittest','discover','-s','tools/map-library-prototype','-p','test_prototype.py','-v'],'unit-output.txt')
    build=command(['dotnet','build','tools/map-library-prototype/PrototypeProbe.csproj','--nologo'],'build-output.txt')
    catalog,meta,vectors=read_index(FOLDER/'catalog.json',FOLDER/'index')
    doc=Document();doc.feed((FOLDER/'review.html').read_text(encoding='utf-8'))
    assert all(anchor in doc.ids for anchor in doc.anchors)
    for src in doc.images:
        assert src.startswith('data:image/png;base64,')
        with Image.open(io.BytesIO(base64.b64decode(src.split(',',1)[1],validate=True))) as im:im.verify()
    links=[]
    for path in [FOLDER/'report.md',FOLDER/'ATTRIBUTION.md',ROOT/'tools/map-library-prototype/README.md']:
        for target in re.findall(r'\]\(([^)]+)\)',path.read_text(encoding='utf-8')):
            if target.startswith(('https:','http:','#')):continue
            assert (path.parent/target.split('#',1)[0]).exists(),(path,target)
            links.append(target)
    runs=['source-final','transfer-a-native','transfer-b-native','transfer-c-native','request-example-native'];runtime_checks=0;native_pngs=0
    for name in runs:
        result=read(FOLDER/name/'result.json');assert result['ok'] and all(c['ok'] for c in result['checks'])
        runtime_checks+=len(result['checks']);native_pngs+=len(list((FOLDER/name).glob('*-map.png')))
    cleanups=[]
    for launch_path in sorted(FOLDER.glob('*/launch.json')):
        cleanup=read(launch_path.parent/'cleanup.json');assert cleanup['archived']
        assert not pathlib.Path(cleanup['mod']).exists() and pathlib.Path(cleanup['archive']).exists()
        cleanups.append(launch_path.parent.name)
    hashes={}
    for name,path,kind in [('installedDev','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll','DEV'),
            ('sourceDev',ROOT/'dev/Assemblies/MapGenAI.dll','DEV'),('installedGeneral','G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/MapGenAI.dll','release'),
            ('dist',ROOT/'dist/Assemblies/MapGenAI.dll','release')]:
        actual=hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest().upper();assert actual==EXPECTED[kind];hashes[name]=actual
    product_diff=subprocess.check_output(['git','status','--porcelain','--','dev','dist'],cwd=ROOT,text=True)
    assert product_diff.strip()=='',product_diff
    transfer=read(FOLDER/'transfer-evaluation.json');retrieval=read(FOLDER/'retrieval-evaluation.json')
    receipt={'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'unit_tests':int(re.search(r'Ran (\d+) tests',tests)[1]),'unit_pass':True,'build_pass':True,
        'native_execution_checks':runtime_checks,'native_maps_with_baselines':native_pngs,'html_image_elements_decoded':len(doc.images),'html_anchor_count':len(doc.anchors),
        'local_markdown_links':len(links),'browser_render_verified':False,'source_geometry':{'passed':transfer['source_comparison_passed'],'cases':transfer['source_comparison_cases']},
        'retrieval_smoke':{'passed':retrieval['passed'],'cases':retrieval['checks'],'top1':retrieval['top1_matches'],'top1_cases':retrieval['top1_cases']},
        'available':[e['id'] for e in catalog['entries'] if e['verified_profiles']],'quarantined':[e['id'] for e in catalog['entries'] if not e['verified_profiles']],
        'archived_owned_runs':cleanups,'product_dll_hashes':hashes,'product_sources_unchanged':True,'paid_api_calls':0,
        'limits':['Oasis area accuracy failure retained and excluded','Native world-water/road profiles untested and excluded','Beauty not approved','No UI integration or product installation']}
    (FOLDER/'verification.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(receipt,ensure_ascii=False))

if __name__=='__main__':main()
