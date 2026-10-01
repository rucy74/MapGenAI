"""Lossless observed solid-rock occupancy for complete library compositions.

The source's actual natural-rock mask includes mineable veins, but never copies
ore definitions, source saves, or terrain guessed from screenshot colors. Native
resource generation stays on the current tile. Zero means unknown/preserve.
"""
import json
import numpy as np


def rock_layer(cells, source_biome):
    if cells.ndim != 2 or not cells.size:
        raise ValueError('Non-empty two-dimensional source cells required')
    values = np.zeros(cells.shape, dtype=np.uint8)
    values[np.isin(cells, ['G', 'S', 'W'])] = 1
    values[cells == 'M'] = 2
    flat = values.ravel()
    cuts = np.r_[0, np.flatnonzero(flat[1:] != flat[:-1]) + 1, len(flat)]
    runs = [{'start': int(a), 'length': int(b-a), 'kind': int(flat[a])}
            for a, b in zip(cuts[:-1], cuts[1:]) if flat[a]]
    layer = {'schema_version': 1, 'mode': 'source-composition',
             'width': cells.shape[1], 'height': cells.shape[0],
             'row_order': 'south-first', 'source_biome': source_biome,
             'runs': runs,
             'policy': 'Known complete inland occupancy only; native geology/resources, protected water/roads/buildings and unknown cells preserved'}
    return layer, {'rock_cells': int((values == 2).sum()),
                   'observed_nonrock_cells': int((values == 1).sum()),
                   'unknown_rock_cells': int((values == 0).sum()),
                   'rock_runs': len(runs), 'omitted_small_rock_cells': 0,
                   'rock_type_policy': 'Target native stone and ore generation, not a source resource transplant',
                   'runtime_contract': 'Developer rock occupancy sidecar; no product state or UI integration'}


def observed_rock(path):
    data = json.loads(path.read_text(encoding='utf-8'))
    if data.get('schema_version') != 2 or data.get('row_order') != 'south-first':
        raise ValueError('Actual per-cell terrain observation v2 required')
    width, height = data['width'], data['height']
    if width <= 0 or height <= 0 or len(data['cells']) != width*height:
        raise ValueError('Invalid source rock dimensions')
    cells = np.asarray(list(data['cells'])).reshape(height, width)
    return rock_layer(cells, data['biome'])


def decode(layer):
    if (layer.get('schema_version') != 1 or layer.get('mode') != 'source-composition'
            or layer.get('row_order') != 'south-first'):
        raise ValueError('Unsupported source rock contract')
    width, height = layer['width'], layer['height']
    if (not isinstance(width, int) or not isinstance(height, int)
            or width <= 0 or height <= 0):
        raise ValueError('Invalid rock dimensions')
    plane = np.zeros(width*height, dtype=np.uint8)
    occupied = np.zeros(len(plane), dtype=bool)
    for run in layer['runs']:
        start, length, kind = run['start'], run['length'], run['kind']
        if (not all(isinstance(x, int) for x in (start, length, kind))
                or start < 0 or length <= 0 or start+length > len(plane)
                or kind not in (1, 2) or occupied[start:start+length].any()):
            raise ValueError('Invalid or overlapping rock run')
        plane[start:start+length] = kind
        occupied[start:start+length] = True
    return plane.reshape(height, width)
