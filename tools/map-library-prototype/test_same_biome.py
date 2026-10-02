"""Literal final-capture fixtures exercising replica export and comparisons."""
import copy
import hashlib
import json
import pathlib
import tempfile
import unittest

import numpy as np
from PIL import Image

from same_biome import (evaluate, load_capture, load_replica_layer, observed_replica,
                        original_phase_pngs, prepare_replica, resolve_snapshot_file, validate_snapshot)


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def capture(folder, ident='source', mutate=None):
    """Hand-set final state; early 99999 grids intentionally differ from final."""
    folder.mkdir(parents=True, exist_ok=True)
    terrain = {'schema_version': 2, 'width': 3, 'height': 2, 'row_order': 'south-first',
               'biome': 'TemperateForest', 'cells': 'GGGGGG', 'terrain_table': ['Soil'],
               'terrain_indices': [0, 0, 0, 0, 0, 0], 'surface_indices': [0, 0, 0, 0, 0, 0]}
    terrain_path = folder / (ident + '-terrain.json')
    write(terrain_path, terrain)
    geology = {'schema_version': 1, 'width': 3, 'height': 2, 'row_order': 'south-first',
               'known_mask': '111111', 'elevation': [.1, .1, .1, .1, .1, .1],
               'caves': [0, 0, 0, 0, 0, 0], 'roof_table': ['None'], 'roof_indices': [0] * 6,
               'walkable': '111111', 'source_terrain_sha256': digest(terrain_path)}
    geology_path = folder / (ident + '-geology.json')
    write(geology_path, geology)
    pixels = np.array([[[20, 40, 80, 255], [80, 30, 20, 255], [40, 50, 90, 255]],
                       [[90, 50, 40, 255], [10, 20, 30, 255], [60, 30, 10, 255]]], dtype=np.uint8)
    Image.fromarray(pixels).save(folder / (ident + '-replica-map.png'))
    Image.fromarray(pixels // np.array([2, 2, 2, 1], dtype=np.uint8)).save(folder / (ident + '-replica-map-default.png'))
    data = {'schema_version': 1, 'mode': 'source-replica', 'biome': 'TemperateForest',
            'width': 3, 'height': 2, 'row_order': 'south-first', 'known_mask': '111111',
            'terrain_table': ['None', 'Soil', 'WaterShallow', 'Granite_Rough', 'ThinIce'],
            'top_indices': [1, 2, 1, 1, 1, 1], 'permanent_indices': [1, 2, 3, 1, 1, 1],
            'surface_indices': [1, 2, 3, 4, 1, 1], 'under_indices': [0, 0, 0, 1, 0, 0],
            'foundation_indices': [0, 0, 3, 0, 0, 0], 'temp_indices': [0, 0, 0, 4, 0, 0],
            'color_table': ['None', 'Red'], 'color_indices': [0, 0, 0, 0, 1, 0],
            'elevation': [.81234567, .8, .7, .6, .5, .4], 'caves': [0, 2.9876543, 0, 0, 0, 0],
            'fertility': [-3, .8, 0, .6, .5, .4], 'roof_table': ['None', 'RoofRockThin', 'RoofRockThick'],
            'roof_indices': [0, 1, 0, 0, 0, 0], 'walkable': '100011',
            'native_roof_supported': '010000', 'projected_roof_supported': '010000',
            'roof_def_metadata': {'RoofRockThin': {'is_natural': True, 'can_collapse': True, 'is_thick_roof': False},
                                  'RoofRockThick': {'is_natural': True, 'can_collapse': True, 'is_thick_roof': True}},
            'edifice_indices': [0, 1, 1, 2, 0, 0],
            'edifices': [{'id': 1, 'def': 'Wall', 'stuff': 'Granite', 'rotation': 1, 'x': 1, 'z': 0,
                          'footprint': [1, 2], 'uses_hit_points': True, 'hit_points': 200,
                          'natural_rock': False, 'resource_rock': False, 'door_open': False},
                         {'id': 2, 'def': 'Slate', 'stuff': 'None', 'rotation': 0, 'x': 0, 'z': 1,
                          'footprint': [3], 'uses_hit_points': True, 'hit_points': 400,
                          'natural_rock': True, 'resource_rock': False, 'door_open': False}],
            'context': {'stage': 99999, 'capture_phase': 'post-map-initialized',
                        'source_biome': 'TemperateForest', 'hilliness': 'Flat',
                        'gl_patch_active': True, 'gl_stable_cave_roof_override': False,
                        'engine_version': 'fixture-1', 'engine_assembly_sha256': 'a' * 64,
                        'active_package_ids': ['ludeon.rimworld']},
            'unsupported': [], 'binding_files': {
                'terrain': ident + '-terrain.json', 'geology': ident + '-geology.json',
                'map_png': ident + '-replica-map.png', 'map_default_png': ident + '-replica-map-default.png'}}
    if mutate:
        mutate(data)
    data['source_bindings'] = {name + '_sha256': digest(folder / namefile)
                               for name, namefile in data['binding_files'].items()}
    path = folder / (ident + '-replica.json')
    write(path, data)
    return path, data


def command(data):
    result = {'action': 'generate', 'params': {}}
    for name in ('ground_layer', 'water_layer', 'rock_layer', 'cave_layer'):
        result[name] = {'schema_version': 1, 'width': 3, 'height': 2,
                        'row_order': 'south-first', 'source_biome': 'TemperateForest'}
        if name != 'ground_layer':
            result[name]['mode'] = 'source-geology' if name == 'cave_layer' else 'source-composition'
    result['ground_layer'].update(materials=[{'def': 'Soil', 'role': 'base'}],
                                 runs=[{'start': 0, 'length': 6, 'material': 1}])
    result['water_layer']['runs'] = [{'start': 0, 'length': 6, 'value': 1}]
    result['rock_layer']['runs'] = [{'start': 0, 'length': 6, 'value': 1}]
    result['cave_layer'].update(known='KNKNNK', elevation=[.8] * 6, caves=[0] * 6, roof_codes=[0] * 6,
        source_terrain_sha256=data['source_bindings']['terrain_sha256'],
        source_geology_sha256=data['source_bindings']['geology_sha256'])
    return result


def native_audit(path, source_path, target_data, patch=None):
    counters = {'top_indices': 0, 'permanent_indices': 0, 'surface_indices': 0, 'under_indices': 0,
                'foundation_indices': 0, 'temp_indices': 0, 'color_indices': 0, 'roof_indices': 0,
                'edifice_indices': 0, 'elevation': 0, 'caves': 0, 'fertility': 0, 'walkable': 0,
                'terrain_table': 0, 'roof_table': 0, 'color_table': 0, 'edifice_records': 0,
                'unsupported_features': 0}
    audit = {'schema_version': 1, 'stage': 99999, 'capture_phase': 'post-map-initialized',
             'exact_selected': True, 'applied': True, 'pass': True, 'source_snapshot_sha256': digest(source_path),
             'existing_things_before': 0, 'existing_roofs_before': 0, 'existing_terrain_cells_before': 0,
             'protected_changes': 0, 'unknown_changes': 0, 'user_state_changes': 0, 'unsafe_roof_cells': 0,
             'field_mismatches': counters, 'map_png_hash_equal': True, 'map_default_png_hash_equal': True,
             'actual_map_png_sha256': target_data['source_bindings']['map_png_sha256'],
             'actual_map_default_png_sha256': target_data['source_bindings']['map_default_png_sha256']}
    if patch:
        audit.update(patch)
    write(path, audit)
    return audit


class ReplicaValidatorTests(unittest.TestCase):
    def test_final_raw_grids_are_literal_south_first_and_not_early_grids(self):
        with tempfile.TemporaryDirectory() as root:
            path, _ = capture(pathlib.Path(root))
            _, fields, _ = load_capture(path)
            self.assertEqual(fields['permanent_indices'].tolist(),
                             [['Soil', 'WaterShallow', 'Granite_Rough'], ['Soil', 'Soil', 'Soil']])
            self.assertEqual(fields['surface_indices'][1, 0], 'ThinIce')
            self.assertEqual(fields['under_indices'][1, 0], 'Soil')
            self.assertEqual(fields['temp_indices'][1, 0], 'ThinIce')
            self.assertEqual(fields['elevation'][0, 0], .81234567)
            self.assertEqual(fields['caves'][0, 1], 2.9876543)
            self.assertEqual(fields['fertility'][0, 0], -3)
            self.assertEqual(fields['walkable'].tolist(), [[True, False, False], [False, True, True]])

    def test_missing_actual_grids_and_unknown_cells_are_not_filled_with_none(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            for name in ('top_indices', 'under_indices', 'foundation_indices', 'temp_indices',
                         'color_indices', 'edifices', 'edifice_indices', 'known_mask', 'unsupported'):
                with self.subTest(name=name):
                    raw = copy.deepcopy(data)
                    raw.pop(name)
                    with self.assertRaises(ValueError):
                        validate_snapshot(raw)
            for patch in ({'known_mask': '111110'}, {'unsupported': ['Hive state']}):
                raw = {**data, **patch}
                with self.assertRaises(ValueError):
                    validate_snapshot(raw)

    def test_early_or_unproven_final_capture_phase_is_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            for patch in ({'capture_phase': 'genstep-99999'}, {'capture_phase': None}, {'stage': True}):
                with self.subTest(patch=patch):
                    raw = copy.deepcopy(data)
                    raw['context'].update(patch)
                    with self.assertRaises(ValueError):
                        validate_snapshot(raw)

    def test_actual_numeric_zero_and_null_are_valid_but_bool_nonfinite_and_bad_indices_are_not(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            for field, value in (('elevation', True), ('elevation', float('nan')), ('caves', float('inf')),
                                 ('caves', -1), ('fertility', '0'), ('top_indices', True),
                                 ('permanent_indices', 0), ('surface_indices', 20)):
                with self.subTest(field=field, value=value):
                    raw = copy.deepcopy(data)
                    raw[field][0] = value
                    with self.assertRaises(ValueError):
                        validate_snapshot(raw)

    def test_unique_edifice_footprints_must_cover_actual_grid_without_overlap(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            mutations = [lambda r: r['edifice_indices'].__setitem__(2, 0),
                         lambda r: r['edifices'][1].update(footprint=[2, 3]),
                         lambda r: r['edifices'][0].update(footprint=[1, 1, 2]),
                         lambda r: r['edifices'][0].update(x=0),
                         lambda r: r['edifices'][0].update(rotation=4),
                         lambda r: r['edifices'][1].update(id=1)]
            for mutation in mutations:
                raw = copy.deepcopy(data)
                mutation(raw)
                with self.assertRaises(ValueError):
                    validate_snapshot(raw)

    def test_no_hit_points_actual_minus_one_is_valid_only_when_not_using_hit_points(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            data['edifices'][0].update(uses_hit_points=False, hit_points=-1)
            validate_snapshot(data)
            data['edifices'][0]['uses_hit_points'] = True
            with self.assertRaises(ValueError):
                validate_snapshot(data)

    def test_unsafe_original_natural_roof_and_missing_support_are_fail_closed(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            for raw in ({**data, 'native_roof_supported': '000000'},
                        {key: value for key, value in data.items() if key != 'native_roof_supported'}):
                with self.assertRaises(ValueError):
                    validate_snapshot(raw)

    def test_custom_loaded_natural_roof_uses_actual_metadata_and_unknown_metadata_fails_closed(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            data['roof_table'][1] = 'CustomNaturalRoof'
            data['roof_def_metadata']['CustomNaturalRoof'] = data['roof_def_metadata'].pop('RoofRockThin')
            validate_snapshot(data)
            data['native_roof_supported'] = '000000'
            with self.assertRaises(ValueError):
                validate_snapshot(data)
            data['native_roof_supported'] = '010000'
            data['roof_def_metadata'].pop('CustomNaturalRoof')
            with self.assertRaises(ValueError):
                validate_snapshot(data)

    def test_missing_actual_edifice_flags_and_open_door_state_cannot_be_guessed_closed(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            for name in ('natural_rock', 'resource_rock', 'door_open'):
                raw = copy.deepcopy(data)
                raw['edifices'][0].pop(name)
                with self.assertRaises(ValueError):
                    validate_snapshot(raw)
            data['edifices'][0]['door_open'] = True
            with self.assertRaises(ValueError):
                validate_snapshot(data)

    def test_noncollapsing_loaded_natural_roof_does_not_require_a_holder(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            data['roof_table'][1] = 'CustomNonCollapsingRoof'
            data['roof_def_metadata'].pop('RoofRockThin')
            data['roof_def_metadata']['CustomNonCollapsingRoof'] = {
                'is_natural': True, 'can_collapse': False, 'is_thick_roof': True}
            data['native_roof_supported'] = '000000'
            validate_snapshot(data)

    def test_unknown_roof_metadata_bool_and_missing_game_assembly_hash_fail_closed(self):
        with tempfile.TemporaryDirectory() as root:
            _, data = capture(pathlib.Path(root))
            raw = copy.deepcopy(data)
            raw['roof_def_metadata']['RoofRockThin']['can_collapse'] = 1
            with self.assertRaises(ValueError):
                validate_snapshot(raw)
            raw = copy.deepcopy(data)
            raw['context'].pop('engine_assembly_sha256')
            with self.assertRaises(ValueError):
                validate_snapshot(raw)

    def test_virtual_source_roof_support_does_not_admit_unanchored_projected_roof(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            data['context']['gl_stable_cave_roof_override'] = True
            data['projected_roof_supported'] = '000000'
            write(path, data)
            before = path.read_bytes()
            with self.assertRaises(ValueError):
                observed_replica(path)
            self.assertEqual(path.read_bytes(), before)

    def test_each_bound_raw_file_and_final_png_has_an_independent_hash_binding(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            path, data = capture(folder)
            for file in data['binding_files'].values():
                with self.subTest(file=file):
                    target = folder / file
                    original = target.read_bytes()
                    target.write_bytes(original + b' ')
                    with self.assertRaises(ValueError):
                        observed_replica(path)
                    target.write_bytes(original)

    def test_unknown_early_raw_observation_rejects_even_with_complete_final_replica(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            path, data = capture(folder)
            geometry_path = folder / data['binding_files']['geology']
            geometry = json.loads(geometry_path.read_text(encoding='utf-8'))
            geometry['known_mask'] = '111110'
            write(geometry_path, geometry)
            data['source_bindings']['geology_sha256'] = digest(geometry_path)
            write(path, data)
            with self.assertRaises(ValueError):
                observed_replica(path)


class ReplicaSelectionTests(unittest.TestCase):
    def prepare(self, path, data, recipe=None, **changes):
        folder = path.parent
        options = {'target_biome': 'TemperateForest', 'width': 3, 'height': 2,
                   'fresh_unedited': True, 'snapshot_file': '../source/' + path.name}
        options.update(changes)
        return prepare_replica(command(data) if recipe is None else recipe, path,
            folder / data['binding_files']['terrain'], folder / data['binding_files']['geology'],
            folder / data['binding_files']['map_png'], folder / data['binding_files']['map_default_png'], **options)

    def test_same_biome_same_size_fresh_explicit_recipe_adds_only_file_metadata(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            original = command(data)
            actual, receipt = self.prepare(path, data, original)
            self.assertTrue(receipt['replica_selected'])
            self.assertEqual(actual['replica_layer'], {'schema_version': 1, 'mode': 'source-replica',
                'source_biome': 'TemperateForest', 'width': 3, 'height': 2,
                'snapshot_file': '../source/source-replica.json', 'sha256': digest(path)})
            self.assertEqual({key: value for key, value in actual.items() if key != 'replica_layer'}, original)
            self.assertNotIn('replica_layer', original)

    def test_other_biome_other_size_and_edited_candidate_preserve_four_sidecar_bytes(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            original = command(data)
            before = json.dumps(original, separators=(',', ':')).encode('utf-8')
            path.unlink()  # Off-condition adaptation must not need a new capture.
            for file in data['binding_files'].values():
                (path.parent / file).unlink()
            for options in ({'target_biome': 'AridShrubland'}, {'width': 4}, {'height': 3},
                            {'fresh_unedited': False}, {'fresh_unedited': 1}):
                with self.subTest(options=options):
                    actual, receipt = self.prepare(path, data, original, **options)
                    self.assertFalse(receipt['replica_selected'])
                    self.assertEqual(json.dumps(actual, separators=(',', ':')).encode('utf-8'), before)

    def test_valid_preexisting_replica_on_edited_candidate_fails_without_mutating_command(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            path, data = capture(root / 'source')
            active, receipt = self.prepare(path, data)
            self.assertTrue(receipt['replica_selected'])
            command_path = root / 'recipes' / 'command.json'
            command_path.parent.mkdir()
            write(command_path, active)
            _, fields, _ = load_replica_layer(active['replica_layer'], command_path, root)
            self.assertEqual(fields['width'], 3)
            self.assertEqual(fields['height'], 2)
            before = json.dumps(active, separators=(',', ':')).encode('utf-8')
            for fresh in (False, None, 1):
                with self.subTest(fresh_unedited=fresh):
                    with self.assertRaises(ValueError):
                        self.prepare(path, data, active, fresh_unedited=fresh)
                    self.assertEqual(json.dumps(active, separators=(',', ':')).encode('utf-8'), before)
                    self.assertEqual(json.loads(command_path.read_text(encoding='utf-8')), active)

    def test_edited_legacy_without_replica_preserves_content_without_opening_source(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            original = command(data)
            before = json.dumps(original, separators=(',', ':')).encode('utf-8')
            path.unlink()
            for file in data['binding_files'].values():
                (path.parent / file).unlink()
            actual, receipt = self.prepare(path, data, original, fresh_unedited=False)
            self.assertFalse(receipt['replica_selected'])
            self.assertEqual(json.dumps(actual, separators=(',', ':')).encode('utf-8'), before)
            self.assertNotIn('replica_layer', actual)
            self.assertEqual(original, actual)

    def test_fresh_cross_biome_or_size_keeps_inactive_existing_replica_metadata(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            active, receipt = self.prepare(path, data)
            self.assertTrue(receipt['replica_selected'])
            before = json.dumps(active, separators=(',', ':')).encode('utf-8')
            path.unlink()
            for file in data['binding_files'].values():
                (path.parent / file).unlink()
            for options in ({'target_biome': 'AridShrubland'}, {'width': 4}, {'height': 3}):
                with self.subTest(options=options):
                    actual, receipt = self.prepare(path, data, active, **options)
                    self.assertFalse(receipt['replica_selected'])
                    self.assertEqual(json.dumps(actual, separators=(',', ':')).encode('utf-8'), before)

    def test_six_core_or_polygon_commands_and_image_recipe_keep_original_content(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            path.unlink()
            (path.parent / data['binding_files']['terrain']).unlink()
            for name in ('lake', 'valley', 'mountain', 'cliff', 'archipelago', 'oasis'):
                original = {'action': 'generate', 'params': {'shape_ops': [{'id': name, 'op': 'add'}]}}
                actual, receipt = self.prepare(path, data, original)
                self.assertFalse(receipt['replica_selected'])
                self.assertEqual(actual, original)
            image = command(data)
            image.pop('rock_layer')
            image.pop('cave_layer')
            self.assertEqual(self.prepare(path, data, image)[0], image)

    def test_selected_missing_capture_hash_disagreement_and_unknown_fail_closed(self):
        with tempfile.TemporaryDirectory() as root:
            path, data = capture(pathlib.Path(root))
            wrong = command(data)
            wrong['cave_layer']['source_geology_sha256'] = '0' * 64
            with self.assertRaises(ValueError):
                self.prepare(path, data, wrong)
            raw = copy.deepcopy(data)
            raw['known_mask'] = '011111'
            write(path, raw)
            with self.assertRaises(ValueError):
                self.prepare(path, data)
            path.unlink()
            with self.assertRaises(FileNotFoundError):
                self.prepare(path, data)

    def test_relative_snapshot_can_reach_source_sibling_but_cannot_escape_artifact_root(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            recipe = root / 'recipes' / 'command.json'
            self.assertEqual(resolve_snapshot_file({'snapshot_file': '../source/file.json'}, recipe, root),
                             root / 'source' / 'file.json')
            for path in ('../../outside.json', 'C:/outside.json', '//server/share/file.json', '/outside.json'):
                with self.subTest(path=path):
                    with self.assertRaises(ValueError):
                        resolve_snapshot_file({'snapshot_file': path}, recipe, root)

    def test_metadata_loader_checks_snapshot_hash_dimensions_and_refuses_inline_raw_fields(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            path, _ = capture(root / 'source')
            layer, _ = observed_replica(path, snapshot_file='../source/source-replica.json')
            recipe = root / 'recipes' / 'command.json'
            _, fields, _ = load_replica_layer(layer, recipe, root)
            self.assertEqual(fields['width'], 3)
            for patch in ({'sha256': '0' * 64}, {'width': True}, {'height': 3},
                          {'source_biome': 'Desert'}, {'elevation': [0] * 6}, {'mode': 'source-geology'}):
                with self.subTest(patch=patch):
                    with self.assertRaises(ValueError):
                        load_replica_layer({**layer, **patch}, recipe, root)


class ReplicaReportTests(unittest.TestCase):
    def case(self, mutation=None):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            source, _ = capture(root / 'source', 'original')
            target, _ = capture(root / 'target', 'candidate', mutation)
            return evaluate(source, target)

    def test_exact_final_capture_has_one_hundred_percent_full_cell_and_both_png_agreement(self):
        result = self.case()
        self.assertTrue(result['fidelity_pass'], result['reasons'])
        for name in ('top_indices', 'permanent_indices', 'surface_indices', 'under_indices',
                     'foundation_indices', 'temp_indices', 'color_indices', 'roof_indices',
                     'edifice_indices', 'elevation', 'caves', 'fertility', 'walkable'):
            self.assertEqual(result['metrics'][name]['compared_cells'], 6)
            self.assertEqual(result['metrics'][name]['mismatch_cells'], 0)
            self.assertEqual(result['metrics'][name]['agreement'], 1)
        for name in ('map_png', 'map_default_png'):
            self.assertEqual(result['metrics'][name]['compared_pixels'], 6)
            self.assertEqual(result['metrics'][name]['agreement'], 1)

    def test_one_cell_static_changes_reject_even_with_identical_pngs_and_no_tolerance(self):
        changes = [('top_indices', 2), ('permanent_indices', 2), ('surface_indices', 2),
                   ('under_indices', 1), ('foundation_indices', 1), ('temp_indices', 4),
                   ('color_indices', 1), ('elevation', .81234577), ('caves', .0000001), ('fertility', -2.9999999)]
        for field, value in changes:
            with self.subTest(field=field):
                result = self.case(lambda raw: raw[field].__setitem__(0, value))
                self.assertTrue(result['comparison_possible'], result['reasons'])
                self.assertEqual(result['metrics'][field]['mismatch_cells'], 1)
                self.assertEqual(result['metrics'][field]['compared_cells'], 6)
                self.assertEqual(result['metrics']['map_png']['mismatch_pixels'], 0)
                self.assertFalse(result['fidelity_pass'])

    def test_roof_and_walkability_wrong_with_identical_pngs_still_reject(self):
        for patch, field in (({'roof_indices': [0, 2, 0, 0, 0, 0]}, 'roof_indices'),
                             ({'walkable': '000011'}, 'walkable')):
            result = self.case(lambda raw: raw.update(patch))
            self.assertEqual(result['metrics'][field]['mismatch_cells'], 1)
            self.assertEqual(result['metrics']['map_default_png']['mismatch_pixels'], 0)
            self.assertFalse(result['fidelity_pass'])

    def test_edifice_stuff_rotation_and_hitpoints_are_checked_beyond_per_cell_def_mask(self):
        for patch in ({'stuff': 'Limestone'}, {'rotation': 2}, {'hit_points': 199}):
            result = self.case(lambda raw: raw['edifices'][0].update(patch))
            self.assertEqual(result['metrics']['edifice_indices']['mismatch_cells'], 0)
            self.assertFalse(result['metrics']['edifices']['equal'])
            self.assertFalse(result['fidelity_pass'])

    def test_edifice_record_ids_are_not_native_thing_identity(self):
        def reordered(raw):
            raw['edifices'].reverse()
            raw['edifices'][0]['id'], raw['edifices'][1]['id'] = 1, 2
            raw['edifice_indices'] = [0, 2, 2, 1, 0, 0]
        result = self.case(reordered)
        self.assertTrue(result['metrics']['edifices']['equal'])
        self.assertTrue(result['fidelity_pass'], result['reasons'])

    def test_each_actual_png_mode_one_pixel_change_rejects_independently(self):
        for name in ('map_png', 'map_default_png'):
            with self.subTest(mode=name), tempfile.TemporaryDirectory() as root:
                root = pathlib.Path(root)
                source, _ = capture(root / 'source', 'original')
                target, raw = capture(root / 'target', 'candidate')
                png = target.parent / raw['binding_files'][name]
                with Image.open(png) as image:
                    pixels = np.asarray(image.convert('RGBA')).copy()
                pixels[1, 2, 0] += 1
                Image.fromarray(pixels).save(png)
                raw['source_bindings'][name + '_sha256'] = digest(png)
                write(target, raw)
                result = evaluate(source, target)
                self.assertEqual(result['metrics'][name]['mismatch_pixels'], 1)
                self.assertEqual(result['metrics'][name]['agreement'], 5 / 6)
                self.assertFalse(result['fidelity_pass'])

    def test_missing_or_invalid_capture_is_a_failed_report_not_false_exact_admission(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            source, _ = capture(root / 'source', 'original')
            result = evaluate(source, root / 'missing-replica.json')
            self.assertFalse(result['comparison_possible'])
            self.assertFalse(result['fidelity_pass'])
            self.assertNotIn('pass', result)  # Native selection audit is separate.

    def test_original_99999_and_final_pngs_are_compared_separately_in_both_modes(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            path, data = capture(root)
            early_true, early_default = root / 'source-map.png', root / 'source-map-default.png'
            early_true.write_bytes((root / data['binding_files']['map_png']).read_bytes())
            with Image.open(root / data['binding_files']['map_default_png']) as image:
                pixels = np.asarray(image.convert('RGBA')).copy()
            pixels[0, 0, 2] += 1
            Image.fromarray(pixels).save(early_default)
            result = original_phase_pngs(path)
            self.assertTrue(result['map_png']['pixel_equal'])
            self.assertTrue(result['map_png']['byte_equal'])
            self.assertEqual(result['map_default_png']['compared_pixels'], 6)
            self.assertEqual(result['map_default_png']['mismatch_pixels'], 1)
            self.assertEqual(result['map_default_png']['agreement'], 5 / 6)
            self.assertFalse(result['map_default_png']['pixel_equal'])


class NativeReplicaAdmissionTests(unittest.TestCase):
    def case(self, patch=None, missing=False, audit_mutation=None, run_ok=True):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            source, _ = capture(root / 'source', 'original')
            target, raw = capture(root / 'target', 'candidate')
            audit_path = target.parent / 'candidate-replica-native-audit.json'
            if not missing:
                audit = native_audit(audit_path, source, raw, patch)
                if audit_mutation:
                    audit_mutation(audit)
                    write(audit_path, audit)
            return evaluate(source, target, run_ok=run_ok)

    def test_matching_full_fields_and_actual_native_selection_audit_admit_exact_static_replica(self):
        row = self.case()
        self.assertTrue(row['fidelity_pass'], row['reasons'])
        self.assertTrue(row['native_audit']['structure_pass'])
        self.assertTrue(row['native_audit']['pass'])
        self.assertTrue(row['admission_pass'])

    def test_missing_final_native_audit_does_not_admit_matching_static_fields(self):
        row = self.case(missing=True)
        self.assertTrue(row['fidelity_pass'])
        self.assertFalse(row['native_audit']['present'])
        self.assertFalse(row['admission_pass'])

    def test_existing_map_or_any_protected_unknown_user_or_unsafe_change_rejects(self):
        for name in ('existing_things_before', 'existing_roofs_before', 'existing_terrain_cells_before',
                     'protected_changes', 'unknown_changes', 'user_state_changes', 'unsafe_roof_cells'):
            with self.subTest(name=name):
                row = self.case({name: 1})
                self.assertTrue(row['fidelity_pass'])
                self.assertTrue(row['native_audit']['structure_pass'])
                self.assertFalse(row['admission_pass'])

    def test_malformed_or_wrong_source_audit_cannot_admit_exact_looking_geometry(self):
        for patch in ({'source_snapshot_sha256': '0' * 64}, {'existing_things_before': False},
                      {'unsafe_roof_cells': -1}, {'applied': 1}, {'actual_map_png_sha256': '0' * 64},
                      {'field_mismatches': {'elevation': 0}}):
            with self.subTest(patch=patch):
                row = self.case(patch)
                self.assertTrue(row['fidelity_pass'])
                self.assertFalse(row['native_audit']['structure_pass'])
                self.assertFalse(row['admission_pass'])

    def test_claimed_pass_with_one_final_field_mismatch_and_missing_counts_rejects(self):
        row = self.case(audit_mutation=lambda audit: audit['field_mismatches'].__setitem__('elevation', 1))
        self.assertTrue(row['fidelity_pass'])
        self.assertFalse(row['admission_pass'])
        row = self.case(audit_mutation=lambda audit: audit.pop('unsafe_roof_cells'))
        self.assertFalse(row['native_audit']['structure_pass'])
        self.assertFalse(row['admission_pass'])

    def test_expected_rejection_guard_proof_is_not_an_exact_fidelity_admission(self):
        row = self.case({'applied': False, 'pass': False, 'expected_rejection': True,
                         'expected_rejection_pass': True})
        self.assertTrue(row['fidelity_pass'])
        self.assertFalse(row['admission_pass'])

    def test_incomplete_or_unverified_whole_run_does_not_admit_individually_exact_candidate(self):
        for value in (False, None, 1, 'true'):
            with self.subTest(run_ok=value):
                row = self.case(run_ok=value)
                self.assertTrue(row['fidelity_pass'])
                self.assertTrue(row['native_audit']['pass'])
                self.assertFalse(row['run_ok'])
                self.assertFalse(row['admission_pass'])


if __name__ == '__main__':
    unittest.main()
