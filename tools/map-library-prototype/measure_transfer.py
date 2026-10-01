"""Compare actual native terrain to sampled source topology, separately from beauty.

Legacy commands retain extra native rocks/ponds. Complete source water sidecars
compare directly with the original pools, while mountain precision remains
diagnostic rather than a blanket clearing instruction.
"""
import argparse, json, pathlib
import numpy as np
from skimage import morphology, measure as regions
from contours import read_terrain

def scaled(mask, size):
    indices=np.minimum((np.arange(size)*mask.shape[0]/size).astype(int),mask.shape[0]-1)
    return mask[np.ix_(indices,indices)]

def compare(expected, actual):
    expected_count=int(expected.sum());actual_count=int(actual.sum())
    if not expected_count:return None
    intersection=int((expected & actual).sum())
    nearby=morphology.dilation(actual,morphology.disk(2))
    return {'expected_cells':expected_count,'actual_cells':actual_count,
            'recall':intersection/expected_count,'recall_within_2_cells':float((expected & nearby).sum()/expected_count),
            'precision':intersection/actual_count if actual_count else 0,
            'iou':intersection/int((expected | actual).sum())}

def retained(mask):
    closed=morphology.closing(mask,morphology.disk(1),mode='ignore')
    components=regions.label(closed,connectivity=1);minimum=max(40,int(mask.size*.0015))
    counts=np.bincount(components.ravel());counts[0]=0
    return counts[components]>=minimum

def measure(folder, runs):
    catalog=json.loads((folder/'catalog.json').read_text(encoding='utf-8'));entries={e['id']:e for e in catalog['entries']}
    records=[]
    for run in runs:
        result=json.loads((run/'result.json').read_text(encoding='utf-8'))
        if not result['ok']:raise ValueError('Incomplete runtime: '+str(run))
        for scene in result['results']:
            entry=entries[scene['id']];actual,_=read_terrain(run/(entry['id']+'-terrain.json'));metrics={};reasons=[]
            if 'reference_run' in entry['source']:
                source=folder/entry['source']['reference_run'];ident=entry['source']['reference_id']
                truth,_=read_terrain(source/(ident+'-terrain.json'))
                baseline,_=read_terrain(run/(entry['id']+'-baseline-terrain.json'))
                if entry['features']['new_mountains']:
                    metrics['mountains']=compare(scaled(retained(truth=='M'),scene['size']),actual=='M')
                if entry['features']['new_water']:
                    exact=entry.get('requires_water_sidecar',False)
                    wet=scaled(np.isin(truth,['S','W']) if exact else retained(np.isin(truth,['S','W'])),scene['size'])
                    metrics['wet_footprint']=compare(wet,np.isin(actual,['S','W']))
                    metrics['wet_with_native_baseline']=compare(wet | np.isin(baseline,['S','W']),np.isin(actual,['S','W']))
                    metrics['deep_core']=compare(scaled(truth=='W',scene['size']),actual=='W')
                    metrics['dry_land']=compare(~wet,~np.isin(actual,['S','W']))
                    if exact:
                        metrics['shallow_layout']=compare(scaled(truth=='S',scene['size']),actual=='S')
                        for key in ('wet_footprint','deep_core','shallow_layout'):
                            value=metrics.get(key)
                            if value and value['iou']<.98:reasons.append(key+' exact source IoU below 0.98')
                        application=json.loads((run/(entry['id']+'-water-application.json')).read_text(encoding='utf-8'))
                        if application['protected_conflicts']:reasons.append('Source composition conflicts with protected native features')
                for feature,value in metrics.items():
                    if entry.get('requires_water_sidecar') and feature=='wet_with_native_baseline':continue
                    if value and value['recall_within_2_cells']<.85:reasons.append(feature+' source recall below 0.85')
                if not entry.get('requires_water_sidecar') and metrics.get('wet_with_native_baseline') and metrics['wet_with_native_baseline']['iou']<.80:reasons.append('Water plus paired native baseline IoU below 0.80')
            # Original native features need not be cleared to improve precision.
            records.append({'run':run.name,'id':entry['id'],'biome':scene['biome'],'size':scene['size'],'tile':scene['tile'],
                            'counts':scene['counts'],'metrics':metrics,'geometry_pass':not reasons,'reasons':reasons,
                            'source_comparison':bool(metrics),
                            'native_authoring_report_present':scene.get('authoring_report_present'),
                            'scope':'Sampled silhouette/depth transfer; unrelated native ground preserved. Not aesthetic approval.'})
    report={'records':records,'checks':len(records),'passed':sum(r['geometry_pass'] for r in records),'failed':sum(not r['geometry_pass'] for r in records),
            'source_comparison_cases':sum(r['source_comparison'] for r in records),
            'source_comparison_passed':sum(r['source_comparison'] and r['geometry_pass'] for r in records),
            'execution_only_controls':sum(not r['source_comparison'] for r in records),
            'protocol':'Source water sidecar: all observed pools/depths, direct source IoU >=0.98 and no protected conflicts; native pond union is diagnostic only. Legacy polygons: two-cell recall >=0.85; water union paired baseline IoU >=0.80. Permanent terrain below seasonal ice. No beauty claim.',
            'no_source_reference':['core-foothills','core-dry-clearing'],'paid_api_calls':0}
    (folder/'transfer-evaluation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'passed':report['passed'],'failed':report['failed'],'rows':[{'run':r['run'],'id':r['id'],'pass':r['geometry_pass'],'reasons':r['reasons']} for r in records]},ensure_ascii=False))
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);p.add_argument('--runs',nargs='+',required=True);a=p.parse_args()
    measure(a.folder,[a.folder/run for run in a.runs])
