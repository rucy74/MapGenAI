"""Observed cave geology regressions using raw files and public entry points."""
import contextlib
import copy
import hashlib
import io
import json
import pathlib
import tempfile
import unittest

import numpy as np

from cave import decode, observed_cave, resample
from finalize_catalog import finalize


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def capture(folder, ident, cells, elevation=None, caves=None, roofs=None,
            walkable=None, known=None, constructed=None, edifice=None, biome='TemperateForest'):
    """Encode hand-set actual fields; do not derive expected results here."""
    cells = np.asarray(cells)
    height, width = cells.shape
    shape = cells.shape
    if elevation is None:
        elevation = np.full(shape, .8)
    if caves is None:
        caves = np.zeros(shape)
    if roofs is None:
        roofs = np.full(shape, 'None', dtype=object)
    if walkable is None:
        walkable = cells != 'M'
    if known is None:
        known = np.ones(shape, dtype=bool)
    if constructed is None:
        constructed = np.zeros(shape, dtype=bool)
    if edifice is None:
        edifice = np.zeros(shape, dtype=bool)
    table = ['None', 'RoofRockThin', 'RoofRockThick', 'RoofConstructed']
    terrain = {'schema_version': 2, 'row_order': 'south-first', 'width': width,
               'height': height, 'biome': biome, 'cells': ''.join(cells.ravel()),
               'terrain_table': ['Soil'], 'terrain_indices': [0] * cells.size,
               'surface_indices': [0] * cells.size, 'terrain_defs': {'Soil': int(cells.size)}}
    terrain_path = folder / (ident + '-terrain.json')
    geology_path = folder / (ident + '-geology.json')
    write_json(terrain_path, terrain)
    flags = lambda values: ''.join('1' if value else '0' for value in np.asarray(values).ravel())
    geology = {'schema_version': 1, 'row_order': 'south-first', 'width': width,
               'height': height, 'known_mask': flags(known),
               'constructed_floor': flags(constructed), 'nonrock_edifice': flags(edifice),
               'elevation': np.asarray(elevation).ravel().tolist(),
               'caves': np.asarray(caves).ravel().tolist(), 'roof_table': table,
               'roof_indices': [table.index(name) for name in np.asarray(roofs).ravel()],
               'walkable': flags(walkable),
               'source_terrain_sha256': hashlib.sha256(terrain_path.read_bytes()).hexdigest()}
    write_json(geology_path, geology)
    return terrain_path, geology_path, terrain, geology


class CaveCaptureTests(unittest.TestCase):
    def simple_capture(self, folder):
        return capture(folder, 'source', np.array([list('GMG'), list('GGG')]),
                       elevation=np.array([[0, .81234567, .2], [.3, .4, .5]]),
                       caves=np.array([[0, 2.9876543, 0], [0, 0, 0]]),
                       roofs=np.array([['None', 'RoofRockThin', 'None'],
                                       ['None', 'None', 'RoofRockThick']], dtype=object))

    def test_non_square_south_first_values_keep_known_zero_and_full_floats(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, _ = self.simple_capture(pathlib.Path(root))
            layer, receipt = observed_cave(terrain, geology)
            fields = decode(layer)
            self.assertEqual((layer['width'], layer['height']), (3, 2))
            self.assertEqual(layer['known'], 'KKKKKK')
            self.assertTrue(fields['known'][0, 0])
            self.assertEqual(fields['elevation'].tolist(), [[0, .81234567, .2], [.3, .4, .5]])
            self.assertEqual(fields['caves'].tolist(), [[0, 2.9876543, 0], [0, 0, 0]])
            self.assertEqual(fields['roof'].tolist(), [[0, 1, 0], [0, 0, 2]])
            self.assertEqual(layer['source_terrain_sha256'], hashlib.sha256(terrain.read_bytes()).hexdigest())
            self.assertEqual(layer['source_geology_sha256'], hashlib.sha256(geology.read_bytes()).hexdigest())
            self.assertEqual(receipt['known_geology_cells'], 6)

    def test_actual_unknown_floor_edifice_and_roof_are_distinct_from_impassable_nature(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, _ = capture(
                pathlib.Path(root), 'source', np.array([list('GNMGGGGW')]),
                elevation=np.array([[0, .5, .81234567, .8, .8, .8, .2, .1]]),
                caves=np.array([[0, 0, 2.9876543, 0, 0, 0, 0, 0]]),
                roofs=np.array([['None', 'None', 'RoofRockThin', 'None', 'None',
                                 'RoofConstructed', 'None', 'None']], dtype=object),
                walkable=np.array([[True, True, False, True, False, True, False, False]]),
                constructed=np.array([[False, False, False, True, False, False, False, False]]),
                edifice=np.array([[False, False, False, False, True, False, False, False]]))
            layer, receipt = observed_cave(terrain, geology)
            self.assertEqual(layer['known'], 'KNKNNNKK')
            self.assertEqual(decode(layer)['known'].tolist(),
                             [[True, False, True, False, False, False, True, True]])
            self.assertEqual(receipt['known_geology_cells'], 4)
            self.assertEqual(receipt['unknown_geology_cells'], 4)
            self.assertEqual(receipt['unsupported_source_roof_cells'], 1)

    def test_raw_known_mask_preserves_unobserved_cells_but_absence_means_all_observed(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, data = capture(pathlib.Path(root), 'source', np.array([list('GGG')]),
                                                known=np.array([[False, True, False]]))
            self.assertEqual(observed_cave(terrain, geology)[0]['known'], 'NKN')
            data.pop('known_mask')
            write_json(geology, data)
            self.assertEqual(observed_cave(terrain, geology)[0]['known'], 'KKK')

    def test_missing_actual_geology_file_fails_closed(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, _, _, _ = self.simple_capture(pathlib.Path(root))
            with self.assertRaises((FileNotFoundError, ValueError)):
                observed_cave(terrain, pathlib.Path(root) / 'missing-geology.json')

    def test_every_required_raw_field_is_required_including_actual_floor_and_edifice_masks(self):
        required = ('elevation', 'caves', 'roof_table', 'roof_indices', 'walkable',
                    'source_terrain_sha256', 'constructed_floor', 'nonrock_edifice')
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, base = self.simple_capture(pathlib.Path(root))
            for field in required:
                with self.subTest(field=field):
                    data = copy.deepcopy(base)
                    data.pop(field)
                    write_json(geology, data)
                    with self.assertRaises(ValueError):
                        observed_cave(terrain, geology)

    def test_geology_schema_orientation_dimensions_and_actual_mask_lengths_are_validated(self):
        cases = ({'schema_version': 2}, {'row_order': 'north-first'}, {'width': 2},
                 {'height': 0}, {'width': True}, {'known_mask': '11111'},
                 {'known_mask': '11111X'}, {'walkable': '11111X'},
                 {'constructed_floor': '00000'}, {'nonrock_edifice': '00000X'})
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, base = self.simple_capture(pathlib.Path(root))
            for patch in cases:
                with self.subTest(patch=patch):
                    write_json(geology, {**base, **patch})
                    with self.assertRaises(ValueError):
                        observed_cave(terrain, geology)

    def test_full_numeric_grids_reject_missing_cells_booleans_nonfinite_and_negative_caves(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, base = self.simple_capture(pathlib.Path(root))
            for field in ('elevation', 'caves'):
                for replacement in ([0] * 5, [0] * 5 + [True], [0] * 5 + ['.8'],
                                    [0] * 5 + [float('nan')], [0] * 5 + [float('inf')],
                                    [0] * 5 + [1e40]):
                    with self.subTest(field=field, replacement=replacement[-1]):
                        write_json(geology, {**base, field: replacement})
                        with self.assertRaises(ValueError):
                            observed_cave(terrain, geology)
            write_json(geology, {**base, 'caves': [0, 0, 0, 0, 0, -.1]})
            with self.assertRaises(ValueError):
                observed_cave(terrain, geology)

    def test_roof_table_and_indices_do_not_guess_missing_or_invalid_roofs(self):
        cases = ({'roof_table': ['RoofRockThin', 'None']}, {'roof_table': ['None', 'None']},
                 {'roof_indices': [0, 0, 0, 0, 0, 4]}, {'roof_indices': [0, 0, 0, 0, 0, -1]},
                 {'roof_indices': [0, 0, 0, 0, 0, 1.0]}, {'roof_indices': [0] * 5})
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, base = self.simple_capture(pathlib.Path(root))
            for patch in cases:
                with self.subTest(patch=patch):
                    write_json(geology, {**base, **patch})
                    with self.assertRaises(ValueError):
                        observed_cave(terrain, geology)

    def test_actual_terrain_sha_binding_rejects_a_different_capture(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, raw, base = self.simple_capture(pathlib.Path(root))
            write_json(geology, {**base, 'source_terrain_sha256': '0' * 64})
            with self.assertRaises(ValueError):
                observed_cave(terrain, geology)
            write_json(geology, base)
            raw['biome'] = 'AridShrubland'
            write_json(terrain, raw)
            with self.assertRaises(ValueError):
                observed_cave(terrain, geology)

    def test_decoder_rejects_unsafe_mode_unknown_roof_code_and_invalid_grids(self):
        with tempfile.TemporaryDirectory() as root:
            terrain, geology, _, _ = self.simple_capture(pathlib.Path(root))
            base, _ = observed_cave(terrain, geology)
        cases = ({'mode': 'clear-roofs'}, {'schema_version': 2}, {'row_order': 'north-first'},
                 {'width': 0}, {'known': 'KKKKK'}, {'known': 'KKKKKX'},
                 {'roof_codes': [0, 0, 0, 0, 0, 3]}, {'roof_codes': [0, 0, 0, 0, 0, 1.0]},
                 {'elevation': [0, 0, 0, 0, 0, float('nan')]}, {'caves': [0, 0, 0, 0, 0, -1]})
        for patch in cases:
            with self.subTest(patch=patch), self.assertRaises(ValueError):
                decode({**base, **patch})


class CaveResampleTests(unittest.TestCase):
    def test_non_square_resampling_preserves_south_first_rows_and_numeric_values(self):
        fields = {'known': np.array([[True, False, True], [False, True, False]]),
                  'elevation': np.array([[10., 11., 12.], [20., 21., 22.]]),
                  'caves': np.array([[1.25, 2.5, 3.75], [4.5, 5.25, 6.]]),
                  'roof': np.array([[0, 1, 2], [2, 1, 0]])}
        actual = resample(fields, 6, 4)
        self.assertEqual(actual['elevation'].tolist(),
                         [[10, 10, 11, 11, 12, 12], [10, 10, 11, 11, 12, 12],
                          [20, 20, 21, 21, 22, 22], [20, 20, 21, 21, 22, 22]])
        self.assertEqual(actual['caves'][2].tolist(), [4.5, 4.5, 5.25, 5.25, 6, 6])
        self.assertEqual(actual['roof'][0].tolist(), [0, 0, 1, 1, 2, 2])
        self.assertEqual(actual['known'][3].tolist(), [False, False, True, True, False, False])

    def test_250_to_300_entrance_width_uses_coordinates_without_scaling_cave_intensity(self):
        caves = np.zeros((250, 250))
        caves[60:100, 80:84] = 3.25
        fields = {'known': np.ones((250, 250), dtype=bool), 'elevation': np.full((250, 250), .81234567),
                  'caves': caves, 'roof': np.zeros((250, 250), dtype=int)}
        actual = resample(fields, 300, 300)
        expected = np.zeros((300, 300))
        expected[72:120, 96:101] = 3.25
        np.testing.assert_array_equal(actual['caves'], expected)
        self.assertEqual(int((actual['caves'] > 0).sum()), 240)
        self.assertEqual(actual['elevation'][72, 96], .81234567)
        self.assertEqual(actual['caves'][72, 96:101].tolist(), [3.25] * 5)

    def test_adjacent_single_cells_have_different_widths_after_250_to_300_mapping(self):
        caves = np.zeros((250, 250))
        caves[100, 100] = 2
        caves[100, 101] = 3
        fields = {'known': np.ones((250, 250), dtype=bool), 'elevation': np.zeros((250, 250)),
                  'caves': caves, 'roof': np.zeros((250, 250), dtype=int)}
        actual = resample(fields, 300, 300)
        self.assertEqual(actual['caves'][120, 119:124].tolist(), [0, 2, 2, 3, 0])
        self.assertEqual(actual['caves'][121, 119:124].tolist(), [0, 2, 2, 3, 0])
        self.assertEqual(actual['caves'][122, 119:124].tolist(), [0, 0, 0, 0, 0])

    def test_invalid_target_dimensions_and_inconsistent_source_fields_fail_closed(self):
        fields = {'known': np.ones((2, 3), dtype=bool), 'elevation': np.zeros((2, 3)),
                  'caves': np.zeros((2, 3)), 'roof': np.zeros((2, 3), dtype=int)}
        for width, height in ((0, 3), (3, 0), (3.0, 2), (True, 2)):
            with self.subTest(width=width, height=height), self.assertRaises(ValueError):
                resample(fields, width, height)
        with self.assertRaises(ValueError):
            resample({**fields, 'caves': np.zeros((3, 2))}, 6, 4)


class CaveCatalogEvidenceTests(unittest.TestCase):
    def finalize_entry(self, records=None, required=True):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            entry = {'id': 'cave-scene'}
            if required:
                entry['requires_cave_sidecar'] = True
            write_json(folder / 'catalog.json', {'entries': [entry]})
            write_json(folder / 'transfer-evaluation.json', {'records': [
                {'id': 'cave-scene', 'run': 'native', 'biome': 'TemperateForest', 'size': 250,
                 'geometry_pass': True, 'reasons': []}]})
            if records is not None:
                write_json(folder / 'cave-evaluation.json', {'records': records})
            with contextlib.redirect_stdout(io.StringIO()):
                finalize(folder)
            return json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))['entries'][0]

    def assert_quarantined(self, entry):
        self.assertEqual(entry['status'], 'prototype-quarantined')
        self.assertEqual(entry['verified_profiles'], [])

    def test_missing_cave_evidence_quarantines_even_when_rock_geometry_passes(self):
        self.assert_quarantined(self.finalize_entry())

    def test_failed_cave_evidence_quarantines_even_when_rock_geometry_passes(self):
        self.assert_quarantined(self.finalize_entry([{'id': 'cave-scene', 'run': 'native', 'pass': False}]))

    def test_wrong_run_or_scene_cave_evidence_cannot_promote(self):
        for record in ({'id': 'cave-scene', 'run': 'other', 'pass': True},
                       {'id': 'other', 'run': 'native', 'pass': True}):
            with self.subTest(record=record):
                self.assert_quarantined(self.finalize_entry([record]))

    def test_passed_actual_cave_evidence_promotes_only_observed_profile(self):
        entry = self.finalize_entry([{'id': 'cave-scene', 'run': 'native', 'pass': True}])
        self.assertEqual(entry['status'], 'prototype-tested')
        self.assertEqual(entry['verified_profiles'],
                         [{'biome': 'TemperateForest', 'map_size': 250, 'hilliness': 'Flat'}])
        self.assertFalse(entry['verification']['aesthetic_approval'])

    def test_no_cave_sidecar_keeps_legacy_evidence_requirement(self):
        entry = self.finalize_entry(required=False)
        self.assertEqual(entry['status'], 'prototype-tested')
        self.assertEqual(entry['verified_profiles'],
                         [{'biome': 'TemperateForest', 'map_size': 250, 'hilliness': 'Flat'}])


class CaveFinalReportTests(unittest.TestCase):
    @staticmethod
    def corridor():
        cells = np.full((20, 20), 'G')
        cells[2:18, 2:18] = 'M'
        cave_values = np.zeros((20, 20))
        cave_values[10, 2:14] = 3.25
        cave_values[10:18, 13] = 3.25
        cells[cave_values > 0] = 'G'
        elevation = np.full((20, 20), .2)
        elevation[2:18, 2:18] = .8
        roofs = np.full((20, 20), 'None', dtype=object)
        roofs[cave_values > 0] = 'RoofRockThin'
        roofs[10, 2] = roofs[17, 13] = 'None'
        return {'cells': cells, 'elevation': elevation, 'caves': cave_values,
                'roofs': roofs, 'walkable': cells != 'M'}

    def evaluate_case(self, source_fields=None, target_changes=None, audit_changes=None,
                      audit_present=True, folder_entrypoint=False, audit_remove=()):
        from cave_report import evaluate, evaluate_fields
        source_fields = self.corridor() if source_fields is None else source_fields
        target_fields = copy.deepcopy(source_fields)
        if target_changes:
            target_fields.update(target_changes)
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source, run = folder / 'source', folder / 'native'
            source.mkdir()
            run.mkdir()
            _, _, source_terrain, source_geology = capture(source, 'original', **source_fields)
            _, _, target_terrain, target_geology = capture(run, 'cave-scene', **target_fields)
            width, height = target_terrain['width'], target_terrain['height']
            audit = {'schema_version': 1, 'stage': 99999, 'row_order': 'south-first',
                     'source_width': source_terrain['width'], 'source_height': source_terrain['height'],
                     'target_width': width, 'target_height': height,
                     'known_cells': width * height, 'unknown_cells': 0, 'protected_cells': 0,
                     'protected_conflicts': 0, 'protected_changes': 0, 'unknown_changes': 0,
                     'elevation_mismatches': 0, 'cave_value_mismatches': 0,
                     'cave_mask_mismatches': 0, 'roof_mismatches': 0, 'protection_kinds': {},
                     'grid_mismatches': 0, 'unsafe_roof_cells': 0, 'unprotected_mismatches': 0,
                     'mismatches': []}
            if audit_changes:
                audit.update(audit_changes)
            for name in audit_remove:
                audit.pop(name)
            if folder_entrypoint:
                write_json(folder / 'catalog.json', {'entries': [
                    {'id': 'cave-scene', 'requires_cave_sidecar': True,
                     'source': {'reference_run': 'source', 'reference_id': 'original'}}]})
                write_json(run / 'result.json', {'ok': True, 'results': [
                    {'id': 'cave-scene', 'biome': target_terrain['biome'], 'size': width,
                     'tile': 7, 'counts': {}}]})
                if audit_present:
                    write_json(run / 'cave-scene-cave-final-audit.json', audit)
                with contextlib.redirect_stdout(io.StringIO()):
                    report = evaluate(folder, [run], write=True)
                self.assertEqual(json.loads((folder / 'cave-evaluation.json').read_text(encoding='utf-8')), report)
                return report['records'][0]
            return evaluate_fields(source_terrain, source_geology, target_terrain, target_geology,
                                   audit if audit_present else None)

    def test_exact_observed_cave_roof_elevation_and_passage_are_a_positive_control(self):
        row = self.evaluate_case()
        self.assertTrue(row['pass'], row['reasons'])
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertEqual(row['connectivity']['split_components'], 0)
        self.assertEqual(row['connectivity']['entrances_lost'], 0)

    def test_same_cave_mask_but_changed_cave_width_value_fails(self):
        source = self.corridor()
        actual = source['caves'].copy()
        actual[10, 8] = 4.25
        row = self.evaluate_case(source, {'caves': actual}, {'cave_value_mismatches': 1})
        self.assertEqual(row['metrics']['cave_mask']['missing_cells'], 0)
        self.assertEqual(row['metrics']['cave_mask']['extra_cells'], 0)
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 1)
        self.assertEqual(row['metrics']['cave_value']['max_abs_error'], 1)
        self.assertFalse(row['pass'])

    def test_same_rock_mask_and_cave_mask_but_elevation_change_fails(self):
        source = self.corridor()
        actual = source['elevation'].copy()
        actual[10, 8] += .05
        row = self.evaluate_case(source, {'elevation': actual}, {'elevation_mismatches': 1})
        self.assertEqual(row['metrics']['cave_mask']['missing_cells'], 0)
        self.assertEqual(row['metrics']['cave_mask']['extra_cells'], 0)
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 1)
        self.assertAlmostEqual(row['metrics']['elevation']['max_abs_error'], .05)
        self.assertFalse(row['pass'])

    def test_float_error_below_one_e_minus_five_tolerance_does_not_false_fail(self):
        source = self.corridor()
        elevation, caves = source['elevation'].copy(), source['caves'].copy()
        elevation[10, 8] += .000005
        caves[10, 8] += .000005
        row = self.evaluate_case(source, {'elevation': elevation, 'caves': caves})
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 0)
        self.assertTrue(row['pass'], row['reasons'])

    def test_tiny_new_positive_cave_cell_fails_even_when_numeric_error_is_below_tolerance(self):
        source = self.corridor()
        actual = source['caves'].copy()
        actual[5, 5] = .000005
        row = self.evaluate_case(source, {'caves': actual}, {'cave_mask_mismatches': 1})
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['cave_mask']['extra_cells'], 1)
        self.assertFalse(row['pass'])

    def test_thin_to_thick_roof_change_fails_with_identical_rock_and_cave_masks(self):
        source = self.corridor()
        actual = source['roofs'].copy()
        actual[10, 8] = 'RoofRockThick'
        row = self.evaluate_case(source, {'roofs': actual}, {'roof_mismatches': 1})
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 1)
        self.assertEqual(row['metrics']['cave_mask']['missing_cells'], 0)
        self.assertEqual(row['metrics']['cave_mask']['extra_cells'], 0)
        self.assertFalse(row['pass'])

    def test_late_nonrock_wall_breaks_four_connected_passage_with_same_rock_and_cave_fields(self):
        source = self.corridor()
        actual_walkable = source['walkable'].copy()
        actual_walkable[10, 13] = False
        edifice = np.zeros((20, 20), dtype=bool)
        edifice[10, 13] = True
        row = self.evaluate_case(source, {'walkable': actual_walkable, 'edifice': edifice})
        # The remaining arms touch only diagonally at (12,10)/(13,11).
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['passage']['missing_cells'], 1)
        self.assertEqual(row['connectivity']['split_components'], 1)
        self.assertGreater(row['connectivity']['entrance_connection_failures'], 0)
        self.assertFalse(row['pass'])

    def test_blocked_original_entrance_fails_even_when_remaining_passage_stays_connected(self):
        source = self.corridor()
        actual_walkable = source['walkable'].copy()
        actual_walkable[10, 2] = False
        edifice = np.zeros((20, 20), dtype=bool)
        edifice[10, 2] = True
        row = self.evaluate_case(source, {'walkable': actual_walkable, 'edifice': edifice})
        self.assertEqual(row['connectivity']['split_components'], 0)
        self.assertGreater(row['connectivity']['entrances_lost'], 0)
        self.assertFalse(row['pass'])

    def test_unknown_source_cell_keeps_native_values_without_becoming_a_false_extra_cave(self):
        source = self.corridor()
        known = np.ones((20, 20), dtype=bool)
        known[0, 0] = False
        source['known'] = known
        elevation, caves, roofs = source['elevation'].copy(), source['caves'].copy(), source['roofs'].copy()
        elevation[0, 0], caves[0, 0], roofs[0, 0] = .6, 3.5, 'RoofRockThick'
        actual = {'known': np.ones((20, 20), dtype=bool), 'elevation': elevation, 'caves': caves, 'roofs': roofs}
        row = self.evaluate_case(source, actual, {'known_cells': 399, 'unknown_cells': 1})
        self.assertEqual(row['metrics']['cave_mask']['extra_cells'], 0)
        self.assertEqual(row['metrics']['elevation']['compared_cells'], 399)
        self.assertTrue(row['pass'], row['reasons'])
        changed = self.evaluate_case(source, actual, {'known_cells': 399, 'unknown_cells': 1, 'unknown_changes': 1})
        self.assertFalse(changed['pass'])

    def test_final_protection_conflict_rejects_otherwise_exact_geometry(self):
        row = self.evaluate_case(audit_changes={'protected_cells': 1, 'protected_conflicts': 1})
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertFalse(row['pass'])

    def test_missing_or_invalid_final_audit_fails_closed(self):
        self.assertFalse(self.evaluate_case(audit_present=False)['pass'])
        for patch in ({'stage': 405}, {'known_cells': True}, {'unknown_cells': 1},
                      {'roof_mismatches': -1}, {'protection_kinds': None}):
            with self.subTest(patch=patch):
                row = self.evaluate_case(audit_changes=patch)
                self.assertFalse(row['final_audit']['structure_pass'])
                self.assertFalse(row['pass'])

    def test_missing_unsafe_roof_count_rejects_identical_observed_geometry(self):
        row = self.evaluate_case(folder_entrypoint=True, audit_remove=('unsafe_roof_cells',))
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['cave_value']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertFalse(row['final_audit']['structure_pass'])
        self.assertFalse(row['pass'])

    def test_unsafe_roof_count_rejects_negative_bool_and_noninteger_values(self):
        for value in (-1, True, False, 0.0, '0', None):
            with self.subTest(value=value):
                row = self.evaluate_case(audit_changes={'unsafe_roof_cells': value})
                self.assertFalse(row['final_audit']['structure_pass'])
                self.assertFalse(row['pass'])

    def test_unsafe_roof_count_is_bounded_by_known_source_cells_not_map_area(self):
        source = self.corridor()
        source['known'] = np.ones((20, 20), dtype=bool)
        source['known'][0, 0] = False
        row = self.evaluate_case(source, audit_changes={
            'known_cells': 399, 'unknown_cells': 1, 'unsafe_roof_cells': 400})
        self.assertEqual(row['mapped_known_cells'], 399)
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertFalse(row['final_audit']['structure_pass'])
        self.assertFalse(row['pass'])

    def test_valid_nonzero_unsafe_roof_count_still_rejects_candidate(self):
        row = self.evaluate_case(audit_changes={'unsafe_roof_cells': 1})
        self.assertEqual(row['metrics']['roof']['mismatch_cells'], 0)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertFalse(row['final_audit']['pass'])
        self.assertFalse(row['pass'])

    def test_actual_folder_entrypoint_reads_observations_and_refuses_absent_final_audit(self):
        positive = self.evaluate_case(folder_entrypoint=True)
        self.assertTrue(positive['pass'], positive['reasons'])
        self.assertEqual(positive['run'], 'native')
        self.assertEqual(positive['id'], 'cave-scene')
        self.assertFalse(self.evaluate_case(folder_entrypoint=True, audit_present=False)['pass'])

    def test_no_sidecar_legacy_and_cross_biome_records_do_not_require_geology(self):
        from cave_report import evaluate
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            run = folder / 'native'
            run.mkdir()
            write_json(folder / 'catalog.json', {'entries': [
                {'id': 'legacy-same', 'requires_cave_sidecar': False},
                {'id': 'legacy-cross', 'requires_cave_sidecar': False}]})
            write_json(run / 'result.json', {'ok': True, 'results': [
                {'id': 'legacy-same', 'biome': 'TemperateForest', 'size': 250, 'tile': 5, 'counts': {}},
                {'id': 'legacy-cross', 'biome': 'AridShrubland', 'size': 300, 'tile': 6, 'counts': {}}]})
            with contextlib.redirect_stdout(io.StringIO()):
                report = evaluate(folder, [run], write=True)
            self.assertEqual(report['records'], [])
            self.assertEqual({row['id'] for row in report['legacy_controls']}, {'legacy-same', 'legacy-cross'})

    def test_actual_report_250_to_300_preserves_l_passage_and_full_cave_values(self):
        source_cells = np.full((250, 250), 'G')
        source_cells[50:110, 70:94] = 'M'
        source_caves = np.zeros((250, 250))
        source_caves[60:64, 70:84] = source_caves[60:110, 80:84] = 3.25
        source_cells[source_caves > 0] = 'G'
        source_elevation = np.full((250, 250), .2)
        source_elevation[50:110, 70:94] = .8
        source_roofs = np.full((250, 250), 'None', dtype=object)
        source_roofs[source_caves > 0] = 'RoofRockThin'
        source_roofs[60:64, 70] = source_roofs[109, 80:84] = 'None'
        target_cells = np.full((300, 300), 'G')
        target_cells[60:132, 84:113] = 'M'
        target_caves = np.zeros((300, 300))
        target_caves[72:77, 84:101] = target_caves[72:132, 96:101] = 3.25
        target_cells[target_caves > 0] = 'G'
        target_elevation = np.full((300, 300), .2)
        target_elevation[60:132, 84:113] = .8
        target_roofs = np.full((300, 300), 'None', dtype=object)
        target_roofs[target_caves > 0] = 'RoofRockThin'
        target_roofs[72:77, 84:86] = target_roofs[131, 96:101] = 'None'
        source = {'cells': source_cells, 'elevation': source_elevation, 'caves': source_caves,
                  'roofs': source_roofs, 'walkable': source_cells != 'M'}
        target = {'cells': target_cells, 'elevation': target_elevation, 'caves': target_caves,
                  'roofs': target_roofs, 'walkable': target_cells != 'M'}
        row = self.evaluate_case(source, target)
        self.assertEqual(row['metrics']['elevation']['compared_cells'], 90000)
        self.assertEqual(row['metrics']['elevation']['mismatch_cells'], 0)
        self.assertEqual(row['metrics']['cave_value']['max_abs_error'], 0)
        self.assertEqual(row['connectivity']['source_components'], 1)
        self.assertEqual(row['connectivity']['split_components'], 0)
        self.assertEqual(row['connectivity']['entrances_lost'], 0)
        self.assertTrue(row['pass'], row['reasons'])


class OriginalDenominatorRockReportTests(unittest.TestCase):
    def evaluate_rock(self, missing_original_rock=False, wrong_ground=False, wrong_audit=False):
        from rock_report import evaluate
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source, run = folder / 'fresh-source', folder / 'native'
            source.mkdir()
            run.mkdir()
            cells = np.full((4, 4), 'G')
            cells[1, 1] = cells[2, 2] = 'M'
            constructed = np.zeros((4, 4), dtype=bool)
            constructed[0, 0] = True
            roofs = np.full((4, 4), 'None', dtype=object)
            roofs[2, 2] = 'RoofConstructed'
            _, _, _, _ = capture(source, 'original', cells, constructed=constructed, roofs=roofs)
            actual_cells = cells.copy()
            if missing_original_rock:
                actual_cells[2, 2] = 'G'
            actual_path, _, actual_terrain, _ = capture(run, 'rock-scene', actual_cells)
            if wrong_ground:
                actual_terrain['terrain_table'] = ['Soil', 'Sand']
                actual_terrain['terrain_indices'][0] = actual_terrain['surface_indices'][0] = 1
                actual_terrain['terrain_defs'] = {'Soil': 15, 'Sand': 1}
                write_json(actual_path, actual_terrain)
            palette = {'terrains': [{'def': name, 'supported': True, 'water': False,
                                     'temporary': False, 'dangerous': False, 'fertility': 1}
                                    for name in ('Soil', 'Sand')]}
            write_json(source / 'native-terrain-palette.json', palette)
            write_json(folder / 'catalog.json', {'entries': [
                {'id': 'rock-scene', 'requires_rock_sidecar': True, 'requires_cave_sidecar': True,
                 'source': {'reference_id': 'original'}}]})
            write_json(run / 'result.json', {'ok': True, 'results': [
                {'id': 'rock-scene', 'biome': 'TemperateForest', 'size': 4, 'tile': 7, 'counts': {}}]})
            receipt = {'protected_changes': 0, 'unknown_changes': 0, 'known_source_mismatches': 0,
                       'protected_conflicts': 0, 'protection_kinds': {}, 'added_rock_cells': 0,
                       'removed_rock_cells': 0, 'removed_resource_cells': 0, 'stage': 404}
            write_json(run / 'rock-scene-rock-application.json', receipt)
            write_json(run / 'rock-scene-rock-grid-application.json', {
                **receipt, 'stage': 199, 'protected_elevation_changes': 0, 'protected_cave_changes': 0,
                'unknown_elevation_changes': 0, 'unknown_cave_changes': 0, 'known_grid_mismatches': 0})
            # Two source cells are physically nonportable: constructed G(0,0)
            # and the M(2,2) under a constructed roof. Only one known M remains.
            audit = {'schema_version': 1, 'stage': 99999, 'row_order': 'south-first',
                     'source_width': 4, 'source_height': 4, 'target_width': 4, 'target_height': 4,
                     'known_rock_cells': 1, 'known_nonrock_cells': 13, 'unknown_cells': 2,
                     'known_source_mismatches': 0, 'missing_source_rock_cells': 0,
                     'extra_rock_cells_on_known_nonrock': 0, 'protected_conflicts': 0,
                     'unprotected_mismatches': 0, 'protection_kinds': {}, 'mismatch_protection_kinds': {},
                     'mismatch_terrain_defs': {}, 'mismatch_building_defs': {},
                     'mismatch_other_thing_defs': {}, 'mismatches': []}
            if wrong_audit:
                audit.update(known_rock_cells=2, known_nonrock_cells=14, unknown_cells=0)
            write_json(run / 'rock-scene-rock-final-audit.json', audit)
            with contextlib.redirect_stdout(io.StringIO()):
                return evaluate(folder, [run], source=source)['records'][0]

    def test_physical_known_audit_counts_exclude_constructed_cells_without_shrinking_raw_truth(self):
        row = self.evaluate_rock()
        self.assertTrue(row['pass'], row['reasons'])
        self.assertEqual(row['final_audit']['known_rock_cells'], 1)
        self.assertEqual(row['final_audit']['known_nonrock_cells'], 13)
        self.assertEqual(row['metrics']['raw_rock']['expected_cells'], 2)
        self.assertEqual(row['ground']['mapped_source_ground_cells'], 14)

    def test_original_rock_omission_still_fails_when_that_cell_is_nonportable_geology(self):
        row = self.evaluate_rock(missing_original_rock=True)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertEqual(row['final_audit']['missing_source_rock_cells'], 0)
        self.assertEqual(row['metrics']['raw_rock']['expected_cells'], 2)
        self.assertEqual(row['metrics']['raw_rock']['missing_cells'], 1)
        self.assertEqual(row['metrics']['raw_rock']['iou'], .5)
        self.assertFalse(row['pass'])

    def test_original_ground_mismatch_still_fails_when_that_cell_is_constructed_geology(self):
        row = self.evaluate_rock(wrong_ground=True)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertEqual(row['ground']['mapped_source_ground_cells'], 14)
        self.assertEqual(row['ground']['named_matching_cells'], 13)
        self.assertAlmostEqual(row['ground']['source_named_ground_agreement'], 13 / 14)
        self.assertFalse(row['pass'])

    def test_structurally_valid_audit_with_wrong_physical_source_counts_fails(self):
        row = self.evaluate_rock(wrong_audit=True)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertEqual(row['metrics']['raw_rock']['iou'], 1)
        self.assertEqual(row['ground']['source_named_ground_agreement'], 1)
        self.assertFalse(row['pass'])


class ObservedCaveCommandTests(unittest.TestCase):
    scenes = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff', 'gl-archipelago', 'gl-oasis',
              'gl-cave-entrance', 'gl-secluded-valley')

    @staticmethod
    def polygon_params(ident, mountains=True):
        # Literal previous authoring command for the hand-set rectangular cells.
        # Expected vertices are not re-exported with the implementation under test.
        prefix = ident.replace('-', '_')
        water = {'op': 'add', 'shape': {
            'id': prefix + '_s_0', 'type': 'composite', 'edge_roughness': 'none',
            'details': 'natural', 'water_profile': 'native',
            'shapes': [{'id': 'p0', 'prim': 'poly', 'verts': [
                [.91919, .92424], [.84343, .91919], [.84848, .84343], [.92424, .84848]]}],
            'compose': [{'op': 'add', 's': 'p0', 'e': .05, 'f': .006, 'fill': 'WaterShallow'}]}}
        mountain = {'op': 'add', 'shape': {
            'id': prefix + '_m_0', 'type': 'composite', 'edge_roughness': 'none', 'details': 'natural',
            'shapes': [{'id': 'p0', 'prim': 'poly', 'verts': [
                [.17172, .17677], [.01515, .17172], [.0202, .01515], [.17677, .0202]]}],
            'compose': [{'op': 'add', 's': 'p0', 'e': 1.05, 'f': .006}]}}
        return {'shape_ops': [mountain, water] if mountains else [water]}

    def build_fixture(self, folder, details=True, caves=False):
        from build_catalog import build
        from PIL import Image
        source = folder / 'source'
        source.mkdir(parents=True)
        cells = np.full((100, 100), 'G')
        cells[2:18, 2:18] = 'M'
        cells[84:92, 84:92] = 'S'
        indices = np.zeros((100, 100), dtype=int)
        indices[cells == 'S'] = 1
        indices[cells == 'M'] = 2
        elevation = np.full((100, 100), .81234567)
        cave_values = np.zeros((100, 100))
        cave_values[80, 80] = 3.25
        roofs = np.full((100, 100), 'None', dtype=object)
        roofs[80, 80] = 'RoofRockThin'
        colors = np.asarray([[100, 50, 20], [0, 0, 255], [70, 70, 70]], dtype=np.uint8)
        inventory, results = [], []
        for ident in self.scenes if caves else self.scenes[:6]:
            terrain_path, geology_path, terrain, geology = capture(
                source, ident, cells, elevation=elevation, caves=cave_values, roofs=roofs)
            terrain.update(terrain_table=['Soil', 'WaterShallow', 'Granite_Rough'],
                           terrain_indices=indices.ravel().tolist(), surface_indices=indices.ravel().tolist(),
                           fertile_cells='N' * 10000,
                           terrain_defs={'Soil': 9680, 'WaterShallow': 64, 'Granite_Rough': 256})
            write_json(terrain_path, terrain)
            geology['source_terrain_sha256'] = hashlib.sha256(terrain_path.read_bytes()).hexdigest()
            write_json(geology_path, geology)
            Image.fromarray(colors[indices][::-1]).save(source / (ident + '-map.png'))
            inventory.append({'id': ident, 'source': 'https://example.invalid/fixture',
                              'file': 'fixture.xml', 'sha256': 'fixture', 'revision': 'fixture'})
            results.append({'id': ident, 'gl_id': ident, 'size': 100, 'tile': 7,
                            'world_seed': 'fixture', 'biome': 'TemperateForest', 'mutators': []})
        write_json(source / 'result.json', {'ok': True, 'results': results})
        write_json(folder / 'gl-inventory.json', {'items': inventory})
        write_json(source / 'native-terrain-palette.json', {'terrains': [
            {'def': name, 'rgb': rgb, 'supported': True, 'water': water, 'temporary': False,
             'dangerous': False, 'has_preview_color': True, 'fertility': 1 if name == 'Soil' else 0}
            for name, rgb, water in (('Soil', [100, 50, 20], False),
                                     ('WaterShallow', [0, 0, 255], True),
                                     ('Granite_Rough', [80, 80, 80], False))],
            'overlays': [{'name': 'SolidStone', 'rgb': [70, 70, 70]}]})
        with contextlib.redirect_stdout(io.StringIO()):
            build(folder, source, details=details, caves=caves)
        catalog = json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))
        commands = {item['id']: json.loads((folder / item['command']).read_text(encoding='utf-8'))
                    for item in catalog['entries']}
        return catalog, commands

    def test_all_eight_observed_cave_commands_omit_duplicate_polygon_edits_and_keep_full_sidecars(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            catalog, commands = self.build_fixture(folder, caves=True)
            self.assertEqual(catalog['excluded'], [])
            self.assertEqual(len(catalog['entries']), 11)
            entries = {item['id']: item for item in catalog['entries']}
            for ident in self.scenes:
                with self.subTest(ident=ident):
                    command = commands[ident]
                    self.assertEqual(command['params'], {})
                    self.assertEqual(command['action'], 'generate')
                    for name in ('ground_layer', 'water_layer', 'rock_layer', 'cave_layer'):
                        self.assertIn(name, command)
                        self.assertEqual(command[name]['width'], 100)
                        self.assertEqual(command[name]['height'], 100)
                    self.assertTrue(entries[ident]['requires_cave_sidecar'])
                    fields = decode(command['cave_layer'])
                    self.assertEqual(int(fields['known'].sum()), 10000)
                    # Water source E is intentionally above the product water
                    # clamp: a remaining polygon authoring pass would flatten it.
                    self.assertEqual(fields['elevation'][88, 88], .81234567)
                    self.assertEqual(fields['caves'][80, 80], 3.25)
                    self.assertEqual(fields['roof'][80, 80], 1)
                    receipt = json.loads((folder / 'recipes' / (ident + '.receipt.json')).read_text(encoding='utf-8'))
                    expected_params = self.polygon_params(ident)
                    expected_hash = hashlib.sha256(json.dumps(
                        expected_params, sort_keys=True, separators=(',', ':')).encode('utf-8')).hexdigest()
                    self.assertEqual(receipt['diagnostic_polygon_params_sha256'], expected_hash)
                    self.assertIs(receipt['product_polygon_params_omitted'], True)
                    self.assertNotIn('polygon_conversion_omitted', receipt)

    def test_image_and_core_commands_keep_their_existing_params_when_caves_are_enabled(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            _, previous = self.build_fixture(root / 'details')
            catalog, captured = self.build_fixture(root / 'caves', caves=True)
            entries = {item['id']: item for item in catalog['entries']}
            for ident in ('image-lake', 'core-foothills', 'core-dry-clearing'):
                with self.subTest(ident=ident):
                    self.assertEqual(captured[ident]['params'], previous[ident]['params'])
                    self.assertTrue(captured[ident]['params']['shape_ops'])
                    self.assertNotIn('cave_layer', captured[ident])
                    self.assertFalse(entries[ident]['requires_cave_sidecar'])
            self.assertEqual(captured['image-lake']['params'], self.polygon_params('image-lake', mountains=False))
            self.assertEqual(captured['core-foothills']['params'], {'shape_ops': [{'op': 'add', 'shape': {
                'id': 'library_foothills', 'type': 'landform', 'landform': 'foothills', 'layout': 'organic',
                'details': 'natural', 'variant': '257', 'position': 'top', 'size': '0.62', 'direction': '0'}}]})
            self.assertEqual(captured['core-dry-clearing']['params'], {'shape_ops': [{'op': 'add', 'shape': {
                'id': 'library_clearing', 'type': 'composite', 'details': 'natural', 'edge_roughness': 'medium',
                'shapes': [{'id': 'plain', 'prim': 'ellipse', 'center': [.52, .48], 'w': .30, 'h': .26}],
                'compose': [{'op': 'add', 's': 'plain', 'e': .05, 'f': .008}]}}]})

    def test_details_only_keeps_all_six_legacy_mountain_and_water_polygon_commands(self):
        with tempfile.TemporaryDirectory() as root:
            root = pathlib.Path(root)
            _, previous = self.build_fixture(root / 'legacy', details=False)
            catalog, detailed = self.build_fixture(root / 'details', details=True)
            self.assertEqual(catalog['excluded'], [])
            for ident in self.scenes[:6]:
                with self.subTest(ident=ident):
                    self.assertEqual(detailed[ident]['params'], previous[ident]['params'])
                    self.assertEqual(detailed[ident]['params'], self.polygon_params(ident))
                    self.assertIn('rock_layer', detailed[ident])
                    self.assertNotIn('cave_layer', detailed[ident])
                    receipt = json.loads((root / 'details' / 'recipes' / (ident + '.receipt.json')).read_text(encoding='utf-8'))
                    self.assertNotIn('product_polygon_params_omitted', receipt)
                    self.assertNotIn('diagnostic_polygon_params_sha256', receipt)


class CavePolygonFallbackTests(unittest.TestCase):
    def test_explicit_cave_build_retains_full_sidecars_when_polygon_fragment_budget_is_exceeded(self):
        from build_catalog import build
        from contours import export
        from PIL import Image
        scenes = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff', 'gl-archipelago', 'gl-oasis',
                  'gl-cave-entrance', 'gl-secluded-valley')
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source = folder / 'source'
            source.mkdir()
            fragmented = np.full((100, 100), 'G')
            for z in (4, 20, 36, 52, 68):
                for x in (4, 20, 36, 52, 68):
                    fragmented[z:z + 8, x:x + 8] = 'M'
            fragmented[84:92, 84:92] = 'S'
            with self.assertRaisesRegex(ValueError, 'Too many fragments'):
                export(fragmented, 'independent_fixture')
            standard = np.full((100, 100), 'G')
            standard[2:18, 2:18] = 'M'
            standard[84:92, 84:92] = 'S'
            inventory, results = [], []
            for ident in scenes:
                cells = fragmented if ident == 'gl-valley' else standard
                cave_values = np.zeros((100, 100))
                cave_values[80, 80] = 3.25
                terrain_path, geology_path, terrain, geology = capture(source, ident, cells, caves=cave_values)
                indices = np.zeros((100, 100), dtype=int)
                indices[cells == 'S'] = 1
                indices[cells == 'M'] = 2
                terrain.update(terrain_table=['Soil', 'WaterShallow', 'Granite_Rough'],
                               terrain_indices=indices.ravel().tolist(), surface_indices=indices.ravel().tolist(),
                               fertile_cells='N' * 10000,
                               terrain_defs={'Soil': int((cells == 'G').sum()),
                                             'WaterShallow': 64, 'Granite_Rough': int((cells == 'M').sum())})
                write_json(terrain_path, terrain)
                geology['source_terrain_sha256'] = hashlib.sha256(terrain_path.read_bytes()).hexdigest()
                write_json(geology_path, geology)
                colors = np.asarray([[100, 50, 20], [0, 0, 255], [70, 70, 70]], dtype=np.uint8)
                Image.fromarray(colors[indices][::-1]).save(source / (ident + '-map.png'))
                inventory.append({'id': ident, 'source': 'https://example.invalid/fixture',
                                  'file': 'fixture.xml', 'sha256': 'fixture', 'revision': 'fixture'})
                results.append({'id': ident, 'gl_id': ident, 'size': 100, 'tile': 7,
                                'world_seed': 'fixture', 'biome': 'TemperateForest', 'mutators': []})
            write_json(source / 'result.json', {'ok': True, 'results': results})
            write_json(folder / 'gl-inventory.json', {'items': inventory})
            write_json(source / 'native-terrain-palette.json', {'terrains': [
                {'def': name, 'rgb': rgb, 'supported': True, 'water': water, 'temporary': False,
                 'dangerous': False, 'has_preview_color': True, 'fertility': 1 if name == 'Soil' else 0}
                for name, rgb, water in (('Soil', [100, 50, 20], False),
                                         ('WaterShallow', [0, 0, 255], True),
                                         ('Granite_Rough', [80, 80, 80], False))],
                'overlays': [{'name': 'SolidStone', 'rgb': [70, 70, 70]}]})
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=True, caves=True)
            catalog = json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))
            self.assertEqual(catalog['excluded'], [])
            entry = next(item for item in catalog['entries'] if item['id'] == 'gl-valley')
            self.assertTrue(entry['requires_cave_sidecar'])
            self.assertTrue(entry['requires_rock_sidecar'])
            command = json.loads((folder / entry['command']).read_text(encoding='utf-8'))
            self.assertEqual(command['params'], {})
            self.assertTrue(all(sidecar in command for sidecar in ('ground_layer', 'water_layer', 'rock_layer', 'cave_layer')))
            self.assertEqual(decode(command['cave_layer'])['caves'][80, 80], 3.25)
            self.assertEqual(command['cave_layer']['known'], 'K' * 10000)
            receipt = json.loads((folder / 'recipes/gl-valley.receipt.json').read_text(encoding='utf-8'))
            self.assertTrue(receipt['polygon_conversion_omitted'].startswith('Too many fragments'))
            self.assertIs(receipt['product_polygon_params_omitted'], True)
            self.assertEqual(receipt['diagnostic_polygon_params_sha256'], hashlib.sha256(b'{}').hexdigest())
            # Identical source cells retain the old exclusion when the explicit
            # cave mode is absent; its validator cannot silently enable replay.
            write_json(source / 'result.json', {'ok': True, 'results': results[:6]})
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=True, caves=False)
            legacy = json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))
            self.assertNotIn('gl-valley', [item['id'] for item in legacy['entries']])
            self.assertTrue(any(item['id'] == 'gl-valley' and item['reason'].startswith('Too many fragments')
                                for item in legacy['excluded']))


if __name__ == '__main__':
    unittest.main(verbosity=2)
