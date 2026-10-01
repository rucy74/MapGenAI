"""Expose only scene/profile combinations that passed actual native replay."""
import argparse, hashlib, json, pathlib

def finalize(folder):
    path=folder/'catalog.json';catalog=json.loads(path.read_text(encoding='utf-8'))
    evidence_path=folder/'transfer-evaluation.json';evidence=json.loads(evidence_path.read_text(encoding='utf-8'))
    for entry in catalog['entries']:
        profiles={};failed=[]
        for row in evidence['records']:
            if row['id']!=entry['id']:continue
            key=(row['biome'],row['size'],'Flat')
            profiles[key]=profiles.get(key,True) and row['geometry_pass']
            if not row['geometry_pass']:failed.append({'run':row['run'],'reasons':row['reasons']})
        entry['verified_profiles']=[{'biome':biome,'map_size':size,'hilliness':hill} for (biome,size,hill),ok in sorted(profiles.items()) if ok]
        entry['status']='prototype-tested' if entry['verified_profiles'] else 'prototype-quarantined'
        entry['verification']={'evidence':'transfer-evaluation.json','evidence_sha256':hashlib.sha256(evidence_path.read_bytes()).hexdigest(),
                               'failed_profiles':failed,'aesthetic_approval':False,'scope':'Observed profile samples only; actual future tile still needs generation'}
    catalog['verification_required']=True
    path.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'available':[e['id'] for e in catalog['entries'] if e['verified_profiles']],
                      'quarantined':[e['id'] for e in catalog['entries'] if not e['verified_profiles']]},ensure_ascii=False))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);finalize(p.parse_args().folder)
