"""Self-contained HTML and native map contact sheet; never AI-generated artwork."""
import argparse, base64, html, json, pathlib
from PIL import Image, ImageDraw, ImageFont

def load(path):return json.loads(path.read_text(encoding='utf-8'))
def picture(path,label):
    data=base64.b64encode(path.read_bytes()).decode()
    return '<figure><img loading="lazy" src="data:image/png;base64,'+data+'" alt="'+html.escape(label)+'"><figcaption>'+html.escape(label)+'</figcaption></figure>'

def create(folder):
    catalog=load(folder/'catalog.json');transfer=load(folder/'transfer-evaluation.json');retrieval=load(folder/'retrieval-evaluation.json');image=load(folder/'image-evaluation.json')
    entries={e['id']:e for e in catalog['entries']};runs=['transfer-a-native','transfer-b-native','transfer-c-native'];sections=[]
    for ident,entry in entries.items():
        cells=[];metrics=[]
        if 'reference_run' in entry['source']:
            src=folder/entry['source']['reference_run']/(entry['source']['reference_id']+'-map.png')
            cells.append(picture(src,'GL 원본 · 250 · '+('사막' if ident=='gl-oasis' else '온대림')))
        else:cells.append(picture(folder/runs[0]/(ident+'-baseline-map.png'),'빈 설정의 기본 게임 · 같은 타일'))
        for run,label in zip(runs,['다른 시드 A · 250','다른 시드 B · 300','건조한 바이옴 C · 250']):
            cells.append(picture(folder/run/(ident+'-map.png'),label))
            row=next(r for r in transfer['records'] if r['run']==run and r['id']==ident)
            parts=[k+': 재현 '+str(round(v['recall_within_2_cells']*100,1))+'%, IoU '+str(round(v['iou']*100,1))+'%' for k,v in row['metrics'].items() if v and k in ('mountains','wet_with_native_baseline','deep_core')]
            state_label=' · 원본 비교 없음: 실제 실행 확인' if not row.get('source_comparison',True) else ' · 윤곽 검사 통과' if row['geometry_pass'] else ' · 자동 추천 제외: '+', '.join(row['reasons'])
            metrics.append('<li>'+html.escape(label+state_label)+'<br><small>'+html.escape(' / '.join(parts) or '원본 비교 없음: 자체 대조 레시피')+'</small></li>')
        sections.append('<section id="'+ident+'"><h2>'+html.escape(entry['title_ko'])+'</h2><p>'+html.escape(entry['description_ko'])+'</p><div class="gallery">'+''.join(cells)+'</div><details><summary>실제 검증 수치·같은 타일 기본 맵</summary><ul>'+''.join(metrics)+'</ul>'+picture(folder/runs[0]/(ident+'-baseline-map.png'),'시드 A의 같은 타일 · 빈 설정 baseline')+'</details><p class="muted">'+html.escape('출처: '+entry['source']['kind']+' · '+entry['source'].get('license',''))+'</p></section>')
    query_rows=[]
    for row in retrieval['results']:
        query_rows.append('<tr><td>'+html.escape(row['query']['text'])+'</td><td>'+html.escape(', '.join(entries[x['id']]['title_ko'] for x in row['candidates']) or '후보 없음')+'</td><td>'+('상위3 내 일치' if row['pass'] and row['acceptable_ids'] else '조건상 제외' if row['pass'] else '미충족')+'</td></tr>')
    document='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MapGenAI · 맵 저장소 프로토타입</title><style>
    *{box-sizing:border-box}body{margin:0;background:#10191b;color:#edf2e8;font:16px/1.65 system-ui,'Malgun Gothic',sans-serif}main{max-width:1350px;margin:auto;padding:32px 24px}h1{font-size:38px;line-height:1.25}h2{color:#c1d7a9}a{color:#8fcedc}nav{display:flex;flex-wrap:wrap;gap:10px;margin:22px 0}nav a{background:#243537;padding:7px 12px;text-decoration:none;border-radius:6px}section{border-top:1px solid #3b4c4e;padding:28px 0}.gallery{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:16px}figure{margin:0}img{display:block;width:100%;height:auto;image-rendering:pixelated;background:#1d2526;aspect-ratio:1}figcaption{font-size:14px;color:#c3cec6;margin-top:8px}.muted,small{color:#acbbb3}details{background:#1e2b2d;padding:12px;margin:18px 0}details figure{max-width:300px}.stats{display:flex;flex-wrap:wrap;gap:14px}.stat{padding:18px;background:#253436;border-radius:9px;min-width:180px}.stat b{display:block;font-size:28px}.note{border-left:4px solid #d4ab64;background:#2b2b22;padding:15px 20px}table{border-collapse:collapse;width:100%;font-size:14px}td,th{text-align:left;border-bottom:1px solid #394b4d;padding:12px}pre{overflow:auto;white-space:pre-wrap;background:#1e2b2d;padding:18px}code{font-family:monospace}@media(max-width:750px){main{padding:18px 14px}h1{font-size:28px}.gallery{grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}table{font-size:12px}td,th{padding:8px}.stat{min-width:140px}}</style><main>
    <p class="muted">2026-10-01 · DEV 개발 실험 · 저장된 실제 게임 결과</p><h1>맵을 찾아서, 다른 타일에서 다시 생성하기</h1>
    <p>GL 원본 구도와 MapGenAI에서 재생한 구도를 비교한다. 모든 지도 그림은 실제 전체 맵을 native Map Preview 색으로 그린 결과이며, 생성형 이미지가 아니다.</p>
    <p class="note">제품 UI에 아직 연결하지 않았다. GL의 XML 그래프를 완전히 번역한 기능도 아니다. 원본은 원래 GL worker로 생성하고, 이식은 관측된 산·물 윤곽을 기존 편집 명령으로 옮긴다. 미관 승인과 기술 검증은 구분한다.</p>
    '''
    document+='<div class="stats"><div class="stat"><b>6</b>GL 원본 지형</div><div class="stat"><b>8 / 9</b>자동 추천 가능 / 실험 레시피</div><div class="stat"><b>'+str(len(transfer['records']))+'</b>재생 지도 + 같은 타일 baseline</div><div class="stat"><b>0</b>유료 API 호출</div></div>'
    document+='<p class="note">원본 윤곽 비교 '+str(transfer['source_comparison_passed'])+'/'+str(transfer['source_comparison_cases'])+' 통과, 자체 대조 레시피 '+str(transfer['execution_only_controls'])+'회는 원본 비교 없이 실행만 확인했다. 오아시스의 작은 물 영역은 원본보다 넓어져 3조건 모두 기준을 넘지 못했다. 그림과 실패 수치는 남기고 자동 추천에서 격리했다.</p>'
    request=load(folder/'request-example.retrieval.json')
    document+='<section id="request"><h2>실제 요청 → 검색 → 후보 생성</h2><p>“'+html.escape(request['query']['text'])+'”를 입력하고, 검색 순서대로 다른 새 월드에서 실제 생성했다. 이 단계는 임베딩/조건 필터의 출력이며 새로운 LLM 제안은 포함하지 않는다.</p><div class="gallery">'+''.join(picture(folder/'request-example-native'/(row['id']+'-map.png'),str(i+1)+' · '+entries[row['id']]['title_ko']) for i,row in enumerate(request['candidates']))+'</div></section>'
    document+='<nav><a href="#request">실제 요청 예시</a>'+''.join('<a href="#'+ident+'">'+html.escape(e['title_ko'])+'</a>' for ident,e in entries.items())+'<a href="#search">검색</a><a href="#limits">한계·다음 단계</a></nav>'+''.join(sections)
    document+='<section id="search"><h2>로컬 의미 검색</h2><p>조건 필터 → 384차원 임베딩 → 코사인 근접 검색 → 같은 구도 중복 제거. 신경망 reranker는 없다. 상위3에 기대한 종류가 포함되거나 제외 조건을 지킨 작은 검사: '+str(retrieval['passed'])+'/'+str(retrieval['checks'])+'. 그 중 답이 있는 요청의 1위 일치: '+str(retrieval['top1_matches'])+'/'+str(retrieval['top1_cases'])+'. 보편적인 정확도나 미관 점수가 아니다.</p><table><thead><tr><th>요청</th><th>검색 순서</th><th>작은 검사</th></tr></thead><tbody>'+''.join(query_rows)+'</tbody></table></section>'
    document+='<section id="image"><h2>이미지 → 맵 실험</h2><p>골짜기·군도의 팔레트로 보정하고, 보정에 쓰지 않은 호수 이미지만 입력했다. 전체 색 분류 일치율은 '+str(round(image['pixel_label_accuracy']*100,2))+'%지만, 갈색 바위/그림자 혼동이 있어 산은 이식하지 않는다. 얕은 물 윤곽과 깊은 중심만 명령으로 옮긴다. 사진·UI가 덮인 이미지·다른 팔레트를 읽는 범용 비전은 아직 없다.</p><pre>'+html.escape(json.dumps(image['per_label'],ensure_ascii=False,indent=2))+'</pre></section>'
    document+='''<section id="limits"><h2>한계와 다음 작업</h2><ul><li>현재 타일 조건을 자동 읽는 제품 UI는 미연결. 실험은 CLI가 정한 새 월드의 타일에서 수행한다.</li><li>타일의 원래 토양·비·온도·기본 물웅덩이는 유지한다. 같은 원본의 주 윤곽은 유지되므로 현재는 가족별 원본 변형이 더 필요하다.</li><li>동굴 지붕·자원·이벤트·GL spawn 규칙은 옮기지 않는다. 기존 강·해안과 관계가 검증되지 않은 새 물 후보는 제외한다.</li><li>현재는 Flat·250/300·일부 바이옴만 검사했다. 임의 모드 조합과 미관을 보장하지 않는다.</li><li>다음: 마음에 드는 사례 선정 → 원본 변형 확대 → 정착 공간/연결 검사 → DEV opt-in 후보 UI 연결.</li></ul></section>
    <section><h2>출처</h2><p>Geological Landforms — m00nl1ght, <a href="https://github.com/m00nl1ght-dev/GeologicalLandforms">원본 GitHub</a>, e8035e2…, 설치 v1.7.13.1. GL 파생 윤곽 레시피·지형 그림과 카탈로그의 GL 자료: <a href="https://creativecommons.org/licenses/by-nc-sa/4.0/">CC BY-NC-SA 4.0</a>. 원본 그래프와 달리 이식에서 동굴·보너스·spawn 기능을 생략했다. 게임 자산의 권리를 변경하는 선언은 아니다.</p><p>E5: <a href="https://huggingface.co/intfloat/multilingual-e5-small">intfloat/multilingual-e5-small</a>, MIT. 가중치는 개발 PC의 별도 캐시에 보관하며 모드 배포에 포함하지 않았다.</p></section></main></html>'''
    (folder/'review.html').write_text(document,encoding='utf-8')
    # Six pairs, rendered without interpolation; captions describe provenance.
    font=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',18);small=ImageFont.truetype('C:/Windows/Fonts/malgun.ttf',14)
    sheet=Image.new('RGB',(1200,700),'#10191b');draw=ImageDraw.Draw(sheet)
    ids=['gl-valley','gl-lone-mountain','gl-cliff','gl-lake','gl-archipelago','gl-oasis']
    for i,ident in enumerate(ids):
        x=(i%3)*400+10;y=(i//3)*350+12;draw.text((x,y),entries[ident]['title_ko'],font=font,fill='#edf2e8')
        for j,(sub,label) in enumerate([('source-final','GL 원본'),('transfer-a-native','MapGenAI 재생')]):
            picture_image=Image.open(folder/sub/(ident+'-map.png')).convert('RGB').resize((184,184),Image.Resampling.NEAREST)
            sheet.paste(picture_image,(x+j*194,y+40));draw.text((x+j*194,y+232),label,font=small,fill='#c3cec6')
        draw.text((x,y+269),'실제 생성 · 다른 시드 · 기본 바닥 유지',font=small,fill='#acbbb3')
    sheet.save(folder/'comparison.png')
    print(json.dumps({'html_bytes':(folder/'review.html').stat().st_size,'sections':len(entries),'embedded_image_elements':document.count('<img ')},ensure_ascii=False))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);create(p.parse_args().folder)
