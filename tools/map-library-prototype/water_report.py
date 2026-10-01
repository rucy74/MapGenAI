"""Prepare, independently measure and show the complete water-layout prototype."""
import argparse, base64, datetime, hashlib, html, json, pathlib, shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from water import water_layer, decode
from build_catalog import build, write
from ground_report import evaluate as measure_ground, picture
from measure_transfer import compare, scaled, measure as measure_geometry

ROOT=pathlib.Path(__file__).resolve().parents[2]
BASE=ROOT/'docs/analysis/2026-10-01-map-library-prototype'

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))

def prepare(folder):
    if folder.exists():raise ValueError('Fresh output folder required')
    folder.mkdir(parents=True);source=folder/'source-native-final';source.mkdir()
    previous=BASE/'ground-v2/source-native-final';copied=[]
    for path in previous.iterdir():
        if path.name in ('result.json','native-terrain-palette.json') or path.name.endswith(('-terrain.json','-map.png','-map-default.png')):
            shutil.copy2(path,source/path.name);copied.append({'name':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    shutil.copy2(BASE/'ground-v2/gl-inventory.json',folder/'gl-inventory.json')
    write(folder/'source-reuse-receipt.json',{'original':'../ground-v2/source-native-final','source_commit':'5cada7f',
        'policy':'Previously generated actual GL references, byte-for-byte reused; not new source generation','files':copied})
    build(folder,source)
    soil={'def':'Soil','supported':True,'water':False,'river':False,'temporary':False,'fertility':1}
    shallow={**soil,'def':'WaterShallow','water':True};deep={**soil,'def':'WaterDeep','water':True}
    cells=np.full((100,100),'G');names=np.full((100,100),'Soil',dtype=object)
    cells[45:61,45:61]='S';names[45:61,45:61]='WaterShallow'
    cells[53:57,53:57]='W';names[53:57,53:57]='WaterDeep'
    cells[4:7,4:7]='N';names[4:7,4:7]=''
    layer,_=water_layer(cells,names,{'terrains':[soil,shallow,deep]},'TemperateForest')
    write(folder/'recipes/water-guard.json',{'action':'generate','params':{},'water_layer':layer})
    unknown=dict(layer,runs=[])
    write(folder/'recipes/unknown-water.json',{'action':'generate','params':{},'water_layer':unknown})
    write(folder/'water-controls-manifest.json',{'world_seed':'map-library-water-controls-20261001','cases':[
        {'id':'water-guard','biome':'TemperateForest','size':100,'command':'recipes/water-guard.json'},
        {'id':'unknown-water','biome':'TemperateForest','size':100,'command':'recipes/unknown-water.json'}]})
    print(json.dumps({'folder':str(folder),'source_files_reused':len(copied),'catalog_entries':len(read(folder/'catalog.json')['entries'])}))

def evaluate(folder,runs):
    catalog=read(folder/'catalog.json');entries={e['id']:e for e in catalog['entries']};rows=[];controls=[]
    for run in runs:
        result=read(run/'result.json')
        if not result['ok']:raise ValueError('Incomplete run: '+str(run))
        for scene in result['results']:
            ident=scene['id'];file=run/(ident+'-water-application.json')
            if not file.exists():continue
            application=read(file);safe=all(application[k]==0 for k in ('protected_changes','unknown_changes','outside_water_height_changes','known_source_mismatches'))
            if ident in ('water-guard','unknown-water'):
                control={'id':ident,'run':run.name,'pass':safe,**application}
                if ident=='water-guard':
                    control['pass'] &= all(n>0 for n in application['protection_kinds'].values())
                    control['pass'] &= all(application[k]>0 for k in ('added_water_cells','cleared_ordinary_pond_cells','depth_changed_cells','unknown_cells','protected_conflicts'))
                    actual=read(run/(ident+'-terrain.json'));table=np.asarray(actual['terrain_table']);terrain=table[np.asarray(actual['terrain_indices']).reshape(100,100)]
                    control['unknown_fixture_preserved']=terrain[5,5]=='WaterShallow';control['pass'] &= control['unknown_fixture_preserved']
                else:
                    after=read(run/(ident+'-terrain.json'));before=read(run/(ident+'-baseline-terrain.json'))
                    def expanded(data):return np.asarray(data['terrain_table'])[data['terrain_indices']]
                    control['native_terrain_identical']=bool(np.array_equal(expanded(after),expanded(before)))
                    control['native_png_identical']=(run/(ident+'-map.png')).read_bytes()==(run/(ident+'-baseline-map.png')).read_bytes()
                    control['pass'] &= control['native_terrain_identical'] and control['native_png_identical']
                if not control['pass']:raise ValueError('Positive protection/control failed: '+repr(control))
                controls.append(control);continue
            entry=entries[ident];reference=folder/entry['source']['reference_run']/(entry['source']['reference_id']+'-terrain.json')
            source=read(reference);actual=read(run/(ident+'-terrain.json'))
            truth=np.asarray(list(source['cells'])).reshape(source['height'],source['width'])
            actual_cells=np.asarray(list(actual['cells'])).reshape(scene['size'],scene['size'])
            wet=scaled(np.isin(truth,['S','W']),scene['size']);target=np.isin(actual_cells,['S','W'])
            metrics={key:compare(scaled(truth==label,scene['size']),actual_cells==label) for key,label in (('shallow','S'),('deep','W'))}
            metrics['wet']=compare(wet,target)
            water_pass=safe and application['protected_conflicts']==0 and all(v is None or v['iou']>=.98 for v in metrics.values())
            # Quantify displacement independently of the recipe RLE/contours.
            expected_xy=np.argwhere(wet);actual_xy=np.argwhere(target)
            centroid=float(np.linalg.norm(expected_xy.mean(axis=0)-actual_xy.mean(axis=0))) if len(expected_xy) and len(actual_xy) else None
            row={'id':ident,'run':run.name,'biome':scene['biome'],'size':scene['size'],'pass':bool(water_pass),
                 'metrics':metrics,'water_centroid_error_cells':centroid,'unexpected_water_cells':int((target & ~wet).sum()),
                 'missing_water_cells':int((wet & ~target).sum()),**application}
            rows.append(row)
    write(folder/'water-evaluation.json',{'records':rows,'controls':controls,'passing_layouts':sum(r['pass'] for r in rows),'layouts':len(rows),
        'protocol':'Independent original per-cell water and depth, including tiny pools. IoU >=0.98; protected conflicts reject; no paired-native-pond union for admission.'})
    return rows,controls

def render(folder,runs):
    rows,controls=evaluate(folder,runs)
    catalog=read(folder/'catalog.json');entries={e['id']:e for e in catalog['entries']}
    source=folder/'source-native-final';prior=BASE/'ground-v2/transfer-a-native-final';current=runs[0]
    doc='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MapGen AI · 물 위치 비교</title><style>body{margin:0;background:#14201f;color:#e6ede5;font:17px/1.65 system-ui}main{max-width:1150px;margin:auto;padding:24px}h1{font-size:30px}h2{font-size:23px}section{background:#1c2c29;padding:20px;margin:20px 0;border-radius:12px}.row{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}img{width:100%;image-rendering:pixelated}figure{margin:0}figcaption{font-size:14px;color:#c4d5c8}a{color:#a0dacc}table{width:100%;border-collapse:collapse;font-size:14px}td,th{padding:8px;border-bottom:1px solid #365249;text-align:left}strong{color:#f2d391}</style><main><h1>산·물·바닥을 같은 구도로 옮기기</h1><p>원본의 작은 연못까지 위치와 수심을 함께 저장합니다. 원본에서 확인된 마른 곳의 일반 연못을 정리하고, 모르는 영역과 강·바다·온천 및 연결된 물, 길·건물은 보호합니다. 보호 대상과 충돌하면 후보로 승인하지 않습니다.</p><p><strong>개발자 저장소 프로토타입입니다.</strong> 게임 추천창·제품 DLL·배포판에는 아직 연결하지 않았습니다. 원본은 직전 실제 GL 캡처를 재사용했고, 수정 결과는 이번에 실제 RimWorld에서 생성했습니다.</p>'''
    order=['gl-lake','gl-valley','gl-cliff','gl-lone-mountain','gl-archipelago','gl-oasis']
    for ident in order:
        row=next(r for r in rows if r['id']==ident and r['run']==current.name)
        doc+='<section><h2>'+html.escape(entries[ident]['title_ko'])+'</h2><div class="row">'+picture(source/(ident+'-map.png'),'GL 원본')+picture(prior/(ident+'-map.png'),'이전 · 물 위치가 다름')+picture(current/(ident+'-map.png'),'이번 · 물 위치 함께 적용')+'</div>'
        metric=row['metrics']['wet'];doc+='<p>물 위치 IoU '+(f"{metric['iou']:.2%}" if metric else '원본 물 없음')+' · 위치 중심 차이 '+(f"{row['water_centroid_error_cells']:.2f}칸" if row['water_centroid_error_cells'] is not None else '해당 없음')+' · 보호 대상 충돌 '+str(row['protected_conflicts'])+'칸 · '+('검사 통과' if row['pass'] else '후보 제외')+'</p></section>'
    doc+='<section><h2>다른 크기·바이옴과 이미지 입력</h2><div class="row">'+picture(runs[1]/'gl-valley-map.png','같은 구도 · 300칸')+picture(runs[2]/'gl-lake-map.png','건조관목림 · 250칸')+picture(current/'image-lake-map.png','확실한 이미지 물·바닥만')+'</div></section>'
    doc+='<section><h2>보호 검사</h2><div class="row">'+picture(runs[-1]/'water-guard-map.png','검사용 강·바다·온천·길·건물 보호')+picture(runs[-1]/'unknown-water-map.png','입력이 불명인 경우 원래 지도 유지')+'</div><p>보호 검사용 지도는 미관 예시가 아닙니다. 원본 구도와 충돌한 보호 지형을 그대로 남기는지 확인했습니다.</p></section>'
    doc+='<section><h2>전체 실제 비교 결과</h2><table><tr><th>원본</th><th>조건</th><th>물 IoU</th><th>충돌</th><th>후보 검사</th></tr>'
    for row in rows:
        metric=row['metrics']['wet'];doc+='<tr><td>'+html.escape(row['id'])+'</td><td>'+html.escape(row['biome'])+' '+str(row['size'])+'</td><td>'+ (f"{metric['iou']:.2%}" if metric else '해당 없음')+'</td><td>'+str(row['protected_conflicts'])+'</td><td>'+('통과' if row['pass'] else '제외')+'</td></tr>'
    doc+='</table><p>전체 복사율·미관 점수는 아닙니다. 산은 현재 생성기의 윤곽 명령을 사용하므로 작은 암석·동굴·자원·건물·이벤트와 원본의 절차적 다양성까지 재현하지는 않습니다. 부분 편집/기존 강·해안 타일의 구도 이식은 계속 제외합니다. HTML 브라우저 렌더는 미검증이며 내장 PNG 디코드와 실제 지도 관찰을 구분합니다.</p><p>GL 원본 및 파생 지도: m00nl1ght · CC BY-NC-SA 4.0 · <a href="../ATTRIBUTION.md">출처 및 귀속</a></p></section></main>'
    (folder/'review.html').write_text(doc,encoding='utf-8')
    font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',18);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',14)
    canvas=Image.new('RGB',(1014,880),'#14201f');draw=ImageDraw.Draw(canvas)
    for n,ident in enumerate(order[:3]):
        y=12+n*290;draw.text((16,y),entries[ident]['title_ko'],font=font,fill='#e6ede5')
        for col,(directory,label) in enumerate(((source,'GL 원본'),(prior,'이전 · 물 위치 미이식'),(current,'이번 · 물 위치 포함'))):
            x=16+col*332;im=Image.open(directory/(ident+'-map.png')).convert('RGB').resize((228,228),Image.Resampling.NEAREST)
            canvas.paste(im,(x,y+32));draw.text((x,y+265),label,font=small,fill='#c4d5c8')
    canvas.save(folder/'comparison.png')
    return rows,controls

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);p.add_argument('--prepare',action='store_true');p.add_argument('--runs',nargs='+');args=p.parse_args()
    if args.prepare:prepare(args.folder)
    else:
        runs=[args.folder/name for name in args.runs];measure_geometry(args.folder,runs[:-1]);measure_ground(args.folder,runs[:-1]);rows,controls=render(args.folder,runs)
        print(json.dumps({'water_layouts':len(rows),'passing':sum(r['pass'] for r in rows),'controls':len(controls)}))
