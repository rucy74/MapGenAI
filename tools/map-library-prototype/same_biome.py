"""Post-initialization static map replica contract; developer-only, no save clone.

Old terrain/geology observations are hash-bound provenance from genstep 99999.
Their fields may differ from the final raw replica; they are not final truth.
Only direct final replicas and final PNG pixels are compared for exact fidelity.
"""
import copy
import hashlib
import json
import pathlib

import numpy as np
from PIL import Image


PHASE = 'post-map-initialized'
LAYERS = ('ground_layer', 'water_layer', 'rock_layer', 'cave_layer')
TERRAIN_GRIDS = ('top_indices', 'permanent_indices', 'surface_indices',
                 'under_indices', 'foundation_indices', 'temp_indices')
BINDINGS = ('terrain', 'geology', 'map_png', 'map_default_png')
MAX_FLOAT = float(np.finfo(np.float32).max)
AUDIT_FIELDS = TERRAIN_GRIDS + ('color_indices', 'roof_indices', 'edifice_indices',
    'elevation', 'caves', 'fertility', 'walkable', 'terrain_table', 'roof_table', 'color_table', 'edifice_records',
    'unsupported_features')
AUDIT_COUNTS = ('existing_things_before', 'existing_roofs_before', 'existing_terrain_cells_before',
                'protected_changes', 'unknown_changes', 'user_state_changes', 'unsafe_roof_cells')


def hash_string(value):
    return isinstance(value, str) and len(value) == 64 and all(char in '0123456789abcdef' for char in value)


def read(path):
    return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))


def sha256(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def dimensions(data):
    values = data.get('width'), data.get('height')
    if any(type(value) is not int or value <= 0 for value in values):
        raise ValueError('Invalid replica dimensions')
    if data.get('row_order') != 'south-first':
        raise ValueError('Replica row order must be south-first')
    return values


def table(data, name):
    values = data.get(name)
    if (not isinstance(values, list) or not values or values[0] != 'None'
            or any(not isinstance(value, str) or not value for value in values)
            or len(set(values)) != len(values)):
        raise ValueError('Invalid replica table: ' + name)
    return np.asarray(values, dtype=object)


def numbers(data, name, area, integer=False):
    values = data.get(name)
    if not isinstance(values, list) or len(values) != area:
        raise ValueError('Missing/full replica numeric grid required: ' + name)
    if integer:
        if any(type(value) is not int for value in values):
            raise ValueError('Invalid integer replica grid: ' + name)
        return np.asarray(values, dtype=np.int64)
    if any(type(value) not in (int, float) for value in values):
        raise ValueError('Invalid numeric replica grid: ' + name)
    result = np.asarray(values, dtype=np.float64)
    if not np.isfinite(result).all() or (np.abs(result) > MAX_FLOAT).any():
        raise ValueError('Invalid finite float replica grid: ' + name)
    return result


def bits(data, name, area):
    value = data.get(name)
    if not isinstance(value, str) or len(value) != area or set(value) - set('01'):
        raise ValueError('Missing/invalid actual replica mask: ' + name)
    return np.asarray(list(value)) == '1'


def indexed(data, name, values, area, nonnull=False):
    indices = numbers(data, name, area, integer=True)
    if ((indices < (1 if nonnull else 0)).any() or (indices >= len(values)).any()):
        raise ValueError('Replica index outside table: ' + name)
    return values[indices]


def validate_snapshot(data):
    """Validate native final raw data; never invent None/zero for missing grids."""
    if (not isinstance(data, dict) or type(data.get('schema_version')) is not int
            or data['schema_version'] != 1 or data.get('mode') != 'source-replica'):
        raise ValueError('Invalid raw replica schema/mode')
    width, height = dimensions(data)
    area = width * height
    biome = data.get('biome')
    if not isinstance(biome, str) or not biome:
        raise ValueError('Missing replica biome')
    if data.get('unsupported') != []:
        raise ValueError('Missing/unsupported original static state; reject replica')
    if not bits(data, 'known_mask', area).all():
        raise ValueError('Unknown source replica cells; exact transfer fails closed')
    context = data.get('context')
    if (not isinstance(context, dict) or type(context.get('stage')) is not int
            or context['stage'] != 99999 or context.get('capture_phase') != PHASE
            or context.get('source_biome') != biome):
        raise ValueError('Final post-initialization replica context required')
    for name in ('hilliness', 'engine_version'):
        if not isinstance(context.get(name), str) or not context[name]:
            raise ValueError('Missing replica environment: ' + name)
    if not hash_string(context.get('engine_assembly_sha256')):
        raise ValueError('Actual replica game assembly hash required')
    for name in ('gl_patch_active', 'gl_stable_cave_roof_override'):
        if type(context.get(name)) is not bool:
            raise ValueError('Invalid replica environment flag: ' + name)
    packages = context.get('active_package_ids')
    if (not isinstance(packages, list) or not packages
            or any(not isinstance(value, str) or not value for value in packages)
            or len(set(packages)) != len(packages)):
        raise ValueError('Invalid loaded replica package ids')
    terrain = table(data, 'terrain_table')
    result = {name: indexed(data, name, terrain, area,
                           nonnull=name in ('permanent_indices', 'surface_indices'))
              for name in TERRAIN_GRIDS}
    result['color_indices'] = indexed(data, 'color_indices', table(data, 'color_table'), area)
    roof_table = table(data, 'roof_table')
    result['roof_indices'] = indexed(data, 'roof_indices', roof_table, area)
    roof_metadata = data.get('roof_def_metadata')
    if not isinstance(roof_metadata, dict):
        raise ValueError('Actual loaded RoofDef metadata required')
    for name in roof_table[1:]:
        metadata = roof_metadata.get(name)
        if (not isinstance(metadata, dict)
                or any(type(metadata.get(key)) is not bool
                       for key in ('is_natural', 'can_collapse', 'is_thick_roof'))):
            raise ValueError('Unknown/missing loaded RoofDef metadata: ' + name)
    for name in ('elevation', 'caves', 'fertility'):
        result[name] = numbers(data, name, area)
    if (result['caves'] < 0).any():
        raise ValueError('Negative replica cave values')
    for name in ('walkable', 'native_roof_supported', 'projected_roof_supported'):
        result[name] = bits(data, name, area)
    collapsible_roofs = np.isin(result['roof_indices'], [name for name in roof_table[1:]
        if roof_metadata[name]['can_collapse']])
    if (collapsible_roofs & (~result['native_roof_supported'] | ~result['projected_roof_supported'])).any():
        raise ValueError('Unsafe original loaded roof; reject replica')
    edifices = data.get('edifices')
    if not isinstance(edifices, list):
        raise ValueError('Actual unique replica edifice records required')
    indices = numbers(data, 'edifice_indices', area, integer=True)
    if (indices < 0).any() or (indices > len(edifices)).any():
        raise ValueError('Invalid replica edifice index')
    covered = np.zeros(area, dtype=np.int64)
    canonical = []
    for ident, record in enumerate(edifices, 1):
        if not isinstance(record, dict) or type(record.get('id')) is not int or record['id'] != ident:
            raise ValueError('Replica edifice ids must be consecutive record ids')
        for name in ('def', 'stuff'):
            if not isinstance(record.get(name), str) or not record[name] or (name == 'def' and record[name] == 'None'):
                raise ValueError('Invalid replica edifice definition/stuff')
        for name, limit in (('x', width), ('z', height), ('rotation', 4)):
            if type(record.get(name)) is not int or not 0 <= record[name] < limit:
                raise ValueError('Invalid replica edifice coordinate/rotation')
        if (type(record.get('uses_hit_points')) is not bool
                or type(record.get('hit_points')) is not int
                or (record['uses_hit_points'] and record['hit_points'] <= 0)
                or (not record['uses_hit_points'] and record['hit_points'] < -1)):
            raise ValueError('Invalid replica edifice hit points')
        if any(type(record.get(name)) is not bool for name in ('natural_rock', 'resource_rock', 'door_open')):
            raise ValueError('Missing actual replica edifice state flags')
        if record['door_open'] or (record['resource_rock'] and not record['natural_rock']):
            raise ValueError('Unsupported/inconsistent actual replica edifice state')
        footprint = record.get('footprint')
        if (not isinstance(footprint, list) or not footprint
                or any(type(cell) is not int or not 0 <= cell < area for cell in footprint)
                or footprint != sorted(set(footprint))
                or record['z'] * width + record['x'] not in footprint):
            raise ValueError('Invalid replica edifice footprint/root')
        if (covered[footprint] != 0).any():
            raise ValueError('Overlapping replica edifice footprints')
        covered[footprint] = ident
        canonical.append((record['def'], record['stuff'], record['x'], record['z'], record['rotation'],
                          tuple(footprint), record['uses_hit_points'], record['hit_points'],
                          record['natural_rock'], record['resource_rock'], record['door_open']))
    if not np.array_equal(indices, covered):
        raise ValueError('Replica per-cell edifice index disagrees with actual footprints')
    names = np.asarray(['None'] + [record['def'] for record in edifices], dtype=object)
    result['edifice_indices'] = names[indices]
    result['edifices'] = sorted(canonical)
    for name, value in tuple(result.items()):
        if isinstance(value, np.ndarray):
            result[name] = value.reshape(height, width)
    result.update(width=width, height=height, biome=biome)
    bindings, files = data.get('source_bindings'), data.get('binding_files')
    if not isinstance(bindings, dict) or not isinstance(files, dict):
        raise ValueError('Replica source bindings and actual file names required')
    for name in BINDINGS:
        digest, file = bindings.get(name + '_sha256'), files.get(name)
        if not hash_string(digest):
            raise ValueError('Invalid replica source hash: ' + name)
        if (not isinstance(file, str) or not file or '/' in file or '\\' in file
                or pathlib.PureWindowsPath(file).drive or file in ('.', '..')):
            raise ValueError('Replica binding file must be a local actual file name')
    return result


def png_pixels(path, width, height):
    with Image.open(path) as image:
        if image.format != 'PNG' or image.size != (width, height):
            raise ValueError('Actual final PNG dimensions/format disagree')
        return np.asarray(image.convert('RGBA')).copy()


def load_capture(snapshot_path, terrain_path=None, geology_path=None,
                 map_png_path=None, map_default_png_path=None):
    path = pathlib.Path(snapshot_path)
    data = read(path)
    fields = validate_snapshot(data)
    supplied = (terrain_path, geology_path, map_png_path, map_default_png_path)
    paths = {}
    for name, explicit in zip(BINDINGS, supplied):
        actual = pathlib.Path(explicit) if explicit is not None else path.parent / data['binding_files'][name]
        if actual.name != data['binding_files'][name]:
            raise ValueError('Replica actual binding file name disagrees: ' + name)
        if sha256(actual) != data['source_bindings'][name + '_sha256']:
            raise ValueError('Replica source SHA binding disagrees: ' + name)
        paths[name] = actual
    terrain, geology = read(paths['terrain']), read(paths['geology'])
    if (type(terrain.get('schema_version')) is not int or terrain['schema_version'] != 2
            or type(geology.get('schema_version')) is not int or geology['schema_version'] != 1
            or dimensions(terrain) != dimensions(data) or dimensions(geology) != dimensions(data)
            or terrain.get('biome') != data['biome']
            or geology.get('source_terrain_sha256') != data['source_bindings']['terrain_sha256']):
        raise ValueError('Replica bound original terrain/geology provenance disagrees')
    if not bits(geology, 'known_mask', fields['width'] * fields['height']).all():
        raise ValueError('Unknown original geology observation; reject exact transfer')
    images = {name: png_pixels(paths[name], fields['width'], fields['height'])
              for name in ('map_png', 'map_default_png')}
    return data, fields, images


def relative_file(value):
    if (not isinstance(value, str) or not value or '\x00' in value
            or pathlib.Path(value).is_absolute() or pathlib.PureWindowsPath(value).drive
            or pathlib.PureWindowsPath(value).is_absolute()):
        raise ValueError('Replica snapshot_file must be a relative path')
    return value


def resolve_snapshot_file(layer, command_path, artifact_root):
    """../ is valid between recipe/source siblings, never outside the artifact."""
    value = relative_file(layer.get('snapshot_file'))
    root = pathlib.Path(artifact_root).resolve()
    path = (pathlib.Path(command_path).resolve().parent / value).resolve()
    if not path.is_relative_to(root):
        raise ValueError('Replica snapshot file escapes the manifest artifact root')
    return path


def observed_replica(snapshot_path, terrain_path=None, geology_path=None,
                     map_png_path=None, map_default_png_path=None, *, snapshot_file=None):
    data, fields, _ = load_capture(snapshot_path, terrain_path, geology_path, map_png_path, map_default_png_path)
    layer = {'schema_version': 1, 'mode': 'source-replica', 'source_biome': data['biome'],
             'width': fields['width'], 'height': fields['height'],
             'snapshot_file': relative_file(snapshot_file if snapshot_file is not None else pathlib.Path(snapshot_path).name),
             'sha256': sha256(snapshot_path)}
    return layer, {'source_replica_sha256': layer['sha256'], 'capture_phase': PHASE,
                   'exact_observed_cells': fields['width'] * fields['height'],
                   'scope': 'Final static terrain grids, edifices, roofs, E/C/fertility/walkability and PNG pixels; not pawns/plants/quests or a source save'}


def load_replica_layer(layer, command_path, artifact_root):
    keys = {'schema_version', 'mode', 'source_biome', 'width', 'height', 'snapshot_file', 'sha256'}
    if (not isinstance(layer, dict) or set(layer) != keys
            or type(layer.get('schema_version')) is not int or layer['schema_version'] != 1
            or layer.get('mode') != 'source-replica'):
        raise ValueError('Replica command must contain schema1 file metadata only')
    path = resolve_snapshot_file(layer, command_path, artifact_root)
    if layer['sha256'] != sha256(path):
        raise ValueError('Replica command snapshot SHA binding disagrees')
    data, fields, images = load_capture(path)
    if (type(layer['width']) is not int or type(layer['height']) is not int
            or (layer['width'], layer['height'], layer['source_biome'])
            != (data['width'], data['height'], data['biome'])):
        raise ValueError('Replica command metadata differs from actual source snapshot')
    return data, fields, images


def explicit_observation(command, biome, width, height):
    if command.get('action') != 'generate' or command.get('params') != {}:
        return False
    for name in LAYERS:
        layer = command.get(name)
        if (not isinstance(layer, dict) or type(layer.get('schema_version')) is not int or layer['schema_version'] != 1
                or layer.get('row_order') != 'south-first' or layer.get('source_biome') != biome
                or type(layer.get('width')) is not int or type(layer.get('height')) is not int
                or layer['width'] != width or layer['height'] != height):
            return False
        mode = 'source-geology' if name == 'cave_layer' else 'source-composition'
        if name != 'ground_layer' and layer.get('mode') != mode:
            return False
    return True


def prepare_replica(command, snapshot_path, terrain_path, geology_path, map_png_path,
                    map_default_png_path, *, target_biome, width, height, fresh_unedited,
                    snapshot_file):
    """Add metadata only to the selected same-biome/same-size fresh candidate.

    Off-condition commands return unchanged content and do not open a missing
    replica. Preexisting replica metadata requires a verified fresh candidate;
    selected missing/unknown/inconsistent captures raise, never adapt.
    """
    if not (type(fresh_unedited) is bool and fresh_unedited) and 'replica_layer' in command:
        raise ValueError('Replica metadata requires a verified fresh unedited candidate')
    cave = command.get('cave_layer')
    if not isinstance(cave, dict):
        return copy.deepcopy(command), {'replica_selected': False, 'mode': 'existing-adaptation'}
    biome, sw, sh = cave.get('source_biome'), cave.get('width'), cave.get('height')
    selected = (type(fresh_unedited) is bool and fresh_unedited
                and type(width) is int and type(height) is int
                and type(sw) is int and type(sh) is int and sw > 0 and sh > 0
                and isinstance(biome, str) and bool(biome)
                and target_biome == biome and (width, height) == (sw, sh)
                and explicit_observation(command, biome, sw, sh))
    if not selected:
        return copy.deepcopy(command), {'replica_selected': False, 'mode': 'existing-adaptation'}
    terrain = read(terrain_path)
    if dimensions(terrain) != (sw, sh) or terrain.get('biome') != biome:
        raise ValueError('Selected recipe differs from actual original biome/dimensions')
    if 'replica_layer' in command:
        raise ValueError('Existing replica metadata cannot be silently replaced')
    layer, receipt = observed_replica(snapshot_path, terrain_path, geology_path,
                                     map_png_path, map_default_png_path, snapshot_file=snapshot_file)
    cave = command['cave_layer']
    source = read(snapshot_path)['source_bindings']
    if (cave.get('source_terrain_sha256') != source['terrain_sha256']
            or cave.get('source_geology_sha256') != source['geology_sha256']):
        raise ValueError('Explicit cave observation and replica source hashes disagree')
    result = copy.deepcopy(command)
    result['replica_layer'] = layer
    return result, {**receipt, 'replica_selected': True, 'mode': 'exact-static-replica'}


def agreement(expected, actual):
    wrong = expected != actual
    return {'compared_cells': int(wrong.size), 'mismatch_cells': int(wrong.sum()),
            'agreement': float((~wrong).mean())}


def compare_pngs(expected_path, actual_path, width, height):
    expected, actual = png_pixels(expected_path, width, height), png_pixels(actual_path, width, height)
    wrong = np.any(expected != actual, axis=2)
    return {'compared_pixels': int(wrong.size), 'mismatch_pixels': int(wrong.sum()),
            'agreement': float((~wrong).mean()), 'pixel_equal': not bool(wrong.any()),
            'byte_equal': sha256(expected_path) == sha256(actual_path)}


def original_phase_pngs(snapshot_path):
    """Diagnostic only: old 99999 PNGs versus native post-init PNGs."""
    path = pathlib.Path(snapshot_path)
    data, fields, _ = load_capture(path)
    terrain_name = data['binding_files']['terrain']
    if not terrain_name.endswith('-terrain.json'):
        raise ValueError('Original terrain binding does not identify original preview files')
    prefix = terrain_name[:-len('-terrain.json')]
    comparisons = {}
    for name, suffix in (('map_png', '-map.png'), ('map_default_png', '-map-default.png')):
        early, final = path.parent / (prefix + suffix), path.parent / data['binding_files'][name]
        if not early.is_file():
            comparisons[name] = {'comparison_possible': False, 'reason': 'Original 99999 PNG missing'}
            continue
        comparisons[name] = {'comparison_possible': True,
                             **compare_pngs(early, final, fields['width'], fields['height'])}
    return comparisons


def native_audit(audit_path, source_snapshot_path, target_data):
    result = {'present': False, 'structure_pass': False, 'pass': False, 'reasons': []}
    try:
        audit = read(audit_path)
    except (OSError, ValueError) as error:
        result['reasons'].append('Final native replica audit missing/invalid: ' + str(error))
        return result
    result['present'] = isinstance(audit, dict) and bool(audit)
    if not result['present']:
        result['reasons'].append('Final native replica audit missing/invalid')
        return result
    reasons = []
    if (type(audit.get('schema_version')) is not int or audit['schema_version'] != 1
            or type(audit.get('stage')) is not int or audit['stage'] != 99999
            or audit.get('capture_phase') != PHASE):
        reasons.append('Invalid final native replica audit phase/schema')
    for name in ('exact_selected', 'applied', 'pass', 'map_png_hash_equal', 'map_default_png_hash_equal'):
        if type(audit.get(name)) is not bool:
            reasons.append('Missing/invalid native replica audit flag: ' + name)
    for name in AUDIT_COUNTS:
        if type(audit.get(name)) is not int or audit[name] < 0:
            reasons.append('Missing/invalid native replica audit count: ' + name)
    mismatches = audit.get('field_mismatches')
    area = target_data['width'] * target_data['height']
    if (not isinstance(mismatches, dict) or not set(AUDIT_FIELDS).issubset(mismatches)
            or any(type(value) is not int or not 0 <= value <= area for value in mismatches.values())):
        reasons.append('Missing/invalid full native replica field mismatch counts')
    if audit.get('source_snapshot_sha256') != sha256(source_snapshot_path):
        reasons.append('Native replica audit refers to a different original snapshot')
    for name in ('map_png', 'map_default_png'):
        if audit.get('actual_' + name + '_sha256') != target_data['source_bindings'][name + '_sha256']:
            reasons.append('Native replica audit actual PNG hash differs: ' + name)
    result['structure_pass'] = not reasons
    if result['structure_pass']:
        for name in ('exact_selected', 'applied', 'pass', 'map_png_hash_equal', 'map_default_png_hash_equal'):
            if not audit[name]:
                reasons.append('Native replica audit did not verify: ' + name)
        for name in AUDIT_COUNTS:
            if audit[name]:
                reasons.append('Native replica audit requires zero: ' + name)
        if any(mismatches.values()):
            reasons.append('Native replica audit found final static mismatches')
    result['reasons'], result['pass'] = reasons, not reasons
    return result


def evaluate(source_snapshot_path, target_snapshot_path, audit_path=None, *, run_ok=False):
    """Compare native original final observations, never exported sidecar truth.

    fidelity_pass quantifies the static capture. Native application/selection
    audit is a separate admission requirement; matching pixels is not admission.
    """
    record = {'source': str(source_snapshot_path), 'target': str(target_snapshot_path),
              'comparison_possible': False, 'fidelity_pass': False, 'metrics': {}, 'reasons': [],
              'admission_pass': False,
              'run_ok': type(run_ok) is bool and run_ok,
              'scope': 'Post-init static capture and actual PNG equality; no save/pawn/plant equivalence'}
    try:
        source, expected, source_images = load_capture(source_snapshot_path)
        target, actual, target_images = load_capture(target_snapshot_path)
        if (expected['width'], expected['height'], expected['biome']) != (actual['width'], actual['height'], actual['biome']):
            raise ValueError('Exact replica requires the same original biome and dimensions')
    except (ValueError, TypeError, KeyError, OSError, OverflowError) as error:
        record['reasons'].append(str(error))
        return record
    record['comparison_possible'] = True
    record['source_context'], record['target_context'] = source['context'], target['context']
    record['source_phase_png_comparison'] = original_phase_pngs(source_snapshot_path)
    metrics = record['metrics']
    for name in TERRAIN_GRIDS + ('color_indices', 'roof_indices', 'edifice_indices',
                                'walkable', 'elevation', 'caves', 'fertility'):
        metrics[name] = agreement(expected[name], actual[name])
        if name in ('elevation', 'caves', 'fertility'):
            metrics[name]['max_abs_error'] = float(np.abs(expected[name] - actual[name]).max())
        if metrics[name]['mismatch_cells']:
            record['reasons'].append('Final actual static field differs: ' + name)
    metrics['edifices'] = {'source_records': len(expected['edifices']), 'target_records': len(actual['edifices']),
                           'equal': expected['edifices'] == actual['edifices']}
    if not metrics['edifices']['equal']:
        record['reasons'].append('Final unique edifice records differ')
    for name in ('map_png', 'map_default_png'):
        wrong = np.any(source_images[name] != target_images[name], axis=2)
        metrics[name] = {'compared_pixels': int(wrong.size), 'mismatch_pixels': int(wrong.sum()),
                         'agreement': float((~wrong).mean()),
                         'source_sha256': source['source_bindings'][name + '_sha256'],
                         'target_sha256': target['source_bindings'][name + '_sha256']}
        if wrong.any():
            record['reasons'].append('Final actual PNG differs: ' + name)
    record['fidelity_pass'] = not record['reasons']
    if audit_path is None:
        target_path = pathlib.Path(target_snapshot_path)
        audit_path = target_path.with_name(target_path.name.replace('-replica.json', '-replica-native-audit.json'))
    record['native_audit'] = native_audit(audit_path, source_snapshot_path, target)
    record['admission_pass'] = record['fidelity_pass'] and record['native_audit']['pass'] and record['run_ok']
    return record
