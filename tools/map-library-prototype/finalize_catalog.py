"""Expose only scene/profile combinations that passed actual native replay."""
import argparse, hashlib, json, pathlib

def finalize(folder):
    path=folder/'catalog.json';catalog=json.loads(path.read_text(encoding='utf-8'))
    evidence_path=folder/'transfer-evaluation.json';evidence=json.loads(evidence_path.read_text(encoding='utf-8'))
    ground_path=folder/'ground-evaluation.json'
    ground=json.loads(ground_path.read_text(encoding='utf-8')) if ground_path.exists() else None
    water_path=folder/'water-evaluation.json'
    water=json.loads(water_path.read_text(encoding='utf-8')) if water_path.exists() else None
    rock_path=folder/'rock-evaluation.json'
    rock=json.loads(rock_path.read_text(encoding='utf-8')) if rock_path.exists() else None
    for entry in catalog['entries']:
        profiles={};failed=[]
        for row in evidence['records']:
            if row['id']!=entry['id']:continue
            key=(row['biome'],row['size'],'Flat')
            ground_ok=True
            if entry.get('requires_ground_sidecar'):
                matches=[] if ground is None else [r for r in ground['records'] if r['run']==row['run'] and r['id']==entry['id']]
                guards=[] if ground is None else [r for r in ground['application_checks'] if r['run']==row['run'] and r['id']==entry['id']]
                ground_ok=bool(matches and guards) and all(r['fidelity_pass'] is not False for r in matches) and all(r['pass'] for r in guards)
                if not ground_ok:failed.append({'run':row['run'],'reasons':['Ground evidence absent or failed']})
            water_ok=True
            if entry.get('requires_water_sidecar'):
                matches=[] if water is None else [r for r in water['records'] if r['run']==row['run'] and r['id']==entry['id']]
                water_ok=bool(matches) and all(r['pass'] for r in matches)
                if not water_ok:failed.append({'run':row['run'],'reasons':['Water layout evidence absent or failed']})
            rock_ok=True
            if entry.get('requires_rock_sidecar'):
                matches=[] if rock is None else [r for r in rock['records'] if r['run']==row['run'] and r['id']==entry['id']]
                rock_ok=bool(matches) and all(r['pass'] for r in matches)
                if not rock_ok:failed.append({'run':row['run'],'reasons':['Rock detail evidence absent or failed']})
            profiles[key]=profiles.get(key,True) and row['geometry_pass'] and ground_ok and water_ok and rock_ok
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
