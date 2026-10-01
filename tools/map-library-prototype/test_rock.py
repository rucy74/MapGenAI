"""Rock/detail admission regressions through the actual Python entry points.

These fixtures make the old recall-only gate pass despite extra native rocks,
and keep one-cell details observable instead of deriving truth from conversion.
"""
import contextlib
import io
import json
import pathlib
import tempfile
import unittest

import numpy as np
from PIL import Image

from build_catalog import build
from finalize_catalog import finalize
from ground import observed_ground
from measure_transfer import measure
from rock import decode, observed_rock, rock_layer
from rock_report import evaluate


def write_json(path, value):
    path.write_text(json.dumps(value), encoding='utf-8')


def natural_terrain(name, rgb, water=False):
    return {'def': name, 'rgb': rgb, 'supported': True, 'water': water,
            'temporary': False, 'dangerous': False, 'has_preview_color': True,
            'fertility': 1 if name == 'Soil' else 0}


class RockEncodingTests(unittest.TestCase):
    def test_single_cell_fragment_and_non_square_coordinates_survive(self):
        layer, receipt = rock_layer(np.array([list('GGM'), list('GNS')]), 'TemperateForest')
        self.assertEqual((layer['width'], layer['height']), (3, 2))
        self.assertEqual(layer['row_order'], 'south-first')
        self.assertEqual(layer['runs'], [
            {'start': 0, 'length': 2, 'kind': 1},
            {'start': 2, 'length': 1, 'kind': 2},
            {'start': 3, 'length': 1, 'kind': 1},
            {'start': 5, 'length': 1, 'kind': 1},
        ])
        self.assertEqual(decode(layer).tolist(), [[1, 1, 2], [1, 0, 1]])
        self.assertEqual(receipt['rock_cells'], 1)
        self.assertEqual(receipt['omitted_small_rock_cells'], 0)

    def test_dry_hole_water_and_unknown_are_distinct_from_solid_rock(self):
        layer, receipt = rock_layer(
            np.array([list('MMMG'), list('MGMN'), list('SWGM')]), 'TemperateForest')
        self.assertEqual(decode(layer).tolist(), [[2, 2, 2, 1], [2, 1, 2, 0], [1, 1, 1, 2]])
        self.assertEqual(receipt['rock_cells'], 6)
        self.assertEqual(receipt['observed_nonrock_cells'], 5)
        self.assertEqual(receipt['unknown_rock_cells'], 1)
        self.assertFalse(any(run['start'] <= 7 < run['start'] + run['length'] for run in layer['runs']))

    def test_observed_capture_keeps_raw_small_rock_and_south_first_position(self):
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'source-terrain.json'
            write_json(path, {'schema_version': 2, 'row_order': 'south-first',
                             'width': 3, 'height': 2, 'biome': 'Desert', 'cells': 'MNGSWG'})
            layer, receipt = observed_rock(path)
            self.assertEqual(decode(layer).tolist(), [[2, 0, 1], [1, 1, 1]])
            self.assertEqual(layer['source_biome'], 'Desert')
            self.assertEqual(receipt['rock_cells'], 1)

    def test_observed_capture_rejects_histogram_wrong_orientation_and_bad_dimensions(self):
        base = {'schema_version': 2, 'row_order': 'south-first', 'width': 2,
                'height': 2, 'biome': 'TemperateForest', 'cells': 'MGGG'}
        cases = [dict(base, schema_version=1), dict(base, row_order='north-first'),
                 dict(base, width=0), dict(base, height=-1), dict(base, cells='MGG'),
                 {'terrain_defs': {'Granite_Rough': 1}}]
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'source-terrain.json'
            for data in cases:
                with self.subTest(data=data):
                    write_json(path, data)
                    with self.assertRaises(ValueError):
                        observed_rock(path)

    def test_decoder_rejects_bad_contract_dimensions_bounds_overlap_and_kinds(self):
        base = {'schema_version': 1, 'mode': 'source-composition', 'row_order': 'south-first',
                'width': 3, 'height': 2, 'runs': []}
        cases = [dict(base, mode='clear-map'), dict(base, schema_version=2),
                 dict(base, row_order='north-first'), dict(base, width=0),
                 dict(base, height=-1), dict(base, width=3.0)]
        bad_runs = [
            [{'start': -1, 'length': 1, 'kind': 2}],
            [{'start': 5, 'length': 2, 'kind': 1}],
            [{'start': 0, 'length': 0, 'kind': 2}],
            [{'start': 0, 'length': 1, 'kind': 0}],
            [{'start': 0, 'length': 1, 'kind': 3}],
            [{'start': 0.5, 'length': 1, 'kind': 2}],
            [{'start': 0, 'length': 1.5, 'kind': 2}],
            [{'start': 0, 'length': 1, 'kind': 2.0}],
            [{'start': 0, 'length': 3, 'kind': 2}, {'start': 2, 'length': 1, 'kind': 1}],
        ]
        cases.extend(dict(base, runs=runs) for runs in bad_runs)
        for layer in cases:
            with self.subTest(layer=layer), self.assertRaises(ValueError):
                decode(layer)

    def test_encoder_rejects_empty_or_non_grid_input(self):
        for cells in (np.array([]), np.empty((0, 2)), np.array(list('MG')), np.empty((1, 2, 3))):
            with self.subTest(shape=cells.shape), self.assertRaises(ValueError):
                rock_layer(cells, 'TemperateForest')


class TransferAdmissionTests(unittest.TestCase):
    def evaluate(self, original, actual, exact=True, new_mountains=True):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source, run = folder / 'source', folder / 'native'
            source.mkdir()
            run.mkdir()
            entry = {'id': 'rock-scene', 'features': {'new_water': False, 'new_mountains': new_mountains},
                     'source': {'reference_run': 'source', 'reference_id': 'raw-source'}}
            if exact:
                entry['requires_rock_sidecar'] = True
            write_json(folder / 'catalog.json', {'entries': [entry]})
            baseline = np.full(actual.shape, 'G')
            for path, cells in ((source / 'raw-source-terrain.json', original),
                                (run / 'rock-scene-terrain.json', actual),
                                (run / 'rock-scene-baseline-terrain.json', baseline)):
                write_json(path, {'width': cells.shape[1], 'height': cells.shape[0],
                                  'cells': ''.join(cells.ravel())})
            write_json(run / 'result.json', {'ok': True, 'results': [
                {'id': 'rock-scene', 'biome': 'TemperateForest', 'size': actual.shape[0],
                 'tile': 7, 'counts': {'mountain': int((actual == 'M').sum())}}]})
            with contextlib.redirect_stdout(io.StringIO()):
                report = measure(folder, [run])
            self.assertEqual(json.loads((folder / 'transfer-evaluation.json').read_text(encoding='utf-8')), report)
            return report['records'][0]

    def test_extra_native_rocks_fail_even_with_perfect_source_recall(self):
        original = np.full((20, 20), 'G')
        original[2:10, 2:10] = 'M'  # 64 observed source rocks.
        actual = original.copy()
        actual[12:19, 12:19] = 'M'  # 49 unrelated native rocks.
        row = self.evaluate(original, actual)
        value = row['metrics']['mountains']
        self.assertEqual(value['expected_cells'], 64)
        self.assertEqual(value['actual_cells'], 113)
        self.assertEqual(value['recall'], 1)
        self.assertEqual(value['recall_within_2_cells'], 1)
        self.assertAlmostEqual(value['precision'], 64 / 113)
        self.assertAlmostEqual(value['iou'], 64 / 113)
        self.assertFalse(row['geometry_pass'])
        self.assertIn('Raw source rock precision/recall/IoU below 0.98', row['reasons'])

    def test_missing_single_cell_source_fragment_is_not_filtered_out_of_truth(self):
        original = np.full((20, 20), 'G')
        original[17, 3] = 'M'
        row = self.evaluate(original, np.full((20, 20), 'G'), new_mountains=False)
        self.assertEqual(row['metrics']['mountains']['expected_cells'], 1)
        self.assertEqual(row['metrics']['mountains']['recall'], 0)
        self.assertEqual(row['metrics']['mountains']['iou'], 0)
        self.assertFalse(row['geometry_pass'])

    def test_exact_single_cell_fragment_passes_without_a_mountain_polygon(self):
        original = np.full((20, 20), 'G')
        original[17, 3] = 'M'
        row = self.evaluate(original, original, new_mountains=False)
        self.assertTrue(row['geometry_pass'])
        self.assertEqual(row['metrics']['mountains']['expected_cells'], 1)
        for metric in ('precision', 'recall', 'iou'):
            self.assertEqual(row['metrics']['mountains'][metric], 1)

    def test_rock_free_source_rejects_even_one_extra_rock(self):
        original = np.full((20, 20), 'G')
        actual = original.copy()
        actual[12, 7] = 'M'
        row = self.evaluate(original, actual, new_mountains=False)
        self.assertIsNone(row['metrics']['mountains'])
        self.assertFalse(row['geometry_pass'])
        self.assertIn('Unexpected rocks in observed rock-free source', row['reasons'])

    def test_matching_rock_free_source_passes(self):
        original = np.full((20, 20), 'G')
        self.assertTrue(self.evaluate(original, original, new_mountains=False)['geometry_pass'])

    def test_iou_gate_rejects_when_precision_and_recall_each_reach_threshold(self):
        original = np.full((30, 30), 'G')
        original[5:15, 5:15] = 'M'  # 100 source cells.
        actual = original.copy()
        actual[5, 5:7] = 'G'
        actual[20, 20:22] = 'M'  # 98 shared / 102 union.
        row = self.evaluate(original, actual)
        value = row['metrics']['mountains']
        self.assertEqual(value['precision'], .98)
        self.assertEqual(value['recall'], .98)
        self.assertAlmostEqual(value['iou'], 98 / 102)
        self.assertFalse(row['geometry_pass'])

    def test_sidecar_absent_keeps_legacy_extra_rock_admission(self):
        original = np.full((20, 20), 'G')
        original[2:10, 2:10] = 'M'
        actual = original.copy()
        actual[12:19, 12:19] = 'M'
        row = self.evaluate(original, actual, exact=False)
        self.assertTrue(row['geometry_pass'])
        self.assertEqual(row['metrics']['mountains']['recall_within_2_cells'], 1)
        self.assertAlmostEqual(row['metrics']['mountains']['iou'], 64 / 113)
        self.assertEqual(row['reasons'], [])


class GroundDetailTests(unittest.TestCase):
    def test_observed_minimum_one_keeps_a_natural_patch_default_twelve_omits(self):
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'terrain.json'
            write_json(path, {'schema_version': 2, 'row_order': 'south-first',
                             'biome': 'TemperateForest', 'terrain_table': ['Missing', 'Sand'],
                             'terrain_indices': [0, 0, 0, 0, 0, 1, 0, 0]})
            cells = np.full((2, 4), 'G')
            palette = {'terrains': [natural_terrain('Sand', [170, 150, 60])]}
            detail, detail_receipt = observed_ground(path, cells, palette, minimum=1)
            legacy, legacy_receipt = observed_ground(path, cells, palette)
            self.assertEqual(detail['materials'], [{'def': 'Sand', 'role': 'sand'}])
            self.assertEqual(detail['runs'], [{'start': 5, 'length': 1, 'material': 1}])
            self.assertEqual(detail_receipt['ground_cells'], 1)
            self.assertEqual(legacy['materials'], [])
            self.assertEqual(legacy['runs'], [])
            self.assertEqual(legacy_receipt['ground_cells'], 0)
            self.assertEqual(legacy_receipt['omitted_ground']['Sand']['cells'], 1)

    def catalog_fixture(self, folder, native=True):
        scene_ids = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff', 'gl-archipelago', 'gl-oasis')
        source = folder / 'source'
        source.mkdir()
        cells = np.full((20, 20), 'G')
        cells[2:10, 2:10] = 'S'
        cells[17, 13] = 'M'
        indices = np.zeros((20, 20), dtype=int)
        indices[2:10, 2:10] = 2
        indices[17, 13] = 3
        indices[14, 16] = 1
        palette = {'terrains': [natural_terrain('Soil', [100, 50, 20]),
                                natural_terrain('Sand', [170, 150, 60]),
                                natural_terrain('WaterShallow', [0, 0, 255], water=True),
                                natural_terrain('Granite_Rough', [80, 80, 80])],
                   'overlays': [{'name': 'SolidStone', 'rgb': [25, 25, 25]}]}
        if native:
            write_json(source / 'native-terrain-palette.json', palette)
        scenes, inventory = [], []
        colors = np.asarray([[100, 50, 20], [170, 150, 60], [0, 0, 255], [25, 25, 25]], dtype=np.uint8)
        for ident in scene_ids:
            scenes.append({'id': ident, 'gl_id': ident, 'size': 20, 'world_seed': 'fixture',
                           'tile': 7, 'biome': 'TemperateForest', 'mutators': []})
            inventory.append({'id': ident, 'source': 'https://example.invalid/fixture',
                              'file': 'fixture.xml', 'sha256': 'fixture', 'revision': 'fixture'})
            write_json(source / (ident + '-terrain.json'),
                       {'schema_version': 2, 'row_order': 'south-first', 'biome': 'TemperateForest',
                        'width': 20, 'height': 20, 'cells': ''.join(cells.ravel()),
                        'terrain_table': ['Soil', 'Sand', 'WaterShallow', 'Granite_Rough'],
                        'terrain_indices': indices.ravel().tolist(), 'fertile_cells': 'N' * 400})
            Image.fromarray(colors[indices][::-1]).save(source / (ident + '-map.png'))
        write_json(source / 'result.json', {'ok': True, 'results': scenes})
        write_json(folder / 'gl-inventory.json', {'items': inventory})
        return source

    def test_actual_catalog_details_path_keeps_tiny_ground_and_rock(self):
        # A large pool makes a valid legacy polygon; the isolated Sand and M
        # cells must survive only via observed detail sidecars, not contours.
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source = self.catalog_fixture(folder)
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=True)
            command = json.loads((folder / 'recipes/gl-lake.json').read_text(encoding='utf-8'))
            ground = command['ground_layer']
            named_runs = [(run['start'], run['length'], ground['materials'][run['material'] - 1]['def'])
                          for run in ground['runs']]
            self.assertIn((296, 1, 'Sand'), named_runs)
            self.assertEqual(decode(command['rock_layer'])[17, 13], 2)
            receipt = json.loads((folder / 'recipes/gl-lake.receipt.json').read_text(encoding='utf-8'))
            self.assertEqual(receipt['rock_cells'], 1)
            self.assertEqual(receipt['omitted_small_rock_cells'], 0)
            entries = {entry['id']: entry for entry in json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))['entries']}
            self.assertTrue(entries['gl-lake']['requires_rock_sidecar'])
            self.assertFalse(entries['image-lake']['requires_rock_sidecar'])
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=False)
            legacy = json.loads((folder / 'recipes/gl-lake.json').read_text(encoding='utf-8'))
            self.assertNotIn('rock_layer', legacy)
            self.assertNotIn({'def': 'Sand', 'role': 'sand'}, legacy['ground_layer']['materials'])

    def test_detail_catalog_missing_native_palette_rejects_but_legacy_fallback_builds(self):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source = self.catalog_fixture(folder, native=False)
            with self.assertRaisesRegex(ValueError, 'Native terrain palette required'):
                build(folder, source, details=True)
            self.assertFalse((folder / 'catalog.json').exists())
            self.assertFalse((folder / 'recipes').exists())
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=False)
            catalog = json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))
            self.assertEqual(catalog['runtime_contract'], 'Product polygon commands only')
            self.assertEqual(len(catalog['entries']), 9)
            self.assertEqual(catalog['excluded'], [])
            command = json.loads((folder / 'recipes/gl-lake.json').read_text(encoding='utf-8'))
            self.assertTrue(command['params']['shape_ops'])
            for sidecar in ('rock_layer', 'ground_layer', 'water_layer'):
                self.assertNotIn(sidecar, command)

    def test_exact_rock_limitation_is_only_on_entries_with_real_rock_sidecars(self):
        detail_limitation = ('Exact observed solid-rock occupancy and tiny supported ground; '
                             'current tile native stone/ore selection, no original roofs/caves/resource layout transplant')
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source = self.catalog_fixture(folder)
            with contextlib.redirect_stdout(io.StringIO()):
                build(folder, source, details=True)
            entries = {entry['id']: entry for entry in json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))['entries']}
            self.assertFalse(entries['image-lake']['requires_rock_sidecar'])
            self.assertNotIn(detail_limitation, entries['image-lake']['limitations'])
            self.assertTrue(entries['gl-lake']['requires_rock_sidecar'])
            self.assertIn(detail_limitation, entries['gl-lake']['limitations'])
            self.assertEqual(sum(entry['requires_rock_sidecar'] for entry in entries.values()), 6)
            for entry in entries.values():
                self.assertEqual(detail_limitation in entry['limitations'], entry['requires_rock_sidecar'])


class CatalogRockEvidenceTests(unittest.TestCase):
    def finalize_entry(self, rock_records=None, rows=None, requires_rock=True):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            entry = {'id': 'rock-scene'}
            if requires_rock:
                entry['requires_rock_sidecar'] = True
            write_json(folder / 'catalog.json', {'entries': [entry]})
            rows = rows or [{'run': 'native', 'id': 'rock-scene', 'biome': 'TemperateForest',
                             'size': 250, 'geometry_pass': True, 'reasons': []}]
            write_json(folder / 'transfer-evaluation.json', {'records': rows})
            if rock_records is not None:
                write_json(folder / 'rock-evaluation.json', {'records': rock_records})
            with contextlib.redirect_stdout(io.StringIO()):
                finalize(folder)
            return json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))['entries'][0]

    def assert_quarantined(self, entry):
        self.assertEqual(entry['status'], 'prototype-quarantined')
        self.assertEqual(entry['verified_profiles'], [])
        self.assertTrue(any('Rock detail evidence absent or failed' in row['reasons']
                            for row in entry['verification']['failed_profiles']))

    def test_missing_rock_evidence_quarantines_geometry_pass(self):
        self.assert_quarantined(self.finalize_entry())

    def test_failed_rock_evidence_quarantines_geometry_pass(self):
        self.assert_quarantined(self.finalize_entry([{'run': 'native', 'id': 'rock-scene', 'pass': False}]))

    def test_unrelated_run_or_scene_evidence_does_not_promote(self):
        for record in ({'run': 'different-run', 'id': 'rock-scene', 'pass': True},
                       {'run': 'native', 'id': 'different-scene', 'pass': True}):
            with self.subTest(record=record):
                self.assert_quarantined(self.finalize_entry([record]))

    def test_passed_evidence_promotes_only_the_observed_profile(self):
        rows = [{'run': 'native', 'id': 'rock-scene', 'biome': 'TemperateForest',
                 'size': 250, 'geometry_pass': True, 'reasons': []},
                {'run': 'arid', 'id': 'rock-scene', 'biome': 'AridShrubland',
                 'size': 300, 'geometry_pass': True, 'reasons': []}]
        entry = self.finalize_entry([{'run': 'native', 'id': 'rock-scene', 'pass': True},
                                     {'run': 'arid', 'id': 'rock-scene', 'pass': False}], rows=rows)
        self.assertEqual(entry['status'], 'prototype-tested')
        self.assertEqual(entry['verified_profiles'],
                         [{'biome': 'TemperateForest', 'map_size': 250, 'hilliness': 'Flat'}])
        self.assertFalse(entry['verification']['aesthetic_approval'])

    def test_one_failed_replay_blocks_other_pass_of_the_same_profile(self):
        rows = [{'run': run, 'id': 'rock-scene', 'biome': 'TemperateForest', 'size': 250,
                 'geometry_pass': True, 'reasons': []} for run in ('native', 'repeat')]
        self.assert_quarantined(self.finalize_entry(
            [{'run': 'native', 'id': 'rock-scene', 'pass': True},
             {'run': 'repeat', 'id': 'rock-scene', 'pass': False}], rows=rows))

    def test_legacy_recipe_does_not_require_rock_evidence(self):
        entry = self.finalize_entry(requires_rock=False)
        self.assertEqual(entry['status'], 'prototype-tested')
        self.assertEqual(entry['verified_profiles'],
                         [{'biome': 'TemperateForest', 'map_size': 250, 'hilliness': 'Flat'}])


class FinalDetailReportTests(unittest.TestCase):
    """Late generation must be measured again instead of trusting stage 404."""

    @staticmethod
    def observation(cells, names):
        table = sorted(set(names.ravel().tolist()))
        indices = [table.index(name) for name in names.ravel()]
        return {'schema_version': 2, 'row_order': 'south-first', 'biome': 'TemperateForest',
                'width': cells.shape[1], 'height': cells.shape[0],
                'cells': ''.join(cells.ravel()), 'terrain_table': table,
                'terrain_indices': indices, 'surface_indices': indices,
                'terrain_defs': {name: int((names == name).sum()) for name in table}}

    def evaluate_details(self, source_cells, actual_cells, source_names=None, actual_names=None,
                         audit=None, audit_present=True, guard=False, fixtures=None):
        with tempfile.TemporaryDirectory() as root:
            folder = pathlib.Path(root)
            source, run = folder / 'source-native-final', folder / 'native'
            source.mkdir()
            run.mkdir()
            ident = 'rock-guard' if guard else 'rock-scene'
            if source_names is None:
                source_names = np.full(source_cells.shape, 'Soil', dtype=object)
            if actual_names is None:
                actual_names = source_names.copy()
            write_json(source / 'raw-source-terrain.json', self.observation(source_cells, source_names))
            write_json(run / (ident + '-terrain.json'), self.observation(actual_cells, actual_names))
            built = natural_terrain('AncientConcrete', [100, 100, 100])
            built['supported'] = False
            write_json(source / 'native-terrain-palette.json', {'terrains': [
                natural_terrain('Soil', [100, 50, 20]), natural_terrain('Sand', [170, 150, 60]),
                natural_terrain('WaterShallow', [0, 0, 255], water=True), built]})
            write_json(folder / 'catalog.json', {'entries': [
                {'id': ident, 'requires_rock_sidecar': True,
                 'source': {'reference_id': 'raw-source'}}]})
            write_json(run / 'result.json', {'ok': True, 'results': [
                {'id': ident, 'biome': 'TemperateForest', 'size': actual_cells.shape[0],
                 'tile': 7, 'counts': {}}]})
            protection_names = ('river', 'sea', 'special_water', 'water', 'road',
                                'constructed_floor', 'building', 'special_rock',
                                'preexisting_rock', 'preexisting_resource', 'other_thing', 'unsupported_terrain')
            receipt = {'protected_changes': 0, 'unknown_changes': 0, 'known_source_mismatches': 0,
                       'protected_conflicts': 1 if guard else 0,
                       'protection_kinds': {key: 1 if guard else 0 for key in protection_names},
                       'added_rock_cells': 1 if guard else 0, 'removed_rock_cells': 1 if guard else 0,
                       'removed_resource_cells': 1 if guard else 0, 'unknown_cells': 1 if guard else 0,
                       'stage': 404}
            early = dict(receipt, stage=199, protected_elevation_changes=0, protected_cave_changes=0,
                         unknown_elevation_changes=0, unknown_cave_changes=0, known_grid_mismatches=0)
            write_json(run / (ident + '-rock-grid-application.json'), early)
            write_json(run / (ident + '-rock-application.json'), receipt)
            final_audit = {'schema_version': 1, 'stage': 99999, 'known_source_mismatches': 0,
                           'row_order': 'south-first', 'source_width': source_cells.shape[1],
                           'source_height': source_cells.shape[0], 'target_width': actual_cells.shape[1],
                           'target_height': actual_cells.shape[0],
                           'known_rock_cells': int((source_cells == 'M').sum()),
                           'known_nonrock_cells': int(np.isin(source_cells, ['G', 'S', 'W']).sum()),
                           'missing_source_rock_cells': 0, 'extra_rock_cells_on_known_nonrock': 0,
                           'protected_conflicts': 0, 'unprotected_mismatches': 0,
                           'unknown_cells': int((~np.isin(source_cells, ['M', 'G', 'S', 'W'])).sum()),
                           'protection_kinds': {}, 'mismatch_protection_kinds': {},
                           'mismatch_terrain_defs': {}, 'mismatch_building_defs': {},
                           'mismatch_other_thing_defs': {}, 'mismatches': []}
            if audit is not None:
                final_audit.update(audit)
            if audit_present:
                write_json(run / (ident + '-rock-final-audit.json'), final_audit)
            if fixtures is not None:
                write_json(run / (ident + '-rock-fixtures.json'), fixtures)
            with contextlib.redirect_stdout(io.StringIO()):
                report = evaluate(folder, [run])
            self.assertEqual(json.loads((folder / 'rock-evaluation.json').read_text(encoding='utf-8')), report)
            return report['guards'][0] if guard else report['records'][0]

    def test_single_supported_source_patch_is_the_denominator_even_when_uncovered(self):
        source_cells = np.full((4, 4), 'G')
        source_names = np.full((4, 4), 'AncientConcrete', dtype=object)
        source_names[1, 1] = 'Sand'
        actual_cells = source_cells.copy()
        actual_cells[1, 1] = 'S'
        actual_names = source_names.copy()
        actual_names[1, 1] = 'WaterShallow'
        row = self.evaluate_details(source_cells, actual_cells, source_names, actual_names)
        ground = row['ground']
        self.assertEqual(row['metrics']['raw_rock']['iou'], 1)
        self.assertEqual(ground['source_natural_ground_cells'], 16)
        self.assertEqual(ground['source_supported_ground_cells'], 1)
        self.assertEqual(ground['source_unsupported_ground_cells'], 15)
        self.assertEqual(ground['mapped_source_ground_cells'], 1)
        self.assertEqual(ground['covered_ground_cells'], 0)
        self.assertEqual(ground['missing_ground_cells'], 1)
        self.assertEqual(ground['named_mismatching_cells'], 1)
        self.assertEqual(ground['source_named_ground_agreement'], 0)
        self.assertEqual(ground['tiny_patches_named']['source_cells'], 1)
        self.assertEqual(ground['tiny_patches_named']['missing_cells'], 1)
        self.assertFalse(row['pass'])

    def test_single_supported_patch_wrong_material_is_mismatch_not_missing_ground(self):
        cells = np.full((4, 4), 'G')
        source_names = np.full((4, 4), 'AncientConcrete', dtype=object)
        source_names[1, 1] = 'Sand'
        actual_names = source_names.copy()
        actual_names[1, 1] = 'Soil'
        row = self.evaluate_details(cells, cells, source_names, actual_names)
        ground = row['ground']
        self.assertEqual(ground['mapped_source_ground_cells'], 1)
        self.assertEqual(ground['covered_ground_cells'], 1)
        self.assertEqual(ground['missing_ground_cells'], 0)
        self.assertEqual(ground['named_matching_cells'], 0)
        self.assertEqual(ground['named_mismatching_cells'], 1)
        self.assertEqual(ground['materials'][0]['def'], 'Sand')
        self.assertEqual(ground['materials'][0]['mapped_cells'], 1)
        self.assertEqual(ground['materials'][0]['tiny_patches_coverage']['included_cells'], 1)
        self.assertEqual(ground['materials'][0]['tiny_patches_named']['included_cells'], 0)
        self.assertFalse(row['pass'])

    def test_same_biome_full_source_named_agreement_accepts_95_but_rejects_90_percent(self):
        cells = np.full((5, 5), 'G')
        source_names = np.full((5, 5), 'AncientConcrete', dtype=object)
        source_names[:4, :] = 'Sand'  # Exactly 20 supported source cells.
        actual_names = source_names.copy()
        actual_names[0, 0] = 'Soil'
        at_boundary = self.evaluate_details(cells, cells, source_names, actual_names)
        self.assertEqual(at_boundary['ground']['named_matching_cells'], 19)
        self.assertEqual(at_boundary['ground']['mapped_source_ground_cells'], 20)
        self.assertEqual(at_boundary['ground']['source_named_ground_agreement'], .95)
        self.assertTrue(at_boundary['pass'])
        actual_names[0, 1] = 'Soil'
        below_boundary = self.evaluate_details(cells, cells, source_names, actual_names)
        self.assertEqual(below_boundary['ground']['source_named_ground_agreement'], .90)
        self.assertFalse(below_boundary['pass'])

    def test_missing_final_audit_rejects_otherwise_exact_result(self):
        cells = np.full((4, 4), 'G')
        cells[2, 1] = 'M'
        self.assertTrue(self.evaluate_details(cells, cells)['pass'])
        row = self.evaluate_details(cells, cells, audit_present=False)
        self.assertEqual(row['metrics']['raw_rock']['iou'], 1)
        self.assertEqual(row['ground']['source_named_ground_agreement'], 1)
        self.assertFalse(row['pass'])

    def test_final_protected_conflict_rejects_raw_rock_iou_at_98_percent(self):
        source = np.full((20, 20), 'G')
        source[:10, :10] = 'M'  # 100 raw source rock cells.
        actual = source.copy()
        actual[0, :2] = 'G'
        row = self.evaluate_details(source, actual, audit={
            'known_source_mismatches': 2, 'missing_source_rock_cells': 2,
            'protected_conflicts': 2, 'unprotected_mismatches': 0,
            'mismatch_protection_kinds': {'constructed_floor': 2},
            'mismatches': [{'x': x, 'z': 0, 'source_kind': 2, 'actual_rock': False,
                            'current_guard_blocked': True, 'protection_kinds': ['constructed_floor'],
                            'terrain': 'Soil', 'building_def': None, 'other_thing_defs': [],
                            'elevation': .71, 'caves': 0, 'roof': None} for x in (0, 1)]})
        rock = row['metrics']['raw_rock']
        self.assertEqual(rock['expected_cells'], 100)
        self.assertEqual(rock['missing_cells'], 2)
        self.assertEqual(rock['precision'], 1)
        self.assertEqual(rock['recall'], .98)
        self.assertEqual(rock['iou'], .98)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertFalse(row['pass'])

    def test_final_unprotected_one_percent_mismatch_stays_within_declared_tolerance(self):
        source = np.full((20, 20), 'G')
        source[:10, :10] = 'M'
        actual = source.copy()
        actual[0, 0] = 'G'
        row = self.evaluate_details(source, actual, audit={
            'known_source_mismatches': 1, 'missing_source_rock_cells': 1,
            'protected_conflicts': 0, 'unprotected_mismatches': 1,
            'mismatches': [{'x': 0, 'z': 0, 'source_kind': 2, 'actual_rock': False,
                            'current_guard_blocked': False, 'protection_kinds': [],
                            'terrain': 'Soil', 'building_def': None, 'other_thing_defs': [],
                            'elevation': .71, 'caves': 0, 'roof': None}]})
        self.assertEqual(row['metrics']['raw_rock']['iou'], .99)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertTrue(row['pass'], row['reasons'])

    def guard_fixture(self):
        cells = np.full((40, 40), 'G')
        cells[5, 5] = cells[12, 12] = cells[12, 15] = cells[30, 30] = 'M'
        source_cells = cells.copy()
        source_cells[20, 31] = 'M'
        source_cells[5, 5] = 'N'
        names = np.full((40, 40), 'Soil', dtype=object)
        names[20, 31] = 'AncientConcrete'
        fixtures = {'schema_version': 1, 'stage': 403.5,
                    'special_constructed_floor': {'x': 31, 'z': 20, 'terrain': 'AncientConcrete',
                                                 'designation_category': None, 'natural': False, 'layerable': True}}
        audit = {'known_source_mismatches': 1, 'missing_source_rock_cells': 1,
                 'protected_conflicts': 1, 'unprotected_mismatches': 0,
                 'mismatch_protection_kinds': {'constructed_floor': 1},
                 'mismatch_terrain_defs': {'AncientConcrete': 1},
                 'mismatches': [{'x': 31, 'z': 20, 'source_kind': 2, 'actual_rock': False,
                                 'current_guard_blocked': True, 'early_snapshot_protected': False,
                                 'protection_kinds': ['constructed_floor'], 'terrain': 'AncientConcrete',
                                 'terrain_natural': False, 'terrain_layerable': True,
                                 'terrain_designation_category': None, 'building_def': None,
                                 'other_thing_defs': [], 'elevation': .71, 'caves': 0, 'roof': None}]}
        return source_cells, cells, names, fixtures, audit

    def test_guard_requires_actual_null_category_floor_to_remain_ancient_concrete(self):
        source_cells, cells, names, fixtures, audit = self.guard_fixture()
        positive = self.evaluate_details(source_cells, cells, actual_names=names,
                                         guard=True, fixtures=fixtures, audit=audit)
        self.assertTrue(positive['pass'], positive['reasons'])
        overwritten_names = names.copy()
        overwritten_names[20, 31] = 'Soil'
        negative = self.evaluate_details(source_cells, cells, actual_names=overwritten_names,
                                         guard=True, fixtures=fixtures, audit=audit)
        self.assertFalse(negative['pass'])

    def test_guard_rejects_missing_floor_fixture_metadata_despite_preserved_actual_floor(self):
        source_cells, cells, names, _, audit = self.guard_fixture()
        row = self.evaluate_details(source_cells, cells, actual_names=names, guard=True, audit=audit)
        self.assertTrue(row['checks']['special_cost_floor_fixture_preserved'])
        self.assertFalse(row['checks']['special_cost_floor_fixture_metadata_present'])
        self.assertFalse(row['pass'])

    def test_guard_rejects_wrong_floor_properties_or_coordinates(self):
        cases = [('designation_category', 'Floors'), ('natural', True), ('layerable', False),
                 ('x', 30), ('z', 21), ('terrain', 'Soil'), ('designation_category', 'missing')]
        for key, value in cases:
            with self.subTest(property=key, value=value):
                source_cells, cells, names, fixtures, audit = self.guard_fixture()
                if value == 'missing':
                    fixtures['special_constructed_floor'].pop(key)
                else:
                    fixtures['special_constructed_floor'][key] = value
                row = self.evaluate_details(source_cells, cells, actual_names=names,
                                            guard=True, fixtures=fixtures, audit=audit)
                self.assertTrue(row['checks']['special_cost_floor_fixture_preserved'])
                self.assertFalse(row['checks']['special_cost_floor_fixture_properties'])
                self.assertFalse(row['pass'])

    def test_invalid_final_audit_fails_closed_even_with_exact_actual_rock(self):
        cells = np.full((4, 4), 'G')
        cells[2, 1] = 'M'
        for audit in ({'stage': 404}, {'row_order': 'north-first'}, {'known_rock_cells': True},
                      {'unknown_cells': 1}, {'known_source_mismatches': 1},
                      {'mismatch_terrain_defs': None}, {'mismatches': None}):
            with self.subTest(audit=audit):
                row = self.evaluate_details(cells, cells, audit=audit)
                self.assertEqual(row['metrics']['raw_rock']['iou'], 1)
                self.assertFalse(row['final_audit']['structure_pass'])
                self.assertFalse(row['pass'])

    def test_plausible_zero_missing_audit_is_checked_against_final_observation(self):
        source = np.full((20, 20), 'G')
        source[:10, :10] = 'M'
        actual = source.copy()
        actual[0, 0] = 'G'
        row = self.evaluate_details(source, actual)
        self.assertEqual(row['metrics']['raw_rock']['missing_cells'], 1)
        self.assertEqual(row['metrics']['raw_rock']['iou'], .99)
        self.assertTrue(row['final_audit']['structure_pass'])
        self.assertEqual(row['final_audit']['missing_source_rock_cells'], 0)
        self.assertFalse(row['pass'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
