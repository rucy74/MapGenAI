"""Prototype semantic retrieval with explicit scope/terrain filters and diversity.
No neural reranker. No current tile state is modified by this Python program.
"""
import argparse, hashlib, json, pathlib, re, time
import numpy as np
from embedding import Embedder

def read_index(catalog_path,index):
    catalog=json.loads(catalog_path.read_text(encoding='utf-8'))
    meta=json.loads((index/'space.json').read_text(encoding='utf-8'))
    if meta['catalog_sha256']!=hashlib.sha256(catalog_path.read_bytes()).hexdigest(): raise ValueError('Catalog/index fingerprint mismatch')
    vectors=np.load(index/'vectors.npy',allow_pickle=False)
    ids=[e['id'] for e in catalog['entries']]
    if ids!=meta['entry_ids'] or vectors.shape!=(len(ids),meta['space']['dimension']): raise ValueError('Entry order/dimension mismatch')
    if catalog.get('verification_required') and any('verified_profiles' not in e for e in catalog['entries']):raise ValueError('Verified catalog missing actual replay profiles')
    if not np.isfinite(vectors).all() or not np.allclose(np.linalg.norm(vectors,axis=1),1,atol=1e-4): raise ValueError('Invalid unnormalized/nonfinite vectors')
    return catalog,meta,vectors

def filters(query):
    text=query['text']
    no_water=query.get('no_water',False) or bool(re.search(r'물\s*(?:없이|없는|넣지\s*마|추가하지\s*마)|호수\s*(?:없이|없는)|\b(?:no|without)\s+(?:new\s+)?(?:water|lake|pond)s?\b',text,re.I))
    no_mountains=query.get('no_mountains',False) or bool(re.search(r'산\s*(?:없이|없는|넣지\s*마)|\b(?:no|without)\s+(?:new\s+)?mountains?\b',text,re.I))
    special=query.get('allow_challenge',False) or bool(re.search(r'군도|섬|협곡|도전|\b(?:island|archipelago|canyon|challenge)',text,re.I))
    return {'no_water':no_water,'no_mountains':no_mountains,'allow_challenge':special}

def reject(entry,query,constraints):
    if query.get('scope','new')!='new': return 'Partial edits do not trigger library replacement'
    if query.get('has_authored_map',False): return 'Existing authored map requires explicit new-layout consent'
    if query.get('has_native_roads',False):return 'Existing world roads not tested by this prototype'
    if query.get('has_native_water',False):return 'Existing world river/coast profile has not been replay-tested; keep the existing generator'
    if entry['status'] not in ('prototype-tested','prototype-draft'): return 'Entry failed actual replay and is quarantined'
    if 'verified_profiles' in entry and not any(profile['biome']==query['biome'] and profile['map_size']==query.get('map_size',250)
            and profile['hilliness']==query.get('hilliness','Flat') for profile in entry['verified_profiles']):return 'Exact biome/size/hilliness combination has no passing replay'
    if query['biome'] not in entry['profiles']['biomes']: return 'Unsupported biome'
    if query.get('map_size',250) not in entry['profiles']['map_sizes']: return 'Untested map size'
    if query.get('hilliness','Flat') not in entry['profiles']['hilliness']: return 'Untested hilliness'
    if constraints['no_water'] and entry['features']['new_water']: return 'New water forbidden'
    if constraints['no_mountains'] and entry['features']['new_mountains']: return 'New mountains forbidden'
    if entry.get('challenge',False) and not constraints['allow_challenge']: return 'Challenge layout requires explicit preference'
    return None

def rank(catalog,vectors,query_vector,query,count=3):
    constraints=filters(query); excluded=[]; eligible=[]
    if re.search(r'\d+\s*(?:%|칸|cells?)|\bpercent\b|왼쪽|오른쪽|북쪽|남쪽|동쪽|서쪽|\b(?:left|right|north|south|east|west)\b',query['text'],re.I):
        return [],[{'reason':'Exact coverage/width/direction requests remain with existing generator; library verification not implemented'}],constraints
    for i,entry in enumerate(catalog['entries']):
        reason=reject(entry,query,constraints)
        if reason: excluded.append({'id':entry['id'],'reason':reason})
        else: eligible.append(i)
    recent=set(query.get('recent_ids',[])); eligible=[i for i in eligible if catalog['entries'][i]['id'] not in recent]
    scores=vectors@query_vector
    pool=sorted(eligible,key=lambda i:float(scores[i]),reverse=True)[:8]
    selected=[]
    while pool and len(selected)<count:
        def utility(i):
            if not selected: return float(scores[i])
            redundancy=max(float(vectors[i]@vectors[j]) for j in selected)
            return float(scores[i])-.15*redundancy
        # Keep one layout per family; no padding with incompatible candidates.
        pool=[i for i in pool if catalog['entries'][i]['family'] not in {catalog['entries'][j]['family'] for j in selected}]
        if not pool: break
        chosen=max(pool,key=utility);pool.remove(chosen);selected.append(chosen)
    return [{'id':catalog['entries'][i]['id'],'similarity':float(scores[i]),'family':catalog['entries'][i]['family'],'command':catalog['entries'][i]['command'],'source':catalog['entries'][i]['source']['kind']} for i in selected],excluded,constraints

def search(catalog_path,index,query,embedder=None):
    clock=time.perf_counter();catalog,meta,vectors=read_index(catalog_path,index)
    possible,excluded,constraints=rank(catalog,vectors,np.zeros(meta['space']['dimension']),query,query.get('count',3))
    if not possible:
        return {'query':query,'constraints':constraints,'candidates':[],'excluded':excluded,'query_cache_hit':False,
                'seconds':time.perf_counter()-clock,'paid_api_calls':0,'reranker':'rules/diversity only',
                'embedding_skipped':True,'warning':'No compatible verified recipe; existing generator remains responsible.'}
    embedder=embedder or Embedder()
    if embedder.space()!=meta['space']: raise ValueError('Query embedding space differs from catalog')
    context=json.dumps({'text':query['text'],'biome':query['biome'],'scope':query.get('scope','new')},ensure_ascii=False,sort_keys=True)
    cache_key=hashlib.sha256((json.dumps(meta['space'],sort_keys=True)+context).encode()).hexdigest()
    cache=index/'query-cache';cache.mkdir(exist_ok=True)
    path=cache/(cache_key+'.npy');hit=path.exists()
    vector=np.load(path,allow_pickle=False) if hit else embedder.encode([query['text']],'query')[0]
    if vector.shape!=(meta['space']['dimension'],) or not np.isfinite(vector).all() or not np.isclose(np.linalg.norm(vector),1,atol=1e-4): raise ValueError('Invalid query vector')
    if not hit: np.save(path,vector,allow_pickle=False)
    results,excluded,constraints=rank(catalog,vectors,vector,query,query.get('count',3))
    return {'query':query,'constraints':constraints,'candidates':results,'excluded':excluded,'query_cache_hit':hit,'seconds':time.perf_counter()-clock,'paid_api_calls':0,'reranker':'rules/diversity only','warning':'Experimental candidates need actual current-tile generation; similarity is not probability or beauty.'}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--catalog',type=pathlib.Path,required=True);p.add_argument('--index',type=pathlib.Path,required=True);p.add_argument('--query',required=True);p.add_argument('--biome',default='TemperateForest');p.add_argument('--native-water',action='store_true');p.add_argument('--scope',default='new');p.add_argument('--output',type=pathlib.Path)
    args=p.parse_args();result=search(args.catalog,args.index,{'text':args.query,'biome':args.biome,'scope':args.scope,'has_native_water':args.native_water})
    data=json.dumps(result,ensure_ascii=False,indent=2)
    if args.output: args.output.write_text(data+'\n',encoding='utf-8')
    print(data)
