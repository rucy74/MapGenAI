"""Observed terrain/image -> editable existing polygon commands, with loss receipt.

This exports a sampled composition, not GL's procedural graph. It never exports
the source biome, world seed, mutators, density bonuses or dormant imageMap data.
"""
import argparse, hashlib, json, pathlib
import numpy as np
from PIL import Image
from skimage import measure, morphology

def read_terrain(path):
    data=json.loads(path.read_text(encoding='utf-8'))
    cells=np.array(list(data['cells'])).reshape(data['height'],data['width'])
    return cells, {'method':'observed-terrain','source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'row_order':'south-first'}

def read_image(path,palette_path):
    image=np.asarray(Image.open(path).convert('RGB'))[::-1].astype(float)
    palette=json.loads(palette_path.read_text(encoding='utf-8'))
    rgb=np.array([x['rgb'] for x in palette['colors']]); labels=np.array([x['label'] for x in palette['colors']])
    distances=np.sum((image[:,:,None,:]-rgb[None,None,:,:])**2,axis=-1)
    nearest=np.argmin(distances,axis=-1)
    cells=labels[nearest]; distance=np.sqrt(np.min(distances,axis=-1))
    uncertain=distance>palette.get('max_distance',38)
    cells[uncertain]='N'
    return cells,{'method':'image-palette-contours','source_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'palette_sha256':hashlib.sha256(palette_path.read_bytes()).hexdigest(),'unknown_fraction':float(np.mean(uncertain)),'note':'Known minimap palette only; screenshots with labels/UI, photos and arbitrary palettes require manual interpretation or a future vision adapter.'}

def polygon(contour,height,width):
    # Border sampling uses half cells outside the map. Clamp and then remove
    # duplicates/collinear points; repeated border vertices fail the real parser.
    for tolerance in (0.7,1,1.4,2,2.8,4):
        simplified=measure.approximate_polygon(contour,tolerance=tolerance)
        points=[]
        for z,x in simplified:
            p=[round(float(np.clip((x-1)/(width-1),0,1)),5),round(float(np.clip((z-1)/(height-1),0,1)),5)]
            if not points or points[-1]!=p: points.append(p)
        if len(points)>1 and points[0]==points[-1]: points.pop()
        changed=True
        while changed and len(points)>3:
            changed=False
            for i in range(len(points)):
                a,b,c=np.array(points[i-1]),np.array(points[i]),np.array(points[(i+1)%len(points)])
                if abs((b[0]-a[0])*(c[1]-b[1])-(b[1]-a[1])*(c[0]-b[0]))<1e-8:
                    points.pop(i);changed=True;break
        if 3<=len(points)<=64: return points
    raise ValueError('Contour cannot fit existing 64-vertex polygon budget')

def component_shapes(mask,label,minimum_cells):
    components=measure.label(mask,connectivity=1)
    shapes=[]; skipped=0
    for region in measure.regionprops(components):
        if region.area<minimum_cells:
            skipped+=int(region.area);continue
        part=components==region.label
        contours=measure.find_contours(np.pad(part.astype(float),1),.5)
        # Positive orientation outlines the outer boundary; holes have the
        # opposite winding. Use signed area relative to the largest contour.
        areas=[sum(c[i,1]*c[(i+1)%len(c),0]-c[(i+1)%len(c),1]*c[i,0] for i in range(len(c)))/2 for c in contours]
        outer_index=max(range(len(contours)),key=lambda i:abs(areas[i])); sign=np.sign(areas[outer_index])
        primitives=[];operations=[]; outer_id=None;last=None
        order=sorted(range(len(contours)),key=lambda i:-abs(areas[i]))
        for j in order:
            if abs(areas[j])<minimum_cells: skipped+=int(abs(areas[j]));continue
            pid='p'+str(len(primitives));primitives.append({'id':pid,'prim':'poly','verts':polygon(contours[j],*mask.shape)})
            if np.sign(areas[j])==sign:
                if outer_id is not None: raise ValueError('Unexpected nested exterior; refuse ambiguous topology')
                outer_id=last=pid
            else:
                # Existing SdfComposite.OpSubtract(a, from) means from minus a.
                # The hole is the cutter, not the target (checked in native replay).
                out='cut'+str(len(operations));operations.append({'op':'sub','a':pid,'from':last,'out':out});last=out
        if outer_id is None: continue
        operation={'op':'add','s':last,'e':1.05 if label=='M' else .05,'f':.006}
        if label in ('W','S','F','R'): operation['fill']={'W':'water','S':'WaterShallow','F':'soil','R':'rich_soil'}[label]
        operations.append(operation)
        if len(primitives)>32 or len(operations)>32: raise ValueError('Existing composite operation budget exceeded')
        shape={'id':label.lower()+'_'+str(len(shapes)), 'type':'composite','edge_roughness':'none','details':'natural','water_profile':'native','shapes':primitives,'compose':operations}
        # water_profile is only valid for fresh water and belongs to the shape,
        # not the catalog or global generation settings.
        if label not in ('W','S'): shape.pop('water_profile')
        shapes.append(shape)
    return shapes,skipped

def export(cells,prefix,water=True,mountains=True,fertile_cells=None):
    minimum=max(40,int(cells.size*.0015));shapes=[];loss={}
    fields=[]
    if fertile_cells is not None: fields.extend([('F',fertile_cells=='F'),('R',fertile_cells=='R')])
    fields.extend([('M',cells=='M'),('S',np.isin(cells,['W','S'])),('W',cells=='W')])
    for label,mask in fields:
        if (label=='M' and not mountains) or (label in ('W','S') and not water): continue
        mask=morphology.closing(mask,morphology.disk(1),mode='ignore')
        parts,skipped=component_shapes(mask,label,minimum)
        for shape in parts:
            shape['id']=prefix+'_'+shape['id'];shapes.append(shape)
        loss[label]={'input_cells':int(np.count_nonzero(mask)),'omitted_small_cells':skipped,'components':len(parts)}
    if not shapes: raise ValueError('No reusable terrain detected')
    if len(shapes)>24: raise ValueError('Too many fragments for a useful composition; reject rather than scatter dots')
    # Explicit oasis fertile ground first, mountains, shallow water, deep cores.
    # Ordinary scenes never import incidental fertility/soil patches.
    command={'action':'generate','params':{'shape_ops':[{'op':'add','shape':s} for s in shapes]}}
    return command,{'loss':loss,'shape_count':len(shapes),'ground_policy':'Preserve native biome terrain; only occupied rock/water outlines are authored','seed_policy':'Current tile seed; sampled source composition retained','procedural_reproduction':False}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--terrain',type=pathlib.Path);p.add_argument('--image',type=pathlib.Path);p.add_argument('--palette',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--prefix',required=True);p.add_argument('--include-image-rocks',action='store_true')
    args=p.parse_args();cells,source=read_terrain(args.terrain) if args.terrain else read_image(args.image,args.palette)
    command,receipt=export(cells,args.prefix,mountains=bool(args.terrain or args.include_image_rocks));args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(command,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    args.output.with_suffix('.receipt.json').write_text(json.dumps({**source,**receipt},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({**source,**receipt},ensure_ascii=False))
