"""User text -> retrieved recipes -> isolated native generation manifest.

This deliberately does not touch the live game's current tile or call an LLM.
It supplies the missing end-to-end developer entry point before UI integration.
"""
import argparse, json, os, pathlib
from retrieve import search

def prepare(folder, query, world_seed, output):
    folder=folder.resolve();output=output.resolve()
    if folder not in output.parents:raise ValueError('Keep request output within the catalog experiment directory')
    if output.exists():raise ValueError('Fresh manifest required; preserve previous experiments')
    result=search(folder/'catalog.json',folder/'index',query)
    cases=[{'id':candidate['id'],'biome':query['biome'],'size':query.get('map_size',250),
            'command':os.path.relpath(folder/candidate['command'],output.parent).replace('\\','/')}
           for candidate in result['candidates']]
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps({'world_seed':world_seed,'request':query,'cases':cases},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    receipt=output.with_suffix('.retrieval.json');receipt.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return {'manifest':str(output),'retrieval':str(receipt),'cases':len(cases),'selected':[c['id'] for c in cases],
            'next':'Use run.ps1 only if cases > 0; actual native generation and visual inspection are still required',
            'paid_api_calls':0,'live_game_modified':False}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);p.add_argument('--query',required=True)
    p.add_argument('--biome',default='TemperateForest');p.add_argument('--size',type=int,choices=[250,300],default=250)
    p.add_argument('--world-seed',required=True);p.add_argument('--output',type=pathlib.Path,required=True)
    p.add_argument('--native-water',action='store_true');p.add_argument('--scope',choices=['new','edit'],default='new')
    p.add_argument('--native-roads',action='store_true')
    p.add_argument('--challenge',action='store_true');p.add_argument('--count',type=int,choices=[1,2,3],default=3)
    a=p.parse_args();print(json.dumps(prepare(a.folder,{'text':a.query,'biome':a.biome,'map_size':a.size,'scope':a.scope,
            'has_native_water':a.native_water,'has_native_roads':a.native_roads,'allow_challenge':a.challenge,'count':a.count},a.world_seed,a.output),ensure_ascii=False))
