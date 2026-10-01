"""Independently measure complete rock occupancy and show actual native captures.

Truth is the original per-cell observation, including ore-occupied M cells. No
recipe RLE, contour closing, or minimum-component filter defines the denominator.
"""
import argparse
import base64
import html
import json
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from skimage import measure

from ground import role
from measure_transfer import scaled


SCENES = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff',
          'gl-archipelago', 'gl-oasis')
GUARDS = ('rock-guard', 'unknown-rock')
INVARIANTS = ('protected_changes', 'unknown_changes', 'known_source_mismatches')
EARLY_INVARIANTS = ('protected_elevation_changes', 'protected_cave_changes',
                    'unknown_elevation_changes', 'unknown_cave_changes',
                    'known_grid_mismatches')
GUARD_PROTECTIONS = ('river', 'sea', 'special_water', 'water', 'road',
                     'constructed_floor', 'building', 'special_rock',
                     'preexisting_rock', 'preexisting_resource', 'other_thing', 'unsupported_terrain')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n',
                    encoding='utf-8')


def observation(path):
    data = read(path)
    if data.get('schema_version') != 2 or data.get('row_order') != 'south-first':
        raise ValueError('Actual south-first per-cell observation v2 required: ' + str(path))
    shape = (data['height'], data['width'])
    if min(shape) <= 0 or len(data['cells']) != shape[0] * shape[1]:
        raise ValueError('Invalid observation dimensions: ' + str(path))
    cells = np.asarray(list(data['cells'])).reshape(shape)
    names = np.asarray(data['terrain_table'])[np.asarray(data['terrain_indices']).reshape(shape)]
    return data, cells, names


def occupancy(expected, actual):
    """Exact integer differences; empty expected/actual equality is meaningful."""
    if expected.shape != actual.shape:
        raise ValueError('Occupancy dimensions differ')
    expected = np.asarray(expected, dtype=bool)
    actual = np.asarray(actual, dtype=bool)
    wanted = int(expected.sum())
    found = int(actual.sum())
    intersection = int((expected & actual).sum())
    extra = int((actual & ~expected).sum())
    missing = int((expected & ~actual).sum())
    union = wanted + extra
    return {'expected_cells': wanted, 'actual_cells': found,
            'intersection_cells': intersection, 'extra_cells': extra,
            'missing_cells': missing,
            'precision': intersection / found if found else (1.0 if not wanted else 0.0),
            'recall': intersection / wanted if wanted else 1.0,
            'iou': intersection / union if union else 1.0}


def component_retention(source_mask, target_mask, size, tiny_limit=None):
    """Label the unmodified source, then map each retained label by nearest cell."""
    labels = measure.label(source_mask, connectivity=1)
    counts = np.bincount(labels.ravel())
    if tiny_limit is None:
        selected = np.flatnonzero((counts < 40) | (counts < source_mask.size * .0015))
        criterion = 'Original 4-connected component <40 cells OR <0.15% of source map'
    else:
        selected = np.flatnonzero(counts < tiny_limit)
        criterion = 'Original 4-connected component <' + str(tiny_limit) + ' cells'
    selected = selected[selected != 0]
    mapped = scaled(labels, size)
    mapped_counts = np.bincount(mapped.ravel(), minlength=len(counts))
    included_counts = np.bincount(mapped[target_mask], minlength=len(counts))
    rows = []
    for label in selected:
        expected = int(mapped_counts[label])
        included = int(included_counts[label])
        rows.append({'source_component': int(label), 'source_cells': int(counts[label]),
                     'mapped_cells': expected, 'included_cells': included,
                     'coverage': included / expected if expected else None,
                     'fully_retained': bool(expected and included == expected)})
    expected = sum(r['mapped_cells'] for r in rows)
    included = sum(r['included_cells'] for r in rows)
    return {'criterion': criterion, 'source_components': len(rows),
            'source_cells': sum(r['source_cells'] for r in rows),
            'mapped_components': sum(r['mapped_cells'] > 0 for r in rows),
            'unobservable_after_scaling': sum(r['mapped_cells'] == 0 for r in rows),
            'fully_retained_components': sum(r['fully_retained'] for r in rows),
            'partially_retained_components': sum(0 < r['included_cells'] < r['mapped_cells'] for r in rows),
            'missing_components': sum(r['mapped_cells'] > 0 and not r['included_cells'] for r in rows),
            'mapped_cells': expected, 'included_cells': included,
            'missing_cells': expected - included,
            'coverage': included / expected if expected else None, 'components': rows}


def supported_ground(palette):
    return {d['def'] for d in palette['terrains']
            if d['supported'] and not d['water'] and not d['temporary']
            and not d.get('dangerous', False) and role(d) != 'unknown'}


def ground_fidelity(source, source_cells, source_names, actual_cells, actual_names,
                    target_biome, palette, size):
    """All supported source natural G cells, even omitted one-cell patches."""
    supported = supported_ground(palette)
    source_ground = (source_cells == 'G') & np.isin(source_names, list(supported))
    expected = scaled(source_ground, size)
    truth_names = scaled(source_names, size)
    actual_ground = (actual_cells == 'G') & np.isin(actual_names, list(supported))
    covered = expected & actual_ground
    matched = covered & (truth_names == actual_names)
    count = int(expected.sum())
    covered_count = int(covered.sum())
    matched_count = int(matched.sum())
    materials = []
    for name in sorted(set(source_names[source_ground].tolist())):
        source_mask = source_ground & (source_names == name)
        mask = expected & (truth_names == name)
        wanted = int(mask.sum())
        actual_covered = int((mask & actual_ground).sum())
        exact = int((mask & matched).sum())
        small_coverage = component_retention(source_mask, actual_ground, size)
        small_named = component_retention(source_mask, matched, size)
        tiny_coverage = component_retention(source_mask, actual_ground, size, tiny_limit=12)
        tiny_named = component_retention(source_mask, matched, size, tiny_limit=12)
        materials.append({'def': name, 'source_cells': int(source_mask.sum()),
                          'mapped_cells': wanted, 'covered_ground_cells': actual_covered,
                          'named_matching_cells': exact,
                          'coverage': actual_covered / wanted if wanted else None,
                          'named_agreement': exact / wanted if wanted else None,
                          'small_patches_coverage': small_coverage,
                          'small_patches_named': small_named,
                          'tiny_patches_coverage': tiny_coverage,
                          'tiny_patches_named': tiny_named})

    def aggregate(key):
        patches = [m[key] for m in materials]
        wanted = sum(p['mapped_cells'] for p in patches)
        included = sum(p['included_cells'] for p in patches)
        return {'source_components': sum(p['source_components'] for p in patches),
                'source_cells': sum(p['source_cells'] for p in patches),
                'mapped_components': sum(p['mapped_components'] for p in patches),
                'fully_retained_components': sum(p['fully_retained_components'] for p in patches),
                'missing_components': sum(p['missing_components'] for p in patches),
                'mapped_cells': wanted, 'included_cells': included,
                'missing_cells': wanted - included,
                'coverage': included / wanted if wanted else None}

    same = source['biome'] == target_biome
    agreement = matched_count / count if count else None
    return {'same_biome': same, 'source_biome': source['biome'],
            'target_biome': target_biome, 'source_natural_ground_cells': int((source_cells == 'G').sum()),
            'source_supported_ground_cells': int(source_ground.sum()),
            'source_unsupported_ground_cells': int(((source_cells == 'G') & ~source_ground).sum()),
            'mapped_source_ground_cells': count, 'covered_ground_cells': covered_count,
            'missing_ground_cells': count - covered_count,
            'named_matching_cells': matched_count, 'named_mismatching_cells': count - matched_count,
            'ground_coverage': covered_count / count if count else None,
            'source_named_ground_agreement': agreement,
            'named_agreement_on_covered_ground': matched_count / covered_count if covered_count else None,
            'same_biome_fidelity_pass': bool(agreement is not None and agreement >= .95) if same else None,
            'small_patches_coverage': aggregate('small_patches_coverage'),
            'small_patches_named': aggregate('small_patches_named'),
            'tiny_patches_coverage': aggregate('tiny_patches_coverage'),
            'tiny_patches_named': aggregate('tiny_patches_named'), 'materials': materials,
            'scope': 'All original supported natural G cells, no recipe mask. Cross-biome name agreement is diagnostic, not exact-copy acceptance.'}


def invariant_check(path, early=False):
    if not path.exists():
        return {'present': False, 'pass': False, 'reasons': ['Missing runtime receipt: ' + path.name]}
    application = read(path)
    required = INVARIANTS + (EARLY_INVARIANTS if early else ())
    reasons = ['Missing invariant ' + k for k in required if k not in application]
    reasons += ['Missing runtime field ' + k for k in
                ('protected_conflicts', 'protection_kinds', 'added_rock_cells',
                 'removed_rock_cells', 'removed_resource_cells') if k not in application]
    if application.get('stage') != (199 if early else 404):
        reasons.append('Unexpected or missing application stage')
    # New explicit protection counters are also inspected, without guessing their values.
    checked = set(required) | {k for k in application if k.endswith('_changes')}
    reasons += [k + ' is nonzero' for k in sorted(checked)
                if k in application and application[k] != 0]
    return {'present': True, 'pass': not reasons, 'reasons': reasons, **application}


def final_audit(path):
    """Read-only final capture, after native structures have been generated."""
    if not path.exists():
        return {'present': False, 'structure_pass': False, 'pass': False,
                'reasons': ['Missing final capture rock audit: ' + path.name]}
    audit = read(path)
    counters = ('known_source_mismatches', 'missing_source_rock_cells',
                'extra_rock_cells_on_known_nonrock', 'protected_conflicts',
                'unprotected_mismatches', 'known_rock_cells', 'known_nonrock_cells', 'unknown_cells')
    dimensions = ('source_width', 'source_height', 'target_width', 'target_height')
    dictionaries = ('protection_kinds', 'mismatch_protection_kinds',
                    'mismatch_terrain_defs', 'mismatch_building_defs',
                    'mismatch_other_thing_defs')
    reasons = ['Missing/invalid final audit counter ' + k for k in counters
               if type(audit.get(k)) is not int or audit[k] < 0]
    reasons += ['Missing/invalid final audit dictionary ' + k for k in dictionaries
                if not isinstance(audit.get(k), dict)]
    reasons += ['Missing/invalid final audit dimension ' + k for k in dimensions
                if type(audit.get(k)) is not int or audit[k] <= 0]
    if audit.get('schema_version') != 1 or audit.get('stage') != 99999:
        reasons.append('Unexpected or missing final audit schema/stage')
    if audit.get('row_order') != 'south-first':
        reasons.append('Unexpected or missing final audit row order')
    if not isinstance(audit.get('mismatches'), list):
        reasons.append('Missing/invalid final audit mismatch records')
    if not reasons:
        total = audit['known_source_mismatches']
        if total != audit['missing_source_rock_cells'] + audit['extra_rock_cells_on_known_nonrock']:
            reasons.append('Final audit mismatch totals disagree')
        if total != audit['protected_conflicts'] + audit['unprotected_mismatches']:
            reasons.append('Final audit protection totals disagree')
        if total != len(audit['mismatches']):
            reasons.append('Final audit mismatch record count disagrees')
        if (audit['known_rock_cells'] + audit['known_nonrock_cells'] + audit['unknown_cells']
                != audit['target_width'] * audit['target_height']):
            reasons.append('Final audit observed/unknown cell totals disagree with dimensions')
    structure_pass = not reasons
    if audit.get('protected_conflicts', 0):
        reasons.append('Final capture source occupancy conflicts with protected target features; reject candidate')
    return {**audit, 'present': True, 'structure_pass': structure_pass,
            'pass': not reasons, 'reasons': reasons}


def identical_observation(before, after):
    def expanded(data, key):
        return np.asarray(data['terrain_table'])[np.asarray(data[key])]
    return {'cells_identical': before['cells'] == after['cells'],
            'dimensions_identical': (before['width'], before['height']) == (after['width'], after['height']),
            'biome_identical': before['biome'] == after['biome'],
            'terrain_defs_identical': before['terrain_defs'] == after['terrain_defs'],
            'permanent_terrain_identical': bool(np.array_equal(expanded(before, 'terrain_indices'), expanded(after, 'terrain_indices'))),
            'surface_terrain_identical': bool(np.array_equal(expanded(before, 'surface_indices'), expanded(after, 'surface_indices')))}


def guard_check(run, scene, early, final, audit):
    ident = scene['id']
    check = {'id': ident, 'run': run.name, 'biome': scene['biome'],
             'size': scene['size'], 'early': early, 'final': final, 'final_audit': audit}
    checks = {'early_invariants': early['pass'], 'final_invariants': final['pass'],
              'final_audit_structure': audit['structure_pass']}
    if ident == 'unknown-rock':
        before = read(run / (ident + '-baseline-terrain.json'))
        after = read(run / (ident + '-terrain.json'))
        checks.update(identical_observation(before, after))
        checks['native_png_identical'] = ((run / (ident + '-map.png')).read_bytes()
                                           == (run / (ident + '-baseline-map.png')).read_bytes())
        checks['default_png_identical'] = ((run / (ident + '-map-default.png')).read_bytes()
                                            == (run / (ident + '-baseline-map-default.png')).read_bytes())
        checks['positive_unknown_cells'] = final.get('unknown_cells', 0) > 0
        checks['no_rock_additions'] = final.get('added_rock_cells') == 0
        checks['no_rock_removals'] = final.get('removed_rock_cells') == 0
        checks['no_resource_removals'] = final.get('removed_resource_cells') == 0
        checks['final_audit_no_known_mismatches'] = audit.get('known_source_mismatches') == 0
        checks['final_audit_no_protected_conflicts'] = audit.get('protected_conflicts') == 0
    else:
        kinds = final.get('protection_kinds', {})
        for kind in GUARD_PROTECTIONS:
            checks['positive_protection_' + kind] = kinds.get(kind, 0) > 0
        for key in ('protected_conflicts', 'added_rock_cells', 'removed_rock_cells',
                    'removed_resource_cells', 'unknown_cells'):
            checks['positive_' + key] = final.get(key, 0) > 0
        _, cells, names = observation(run / (ident + '-terrain.json'))
        checks['unknown_rock_fixture_preserved'] = bool(cells[5, 5] == 'M')
        checks['new_rock_fixture_added'] = bool(cells[30, 30] == 'M')
        checks['preexisting_resource_fixture_preserved'] = bool(cells[12, 12] == 'M')
        checks['ordinary_rock_fixture_removed'] = bool(cells[12, 13] != 'M')
        checks['ordinary_resource_fixture_removed'] = bool(cells[12, 14] != 'M')
        checks['preexisting_rock_fixture_preserved'] = bool(cells[12, 15] == 'M')
        checks['special_cost_floor_fixture_preserved'] = bool(
            cells[20, 31] == 'G' and names[20, 31] == 'AncientConcrete')
        fixture_path = run / (ident + '-rock-fixtures.json')
        fixture = read(fixture_path) if fixture_path.exists() else {}
        check['fixtures'] = fixture
        floor = fixture.get('special_constructed_floor', {})
        checks['special_cost_floor_fixture_metadata_present'] = bool(fixture)
        checks['special_cost_floor_fixture_properties'] = bool(
            fixture.get('schema_version') == 1 and fixture.get('stage') == 403.5
            and floor.get('x') == 31 and floor.get('z') == 20
            and floor.get('terrain') == 'AncientConcrete'
            and 'designation_category' in floor and floor['designation_category'] is None
            and floor.get('natural') is False and floor.get('layerable') is True)
        checks['final_audit_positive_protected_conflicts'] = audit.get('protected_conflicts', 0) > 0
    check['checks'] = checks
    check['pass'] = all(checks.values())
    check['reasons'] = [key for key, value in checks.items() if not value]
    return check


def evaluate(folder, runs, source=None):
    entries = {e['id']: e for e in read(folder / 'catalog.json')['entries']}
    source_root = source if source is not None else folder / 'source-native-final'
    palette = read(source_root / 'native-terrain-palette.json')
    records, guards, legacy = [], [], []
    for run in runs:
        result = read(run / 'result.json')
        if not result['ok']:
            raise ValueError('Incomplete native generation: ' + str(run))
        for scene in result['results']:
            ident = scene['id']
            if ident in ('cave-guard', 'unknown-cave', 'cave-unsafe'):
                continue  # Dedicated cave evaluator must prove these controls.
            path = run / (ident + '-rock-application.json')
            if (not path.exists() and ident not in GUARDS
                    and not entries[ident].get('requires_rock_sidecar', False)):
                legacy.append({'id': ident, 'run': run.name, 'biome': scene['biome'],
                               'size': scene['size'], 'rock_sidecar_applied': False,
                               'scope': 'Legacy generator; same-profile prior capture comparison belongs to the legacy regression receipt.'})
                continue
            early = invariant_check(run / (ident + '-rock-grid-application.json'), early=True)
            final = invariant_check(path)
            audit = final_audit(run / (ident + '-rock-final-audit.json'))
            if ident in GUARDS:
                guards.append(guard_check(run, scene, early, final, audit))
                continue
            entry = entries[ident]
            source_id = entry['source']['reference_id']
            source_path = source_root / (source_id + '-terrain.json')
            source, source_cells, source_names = observation(source_path)
            actual, actual_cells, actual_names = observation(run / (ident + '-terrain.json'))
            size = scene['size']
            rock = occupancy(scaled(source_cells == 'M', size), actual_cells == 'M')
            reasons = ['early: ' + reason for reason in early['reasons']]
            reasons += ['final: ' + reason for reason in final['reasons']]
            reasons += audit['reasons']
            if audit['structure_pass']:
                known_nonrock = scaled(np.isin(source_cells, ['G', 'S', 'W']), size)
                known_rock = scaled(source_cells == 'M', size)
                if entry.get('requires_cave_sidecar'):
                    # This is an independent physical source classification,
                    # not the recipe's declared known mask. Keep full original
                    # M and ground denominators above unchanged.
                    geology = read(source_root / (source_id + '-geology.json'))
                    original_shape = source_cells.shape
                    roof_names = np.asarray(geology['roof_table'])[np.asarray(geology['roof_indices']).reshape(original_shape)]
                    portable = np.isin(roof_names, ['None', 'RoofRockThin', 'RoofRockThick'])
                    portable &= np.asarray(list(geology['known_mask'])).reshape(original_shape) == '1'
                    for field in ('constructed_floor', 'nonrock_edifice'):
                        portable &= np.asarray(list(geology[field])).reshape(original_shape) == '0'
                    portable = scaled(portable, size)
                    known_nonrock &= portable; known_rock &= portable
                actual_known_extra = int((known_nonrock & (actual_cells == 'M')).sum())
                if (audit['source_width'], audit['source_height']) != (source['width'], source['height']):
                    reasons.append('Final audit source dimensions disagree with independent observation')
                if (audit['target_width'], audit['target_height']) != (actual['width'], actual['height']):
                    reasons.append('Final audit target dimensions disagree with independent observation')
                if audit['known_rock_cells'] != int(known_rock.sum()) or audit['known_nonrock_cells'] != int(known_nonrock.sum()):
                    reasons.append('Final audit source occupancy counts disagree with independent original-source observation')
                known_missing = int((known_rock & (actual_cells != 'M')).sum())
                if audit['missing_source_rock_cells'] != known_missing:
                    reasons.append('Final audit missing rock count disagrees with independent original-source observation')
                if audit['extra_rock_cells_on_known_nonrock'] != actual_known_extra:
                    reasons.append('Final audit known-nonrock extra count disagrees with independent original-source observation')
            if early.get('protected_conflicts', 0) or final.get('protected_conflicts', 0):
                reasons.append('Source composition conflicts with protected target features; reject candidate')
            for key in ('iou', 'precision', 'recall'):
                if rock[key] < .98:
                    reasons.append('raw rock ' + key + ' below 0.98')
            ground = ground_fidelity(source, source_cells, source_names, actual_cells,
                                     actual_names, scene['biome'], palette, size)
            if ground['same_biome_fidelity_pass'] is False:
                reasons.append('Same-biome full-source named ground agreement below 0.95')
            records.append({'id': ident, 'run': run.name, 'biome': scene['biome'],
                            'size': size, 'tile': scene['tile'], 'pass': not reasons,
                            'source_reference_id': source_id,
                            'source_rock_cells': int((source_cells == 'M').sum()),
                            'metrics': {'raw_rock': rock,
                                        'wet': occupancy(scaled(np.isin(source_cells, ['S', 'W']), size), np.isin(actual_cells, ['S', 'W']))},
                            'extra_rock_cells': rock['extra_cells'], 'missing_rock_cells': rock['missing_cells'],
                            'small_rock_components': component_retention(source_cells == 'M', actual_cells == 'M', size),
                            'ground': ground, 'early': early, 'application': final, 'final_audit': audit,
                            'counts': scene['counts'], 'reasons': reasons})
    report = {'records': records, 'guards': guards, 'legacy_controls': legacy,
              'checks': len(records), 'passed': sum(r['pass'] for r in records),
              'failed': sum(not r['pass'] for r in records),
              'guard_checks': len(guards), 'guard_passed': sum(g['pass'] for g in guards),
              'guard_failed': sum(not g['pass'] for g in guards),
              'same_biome_ground_checks': sum(r['ground']['same_biome'] for r in records),
              'same_biome_ground_passed': sum(r['ground']['same_biome_fidelity_pass'] is True for r in records),
              'final_audit_checks': len(records),
              'final_audit_passed': sum(r['final_audit']['pass'] for r in records),
              'final_audit_missing': sum(not r['final_audit']['present'] for r in records),
              'final_audit_invalid': sum(r['final_audit']['present'] and not r['final_audit']['structure_pass'] for r in records),
              'final_protected_conflicts': sum(r['final_audit'].get('protected_conflicts', 0) for r in records
                                               if type(r['final_audit'].get('protected_conflicts')) is int),
              'protocol': 'Raw source cells M including ore occupancy vs final cells M; exact extra/missing counts, IoU/precision/recall >=0.98, zero early/final invariant violations. Required read-only final capture audit rejects every protected occupancy conflict, even when raw IoU passes; missing/invalid audit fails closed. Original small components are reported independently without closing or omission. All supported original natural G cells define ground coverage/agreement, regardless of recipe coverage; same-biome named ground agreement >=0.95 is also required for final passage. Cross-biome exact-name fidelity is diagnostic only.',
              'boundaries': {'resources': 'Current tile native ore generation; newly generated ore outside the observed footprint can be removed. Source ore types and quantities are not cloned. Preexisting/special resource fixtures are protected.',
                             'geology': 'New ordinary rock uses target native stone; source stone types are not cloned.',
                             'roof_caves': 'Early elevation/cave grid invariants are checked. Source cave contents, roof topology, events and structures are not transplanted; final cave/roof correspondence is not a fidelity claim.',
                             'product': 'Developer probe/sidecars only. Product UI, installed DLL and distribution are unchanged by this tool.',
                             'visual': 'Actual native PNGs only; numerical passage is not aesthetic or live-game approval.'},
              'paid_api_calls': 0}
    write(folder / 'rock-evaluation.json', report)
    return report


def percent(value):
    return '해당 없음' if value is None else f'{value:.2%}'


def picture(path, label):
    encoded = base64.b64encode(path.read_bytes()).decode()
    uri = 'data:image/png;base64,' + encoded
    label = html.escape(label)
    return '<figure><a href="' + uri + '" target="_blank"><img src="' + uri + '" alt="' + label + '"></a><figcaption>' + label + '</figcaption></figure>'


def audit_summary(audit):
    keys = ('present', 'structure_pass', 'pass', 'stage', 'known_source_mismatches',
            'missing_source_rock_cells', 'extra_rock_cells_on_known_nonrock',
            'protected_conflicts', 'unprotected_mismatches', 'unknown_cells',
            'mismatch_protection_kinds', 'mismatch_terrain_defs',
            'mismatch_building_defs', 'mismatch_other_thing_defs', 'roof_defs',
            'natural_roof_cells', 'roofed_known_rock_cells',
            'natural_roofed_known_rock_cells', 'reasons')
    return {key: audit[key] for key in keys if key in audit}


def review(folder, runs, report):
    entries = {e['id']: e for e in read(folder / 'catalog.json')['entries']}
    source = folder / 'source-native-final'
    previous = folder.parent / 'water-v3/transfer-a-native-r2'
    run = next(r for r in runs if all((r / (ident + '-map.png')).exists() for ident in SCENES))
    rows = [r for r in report['records'] if r['run'] == run.name]
    first = {r['id']: r for r in rows}
    doc = '''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MapGen AI · 작은 바위와 바닥 보존</title><style>
    *{box-sizing:border-box}body{margin:0;background:#14201f;color:#e6ede5;font:16px/1.65 system-ui}main{max-width:1200px;margin:auto;padding:24px}h1{font-size:30px;line-height:1.3}h2{font-size:23px}section{background:#1c2c29;padding:20px;margin:20px 0;border-radius:12px}.row{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}img{display:block;width:100%;image-rendering:pixelated}figure{margin:0}figcaption{font-size:14px;color:#c4d5c8;margin-top:5px}a{color:#a0dacc}.scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:14px}td,th{border-bottom:1px solid #365249;padding:8px;text-align:left;vertical-align:top}code{overflow-wrap:anywhere}details{margin:14px 0}summary{cursor:pointer}strong{color:#f2d391}.status{font-weight:bold;color:#a0dacc}.fail{color:#ffb5a4}.note{color:#c4d5c8}pre{white-space:pre-wrap;overflow-wrap:anywhere}ul{padding-left:23px}@media(max-width:700px){main{padding:12px}section{padding:14px}.row{grid-template-columns:1fr}h1{font-size:26px}td,th{padding:6px}}
    </style></head><body><main><h1>작은 바위 조각과 바닥 패치까지 보존</h1><p>원본 지도에서 실제로 관찰한 모든 자연 바위 칸과 일반 광석이 차지한 칸을 읽습니다. 작은 조각을 생략하거나 윤곽을 매끈하게 닫지 않고, 현재 타일에서 만들어진 바위의 최종 위치와 직접 비교합니다.</p><p><strong>개발자 프로토타입입니다.</strong> 제품 UI·설치 DLL·배포판은 이번 작업의 대상이 아닙니다. 아래 그림은 실제 RimWorld 네이티브 캡처이며 원본과 결과를 닮게 다시 그린 이미지가 아닙니다.</p>'''
    doc += '<section><h2>원본 → 이전 물·바닥 이식 → 이번 세부 보존</h2><p>이전 열은 water-v3/transfer-a-native-r2, 이번 열은 ' + html.escape(run.name) + '입니다. 같은 바이옴·크기의 첫 실험을 나란히 봅니다. 이미지를 누르면 원본 해상도로 열립니다.</p>'
    for ident in SCENES:
        entry = entries[ident]
        source_id = entry['source']['reference_id']
        doc += '<h3>' + html.escape(entry['title_ko']) + ' <small>' + ident + '</small></h3><div class="row">'
        doc += picture(source / (source_id + '-map.png'), 'GL 원본 · 실제 지형 색')
        doc += picture(previous / (ident + '-map.png'), '이전 · 물과 바닥')
        doc += picture(run / (ident + '-map.png'), '이번 · 작은 바위와 바닥 포함') + '</div>'
        r = first[ident]
        rock = r['metrics']['raw_rock']
        small = r['small_rock_components']
        ground = r['ground']
        doc += '<p class="note">원본 바위 ' + str(r['source_rock_cells']) + '칸 · 최종 추가 오차 ' + str(rock['extra_cells']) + '칸 / 누락 ' + str(rock['missing_cells']) + '칸 · 바위 IoU ' + percent(rock['iou']) + ' · 작은 원본 조각 ' + str(small['source_components']) + '개, 실제 포함 ' + percent(small['coverage']) + ' · 원본 자연 바닥 전체 이름 일치 ' + percent(ground['source_named_ground_agreement']) + '</p>'
        doc += '<details><summary>일반 미리보기 색으로 비교</summary><div class="row">' + picture(source / (source_id + '-map-default.png'), '원본 · 일반 색') + picture(previous / (ident + '-map-default.png'), '이전 · 일반 색') + picture(run / (ident + '-map-default.png'), '이번 · 일반 색') + '</div></details>'
    doc += '</section><section><h2>원본 전체를 분모로 한 검사</h2><p>바위 비교의 정답은 원본 terrain.json의 M 칸입니다. 광석 칸을 포함하며 source RLE·기존 최소 크기·closing 결과는 정답에 사용하지 않습니다. 크기가 달라지면 원본 칸 위치를 가장 가까운 칸으로 대응시킵니다. 작은 조각은 원본에서 4방향으로 연결된 조각이 40칸 미만이거나 지도 전체의 0.15% 미만인 경우입니다.</p><p>바닥도 원본의 지원 가능한 자연 G 칸 전체를 분모로 삼습니다. 결과에서 바위·물 때문에 가려진 칸이나 레시피에서 빠졌던 칸도 누락으로 셉니다. 같은 바이옴은 재료 이름 보존을 평가하고, 다른 바이옴은 적응 사례로 표시합니다.</p><div class="scroll"><table><thead><tr><th>사례 / 실험</th><th>조건</th><th>최종 결과</th><th>최종 보호 충돌</th><th>추가 / 누락</th><th>precision / recall / IoU</th><th>작은 바위 포함</th><th>전체 자연 바닥 coverage / 이름</th><th>12칸 미만 바닥 이름</th></tr></thead><tbody>'
    for r in report['records']:
        rock, ground = r['metrics']['raw_rock'], r['ground']
        adaptation = '' if ground['same_biome'] else '<br><strong>바이옴 적응 · 이름은 진단값</strong>'
        doc += '<tr><td>' + html.escape(r['id'] + ' / ' + r['run']) + '</td><td>' + html.escape(r['biome']) + ' ' + str(r['size']) + '</td><td class="' + ('status' if r['pass'] else 'fail') + '">' + ('PASS' if r['pass'] else 'FAIL') + '</td><td>' + str(r['final_audit'].get('protected_conflicts', '감사 없음')) + '</td><td>' + str(rock['extra_cells']) + ' / ' + str(rock['missing_cells']) + '</td><td>' + ' / '.join(percent(rock[k]) for k in ('precision', 'recall', 'iou')) + '</td><td>' + percent(r['small_rock_components']['coverage']) + '</td><td>' + percent(ground['ground_coverage']) + ' / ' + percent(ground['source_named_ground_agreement']) + adaptation + '</td><td>' + percent(ground['tiny_patches_named']['coverage']) + '</td></tr>'
    doc += '</tbody></table></div><p>원본 세부 보존 통과 ' + str(report['passed']) + '/' + str(report['checks']) + ' · 보호 양성/unknown 대조 통과 ' + str(report['guard_passed']) + '/' + str(report['guard_checks']) + ' · 같은 바이옴 바닥 이름 95% 이상 ' + str(report['same_biome_ground_passed']) + '/' + str(report['same_biome_ground_checks']) + '. 바위의 precision·recall·IoU는 각각 98% 이상, 같은 바이옴의 원본 자연 바닥 전체 이름 일치율은 95% 이상이어야 하며, 초기·중간·최종 어느 단계에서든 보호 대상 충돌이 있으면 후보를 거부합니다. 통과는 미적 평가를 의미하지 않습니다.</p></section>'
    doc += '<section><h2>모든 자연 재료와 작은 바닥 패치</h2><p>첫 실험의 재료별 원본 전체와 이전에 생략되던 12칸 미만 패치를 확인합니다. 한 칸짜리 패치도 분모에 포함합니다.</p><div class="scroll"><table><thead><tr><th>지도 / 재료</th><th>원본 칸</th><th>실제 G coverage</th><th>재료 이름 일치</th><th>12칸 미만 원본 조각 / 칸</th><th>작은 패치 coverage / 이름</th></tr></thead><tbody>'
    for r in rows:
        for m in r['ground']['materials']:
            tiny = m['tiny_patches_named']
            doc += '<tr><td>' + html.escape(r['id'] + ' / ' + m['def']) + '</td><td>' + str(m['source_cells']) + '</td><td>' + percent(m['coverage']) + '</td><td>' + percent(m['named_agreement']) + '</td><td>' + str(tiny['source_components']) + ' / ' + str(tiny['source_cells']) + '</td><td>' + percent(m['small_patches_coverage']['coverage']) + ' / ' + percent(m['small_patches_named']['coverage']) + '</td></tr>'
    doc += '</tbody></table></div></section><section><h2>보호와 대조 사례</h2><p>초기 elevation/cave 격자 적용과 최종 바위 적용의 보호·unknown 변화 수를 각각 검사합니다. unknown-rock은 전체 칸 분류, 영구/표면 TerrainDef, 재료별 수량과 두 PNG가 paired baseline과 같은지도 직접 비교합니다. rock-guard는 보호 대상이 실제로 있고, 바위 추가·제거·일반 광석 제거가 실제로 일어나는 양성 사례입니다.</p>'
    for guard in report['guards']:
        doc += '<details><summary>' + html.escape(guard['id'] + ' / ' + guard['run']) + ' · ' + ('PASS' if guard['pass'] else 'FAIL') + '</summary><pre>' + html.escape(json.dumps(guard, ensure_ascii=False, indent=2)) + '</pre></details>'
    doc += '<p>category가 없는 특수 건축 바닥도 보호합니다. rock-guard의 (x31,z20) AncientConcrete는 최종 terrain.json에서 재료 이름을 직접 확인합니다.</p></section><section><h2>후반 native 생성까지 끝난 최종 감사</h2><p>바위(404)와 바닥(405) 적용 뒤에도 native 구조물과 지형이 생길 수 있습니다. 최종 캡처(99999)는 맵을 수정하지 않고 실제 바위 누락·추가와 현재 보호 대상의 충돌을 기록합니다. 바위 IoU가 98% 이상이어도 최종 보호 충돌은 한 칸부터 후보 거부 조건입니다. 감사 파일이 없거나 필수 항목·원본 계측과 맞지 않아도 통과시키지 않습니다. 지붕·동굴 수치는 진단이며 원본 복제의 정확도 기준이 아닙니다.</p>'
    for r in report['records']:
        doc += '<details><summary>' + html.escape(r['id'] + ' / ' + r['run']) + ' · ' + ('PASS' if r['pass'] else 'FAIL') + '</summary><p>' + html.escape('; '.join(r['reasons']) or '최종 충돌 없이 수치 기준 충족') + '</p><pre>' + html.escape(json.dumps(audit_summary(r['final_audit']), ensure_ascii=False, indent=2)) + '</pre></details>'
    doc += '<p>core-foothills와 core-dry-clearing은 rock sidecar를 쓰지 않는 기존 생성 대조입니다. 그 결과를 native baseline과 같아야 한다고 판단하지 않습니다. 이전의 같은 world/tile/profile 캡처와의 별도 회귀 비교를 사용합니다.</p></section><section><h2>적용 범위와 남은 한계</h2><ul><li>광석은 현재 타일에서 새로 생성합니다. 완전한 원본 바위 영역 밖으로 새 광석이 튀어나오면 제거할 수 있으며 removed_resource_cells로 별도 기록합니다. 원본의 광석 종류·수량을 복제하지 않습니다. 기존·특수 자원은 보호합니다.</li><li>새 일반 바위는 현재 타일의 native stone을 사용합니다. 원본의 암석 종류를 그대로 옮기는 지질 복사는 아닙니다.</li><li>물·강·바다·특수 물·도로·건축 바닥·건물·기존/특수 바위와 unknown 칸을 보존합니다. 보호 대상과 원본 점유가 충돌하면 그대로 남기고 후보를 거부합니다.</li><li>초기 cave/elevation 불변 조건은 검사하지만 원본 동굴 내용·지붕 배치·자원 분포·구조물·이벤트를 이식하지 않습니다. 최종 지붕/동굴 모양의 원본 일치를 주장하지 않습니다.</li><li>직접 terrain 데이터가 있는 6개 원본 사례의 실험입니다. 이미지 입력은 별도 보수적 경로로 유지합니다. 사진·JPEG·임의 모드와 실제 플레이 품질은 미검증입니다.</li><li>제품 채팅·프리뷰·저장·Undo 연결은 후속 작업입니다. 이 도구는 제품 UI·설치 DLL·배포판을 변경하지 않으며 유료 API를 호출하지 않습니다.</li></ul><p class="note">HTML의 모든 지도 PNG는 파일 안에 포함됩니다. 이 보고서 생성 자체는 브라우저 렌더 검증이나 사용자 미적 승인이 아닙니다.</p></section><footer><p>GL: m00nl1ght · Geological Landforms · 파생 레시피/지형 그림 CC BY-NC-SA 4.0. 원본 귀속은 상위 ATTRIBUTION.md를 유지합니다. 게임 자산의 권리를 변경하지 않습니다.</p></footer></main></body></html>'
    (folder / 'review.html').write_text(doc, encoding='utf-8')
    font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 20)
    small_font = ImageFont.truetype('C:/Windows/Fonts/malgun.ttf', 15)
    sheet = Image.new('RGB', (1014, 880), '#14201f')
    draw = ImageDraw.Draw(sheet)
    for row, ident in enumerate(('gl-lake', 'gl-valley', 'gl-cliff')):
        y = row * 290 + 12
        draw.text((16, y), entries[ident]['title_ko'], font=font, fill='#e6ede5')
        source_id = entries[ident]['source']['reference_id']
        for col, (directory, image_id, label) in enumerate(((source, source_id, 'GL 원본'),
                                                            (previous, ident, '이전 · 물/바닥'),
                                                            (run, ident, '이번 · 작은 조각 포함'))):
            with Image.open(directory / (image_id + '-map.png')) as capture:
                native = capture.convert('RGB').resize((228, 228), Image.Resampling.NEAREST)
            x = 16 + col * 332
            sheet.paste(native, (x, y + 32))
            draw.text((x, y + 263), label, font=small_font, fill='#c4d5c8')
    sheet.save(folder / 'comparison.png')
    return {'html_bytes': len(doc.encode('utf-8')), 'embedded_native_images': doc.count('<img'),
            'comparison_source_run': run.name, 'comparison_previous_run': str(previous)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', type=pathlib.Path, required=True)
    parser.add_argument('--runs', nargs='+', required=True)
    args = parser.parse_args()
    runs = [args.folder / name for name in args.runs]
    report = evaluate(args.folder, runs)
    rendered = review(args.folder, runs, report)
    print(json.dumps({'passed': report['passed'], 'failed': report['failed'],
                      'guard_passed': report['guard_passed'], 'guard_failed': report['guard_failed'],
                      **rendered}, ensure_ascii=False))
    if report['failed'] or report['guard_failed']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
