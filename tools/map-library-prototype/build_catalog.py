"""Build a small attributed developer catalog and native transfer fixtures."""
import argparse, collections, hashlib, json, pathlib
import numpy as np
from PIL import Image
from contours import read_terrain, read_image, export

NAMES={
 'gl-lake':('불규칙한 호숫가','Irregular lakeside','lake','구불구불한 호숫가와 넓게 이어지는 정착 공간.','An irregular lake shoreline with broad connected settlement ground.'),
 'gl-valley':('두 산 사이 열린 골짜기','Open mountain valley','valley','양쪽 산자락 사이로 길게 이어지는 넓은 골짜기.','A broad connected valley between two rocky mountain sides.'),
 'gl-lone-mountain':('외딴 산과 열린 평야','Lone mountain and open plain','lone-mountain','한쪽에 자리한 큰 바위산과 주위의 넓게 열린 평야.','One isolated rocky mountain beside a wide open plain.'),
 'gl-cliff':('절벽 옆 넓은 빈터','Open ground beside a cliff','cliff','한쪽 가장자리에 길게 이어진 바위 절벽과 반대쪽의 넓은 정착 공간.','A continuous rocky cliff on one side and broad settlement space on the other.'),
 'gl-archipelago':('얕은 물로 이어진 군도','Shallow-water archipelago','archipelago','넓은 물 사이에 여러 섬. 얕은 물로 이동할 수 있는 특별한 지형.','Several islands amid broad water, linked by walkable shallows. A special island challenge.'),
 'gl-oasis':('사막의 작은 오아시스','Small desert oasis','oasis','사막 한가운데 작은 얕은 물과 주변의 국소적인 경작 가능한 토양.','A small shallow desert pool surrounded by localized farmable soil.'),
}

def write(path,obj):
    path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def build(folder,source):
    result=json.loads((source/'result.json').read_text(encoding='utf-8'))
    if not result['ok'] or len(result['results'])!=6: raise ValueError('Complete six-scene GL capture required')
    inventory=json.loads((folder/'gl-inventory.json').read_text(encoding='utf-8'))['items']; original={e['id']:e for e in inventory}
    entries=[];excluded=[];receipt=[]
    for scene in result['results']:
        ident=scene['id']; title,en_title,family,ko,en=NAMES[ident]
        cells,origin=read_terrain(source/(ident+'-terrain.json'))
        data=json.loads((source/(ident+'-terrain.json')).read_text(encoding='utf-8'))
        fertile=np.array(list(data['fertile_cells'])).reshape(cells.shape) if ident=='gl-oasis' else None
        try:
            command,loss=export(cells,ident.replace('-','_'),water=ident in ('gl-lake','gl-oasis','gl-archipelago'),fertile_cells=fertile)
        except ValueError as error:
            excluded.append({'id':ident,'reason':str(error),'source_retained':True});continue
        path=folder/'recipes'/(ident+'.json');write(path,command);write(path.with_suffix('.receipt.json'),{**origin,**loss})
        source_info=original[scene['gl_id']]
        kinds={op['shape']['compose'][-1].get('fill') for op in command['params']['shape_ops']}
        entry={'id':ident,'title_ko':title,'title_en':en_title,'description_ko':ko,'description_en':en,'family':family,
               'command':path.relative_to(folder).as_posix(),'status':'prototype-draft','challenge':ident=='gl-archipelago',
               'profiles':{'biomes':['Desert','AridShrubland'] if ident=='gl-oasis' else ['TemperateForest','AridShrubland'],'map_sizes':[250,300],'hilliness':['Flat'],'native_water':'Not supported for added water; explicit exclusion'},
               'features':{'new_water':bool(kinds & {'water','WaterShallow'}),'new_mountains':bool(np.any(cells=='M')),'global_density_edits':False},
               'source':{'kind':'GL graph sampled to editable polygons','author':'m00nl1ght','license':'CC-BY-NC-SA-4.0','url':source_info['source'],
                         'graph':source_info['file'],'graph_sha256':source_info['sha256'],'graph_revision':source_info['revision'],
                         'reference_run':source.relative_to(folder).as_posix(),'reference_id':ident,'source_settings':{'gl_id':scene['gl_id'],'map_size':scene['size'],'world_seed':scene['world_seed'],'tile':scene['tile'],'biome':scene['biome'],'mutators':scene['mutators']}},
               'limitations':['Sampled geometry, not a procedural GL graph conversion','Caves, roofs, incidents, resources and spawn logic are not transplanted','Visual approval pending; technical execution alone is not beauty']}
        entries.append(entry);receipt.append({'id':ident,**loss})
    # Palette calibration uses other scenes, never the held-out lake's labels.
    palette=[]
    for ident in ('gl-valley','gl-archipelago'):
        cells,_=read_terrain(source/(ident+'-terrain.json'))
        rgb=np.asarray(Image.open(source/(ident+'-map.png')).convert('RGB'))[::-1]
        for label in ('M','W','S','G'):
            colors=collections.Counter(map(tuple,rgb[cells==label].tolist()))
            for color,count in colors.most_common(3):
                if count>=50:palette.append({'label':label,'rgb':list(color),'calibration_scene':ident})
    palette_path=folder/'minimap-palette.json';write(palette_path,{'colors':palette,'max_distance':38,'protocol':'Native Map Preview colors, calibrated on valley/archipelago; lake held out'})
    cells,image_origin=read_image(source/'gl-lake-map.png',palette_path)
    # Brown ground, rough rock and shadows are ambiguous in a palette alone.
    # Keep this fallback to water silhouette/depth; never invent mountains from
    # uncertain brown pixels. Original game rocks remain on the target tile.
    image_command,image_loss=export(cells,'image_lake',mountains=False)
    write(folder/'recipes/image-lake.json',image_command);write(folder/'recipes/image-lake.receipt.json',{**image_origin,**image_loss})
    truth,_=read_terrain(source/'gl-lake-terrain.json')
    per_label={label:{'predicted_cells':int((cells==label).sum()),'actual_cells':int((truth==label).sum()),
        'precision':float(((cells==label)&(truth==label)).sum()/max(1,(cells==label).sum())),
        'recall':float(((cells==label)&(truth==label)).sum()/max(1,(truth==label).sum()))} for label in ('M','S','W','G')}
    write(folder/'image-evaluation.json',{'input':'source-final/gl-lake-map.png','calibration':['gl-valley','gl-archipelago'],'held_out':'gl-lake','pixel_label_accuracy':float(np.mean(cells==truth)),
         'per_label':per_label,'exported_features':['shallow footprint','deep cores'],'rocks_exported':False,**image_origin,**image_loss,'scope':'Known native minimap palette water contours only; not general vision, no API model used'})
    base=next(e for e in entries if e['id']=='gl-lake')
    image_entry=json.loads(json.dumps(base));image_entry.update({'id':'image-lake','title_ko':'이미지에서 읽은 호숫가','title_en':'Lakeside from an image','command':'recipes/image-lake.json'})
    image_entry['source']['kind']='Image palette and contour fallback';image_entry['limitations'].append('Known minimap palette only, unknown colors preserved as unclassified')
    image_entry['features']['new_mountains']=False
    image_entry['limitations'].append('Water contours only; ambiguous rock/ground/shadow colors are not transplanted')
    entries.append(image_entry)
    own=[('core-foothills','완만한 산기슭의 넓은 평지','Gentle foothill plain','foothills','큰 산기슭 옆에 이어진 넓은 평지. 새 물을 넣지 않는다.','Broad connected ground beside gentle foothills, with no new water.',
          {'shape_ops':[{'op':'add','shape':{'id':'library_foothills','type':'landform','landform':'foothills','layout':'organic','details':'natural','variant':'257','position':'top','size':'0.62','direction':'0'}}]}),
         ('core-dry-clearing','물과 산을 더하지 않는 작은 빈터','Small dry clearing','clearing','원래 풍경을 유지하면서 산과 물을 추가하지 않는 작은 정착 빈터.','Keep the original landscape, opening a modest settlement clearing without adding mountains or water.',
          {'shape_ops':[{'op':'add','shape':{'id':'library_clearing','type':'composite','details':'natural','edge_roughness':'medium','shapes':[{'id':'plain','prim':'ellipse','center':[.52,.48],'w':.30,'h':.26}],'compose':[{'op':'add','s':'plain','e':.05,'f':.008}]}}]})]
    for ident,title,en_title,family,ko,en,params in own:
        write(folder/'recipes'/(ident+'.json'),{'action':'generate','params':params})
        entries.append({'id':ident,'title_ko':title,'title_en':en_title,'family':family,'description_ko':ko,'description_en':en,'command':'recipes/'+ident+'.json','status':'prototype-draft','challenge':False,
                        'profiles':{'biomes':['TemperateForest','AridShrubland','Desert'],'map_sizes':[250,300],'hilliness':['Flat']},
                        'features':{'new_water':False,'new_mountains':ident=='core-foothills','global_density_edits':False},
                        'source':{'kind':'MapGenAI existing native generator','author':'MapGenAI','license':'Project original authoring','reference_id':ident},'limitations':['Visual approval pending','Modest local clearing can be a small change on an already flat tile']})
    write(folder/'catalog.json',{'schema_version':1,'purpose':'Developer prototype, not a distributed preset pack','entries':entries,'excluded':excluded,'reference_source_graphs':44,'paid_api_calls':0})
    write(folder/'conversion-receipt.json',{'entries':receipt,'excluded':excluded,'ground_policy':'Native biome ground preserved except explicitly requested oasis fertile areas','water_depth':'Shallow footprint first, sampled deep cores last; no automatic all-deep water'} )
    for name,seed,size in [('transfer-a','map-library-target-a-20261001',250),('transfer-b','map-library-target-b-20261001',300),('transfer-c','map-library-target-c-20261001',250)]:
        cases=[]
        for e in entries:
            biome=('Desert' if e['id']=='gl-oasis' else 'TemperateForest') if name!='transfer-c' else ('Desert' if e['id']=='gl-oasis' else 'AridShrubland')
            cases.append({'id':e['id'],'biome':biome,'size':size,'command':e['command']})
        write(folder/(name+'-manifest.json'),{'world_seed':seed,'cases':cases})
    print(json.dumps({'entries':len(entries),'excluded':excluded,'recipes':[e['id'] for e in entries]},ensure_ascii=False))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--folder',type=pathlib.Path,required=True);p.add_argument('--source',type=pathlib.Path,required=True);args=p.parse_args();build(args.folder,args.source)
