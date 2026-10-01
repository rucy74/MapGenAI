"""Measure actual native cave grids, roofs and passages independently of recipes.

Observed source cells are the denominator. This module never reads cave-layer
RLE, exporter arrays, map colors or a working-grid application as source truth.
"""
import argparse
import base64
import hashlib
import html
import io
import json
import pathlib

import numpy as np
from PIL import Image
from skimage import measure


TOLERANCE = 1e-5
NATURAL_ROOFS = ('None', 'RoofRockThin', 'RoofRockThick')
AUDIT_COUNTS = ('known_cells', 'unknown_cells', 'protected_cells',
                'protected_conflicts', 'protected_changes', 'unknown_changes',
                'elevation_mismatches', 'cave_value_mismatches',
                'cave_mask_mismatches', 'roof_mismatches', 'unsafe_roof_cells')
SCENES = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff',
          'gl-archipelago', 'gl-oasis', 'gl-cave-entrance', 'gl-secluded-valley')
CONTROLS = ('cave-guard', 'unknown-cave', 'cave-unsafe')
GUARD_KINDS = ('preexisting_roof', 'constructed_roof', 'river', 'sea',
               'special_water', 'water', 'road', 'constructed_floor', 'building',
               'other_thing', 'unsupported_terrain', 'preexisting_resource', 'preexisting_rock')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def dimensions(data):
    width, height = data.get('width'), data.get('height')
    if (type(width) is not int or type(height) is not int or min(width, height) <= 0
            or data.get('row_order') != 'south-first'):
        raise ValueError('Positive dimensions and south-first row order required')
    return width, height


def binary(data, name, shape, default=None):
    value = data.get(name, default)
    if (not isinstance(value, str) or len(value) != shape[0] * shape[1]
            or set(value) - set('01')):
        raise ValueError('Actual physical 0/1 grid required: ' + name)
    return np.asarray(list(value)).reshape(shape) == '1'


def numeric(data, name, shape, integer=False):
    value = data.get(name)
    if (not isinstance(value, list) or len(value) != shape[0] * shape[1]
            or any(isinstance(v, bool) or not isinstance(v, (int, float)) for v in value)
            or (integer and any(type(v) is not int for v in value))):
        raise ValueError('Full numeric actual grid required: ' + name)
    grid = np.asarray(value, dtype=np.float64).reshape(shape)
    if not np.isfinite(grid).all() or np.abs(grid).max() > np.finfo(np.float32).max:
        raise ValueError('Finite native float grid required: ' + name)
    return grid.astype(np.int64) if integer else grid


def actual_fields(terrain, geology):
    if terrain.get('schema_version') != 2 or geology.get('schema_version') != 1:
        raise ValueError('Actual terrain schema2 and geology schema1 required')
    width, height = dimensions(terrain)
    if dimensions(geology) != (width, height):
        raise ValueError('Terrain and geology capture dimensions differ')
    shape = (height, width)
    cells = terrain.get('cells')
    if not isinstance(cells, str) or len(cells) != width * height or set(cells) - set('MGSWN'):
        raise ValueError('Actual occupancy cells required')
    cells = np.asarray(list(cells)).reshape(shape)
    indices = numeric(geology, 'roof_indices', shape, integer=True)
    table = geology.get('roof_table')
    if (not isinstance(table, list) or not table or table[0] != 'None'
            or any(not isinstance(v, str) or not v for v in table)
            or len(set(table)) != len(table) or indices.min() < 0 or indices.max() >= len(table)):
        raise ValueError('Actual roof table and indices required')
    sha = geology.get('source_terrain_sha256')
    if not isinstance(sha, str) or len(sha) != 64 or set(sha.lower()) - set('0123456789abcdef'):
        raise ValueError('Terrain capture SHA256 provenance required')
    caves = numeric(geology, 'caves', shape)
    if (caves < 0).any():
        raise ValueError('Negative actual cave values unsupported')
    return {'cells': cells, 'elevation': numeric(geology, 'elevation', shape),
            'caves': caves, 'roof': np.asarray(table)[indices],
            'observed': binary(geology, 'known_mask', shape, '1' * (width * height)),
            'walkable': binary(geology, 'walkable', shape),
            'constructed_floor': binary(geology, 'constructed_floor', shape),
            'nonrock_edifice': binary(geology, 'nonrock_edifice', shape)}


def portable_known(fields):
    # Actual classifications include null-category ancient floors. Walkability
    # is deliberately absent: ordinary water remains observed known geology.
    return (fields['observed'] & (fields['cells'] != 'N')
            & np.isin(fields['roof'], NATURAL_ROOFS)
            & ~fields['constructed_floor'] & ~fields['nonrock_edifice'])


def sample(grid, shape):
    ys = np.arange(shape[0]) * grid.shape[0] // shape[0]
    xs = np.arange(shape[1]) * grid.shape[1] // shape[1]
    return grid[ys[:, None], xs]


def occupancy(expected, actual):
    wanted, found = int(expected.sum()), int(actual.sum())
    intersection = int((expected & actual).sum())
    extra, missing = int((actual & ~expected).sum()), int((expected & ~actual).sum())
    return {'expected_cells': wanted, 'actual_cells': found,
            'intersection_cells': intersection, 'missing_cells': missing, 'extra_cells': extra,
            'precision': intersection / found if found else (1.0 if not wanted else 0.0),
            'recall': intersection / wanted if wanted else 1.0,
            'iou': intersection / (wanted + extra) if wanted + extra else 1.0}


def scalar(expected, actual, mask):
    difference = np.abs(expected - actual)[mask]
    return {'compared_cells': int(mask.sum()),
            'mismatch_cells': int((difference > TOLERANCE).sum()),
            'max_abs_error': float(difference.max()) if difference.size else 0.0,
            'mean_abs_error': float(difference.mean()) if difference.size else 0.0,
            'root_mean_square_error': float(np.sqrt(np.square(difference).mean())) if difference.size else 0.0,
            'tolerance': TOLERANCE}


def adjacent(mask):
    result = np.zeros_like(mask)
    result[1:] |= mask[:-1]
    result[:-1] |= mask[1:]
    result[:, 1:] |= mask[:, :-1]
    result[:, :-1] |= mask[:, 1:]
    return result


def boundary(mask):
    result = np.zeros_like(mask)
    result[0] = mask[0]
    result[-1] = mask[-1]
    result[:, 0] |= mask[:, 0]
    result[:, -1] |= mask[:, -1]
    return result


def passages(source, target, known):
    """Preserve source tunnel components and their actual exterior contacts.

    Connections are measured inside each original tunnel. A path across the
    surrounding plain cannot hide a blocked neck within a cave.
    """
    source_known = portable_known(source)
    passage = source_known & (source['caves'] > 0) & source['walkable']
    source_labels = measure.label(passage, connectivity=1)
    mapped = sample(source_labels, known.shape)
    expected = mapped > 0
    actual = known & (target['caves'] > 0) & target['walkable']
    exterior = (source_known & source['walkable'] & (source['caves'] <= 0)
                & (source['elevation'] <= .7) & (source['roof'] == 'None'))
    mouths = passage & adjacent(exterior)
    world_mouths = boundary(passage)
    mouths |= world_mouths
    mouth_labels = measure.label(mouths, connectivity=1)
    mapped_mouths = sample(mouth_labels, known.shape)
    mapped_exterior = sample(exterior, known.shape)
    current_contacts = expected & actual & adjacent(mapped_exterior & target['walkable'])
    current_contacts |= boundary(sample(world_mouths, known.shape) & actual)
    # Count actual inside/outside adjacency edges, not only surviving mouth
    # groups. A blocked exterior cell can narrow a still-connected entrance.
    entrance_edges, entrance_edges_missing = 0, 0
    for axis in (0, 1):
        low = [slice(None), slice(None)]
        high = [slice(None), slice(None)]
        low[axis], high[axis] = slice(None, -1), slice(1, None)
        low, high = tuple(low), tuple(high)
        for inside, outside in ((low, high), (high, low)):
            edges = expected[inside] & mapped_exterior[outside]
            retained = actual[inside] & target['walkable'][outside]
            entrance_edges += int(edges.sum())
            entrance_edges_missing += int((edges & ~retained).sum())
    rows, split, lost, unobservable, connection_failures = [], 0, 0, 0, 0
    entrance_rows, entrances_lost = [], 0
    for ident in range(1, int(source_labels.max()) + 1):
        expected_component = mapped == ident
        cells = int(expected_component.sum())
        expected_parts = int(measure.label(expected_component, connectivity=1).max())
        actual_labels = measure.label(expected_component & actual, connectivity=1)
        parts = int(actual_labels.max())
        unobservable += not cells
        lost += bool(cells and not parts)
        # A source component was connected before scaling. Exact agreement
        # with a nearest-cell projection that severs it still loses fidelity.
        split += bool(parts > 1)
        mouth_ids = np.unique(mapped_mouths[expected_component])
        mouth_ids = mouth_ids[mouth_ids != 0]
        entrance_parts = []
        for mouth_id in mouth_ids:
            mask = expected_component & (mapped_mouths == mouth_id)
            contacts = mask & current_contacts
            connected_parts = set(int(v) for v in np.unique(actual_labels[contacts]) if v)
            entrances_lost += not connected_parts
            entrance_parts.append(connected_parts)
            entrance_rows.append({'source_entrance': int(mouth_id), 'source_component': ident,
                                  'mapped_cells': int(mask.sum()), 'contact_cells': int(contacts.sum()),
                                  'preserved': bool(connected_parts)})
        connected = bool(entrance_parts and set.intersection(*entrance_parts))
        if len(entrance_parts) > 1 and not connected:
            connection_failures += 1
        rows.append({'source_component': ident, 'source_cells': int((source_labels == ident).sum()),
                     'mapped_cells': cells, 'included_cells': int((expected_component & actual).sum()),
                     'expected_parts_after_scaling': expected_parts, 'actual_connected_parts': parts,
                     'entrances': len(mouth_ids), 'entrances_connected': connected if mouth_ids.size else None})
    # A downscaled entrance can disappear even when some of its tunnel survives.
    mapped_ids = set(int(v) for v in np.unique(mapped_mouths) if v)
    absent_mouths = int(mouth_labels.max()) - len(mapped_ids)
    entrances_lost += absent_mouths
    return occupancy(expected, actual), {
        'source_components': int(source_labels.max()), 'source_passage_cells': int(passage.sum()),
        'mapped_components': sum(r['mapped_cells'] > 0 for r in rows),
        'split_components': int(split), 'lost_components': int(lost),
        'unobservable_components': int(unobservable),
        'source_entrances': int(mouth_labels.max()), 'entrances_lost': int(entrances_lost),
        'unobservable_entrances': absent_mouths, 'entrance_connection_failures': connection_failures,
        'entrance_edges': entrance_edges, 'entrance_edges_missing': entrance_edges_missing,
        'components': rows, 'entrances': entrance_rows,
        'definition': '4-connected observed positive-C walkable passage; entrances touch observed walkable no-roof low-elevation exterior or map boundary; no exterior bypass'}, sample(mouths, known.shape)


def final_audit(audit, source, target, known, metrics):
    result = {'present': isinstance(audit, dict) and bool(audit), 'structure_pass': False,
              'pass': False, 'reasons': []}
    if not result['present']:
        result['reasons'].append('Final cave audit missing; reject candidate')
        return result
    result.update({key: value for key, value in audit.items()
                   if key not in ('present', 'structure_pass', 'pass', 'reasons')})
    reasons = []
    if (audit.get('schema_version') != 1 or audit.get('stage') != 99999
            or audit.get('row_order') != 'south-first'):
        reasons.append('Invalid final cave audit schema/stage/order')
    for name, value in (('source_width', source['cells'].shape[1]),
                        ('source_height', source['cells'].shape[0]),
                        ('target_width', target['cells'].shape[1]),
                        ('target_height', target['cells'].shape[0])):
        if type(audit.get(name)) is not int or audit[name] != value:
            reasons.append('Final cave audit dimensions disagree: ' + name)
    for name in AUDIT_COUNTS:
        if type(audit.get(name)) is not int or audit[name] < 0:
            reasons.append('Missing/invalid final cave audit count: ' + name)
    kinds = audit.get('protection_kinds')
    if (not isinstance(kinds, dict) or any(not isinstance(k, str) or type(v) is not int or v < 0
                                           for k, v in kinds.items())):
        reasons.append('Invalid final cave audit protection kinds')
    if not reasons:
        counts = {'known_cells': int(known.sum()), 'unknown_cells': int((~known).sum()),
                  'elevation_mismatches': metrics['elevation']['mismatch_cells'],
                  'cave_value_mismatches': metrics['cave_value']['mismatch_cells'],
                  'cave_mask_mismatches': metrics['cave_mask']['missing_cells'] + metrics['cave_mask']['extra_cells'],
                  'roof_mismatches': metrics['roof']['mismatch_cells']}
        for name, value in counts.items():
            if audit[name] != value:
                reasons.append('Final cave audit disagrees with independent actual source: ' + name)
        if audit['protected_cells'] > known.size or audit['protected_conflicts'] > int(known.sum()):
            reasons.append('Invalid final cave audit protected totals')
        if audit['unsafe_roof_cells'] > int(known.sum()):
            reasons.append('Invalid final cave audit unsafe roof total exceeds known source cells')
    result['structure_pass'] = not reasons
    if result['structure_pass']:
        for name in ('protected_conflicts', 'protected_changes', 'unknown_changes'):
            if audit[name]:
                reasons.append('Final cave audit ' + name + ' is nonzero; reject candidate')
        if audit['unsafe_roof_cells']:
            reasons.append('Final cave audit unsafe source natural roofs; reject candidate')
    result['reasons'] = reasons
    result['pass'] = not reasons
    return result


def evaluate_fields(source_terrain, source_geology, target_terrain, target_geology, audit):
    """Return an independent record, including fail-closed invalid observations."""
    record = {'pass': False, 'comparison_possible': False, 'reasons': [], 'metrics': {},
              'connectivity': {}, 'final_audit': {'present': bool(audit), 'structure_pass': False, 'pass': False}}
    try:
        source = actual_fields(source_terrain, source_geology)
        target = actual_fields(target_terrain, target_geology)
    except (ValueError, TypeError, KeyError, OverflowError) as error:
        record['reasons'].append('Invalid actual geology observation: ' + str(error))
        return record
    record['comparison_possible'] = True
    known = sample(portable_known(source), target['cells'].shape)
    mapped = {key: sample(value, known.shape) for key, value in source.items()}
    raw = mapped['observed'] & (mapped['cells'] != 'N')
    roof_wrong = mapped['roof'] != target['roof']
    mask_expected, mask_actual = known & (mapped['caves'] > 0), known & (target['caves'] > 0)
    passage, connectivity, entrances = passages(source, target, known)
    roof = {'compared_cells': int(known.sum()), 'mismatch_cells': int((known & roof_wrong).sum()),
            'expected_natural_roof_cells': int((known & (mapped['roof'] != 'None')).sum()),
            'actual_natural_roof_cells': int((known & np.isin(target['roof'], NATURAL_ROOFS[1:])).sum())}
    for name, mask in (('inside_cave', mask_expected), ('outside_cave', known & ~mask_expected),
                       ('entrance', known & entrances)):
        roof[name] = {'compared_cells': int(mask.sum()), 'mismatch_cells': int((mask & roof_wrong).sum())}
    metrics = {'elevation': scalar(mapped['elevation'], target['elevation'], known),
               'cave_value': scalar(mapped['caves'], target['caves'], known),
               'cave_mask': occupancy(mask_expected, mask_actual), 'roof': roof, 'passage': passage,
               'roofed_walkable_passage': occupancy(
                   mask_expected & mapped['walkable'] & (mapped['roof'] != 'None'),
                   mask_actual & target['walkable'] & np.isin(target['roof'], NATURAL_ROOFS[1:]))}
    source_known = portable_known(source)
    record['source'] = {'width': source_terrain['width'], 'height': source_terrain['height'],
                        'raw_cells': source['cells'].size, 'observed_cells': int(source['observed'].sum()),
                        'portable_known_cells': int(source_known.sum()), 'unknown_cells': int((~source_known).sum()),
                        'raw_cave_cells': int((source['caves'] > 0).sum()),
                        'portable_cave_cells': int((source_known & (source['caves'] > 0)).sum()),
                        'raw_walkable_cave_cells': int(((source['caves'] > 0) & source['walkable']).sum()),
                        'raw_natural_roof_cells': int(np.isin(source['roof'], NATURAL_ROOFS[1:]).sum()),
                        'unsupported_roof_cells': int((~np.isin(source['roof'], NATURAL_ROOFS)).sum()),
                        'constructed_floor_cells': int(source['constructed_floor'].sum()),
                        'nonrock_edifice_cells': int(source['nonrock_edifice'].sum())}
    record['mapped_known_cells'] = int(known.sum())
    record['mapped_unknown_cells'] = int((~known).sum())
    record['metrics'] = metrics
    record['raw_diagnostics'] = {'elevation': scalar(mapped['elevation'], target['elevation'], raw),
                                 'cave_value': scalar(mapped['caves'], target['caves'], raw),
                                 'roof': {'compared_cells': int(raw.sum()),
                                          'mismatch_cells': int((raw & roof_wrong).sum())}}
    record['connectivity'] = connectivity
    record['final_audit'] = final_audit(audit, source, target, known, metrics)
    reasons = list(record['final_audit']['reasons'])
    if not known.any():
        reasons.append('No independently supported source geology cells')
    unobserved = int((known & ~target['observed']).sum())
    record['unobserved_target_known_cells'] = unobserved
    if unobserved:
        reasons.append('Target observation missing known source cells')
    for name in ('elevation', 'cave_value'):
        if metrics[name]['mismatch_cells']:
            reasons.append(name + ' max absolute error exceeds 1e-5')
    for name in ('cave_mask', 'passage', 'roofed_walkable_passage'):
        if metrics[name]['missing_cells'] or metrics[name]['extra_cells']:
            reasons.append(name + ' missing/extra cells; exact preservation required')
    if roof['mismatch_cells']:
        reasons.append('Actual known natural roof/None mismatch; exact preservation required')
    for name in ('split_components', 'lost_components', 'unobservable_components',
                 'entrances_lost', 'entrance_connection_failures', 'entrance_edges_missing'):
        if connectivity[name]:
            reasons.append('Passage connectivity failure: ' + name)
    record['reasons'] = reasons
    record['pass'] = not reasons
    return record


def bound_capture(terrain_path, geology_path):
    terrain, geology = read(terrain_path), read(geology_path)
    sha = hashlib.sha256(terrain_path.read_bytes()).hexdigest()
    if geology.get('source_terrain_sha256', '').lower() != sha:
        raise ValueError('Actual geology SHA256 differs from paired terrain capture: ' + str(geology_path))
    return terrain, geology


def application(path, stage):
    result = {'present': path.exists(), 'pass': False, 'reasons': []}
    if not result['present']:
        result['reasons'].append('Missing cave application: ' + path.name)
        return result
    try:
        data = read(path)
        result.update(data)
        if data.get('schema_version') != 1 or data.get('stage') != stage:
            result['reasons'].append('Invalid cave application stage/schema')
        for name in ('protected_changes', 'unknown_changes'):
            if type(data.get(name)) is not int or data[name] != 0:
                result['reasons'].append('Cave application invariant nonzero/invalid: ' + name)
        if stage == 199 and (type(data.get('known_grid_mismatches')) is not int
                             or data['known_grid_mismatches'] != 0):
            result['reasons'].append('Cave application known grid mismatches nonzero/invalid')
        if not isinstance(data.get('protection_kinds'), dict):
            result['reasons'].append('Missing cave application protection kinds')
        result['pass'] = not result['reasons']
    except (OSError, ValueError, TypeError) as error:
        result['reasons'].append('Invalid cave application: ' + str(error))
    return result


def control_check(run, scene):
    """Test positive safety fixtures; expectation PASS never admits a control."""
    run = pathlib.Path(run)
    ident = scene['id']
    record = {'id': ident, 'run': run.name, 'biome': scene.get('biome'), 'size': scene.get('size'),
              'fidelity_pass': False, 'expectation_pass': False, 'pass': False,
              'checks': {}, 'reasons': [], 'scope': 'Safety control; expectation PASS is not geology fidelity or catalog admission.'}
    checks = record['checks']
    early = application(run / (ident + '-cave-grid-application.json'), 199)
    late = application(run / (ident + '-cave-roof-application.json'), 1601)
    record.update({'early': early, 'application': late})
    checks['early_invariants'] = early['pass']
    checks['roof_invariants'] = late['pass']
    try:
        terrain, geology = bound_capture(run / (ident + '-terrain.json'), run / (ident + '-geology.json'))
        actual = actual_fields(terrain, geology)
        fixture = read(run / (ident + '-cave-fixtures.json'))
        audit = read(run / (ident + '-cave-final-audit.json'))
        record.update({'fixtures': fixture, 'final_audit': audit})
        checks['fixture_metadata'] = bool(fixture.get('schema_version') == 1
                                          and fixture.get('early_stage') == 198
                                          and fixture.get('late_stage') == 1600.5)
        shape = actual['cells'].shape
        checks['audit_schema_dimensions_counts'] = bool(
            audit.get('schema_version') == 1 and audit.get('stage') == 99999
            and audit.get('row_order') == 'south-first'
            and all(type(audit.get(k)) is int and audit[k] > 0 for k in ('source_width', 'source_height'))
            and audit.get('target_width') == shape[1] and audit.get('target_height') == shape[0]
            and all(type(audit.get(k)) is int and audit[k] >= 0 for k in AUDIT_COUNTS)
            and audit.get('known_cells', -1) + audit.get('unknown_cells', -1) == actual['cells'].size
            and audit.get('protected_cells', -1) <= actual['cells'].size
            and audit.get('protected_conflicts', -1) <= audit.get('known_cells', -1)
            and audit.get('unsafe_roof_cells', -1) <= audit.get('known_cells', -1)
            and isinstance(audit.get('protection_kinds'), dict)
            and all(isinstance(k, str) and type(v) is int and v >= 0
                    for k, v in audit.get('protection_kinds', {}).items()))
        checks['final_mutation_invariants'] = bool(audit.get('protected_changes') == 0
                                                   and audit.get('unknown_changes') == 0)
        if ident in ('cave-guard', 'unknown-cave'):
            unknown = fixture.get('unknown', {})
            checks['unknown_fixture_metadata'] = bool(unknown.get('x') == 5 and unknown.get('z') == 5
                and unknown.get('elevation') == .37 and unknown.get('caves') == 2
                and unknown.get('roof') == 'RoofRockThin')
            checks['unknown_actual_positive_geology_rock_preserved'] = bool(
                actual['cells'][5, 5] == 'M' and actual['roof'][5, 5] == 'RoofRockThin'
                and abs(actual['elevation'][5, 5] - .37) <= TOLERANCE
                and abs(actual['caves'][5, 5] - 2) <= TOLERANCE)
        if ident == 'cave-guard':
            for kind in GUARD_KINDS:
                checks['positive_protection_' + kind] = bool(
                    type(early.get('protection_kinds', {}).get(kind)) is int
                    and early['protection_kinds'][kind] > 0)
            for name, point, roof in (
                    ('preexisting_natural_roof', [20, 20], 'RoofRockThin'),
                    ('preexisting_constructed_roof', [21, 20], 'RoofConstructed'),
                    ('late_constructed_roof', [32, 20], 'RoofConstructed'),
                    ('safe_roof_clear', [10, 10], 'None'),
                    ('safe_roof_add', [40, 30], 'RoofRockThick')):
                checks[name + '_metadata'] = fixture.get(name) == point
                checks[name + '_actual'] = bool(actual['roof'][point[1], point[0]] == roof)
            checks['preexisting_resource_actual'] = bool(fixture.get('preexisting_resource') == [12, 12]
                                                        and actual['cells'][12, 12] == 'M')
            checks['preexisting_rock_actual'] = bool(fixture.get('preexisting_rock') == [13, 12]
                                                    and actual['cells'][12, 13] == 'M')
            checks['safe_roof_add_positive'] = type(late.get('added_roof_cells')) is int and late['added_roof_cells'] > 0
            checks['safe_roof_clear_positive'] = type(late.get('cleared_roof_cells')) is int and late['cleared_roof_cells'] > 0
            checks['final_source_conflicts_reject_fidelity'] = type(audit.get('protected_conflicts')) is int and audit['protected_conflicts'] > 0
        elif ident == 'unknown-cave':
            baseline_terrain, baseline_geology = bound_capture(run / (ident + '-baseline-terrain.json'),
                                                               run / (ident + '-baseline-geology.json'))
            baseline = actual_fields(baseline_terrain, baseline_geology)
            checks['entire_actual_geology_identical'] = all(np.array_equal(actual[k], baseline[k]) for k in actual)
            differences = {}
            for name in actual:
                coords = np.argwhere(actual[name] != baseline[name])
                differences[name] = {'different_cells': len(coords),
                    'sample': [{'x': int(x), 'z': int(z), 'actual': actual[name][z, x].item(),
                                'baseline': baseline[name][z, x].item()} for z, x in coords[:64]],
                    'sample_truncated': len(coords) > 64}
            record['baseline_field_differences'] = differences
            checks['entire_terrain_json_identical'] = (run / (ident + '-terrain.json')).read_bytes() == (run / (ident + '-baseline-terrain.json')).read_bytes()
            for suffix in ('map.png', 'map-default.png'):
                checks['entire_' + suffix + '_identical'] = (run / (ident + '-' + suffix)).read_bytes() == (run / (ident + '-baseline-' + suffix)).read_bytes()
            checks['all_unknown_audit_no_changes'] = bool(audit.get('known_cells') == 0
                and audit.get('unknown_cells') == actual['cells'].size
                and all(audit.get(k) == 0 for k in ('protected_conflicts', 'elevation_mismatches',
                    'cave_value_mismatches', 'cave_mask_mismatches', 'roof_mismatches')))
            checks['all_unknown_no_roof_edits'] = all(late.get(k) == 0 for k in ('added_roof_cells', 'cleared_roof_cells', 'changed_roof_cells'))
        elif ident == 'cave-unsafe':
            checks['unsafe_metadata_region'] = fixture.get('unsafe_fixture_rect') == [70, 70, 20, 20]
            region = (slice(70, 90), slice(70, 90))
            checks['unsafe_actual_positive_observed_cave_values'] = bool(
                shape[0] >= 90 and shape[1] >= 90
                and np.all(np.abs(actual['elevation'][region] - .82) <= TOLERANCE)
                and np.all(np.abs(actual['caves'][region] - 1) <= TOLERANCE))
            checks['unsafe_roof_cells_positive'] = bool(type(late.get('unsafe_roof_cells')) is int
                and late['unsafe_roof_cells'] > 0 and type(audit.get('unsafe_roof_cells')) is int
                and audit['unsafe_roof_cells'] > 0)
            checks['unsafe_requested_thin_roof_not_applied'] = bool(np.all(actual['roof'][region] != 'RoofRockThin'))
            checks['unsafe_no_new_roof_added'] = type(late.get('added_roof_cells')) is int and late['added_roof_cells'] == 0
            checks['unsafe_actual_no_solid_rock_support'] = bool(np.all(actual['cells'][region] != 'M'))
            checks['unsafe_actual_no_nonrock_support'] = bool(np.all(~actual['nonrock_edifice'][region]))
            checks['unsafe_final_conflicts_reject_fidelity'] = bool(type(audit.get('protected_conflicts')) is int
                and audit['protected_conflicts'] > 0 and type(audit.get('roof_mismatches')) is int
                and audit['roof_mismatches'] > 0)
    except (OSError, ValueError, TypeError, KeyError, IndexError) as error:
        checks['actual_control_observations_available'] = False
        record['reasons'].append('Invalid/missing independent control evidence: ' + str(error))
    record['reasons'] += [name for name, passed in checks.items() if not passed]
    record['expectation_pass'] = not record['reasons']
    record['pass'] = record['expectation_pass']
    return record


def evaluate(folder, runs, write=True):
    folder = pathlib.Path(folder)
    entries = {entry['id']: entry for entry in read(folder / 'catalog.json')['entries']}
    records, legacy, guards = [], [], []
    for run in runs:
        run = pathlib.Path(run)
        if not run.is_absolute():
            run = folder / run
        result = read(run / 'result.json')
        if result.get('ok') is not True:
            raise ValueError('Incomplete actual native run: ' + str(run))
        for scene in result['results']:
            ident = scene['id']
            if ident in CONTROLS:
                guards.append(control_check(run, scene))
                continue
            entry = entries.get(ident, {})
            if not entry.get('requires_cave_sidecar', False):
                legacy.append({'id': ident, 'run': run.name, 'biome': scene.get('biome'),
                               'size': scene.get('size'), 'cave_sidecar_applied': False,
                               'scope': 'Legacy control; absent geology is not interpreted as zero caves.'})
                continue
            reference = entry.get('source', {})
            source_id = reference.get('reference_id')
            source_run = reference.get('reference_run')
            try:
                if not isinstance(source_id, str) or not isinstance(source_run, str):
                    raise ValueError('Catalog explicit source reference_run/reference_id required')
                source_folder = pathlib.Path(source_run)
                if not source_folder.is_absolute():
                    source_folder = folder / source_folder
                source, source_geology = bound_capture(source_folder / (source_id + '-terrain.json'),
                                                       source_folder / (source_id + '-geology.json'))
                actual, actual_geology = bound_capture(run / (ident + '-terrain.json'),
                                                       run / (ident + '-geology.json'))
                audit_path = run / (ident + '-cave-final-audit.json')
                record = evaluate_fields(source, source_geology, actual, actual_geology,
                                         read(audit_path) if audit_path.exists() else None)
            except (ValueError, TypeError, OSError, KeyError) as error:
                record = {'pass': False, 'comparison_possible': False, 'metrics': {},
                          'connectivity': {}, 'final_audit': {'present': False, 'structure_pass': False, 'pass': False},
                          'reasons': ['Invalid/missing independently paired source/target capture: ' + str(error)]}
            record.update({'id': ident, 'run': run.name, 'biome': scene.get('biome'),
                           'size': scene.get('size'), 'tile': scene.get('tile'),
                           'source_reference_id': source_id, 'source_reference_run': source_run})
            records.append(record)
    report = {'schema_version': 1, 'records': records, 'legacy_controls': legacy, 'guards': guards,
              'checks': len(records), 'passed': sum(record['pass'] for record in records),
              'failed': sum(not record['pass'] for record in records),
              'guard_checks': len(guards), 'guard_passed': sum(guard['expectation_pass'] for guard in guards),
              'guard_failed': sum(not guard['expectation_pass'] for guard in guards),
              'final_audit_missing': sum(not record['final_audit']['present'] for record in records),
              'final_audit_invalid': sum(record['final_audit']['present'] and not record['final_audit']['structure_pass'] for record in records),
              'nonempty_source_cave_checks': sum(record.get('source', {}).get('raw_cave_cells', 0) > 0 for record in records),
              'nonempty_portable_source_cave_checks': sum(record.get('source', {}).get('portable_cave_cells', 0) > 0 for record in records),
              'nonempty_portable_source_cave_passed': sum(record['pass'] and record.get('source', {}).get('portable_cave_cells', 0) > 0 for record in records),
              'scope': 'Independent actual geology only; final admission also requires existing rock, water and supported ground gates. Technical fidelity is not visual or gameplay approval.',
              'scaling': 'Nearest observed source cell; cave/elevation float values never scaled or rounded',
              'paid_api_calls': 0}
    if write:
        (folder / 'cave-evaluation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    return report


def data_image(path):
    if not path.exists():
        return '<p class="absent">실제 PNG 미캡처</p>'
    return '<img loading="lazy" src="data:image/png;base64,' + base64.b64encode(path.read_bytes()).decode('ascii') + '">'


def review(folder, runs, report, rock_only_runs=()):
    """Self-contained actual captures with separate, explicitly derived diagnostics."""
    folder = pathlib.Path(folder)
    records = report['records']
    first_run = next((r['run'] for r in records if r['id'] in SCENES), None)
    first_path = next((pathlib.Path(r) for r in runs if pathlib.Path(r).name == first_run), None)
    cards = []
    for ident in SCENES:
        record = next((r for r in records if r['id'] == ident and r['run'] == first_run), None)
        if record is None:
            continue
        source_folder = pathlib.Path(record['source_reference_run'])
        if not source_folder.is_absolute():
            source_folder = folder / source_folder
        target_run = first_path
        if not target_run.is_absolute():
            target_run = folder / target_run
        source_id = record['source_reference_id']
        panels = ['<figure><figcaption>실제 원본 native · ' + html.escape(source_folder.name) + '</figcaption>' + data_image(source_folder / (source_id + '-map.png')) + '</figure>']
        paired = None
        target_results = read(target_run / 'result.json')
        target_scene = next(s for s in target_results['results'] if s['id'] == ident)
        for name in rock_only_runs:
            candidate_run = pathlib.Path(name)
            if not candidate_run.is_absolute():
                candidate_run = folder / candidate_run
            candidate_result = read(candidate_run / 'result.json')
            scene = next((s for s in candidate_result['results'] if s['id'] == ident), None)
            seeds_equal = bool(scene and scene.get('world_seed') is not None
                               and scene.get('world_seed') == target_scene.get('world_seed'))
            if scene and seeds_equal and scene.get('tile') == target_scene.get('tile') and scene.get('size') == target_scene.get('size'):
                paired = candidate_run / (ident + '-map.png')
                break
        panels.append('<figure><figcaption>동일 tile/seed rock-only replay</figcaption>' +
                      (data_image(paired) if paired else '<p class="absent">동일 조건 비교 미캡처</p>') + '</figure>')
        panels.append('<figure><figcaption>새 geology replay 실제 PNG · '
                      + html.escape(record['run'] + ' / ' + record['biome'] + ' / ' + str(record['size']))
                      + '</figcaption>' + data_image(target_run / (ident + '-map.png')) + '</figure>')
        diagnostics = []
        if record.get('comparison_possible'):
            terrain, geology = bound_capture(target_run / (ident + '-terrain.json'), target_run / (ident + '-geology.json'))
            target_fields = actual_fields(terrain, geology)
            source_terrain, source_geology = bound_capture(source_folder / (source_id + '-terrain.json'), source_folder / (source_id + '-geology.json'))
            source_fields = actual_fields(source_terrain, source_geology)
            scales = {name: max(float(source_fields[name].max()), float(target_fields[name].max()))
                      for name in ('caves', 'elevation')}
            for origin, fields in (('원본', source_fields), ('새 결과', target_fields)):
                for name in ('roof', 'caves', 'elevation', 'walkable'):
                    rgb = np.zeros(fields['cells'].shape + (3,), dtype=np.uint8)
                    if name == 'roof':
                        rgb[fields[name] == 'RoofRockThin'] = (88, 178, 238)
                        rgb[fields[name] == 'RoofRockThick'] = (191, 118, 247)
                        rgb[~np.isin(fields[name], NATURAL_ROOFS)] = (252, 165, 62)
                    elif name == 'walkable':
                        rgb[fields['walkable'] & (fields['caves'] > 0)] = (88, 213, 149)
                    else:
                        grid = fields[name]
                        maximum = scales[name]
                        normalized = grid / maximum if maximum > 0 else grid
                        rgb[..., 1] = (np.clip(normalized, 0, 1) * 210).astype(np.uint8)
                        rgb[..., 2] = (np.clip(normalized, 0, 1) * 255).astype(np.uint8)
                    stream = io.BytesIO()
                    Image.fromarray(rgb[::-1]).save(stream, format='PNG')
                    caption = origin + ' ' + name + ' 전체 raw 진단 · 게임 화면 아님'
                    if name in scales:
                        caption += ' · 공통 최대값 ' + format(scales[name], '.5g')
                    diagnostics.append('<figure><figcaption>' + caption + '</figcaption><img src="data:image/png;base64,' + base64.b64encode(stream.getvalue()).decode('ascii') + '"></figure>')
        status = 'PASS' if record['pass'] else 'REJECT'
        reasons = '<ul>' + ''.join('<li>' + html.escape(reason) + '</li>' for reason in record['reasons']) + '</ul>' if record['reasons'] else ''
        cards.append('<section><h2>' + html.escape(ident) + ' · ' + status + '</h2><div class="captures">' + ''.join(panels) + '</div><details><summary>실제 지붕 / C / E / 통로 측정</summary><div class="diagnostics">' + ''.join(diagnostics) + '</div><pre>' + html.escape(json.dumps({'source': record.get('source'), 'metrics': record.get('metrics'), 'connectivity': record.get('connectivity'), 'final_audit': record.get('final_audit')}, ensure_ascii=False, indent=2)) + '</pre></details>' + reasons + '</section>')
    # A first-run overview must not hide successful cave observations in other
    # profiles. Registration remains separate from a cave-field PASS.
    catalog_path, water_path = folder / 'catalog.json', folder / 'water-evaluation.json'
    catalog = {entry['id']: entry for entry in read(catalog_path)['entries']} if catalog_path.exists() else {}
    water = {(row['run'], row['id']): row for row in read(water_path)['records']} if water_path.exists() else {}
    highlights = [record for record in records if record['pass']
                  and record.get('source', {}).get('portable_cave_cells', 0) > 0]
    for record in highlights:
        ident = record['id']
        source_folder = pathlib.Path(record['source_reference_run'])
        if not source_folder.is_absolute():
            source_folder = folder / source_folder
        target_run = next((pathlib.Path(run) for run in runs if pathlib.Path(run).name == record['run']),
                          pathlib.Path(record['run']))
        if not target_run.is_absolute():
            target_run = folder / target_run
        source_id = record['source_reference_id']
        source_terrain, source_geology = bound_capture(source_folder / (source_id + '-terrain.json'),
                                                       source_folder / (source_id + '-geology.json'))
        target_terrain, target_geology = bound_capture(target_run / (ident + '-terrain.json'),
                                                       target_run / (ident + '-geology.json'))
        source_fields = actual_fields(source_terrain, source_geology)
        target_fields = actual_fields(target_terrain, target_geology)
        entry = catalog.get(ident, {})
        hilliness = target_geology.get('roof_support_context', {}).get('hilliness')
        profiles = [profile for profile in entry.get('verified_profiles', [])
                    if profile.get('biome') == record['biome']
                    and profile.get('map_size') == record['size']
                    and (hilliness is None or profile.get('hilliness') == hilliness)]
        water_row = water.get((record['run'], ident))
        if profiles and water_row is not None and water_row.get('pass') is True:
            admission = '카탈로그 등록 profile 표본 · 암석·물·바닥과 동굴 모두 통과. 모든 타일 또는 미관을 보장하지 않습니다.'
        elif water_row is not None and water_row.get('pass') is False:
            unexpected = water_row.get('unexpected_water_cells')
            wet_iou = (water_row.get('metrics', {}).get('wet') or {}).get('iou')
            water_detail = (' · 물 ' + str(unexpected) + '칸 추가' if type(unexpected) is int else '')
            if isinstance(wet_iou, (int, float)) and not isinstance(wet_iou, bool):
                water_detail += ' · wet IoU ' + format(wet_iou * 100, '.2f') + '%'
            admission = '동굴 필드만 통과' + water_detail + ' · 최종 등록 제외.'
        elif not profiles and entry.get('verification'):
            admission = '동굴 필드 PASS · 일치하는 verified profile이 없어 최종 등록 제외.'
        else:
            admission = '동굴 필드 PASS · 최종 등록 미확정. 등록은 암석·물·바닥도 동시에 PASS해야 합니다.'
        source_label = ('원본 실제 native PNG · ' + source_folder.name + ' / ' + source_id
                        + ' / ' + source_terrain['biome'] + ' / '
                        + str(source_terrain['width']) + '×' + str(source_terrain['height']))
        target_label = ('새 실제 native PNG · ' + record['run'] + ' / ' + record['biome']
                        + ' / ' + str(target_terrain['width']) + '×' + str(target_terrain['height']))
        panels = ['<figure><figcaption>' + html.escape(source_label) + '</figcaption>'
                  + data_image(source_folder / (source_id + '-map.png')) + '</figure>',
                  '<figure><figcaption>' + html.escape(target_label) + '</figcaption>'
                  + data_image(target_run / (ident + '-map.png')) + '</figure>']
        diagnostics = []
        for origin, fields in ((source_folder.name, source_fields), (record['run'], target_fields)):
            for name in ('roof', 'actual_passage'):
                rgb = np.zeros(fields['cells'].shape + (3,), dtype=np.uint8)
                if name == 'roof':
                    rgb[fields['roof'] == 'RoofRockThin'] = (88, 178, 238)
                    rgb[fields['roof'] == 'RoofRockThick'] = (191, 118, 247)
                    rgb[~np.isin(fields['roof'], NATURAL_ROOFS)] = (252, 165, 62)
                    label = '실제 지붕 raw · None 검정 / Thin 파랑 / Thick 보라 / 기타 주황'
                else:
                    rgb[fields['walkable'] & (fields['caves'] > 0)] = (88, 213, 149)
                    label = '실제 통로 raw · C>0 & walkable 초록'
                stream = io.BytesIO()
                Image.fromarray(rgb[::-1]).save(stream, format='PNG')
                diagnostics.append('<figure><figcaption>' + html.escape(origin + ' · ' + label)
                                   + ' · 게임 화면 아님</figcaption><img src="data:image/png;base64,'
                                   + base64.b64encode(stream.getvalue()).decode('ascii') + '"></figure>')
        details = {'source_reference_run': record['source_reference_run'],
                   'run': record['run'], 'biome': record['biome'], 'size': record['size'],
                   'cave_field_pass': record['pass'], 'matching_verified_profiles': profiles,
                   'water': water_row, 'metrics': record['metrics'], 'connectivity': record['connectivity']}
        title = '실제 동굴 field 통과 사례 · ' + ident + ' · ' + record['run']
        cards.append('<section class="cave-success"><h2>' + html.escape(title) + '</h2><p>'
                     + html.escape(admission) + '</p><div class="captures pair">' + ''.join(panels)
                     + '</div><p>아래는 전체 raw 관측의 지붕·통로 진단입니다. 지원되는 known 원본 영역만 재현 판정에 사용하며, 게임 PNG와 구분합니다.</p>'
                     + '<div class="diagnostics">' + ''.join(diagnostics) + '</div><details><summary>동굴·물·등록 profile 실제 평가값</summary><pre>'
                     + html.escape(json.dumps(details, ensure_ascii=False, indent=2)) + '</pre></details></section>')
    if report.get('guards'):
        cards.append('<section><h2>보호·unknown·안전하지 않은 지붕 대조군</h2><p>기대 동작 ' + str(report['guard_passed']) + ' / ' + str(report['guard_checks']) + '. 기대 동작 PASS는 재현 품질 또는 카탈로그 입장 PASS가 아닙니다. unknown 전 영역의 물리 관측 차이도 실패로 기록합니다.</p><details><summary>대조군 실제 감사 및 원자료 차이</summary><pre>' + html.escape(json.dumps(report['guards'], ensure_ascii=False, indent=2)) + '</pre></details></section>')
    nonempty = [record for record in records if record.get('source', {}).get('portable_cave_cells', 0) > 0]
    summary = ('<p>지질 필드 비교 ' + str(report['passed']) + ' / ' + str(report['checks'])
               + ' PASS · 전체 재현 또는 최종 등록 PASS가 아닙니다.</p><p>지원되는 실제 동굴(C&gt;0) 원본 비교 '
               + str(sum(record['pass'] for record in nonempty)) + ' / ' + str(len(nonempty))
               + ' PASS.</p><p>Prototype · 제품 추천 UI 미연결. 등록은 기존 암석·물·전체 지원 바닥도 동시에 PASS해야 합니다. '
               + '기술 검증은 미관·플레이 승인과 별개입니다.</p>')
    document = '<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Actual cave fidelity</title><style>body{margin:0;background:#10151d;color:#e4eaf2;font:16px/1.55 system-ui}main{max-width:1450px;margin:auto;padding:24px}section{background:#1b2330;border-radius:16px;padding:20px;margin:24px 0}h1,h2{line-height:1.2}.captures,.diagnostics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.diagnostics{grid-template-columns:repeat(4,minmax(0,1fr))}figure{margin:0}figcaption{padding:8px 0;color:#cbd5e1}img{width:100%;height:auto;image-rendering:pixelated}.absent{background:#273140;min-height:150px;padding:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere;max-height:600px;overflow:auto;font-size:12px}@media(max-width:760px){main{padding:12px}.captures,.diagnostics{grid-template-columns:1fr}section{padding:12px}}</style><main><h1>실제 동굴 관측 재현 검증</h1>' + summary + '<p>CaveGrid 양수, 실제 자연 지붕/None, E/C float, walkable 통로 및 출입구 연결을 따로 검사합니다. 원본의 인공 지붕·바닥·건물은 전체 raw 기록에 남기며 휴대 가능한 비교에서는 제외합니다. 지붕은 지도 PNG로 추론하지 않습니다. 광석·자원 양·save/world 설정 복제, 제품 UI 및 설치 DLL 변경 여부는 본 지질 비교의 판정 범위 밖이며 별도 실행 영수증으로 확인합니다.</p>' + ''.join(cards) + '</main></html>'
    document = document.replace('.diagnostics{grid-template-columns:repeat(4,minmax(0,1fr))}',
                                '.diagnostics{grid-template-columns:repeat(4,minmax(0,1fr))}.pair{grid-template-columns:repeat(2,minmax(0,1fr))}', 1)
    (folder / 'cave-review.html').write_text(document, encoding='utf-8')
    return document


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', type=pathlib.Path, required=True)
    parser.add_argument('--runs', nargs='+', required=True)
    parser.add_argument('--rock-only-runs', nargs='*', default=[])
    parser.add_argument('--review', action='store_true')
    args = parser.parse_args()
    report = evaluate(args.folder, args.runs)
    if args.review:
        review(args.folder, args.runs, report, args.rock_only_runs)
    print(json.dumps({k: report[k] for k in ('checks', 'passed', 'failed', 'final_audit_missing', 'final_audit_invalid')}, ensure_ascii=False))
    return 1 if report['failed'] or report['guard_failed'] or not (report['checks'] or report['guard_checks']) else 0


if __name__ == '__main__':
    raise SystemExit(main())
