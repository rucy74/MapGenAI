"""Named natural ground and conservative native-preview color interpretation.

Ground is a separate developer sidecar, never a source save/world snapshot. Zero
cells preserve the target. Runtime protection and biome adaptation remain explicit.
"""
import hashlib, json
import numpy as np
from PIL import Image
from skimage import measure

def role(terrain):
    name=terrain['def']
    known={'Soil':'base','SoilRich':'fertile','Sand':'sand','SoftSand':'sand','Gravel':'gravel',
           'Mud':'mud','MarshyTerrain':'marsh','Ice':'ice'}
    if name in known:return known[name]
    if name.endswith(('_Rough','_RoughHewn')):return 'rock-ground'
    if terrain.get('fertility',0)>1:return 'fertile'
    if terrain.get('fertility',0)>0:return 'base'
    return 'unknown'

def ground_layer(names, cells, palette, source_biome, minimum=12):
    """RLE for actual, supported, dry natural ground only; no guessed material."""
    definitions={t['def']:t for t in palette['terrains']}
    materials=[];plane=np.zeros(cells.shape,dtype=np.int32);omitted={}
    for name in sorted(set(names[cells=='G'].tolist())-{'','N'}):
        mask=(names==name)&(cells=='G');meta=definitions.get(name)
        if not meta or not meta['supported'] or meta['water'] or meta['temporary'] or meta.get('dangerous') or role(meta)=='unknown':
            omitted[name]={'cells':int(mask.sum()),'reason':'Unavailable, unsafe, or no portable ground role'};continue
        components=measure.label(mask,connectivity=1);counts=np.bincount(components.ravel());counts[0]=0
        retained=mask & (counts[components]>=minimum)
        if not retained.any():
            omitted[name]={'cells':int(mask.sum()),'reason':'Only tiny components'};continue
        materials.append({'def':name,'role':role(meta)})
        plane[retained]=len(materials)
        if mask.sum()>retained.sum():omitted[name]={'cells':int(mask.sum()-retained.sum()),'reason':'Tiny components'}
    flat=plane.ravel();changes=np.r_[0,np.flatnonzero(flat[1:]!=flat[:-1])+1,len(flat)]
    runs=[{'start':int(a),'length':int(b-a),'material':int(flat[a])} for a,b in zip(changes[:-1],changes[1:]) if flat[a]]
    layer={'schema_version':1,'width':cells.shape[1],'height':cells.shape[0],'row_order':'south-first',
           'source_biome':source_biome,'materials':materials,'runs':runs,
           'policy':'Native water, rock, buildings, roads and elevations protected at application; unknown preserved'}
    return layer,{'ground_cells':int((plane>0).sum()),'ground_materials':materials,'ground_runs':len(runs),
                  'omitted_ground':omitted,'ground_policy':layer['policy'],
                  'runtime_contract':'Developer ground sidecar; not yet serialized by product TileMapState'}

def observed_ground(path,cells,palette,minimum=12):
    data=json.loads(path.read_text(encoding='utf-8'))
    if data.get('schema_version')!=2 or data.get('row_order')!='south-first':
        raise ValueError('Per-cell TerrainDef v2 capture required; a histogram cannot recreate ground')
    table=np.asarray(data['terrain_table']);indices=np.asarray(data['terrain_indices']).reshape(cells.shape)
    if indices.min()<0 or indices.max()>=len(table):raise ValueError('Invalid terrain table indices')
    return ground_layer(table[indices],cells,palette,data['biome'],minimum=minimum)

def image_palette(native,mode='true'):
    if mode not in ('true','default'):raise ValueError('Unknown native preview mode')
    colors=[];missing=[]
    for terrain in native['terrains']:
        name=terrain['def'];label='N';material=None
        # RGB gives depth, not the original river/lake/mutator semantics.
        if name in ('WaterDeep','WaterOceanDeep','WaterMovingDeep'):label='W'
        elif name in ('WaterShallow','WaterOceanShallow','WaterMovingShallow','WaterMovingChestDeep'):label='S'
        elif terrain['supported'] and not terrain['water'] and not terrain['temporary'] and not terrain.get('dangerous') and role(terrain)!='unknown':label='G';material=name
        found=terrain['has_preview_color'] if mode=='true' else terrain.get('default_has_preview_color',False)
        rgb=terrain['rgb'] if mode=='true' else terrain.get('default_rgb')
        if not found:
            missing.append(name);continue
        colors.append({'label':label,'terrain':material,'rgb':rgb,'native_def':name})
    for overlay in native['overlays']:
        label='N' if overlay['name']=='MissingTerrainColor' else 'M'
        colors.append({'label':label,'terrain':None,'rgb':overlay['rgb'],'overlay':overlay['name']})
    return {'schema_version':2,'renderer_mode':mode,'colors':colors,'max_distance':2,'min_margin':1,
            'missing_preview_colors':missing,'protocol':'Loaded native renderer catalog, exact-color tolerance; competing materials become unknown'}

def read_image_materials(path,palette_path):
    rgb=np.asarray(Image.open(path).convert('RGB'))[::-1].astype(np.float32)
    palette=json.loads(palette_path.read_text(encoding='utf-8'))
    # Collapse aliases of the same semantic class, while keeping Soil/SoilRich/
    # gravel distinct even when their RGB is identical. No nearest-color guess.
    keys=[];samples=[]
    for row in palette['colors']:
        key=(row['label'],row.get('terrain') or '')
        if key not in keys:keys.append(key)
        samples.append((keys.index(key),np.asarray(row['rgb'],dtype=np.float32)))
    if not samples:raise ValueError('Empty color palette')
    flat=rgb.reshape(-1,3);labels=np.full(len(flat),'N');names=np.full(len(flat),'',dtype=object)
    distant=ambiguous=0
    for start in range(0,len(flat),2048):
        part=flat[start:start+2048];dist=np.full((len(part),len(keys)),np.inf,dtype=np.float32)
        for index,color in samples:dist[:,index]=np.minimum(dist[:,index],np.sqrt(np.sum((part-color)**2,axis=1)))
        nearest=np.argmin(dist,axis=1);best=dist[np.arange(len(part)),nearest]
        second=np.partition(dist,1,axis=1)[:,1] if len(keys)>1 else np.full(len(part),np.inf)
        far=best>palette.get('max_distance',2);conflict=(second-best)<palette.get('min_margin',1)
        distant+=int(far.sum());ambiguous+=int((conflict & ~far).sum())
        for index,key in enumerate(keys):
            selected=(nearest==index)&~far&~conflict
            labels[start:start+len(part)][selected]=key[0];names[start:start+len(part)][selected]=key[1]
    cells=labels.reshape(rgb.shape[:2]);names=names.reshape(cells.shape)
    receipt={'method':'native-palette-with-ambiguity-rejection','source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
             'palette_sha256':hashlib.sha256(palette_path.read_bytes()).hexdigest(),
             'unknown_fraction':float((cells=='N').mean()),'distant_pixels':distant,'ambiguous_pixels':ambiguous,
             'recognized_unusable_pixels':int((cells=='N').sum())-distant-ambiguous,
             'palette_colors':len(samples),'semantic_classes':len(keys),
             'note':'Known native renderer only; unknown/conflicting ground preserved. A color cannot prove river, hot spring, seasonal underlying water or constructed-floor identity.'}
    return cells,names,receipt
