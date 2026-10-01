"""Small pre-labelled synthetic query smoke set; not a general quality benchmark."""
import argparse, json, pathlib, time
from embedding import Embedder
from retrieve import search

CASES=[
 ('Q01','구불구불한 호숫가에 집을 짓고 싶어','TemperateForest',{'gl-lake','image-lake'},{}),
 ('Q02','산 사이로 길게 열린 넓은 골짜기를 원해','TemperateForest',{'gl-valley'},{}),
 ('Q03','평야에 외딴 바위산 하나 있는 곳','TemperateForest',{'gl-lone-mountain'},{}),
 ('Q04','절벽 옆에 넓은 빈터가 있는 곳','TemperateForest',{'gl-cliff'},{}),
 ('Q05','여러 섬이 얕은 물로 이어진 군도','TemperateForest',{'gl-archipelago'},{}),
 ('Q06','사막에서 농사 지을 수 있는 작은 오아시스','Desert',{'gl-oasis'},{}),
 ('Q07','A winding lakeshore with space for a village','TemperateForest',{'gl-lake','image-lake'},{}),
 ('Q08','A broad valley between rocky mountain sides','TemperateForest',{'gl-valley'},{}),
 ('Q09','No mountains and no water, keep an open settlement clearing','TemperateForest',{'core-dry-clearing'},{}),
 ('Q10','완만한 산기슭 옆에 넓은 평지, 물은 없이','TemperateForest',{'core-foothills','gl-cliff'},{}),
 ('Q11','기존 강가에 정착 공간을 넓혀 줘','TemperateForest',{'core-foothills','core-dry-clearing'},{'has_native_water':True}),
 ('Q12','온천만 추가해 줘','TemperateForest',set(),{'scope':'edit'}),
 ('Q13','도넛 내부의 70%만 비옥한 토양으로','TemperateForest',set(),{}),
 ('Q14','왼쪽에 호수를 놓아 줘','TemperateForest',set(),{}),
 ('Q15','얼음 벌판에 아름다운 정착지','IceSheet',set(),{}),
]

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);a=p.parse_args();e=Embedder();rows=[]
    for ident,text,biome,acceptable,extra in CASES:
        query={'text':text,'biome':biome,**extra};result=search(a.folder/'catalog.json',a.folder/'index',query,e)
        ids=[x['id'] for x in result['candidates']];ok=bool(set(ids)&acceptable) if acceptable else len(ids)==0
        rows.append({'id':ident,'kind':'synthetic, labelled before retrieval','acceptable_ids':sorted(acceptable),'pass':ok,
                     'top1_match':bool(ids and ids[0] in acceptable) if acceptable else None,**result})
    again=search(a.folder/'catalog.json',a.folder/'index',rows[0]['query'],e)
    report={'checks':len(rows),'passed':sum(r['pass'] for r in rows),'failed':sum(not r['pass'] for r in rows),
            'top1_matches':sum(r['top1_match'] is True for r in rows),'top1_cases':sum(r['top1_match'] is not None for r in rows),
            'paid_api_calls':0,'repeat_query_cache_hit':again['query_cache_hit'],'results':rows,'scope':'Small smoke set against nine authored entries, not user beauty preference or universal retrieval accuracy'}
    (a.folder/'retrieval-evaluation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'passed':report['passed'],'failed':report['failed'],'repeat_query_cache_hit':again['query_cache_hit'],'rows':[{'id':r['id'],'pass':r['pass'],'selected':[x['id'] for x in r['candidates']]} for r in rows]},ensure_ascii=False))
