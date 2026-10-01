"""Independent ground checks and mobile-friendly native image comparison."""
import argparse, base64, hashlib, html, json, pathlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from ground import read_image_materials, image_palette, ground_layer, role

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path,value):path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def names(path):
    data=read(path)
    return data,np.asarray(data['terrain_table'])[np.asarray(data['terrain_indices']).reshape(data['height'],data['width'])]
def scale(array,size):
    indexes=np.minimum((np.arange(size)*len(array)/size).astype(int),len(array)-1)
    return array[np.ix_(indexes,indexes)]
def plane(layer):
    values=np.full(layer['width']*layer['height'],'',dtype=object)
    for run in layer['runs']:values[run['start']:run['start']+run['length']]=layer['materials'][run['material']-1]['def']
    return values.reshape(layer['height'],layer['width'])
def prepare_extra(folder):
    native=read(folder/'source-native-final/native-terrain-palette.json')
    write(folder/'minimap-default-palette.json',image_palette(native,'default'))
    cells,materials,receipt=read_image_materials(folder/'source-native-final/gl-lake-map-default.png',folder/'minimap-default-palette.json')
    from contours import export
    command,loss=export(cells,'default_image_lake',mountains=False)
    layer,ground_receipt=ground_layer(materials,cells,native,'TemperateForest')
    command['ground_layer']=layer;write(folder/'recipes/image-default-lake.json',command)
    truth,truth_names=names(folder/'source-native-final/gl-lake-terrain.json')
    known=materials!='';ground_match=float((materials[known]==truth_names[known]).mean()) if known.any() else None
    write(folder/'image-default-evaluation.json',{**receipt,**loss,**ground_receipt,'named_ground_precision':ground_match,'held_out':'gl-lake'})
    cases=[{'id':'image-default-lake','biome':'TemperateForest','size':250,'command':'recipes/image-default-lake.json'},
           {'id':'desert-lakeside','biome':'Desert','size':250,'command':'recipes/gl-lake.json'}]
    for ident,material in [('missing-ground','MapLibrary_NotInstalled'),('unsafe-ground','WaterDeep'),('ground-guard','SoilRich')]:
        command={'action':'generate','params':{},'ground_layer':{'schema_version':1,'source_biome':'TemperateForest','width':100,'height':100,
                 'row_order':'south-first','materials':[{'def':material,'role':'fertile'}],'runs':[{'start':0,'length':10000,'material':1}]}}
        write(folder/'recipes'/(ident+'.json'),command)
        cases.append({'id':ident,'biome':'TemperateForest','size':100,'command':'recipes/'+ident+'.json'})
    write(folder/'extra-manifest.json',{'world_seed':'map-library-ground-positive-controls-20261001','cases':cases})
    print(json.dumps({'extra_cases':len(cases),'default_ground_precision':ground_match,'default_unknown':receipt['unknown_fraction']}))

def evaluate(folder,runs):
    native=read(folder/'source-native-final/native-terrain-palette.json');defs={d['def']:d for d in native['terrains']}
    records=[];checks=[]
    for run in runs:
        result=read(run/'result.json')
        if not result['ok']:raise ValueError('Incomplete native generation: '+str(run))
        for scene in result['results']:
            ident=scene['id'];application=run/(ident+'-ground-application.json')
            if not application.exists():continue
            receipt=read(application);check={'run':run.name,'id':ident,'changed':receipt['changed_cells'],'protected':receipt['protected_cells'],
                   'protected_changes':receipt['protected_changes'],'unmapped_changes':receipt['unmapped_changes'],
                   'elevation_changes':receipt['elevation_changes'],'unmapped':receipt['unmapped_cells'],'adapted':receipt['adapted_cells'],
                   'preserved_unresolved':receipt['preserved_unresolved_cells'],'protection_kinds':receipt['protection_kinds']}
            check['pass']=check['protected_changes']==check['unmapped_changes']==check['elevation_changes']==0;checks.append(check)
            if ident in ('missing-ground','unsafe-ground'):
                actual,actual_names=names(run/(ident+'-terrain.json'));_,baseline=names(run/(ident+'-baseline-terrain.json'))
                unchanged=int((actual_names!=baseline).sum())
                if receipt['changed_cells']!=0 or receipt['preserved_unresolved_cells']<=0 or unchanged:
                    raise ValueError('Missing/unsafe ground must keep complete native terrain: '+ident)
                check.update({'positive_unresolved_cells':receipt['preserved_unresolved_cells'],'final_cells_changed_from_native':unchanged})
                continue
            source_id='gl-lake' if ident in ('image-lake','image-default-lake','desert-lakeside') else ident
            source_file=folder/'source-native-final'/(source_id+'-terrain.json')
            if not source_file.exists():continue
            source,source_names=names(source_file);actual,actual_names=names(run/(ident+'-terrain.json'))
            truth=scale(source_names,scene['size']);source_ground=scale(np.asarray(list(source['cells'])).reshape(source_names.shape)=='G',scene['size'])
            # Independent source TerrainDefs, not the converter's own output.
            safe={d['def'] for d in native['terrains'] if d['supported'] and not d['water'] and not d['temporary'] and not d['dangerous'] and role(d)!='unknown'}
            eligible=source_ground & np.isin(truth,list(safe)) & np.isin(actual_names,list(safe))
            eligible &= np.asarray(list(actual['cells'])).reshape(actual_names.shape)=='G'
            recipe_id='gl-lake' if ident=='desert-lakeside' else ident
            requested=plane(read(folder/'recipes'/(recipe_id+'.json'))['ground_layer'])
            eligible &= scale(requested!='',scene['size'])
            same=source['biome']==scene['biome']
            fidelity=float((truth[eligible]==actual_names[eligible]).mean()) if eligible.any() else None
            row={'run':run.name,'id':ident,'biome':scene['biome'],'size':scene['size'],'same_biome':same,
                 'source_reference_cells':int(eligible.sum()),'source_named_ground_agreement':fidelity,
                 'fidelity_pass':fidelity>=.95 if same and fidelity is not None else None,
                 'scope':'Independent per-cell source ground within imported dry masks; actual protected water/rock/constructed floors excluded. Cross-biome adaptation is not exact-copy fidelity.'}
            if same and not row['fidelity_pass']:raise ValueError('Ground fidelity below 95%: '+str(row))
            records.append(row)
    image=read(folder/'image-evaluation.json')
    cells,materials,_=read_image_materials(folder/'source-native-final/gl-lake-map.png',folder/'minimap-palette.json')
    _,truth=names(folder/'source-native-final/gl-lake-terrain.json');known=materials!=''
    image['named_ground_precision']=float((materials[known]==truth[known]).mean());write(folder/'image-evaluation.json',image)
    report={'records':records,'application_checks':checks,'passing_applications':sum(c['pass'] for c in checks),'applications':len(checks),
            'same_biome_fidelity_passes':sum(r['fidelity_pass'] is True for r in records),
            'same_biome_fidelity_checks':sum(r['same_biome'] for r in records),'image_named_ground_precision':image['named_ground_precision'],
            'scope':'Developer ground sidecar, independently compared to source TerrainDefs. Neither general vision nor product integration nor aesthetic approval.'}
    write(folder/'ground-evaluation.json',report)
    return report

def picture(path,label):
    encoded=base64.b64encode(path.read_bytes()).decode()
    return '<figure><a href="data:image/png;base64,'+encoded+'" target="_blank"><img src="data:image/png;base64,'+encoded+'" alt="'+html.escape(label)+'"></a><figcaption>'+html.escape(label)+'</figcaption></figure>'

def review(folder,runs,report):
    base=folder.parent;source=folder/'source-native-final';entries={e['id']:e for e in read(folder/'catalog.json')['entries']}
    doc='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MapGen AI · 바닥 보존 비교</title><style>body{margin:0;background:#14201f;color:#e6ede5;font:17px/1.65 system-ui}main{max-width:1150px;margin:auto;padding:24px}h1{font-size:30px}h2{font-size:23px}section{background:#1c2c29;padding:20px;margin:20px 0;border-radius:12px}.row{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px}img{width:100%;image-rendering:pixelated}figure{margin:0}figcaption{font-size:14px;color:#c4d5c8}a{color:#a0dacc}table{border-collapse:collapse;width:100%;font-size:14px}td,th{border-bottom:1px solid #365249;padding:7px;text-align:left}code{overflow-wrap:anywhere}.swatches{display:flex;flex-wrap:wrap;gap:9px}.swatch{padding:5px 9px;border-left:18px solid var(--color);background:#20362e}details{margin:12px 0}summary{cursor:pointer}strong{color:#f2d391}</style><main><h1>맵의 바닥까지 가져오기</h1><p>산·물 윤곽에 이어 흙·비옥한 토양·모래·자갈·진흙·바위 바닥의 위치를 읽습니다. 같은 바이옴은 바닥 이름을 보존하고, 다른 바이옴은 역할에 맞게 조정합니다. 물·바위·건물·길과 높이는 바닥 적용 중 보호합니다.</p><p><strong>개발자 프로토타입입니다.</strong> 제품 채팅·이미지 입력·설치 DLL·배포판은 변경하지 않았습니다. 모든 지도는 실제 RimWorld 생성 결과이며 Map Preview 자체 색으로 그렸습니다. 일반 색상과 실제 토양 색상을 혼합하지 않습니다.</p>'''
    doc+='<section><h2>이전 결과와 비교</h2><p>왼쪽부터 GL 원본, 이전 윤곽 이식, 바닥을 포함한 이식입니다. 이전/새 결과의 타일·시드가 같은 사례를 사용합니다. 원래 있던 바위와 물을 보호하므로 완전한 복사 지도는 아닙니다.</p>'
    for ident in ('gl-lake','gl-valley','gl-cliff','gl-archipelago'):
        doc+='<h3>'+entries[ident]['title_ko']+'</h3><div class="row">'+picture(source/(ident+'-map.png'),'GL 원본 · 실제 토양 색')+picture(base/'transfer-a-native'/(ident+'-map.png'),'이전 · 기본 바닥 유지')+picture(runs[0]/(ident+'-map.png'),'이번 · 바닥 위치 포함')+'</div>'
        doc+='<details><summary>익숙한 일반 미리보기 색상으로 보기</summary><div class="row">'+picture(source/(ident+'-map-default.png'),'GL 원본 · 일반 미리보기')+picture(runs[0]/(ident+'-map-default.png'),'이번 · 일반 미리보기')+'</div></details>'
    doc+='</section><section><h2>이미지만 있을 때</h2><p>활성 지형에서 실제 미리보기 색을 읽습니다. 같은 색을 쓰는 서로 다른 바닥, 그림자와 구분이 안 되는 픽셀, 색이 없는 바닥은 제외합니다. 해당 칸에는 현재 타일의 바닥이 남습니다. 아래 호수는 색 보정용 지도에 포함되지 않은 검증 사례입니다.</p><div class="row">'+picture(source/'gl-lake-map.png','입력 이미지')+picture(runs[0]/'image-lake-map.png','확실한 물·바닥만 이식')+'</div>'
    image=read(folder/'image-evaluation.json');default=read(folder/'image-default-evaluation.json')
    doc+='<p>이 호수의 확실하다고 판정한 바닥 픽셀 이름 일치: '+f"{image['named_ground_precision']:.1%}"+' · 제외한 픽셀: '+f"{image['unknown_fraction']:.1%}"+'. 한 사례의 측정이며 모든 이미지의 정확도가 아닙니다.</p><p><strong>일반 색상에는 흙·자갈·이끼 바닥이 같은 RGB로 표시됩니다.</strong> 색상표를 늘려도 원래 재료를 구분할 수 없어 이번 일반 색상 입력의 '+f"{default['unknown_fraction']:.1%}"+'를 이식하지 않았습니다. 확실한 일부 물·모래·진흙·비옥한 토양만 읽습니다. 바닥까지 충실하게 가져오려면 직접 TerrainDef 데이터나 실제 토양 색상 이미지가 필요합니다. 사진·UI·JPEG·다른 모드 팔레트·색상 모드 혼합은 미검증입니다.</p></section>'
    extra=runs[-1]
    doc+='<section><h2>다른 바이옴으로 옮길 때</h2><p>건조관목림에서는 원래 기본 토양과 바위 바닥을 유지하고, 물가의 젖은 바닥을 실제 물 가까이로 제한합니다. 사막으로 옮긴 숲의 호숫가는 진흙/습지 패치를 모래로 바꾸고, 넓은 흙밭·비옥한 토양을 자동으로 만들지 않습니다.</p><div class="row">'+picture(runs[2]/'gl-lake-map-default.png','건조관목림 · 호숫가')+picture(extra/'desert-lakeside-map-default.png','사막 · 호숫가')+'</div></section>'
    palette=read(source/'native-terrain-palette.json');doc+='<section><h2>바닥 색상 목록</h2><p>이번 격리 프로필에서 활성 지형 '+str(len(palette['terrains']))+'종을 조회했습니다. 모든 지형을 자동 이식하는 뜻은 아닙니다. 지을 수 있는 바닥·도로·특수 물·임시 바닥은 별도로 취급합니다.</p><div class="swatches">'
    for name in ('Soil','SoilRich','Sand','SoftSand','Gravel','Mud','MarshyTerrain','Granite_Rough'):
        t=next(t for t in palette['terrains'] if t['def']==name);rgb=t['rgb'];color='#'+''.join(f'{v:02x}' for v in rgb)
        doc+='<span class="swatch" style="--color:'+color+'">'+html.escape(t['label'])+' · '+name+'</span>'
    doc+='</div><p>등록된 TerrainDef에 색이 없으면 팔레트에서 unknown으로 남깁니다. TerrainDef 자체가 없거나 바닥용으로 사용할 수 없으면 실제 생성에서도 칠하지 않습니다. 직접 데이터 경로는 미리보기 색이 없어도 바닥 이름을 읽을 수 있습니다.</p></section>'
    doc+='<section><h2>실제 검사</h2><p>바닥 적용 보호 검사 '+str(report['passing_applications'])+'/'+str(report['applications'])+' · 같은 바이옴 원본 바닥 비교 '+str(report['same_biome_fidelity_passes'])+'/'+str(report['same_biome_fidelity_checks'])+'. 색/원본을 서로 맞춰 만든 그림이 아니라 생성된 지형 이름을 검사했습니다.</p><table><tr><th>맵</th><th>조건</th><th>비교 가능한 바닥</th><th>원본 바닥 이름 일치</th></tr>'
    for r in report['records']:
        doc+='<tr><td>'+r['id']+'</td><td>'+r['biome']+' '+str(r['size'])+'</td><td>'+str(r['source_reference_cells'])+'</td><td>'+((f"{r['source_named_ground_agreement']:.1%}") if r['same_biome'] else '바이옴 조정 · 단순 복사 비교 제외')+'</td></tr>'
    doc+='</table><p>물·도로·건축 바닥·벽을 넣은 실제 검사 맵에서 보호 대상이 존재함을 확인했습니다. 존재하지 않는 바닥과 WaterDeep을 일반 바닥으로 지정한 검사에서는 전체 지형이 원래 생성 결과와 같습니다.</p></section><section><h2>남은 한계</h2><p>작은 바닥 조각은 12칸 미만이면 생략합니다. 임시 얼음 아래의 지형은 직접 데이터로 읽고 이미지에서 추측하지 않습니다. 물·산 윤곽의 근사 오차, 동굴·자원·이벤트와 절차적 변형은 이번 바닥 작업에서 해결하지 않았습니다. 실제 검사에서 실패한 오아시스 등의 후보는 계속 격리합니다. 제품 연결 전에는 바닥 sidecar를 저장·Undo·프리뷰 경로에 통합해야 합니다.</p><p>프로브의 기존 무창 렌더링 경고는 보고서에 구별하여 기록합니다. HTML은 이미지 포함·링크 정적 검사를 수행하며 브라우저 렌더 검증은 별개입니다.</p></section><footer><p>GL: m00nl1ght · <a href="https://github.com/m00nl1ght-dev/GeologicalLandforms">Geological Landforms</a> · 파생 레시피/지형 그림 CC BY-NC-SA 4.0. 원본 귀속은 상위 ATTRIBUTION.md를 유지합니다. 게임 자산의 권리를 변경하지 않습니다.</p></footer></main></html>'
    (folder/'review.html').write_text(doc,encoding='utf-8')
    font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',20);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',15)
    sheet=Image.new('RGB',(1014,880),'#14201f');draw=ImageDraw.Draw(sheet)
    for row,ident in enumerate(('gl-lake','gl-valley','gl-cliff')):
        y=row*290+12;draw.text((16,y),entries[ident]['title_ko'],font=font,fill='#e6ede5')
        for col,(sub,label) in enumerate(((source,'GL 원본'),(base/'transfer-a-native','이전 · 바닥 유지'),(runs[0],'이번 · 바닥 포함'))):
            image=Image.open(sub/(ident+'-map.png')).convert('RGB').resize((228,228),Image.Resampling.NEAREST)
            x=16+col*332;sheet.paste(image,(x,y+32));draw.text((x,y+263),label,font=small,fill='#c4d5c8')
    sheet.save(folder/'comparison.png');print(json.dumps({'html_bytes':len(doc.encode()),'native_images':doc.count('<img'),'rows':len(report['records'])}))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);p.add_argument('--prepare-extra',action='store_true');p.add_argument('--runs',nargs='*');args=p.parse_args()
    if args.prepare_extra:prepare_extra(args.folder)
    else:
        runs=[args.folder/r for r in args.runs];report=evaluate(args.folder,runs);review(args.folder,runs,report)
