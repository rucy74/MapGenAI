"""Observed inland water layout, including small pools and known dry space.

This developer sidecar belongs to a complete source composition. It is never an
instruction to clear unknown pixels or native rivers/coasts in a partial edit.
"""
import json
import numpy as np
from ground import role

FRESH = {'WaterShallow': 2, 'WaterDeep': 3}

def water_layer(cells, names, palette, biome, image=False):
    definitions = {t['def']: t for t in palette['terrains']}
    plane = np.zeros(cells.shape, dtype=np.uint8)
    for name in set(names.ravel().tolist()) - {'', 'N'}:
        meta = definitions.get(name)
        if not meta or meta.get('temporary') or meta.get('dangerous') or meta.get('river'):
            continue
        mask = names == name
        if name in FRESH:
            plane[mask & np.isin(cells, ['W', 'S'])] = FRESH[name]
        elif not meta['water'] and (not image or (meta['supported'] and role(meta) != 'unknown')):
            # A recorded constructed/unknown-role dry material still proves dry
            # space. This does not copy that material or authorize changes to a
            # target building/floor; runtime protects those independently.
            plane[mask & np.isin(cells, ['G', 'M'])] = 1
    if image:
        # Only used for this catalog's known inland GL Lake preview. A generic
        # image color cannot establish river, sea or hot-spring identity.
        plane[cells == 'S'] = 2
        plane[cells == 'W'] = 3
    flat = plane.ravel()
    ends = np.r_[0, np.flatnonzero(flat[1:] != flat[:-1]) + 1, len(flat)]
    runs = [{'start': int(a), 'length': int(b-a), 'kind': int(flat[a])}
            for a, b in zip(ends[:-1], ends[1:]) if flat[a]]
    layer = {'schema_version': 1, 'mode': 'source-composition', 'width': cells.shape[1],
             'height': cells.shape[0], 'row_order': 'south-first', 'source_biome': biome,
             'source_kind': 'known-inland-preview' if image else 'observed-permanent-terrain',
             'runs': runs, 'policy': 'Known dry cells clear ordinary ponds; special/connected water, roads and structures protected; unknown untouched'}
    return layer, {'source_water_cells': int((plane >= 2).sum()), 'known_dry_cells': int((plane == 1).sum()),
                   'unclassified_water_scope_cells': int((plane == 0).sum()), 'water_runs': len(runs),
                   'water_fragment_omissions': 0, 'water_policy': layer['policy']}

def observed_water(path, cells, palette):
    data = json.loads(path.read_text(encoding='utf-8'))
    if data.get('schema_version') != 2 or data.get('row_order') != 'south-first':
        raise ValueError('Per-cell permanent TerrainDef v2 capture required for water layout')
    names = np.asarray(data['terrain_table'])[np.asarray(data['terrain_indices']).reshape(cells.shape)]
    return water_layer(cells, names, palette, data['biome'])

def decode(layer):
    if layer.get('schema_version') != 1 or layer.get('mode') != 'source-composition' or layer.get('row_order') != 'south-first':
        raise ValueError('Invalid source water coordinate contract')
    width, height = layer['width'], layer['height']
    if width <= 0 or height <= 0:
        raise ValueError('Invalid water dimensions')
    plane = np.zeros(width*height, dtype=np.uint8); end = 0
    for run in layer['runs']:
        start, length, kind = run['start'], run['length'], run['kind']
        if start < end or length <= 0 or start+length > len(plane) or kind not in (1, 2, 3):
            raise ValueError('Invalid water RLE')
        plane[start:start+length] = kind; end = start+length
    return plane.reshape(height, width)
