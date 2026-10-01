"""Observed native geology for complete developer compositions, never image guesses."""
import hashlib
import json
import numpy as np

ROOFS = {'None': 0, 'RoofRockThin': 1, 'RoofRockThick': 2}


def dimensions(data):
    width, height = data.get('width'), data.get('height')
    if (type(width) is not int or type(height) is not int
            or width <= 0 or height <= 0 or data.get('row_order') != 'south-first'):
        raise ValueError('Positive integer dimensions and south-first order required')
    return width, height


def numbers(data, name, shape, integer=False):
    raw = data.get(name)
    if not isinstance(raw, list) or len(raw) != shape[0]*shape[1]:
        raise ValueError('Missing or invalid full grid: '+name)
    if any(isinstance(n, bool) or not isinstance(n, (int, float)) for n in raw):
        raise ValueError('Numeric grid required: '+name)
    if integer and any(type(n) is not int for n in raw):
        raise ValueError('Integer grid required: '+name)
    grid = np.asarray(raw, dtype=np.float64).reshape(shape)
    if not np.isfinite(grid).all() or np.abs(grid).max() > np.finfo(np.float32).max:
        raise ValueError('Finite native float grid required: '+name)
    return grid.astype(np.int64) if integer else grid


def observed_cave(terrain_path, geology_path, palette=None):
    """Read independently captured working grids before native ClearWorkingData."""
    terrain = json.loads(terrain_path.read_text(encoding='utf-8'))
    data = json.loads(geology_path.read_text(encoding='utf-8'))
    if terrain.get('schema_version') != 2 or data.get('schema_version') != 1:
        raise ValueError('Actual terrain v2 and geology v1 observations required')
    width, height = dimensions(data)
    if dimensions(terrain) != (width, height):
        raise ValueError('Source terrain/geology dimensions differ')
    shape = (height, width)
    sha = hashlib.sha256(terrain_path.read_bytes()).hexdigest()
    if data.get('source_terrain_sha256', '').lower() != sha:
        raise ValueError('Geology does not belong to the actual terrain capture')
    cells = terrain.get('cells')
    if not isinstance(cells, str) or len(cells) != width*height or set(cells)-set('MGSWN'):
        raise ValueError('Invalid observed occupancy')
    cells = np.asarray(list(cells)).reshape(shape)
    elevation = numbers(data, 'elevation', shape)
    caves = numbers(data, 'caves', shape)
    if (caves < 0).any():
        raise ValueError('Negative cave intensity is unsupported')
    roof_indices = numbers(data, 'roof_indices', shape, integer=True)
    table = data.get('roof_table')
    if (not isinstance(table, list) or not table or table[0] != 'None'
            or any(not isinstance(n, str) or not n for n in table)
            or len(set(table)) != len(table)
            or roof_indices.min() < 0 or roof_indices.max() >= len(table)):
        raise ValueError('Invalid actual roof table/indices')
    walkable = data.get('walkable')
    if not isinstance(walkable, str) or len(walkable) != width*height or set(walkable)-set('01'):
        raise ValueError('Actual walkability required')
    walkable = np.asarray(list(walkable)).reshape(shape) == '1'
    observed = data.get('known_mask', '1'*(width*height))
    if not isinstance(observed, str) or len(observed) != width*height or set(observed)-set('01'):
        raise ValueError('Invalid observation known mask')
    roof_names = np.asarray(table)[roof_indices]
    known = (np.asarray(list(observed)).reshape(shape) == '1') & (cells != 'N')
    unsupported_roof = ~np.isin(roof_names, list(ROOFS))
    known &= ~unsupported_roof
    # Physical source classifications are captured from actual native objects,
    # including ancient floors with a null designation category. A material
    # label/color or an impassable water tile cannot stand in for these masks.
    for field in ('constructed_floor', 'nonrock_edifice'):
        mask = data.get(field)
        if not isinstance(mask, str) or len(mask) != width*height or set(mask)-set('01'):
            raise ValueError('Actual physical source mask required: '+field)
        known &= np.asarray(list(mask)).reshape(shape) == '0'
    roofs = np.zeros(shape, dtype=np.uint8)
    for name, code in ROOFS.items():
        roofs[roof_names == name] = code
    layer = {'schema_version': 1, 'mode': 'source-geology', 'width': width,
             'height': height, 'row_order': 'south-first', 'source_biome': terrain['biome'],
             'known': ''.join(np.where(known.ravel(), 'K', 'N')),
             'elevation': elevation.ravel().tolist(), 'caves': caves.ravel().tolist(),
             'roof_codes': roofs.ravel().tolist(), 'source_terrain_sha256': sha,
             'source_geology_sha256': hashlib.sha256(geology_path.read_bytes()).hexdigest(),
             'policy': 'Observed natural geology only; protected current structures, roofs, water and unknown preserved; native target stone/resources'}
    receipt = {'known_geology_cells': int(known.sum()), 'unknown_geology_cells': int((~known).sum()),
               'raw_cave_cells': int((caves > 0).sum()),
               'portable_cave_cells': int(((caves > 0) & known).sum()),
               'raw_natural_roof_cells': int(np.isin(roof_names, ['RoofRockThin', 'RoofRockThick']).sum()),
               'unsupported_source_roof_cells': int(unsupported_roof.sum()),
               'source_terrain_sha256': sha, 'source_geology_sha256': layer['source_geology_sha256'],
               'runtime_contract': 'Explicit developer cave geology; not product UI/save/Undo or inferred from an image',
               'scaling': 'Nearest source cell; observed grid intensities retained, coordinate width scales with map size'}
    return layer, receipt


def decode(layer):
    if layer.get('schema_version') != 1 or layer.get('mode') != 'source-geology':
        raise ValueError('Unsupported cave geology contract')
    width, height = dimensions(layer)
    shape = (height, width)
    known = layer.get('known')
    if not isinstance(known, str) or len(known) != width*height or set(known)-set('KN'):
        raise ValueError('Invalid cave known mask')
    result = {'known': np.asarray(list(known)).reshape(shape) == 'K',
              'elevation': numbers(layer, 'elevation', shape),
              'caves': numbers(layer, 'caves', shape),
              'roof': numbers(layer, 'roof_codes', shape, integer=True)}
    if (result['caves'] < 0).any() or not np.isin(result['roof'], [0, 1, 2]).all():
        raise ValueError('Unsupported caves or natural roof code')
    return result


def resample(fields, width, height):
    if type(width) is not int or type(height) is not int or width <= 0 or height <= 0:
        raise ValueError('Positive target dimensions required')
    shape = fields['known'].shape
    if len(shape) != 2 or not all(v.shape == shape for v in fields.values()):
        raise ValueError('Consistent source fields required')
    ys = np.arange(height)*shape[0]//height
    xs = np.arange(width)*shape[1]//width
    return {name: grid[ys[:, None], xs] for name, grid in fields.items()}
