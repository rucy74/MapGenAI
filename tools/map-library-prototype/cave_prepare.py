"""Fresh native cave references and explicit geology replay; no product changes."""
import argparse
import hashlib
import json
import pathlib
import shutil
import numpy as np
from build_catalog import build, write
from rock import rock_layer
from water import water_layer


def prepare(folder, source):
    build(folder, source, details=True, caves=True)
    for name in ('a', 'b', 'c'):
        data = json.loads((folder/('transfer-'+name+'-manifest.json')).read_text(encoding='utf-8'))
        for scene in data['cases']:
            if scene['id'] in ('gl-cave-entrance', 'gl-secluded-valley'):
                scene['compare_without_cave'] = True
        write(folder/('cave-'+name+'-manifest.json'), data)
    previous = folder.parent/'rock-v4'
    for ident in ('rock-guard', 'unknown-rock'):
        shutil.copy2(previous/'recipes'/(ident+'.json'), folder/'recipes'/(ident+'.json'))
    size = 100
    cells = np.full((size, size), 'G')
    cells[20:60, 20:60] = 'M'
    cells[39:43, 20:60] = 'G'
    cells[5, 5] = 'N'
    rock, _ = rock_layer(cells, 'TemperateForest')
    elevation = np.full((size, size), .2)
    elevation[20:60, 20:60] = .82
    caves = np.zeros((size, size)); caves[39:43, 20:60] = 1
    roof = np.zeros((size, size), dtype=int)
    roof[20:60, 20:60] = 2; roof[39:43, 20:60] = 1
    layer = {'schema_version': 1, 'mode': 'source-geology', 'width': size,
             'height': size, 'row_order': 'south-first', 'source_biome': 'TemperateForest',
             'known': ''.join(np.where(cells.ravel() == 'N', 'N', 'K')),
             'elevation': elevation.ravel().tolist(), 'caves': caves.ravel().tolist(),
             'roof_codes': roof.ravel().tolist(), 'source_terrain_sha256': 'synthetic-control',
             'source_geology_sha256': 'synthetic-control'}
    write(folder/'recipes/cave-guard.json', {'action': 'generate', 'params': {},
          'rock_layer': rock, 'cave_layer': layer})
    unknown_rock, _ = rock_layer(np.full((size, size), 'N'), 'TemperateForest')
    # Other sidecars deliberately request changes. The all-unknown geometry
    # contract must preserve the target across the combined application too.
    wet = np.full((size, size), 'G'); wet[35:65, 35:65] = 'S'
    wet_names = np.full((size, size), 'Soil', dtype=object)
    wet_names[35:65, 35:65] = 'WaterShallow'
    soil = {'def': 'Soil', 'supported': True, 'water': False, 'river': False,
            'temporary': False, 'fertility': 1}
    water, _ = water_layer(wet, wet_names, {'terrains': [soil, {**soil, 'def': 'WaterShallow', 'water': True}]}, 'TemperateForest')
    ground = {'schema_version': 1, 'source_biome': 'TemperateForest', 'width': size,
              'height': size, 'row_order': 'south-first',
              'materials': [{'def': 'SoilRich', 'role': 'fertile'}],
              'runs': [{'start': 0, 'length': size*size, 'material': 1}]}
    write(folder/'recipes/unknown-cave.json', {'action': 'generate', 'params': {},
          'rock_layer': unknown_rock, 'water_layer': water, 'ground_layer': ground,
          'cave_layer': dict(layer, known='N'*(size*size))})
    unsafe_cells = cells.copy(); unsafe_cells[70:90, 70:90] = 'G'
    unsafe_rock, _ = rock_layer(unsafe_cells, 'TemperateForest')
    unsafe_elevation = elevation.copy(); unsafe_elevation[70:90, 70:90] = .82
    unsafe_caves = caves.copy(); unsafe_caves[70:90, 70:90] = 1
    unsafe_roof = roof.copy(); unsafe_roof[70:90, 70:90] = 1
    write(folder/'recipes/cave-unsafe.json', {'action': 'generate', 'params': {},
          'rock_layer': unsafe_rock, 'cave_layer': dict(layer,
          elevation=unsafe_elevation.ravel().tolist(), caves=unsafe_caves.ravel().tolist(),
          roof_codes=unsafe_roof.ravel().tolist())})
    write(folder/'cave-controls-manifest.json', {
        'world_seed': 'map-library-rock-controls-20261001', 'cases': [
            {'id': ident, 'biome': 'TemperateForest', 'size': size,
             'command': 'recipes/'+ident+'.json'}
            for ident in ('rock-guard', 'unknown-rock', 'cave-guard', 'unknown-cave', 'cave-unsafe')]})
    files = []
    for path in sorted(source.iterdir()):
        if path.suffix in ('.json', '.png'):
            files.append({'name': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    write(folder/'source-capture-receipt.json', {'reference_run': source.relative_to(folder).as_posix(),
          'fresh_gl_maps': 8, 'paid_api_calls': 0, 'files': files,
          'policy': 'Fresh actual GL native grids/roofs captured in genstep99999; old terrain-v2 references remain unchanged'})


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', type=pathlib.Path, required=True)
    parser.add_argument('--source', type=pathlib.Path, required=True)
    args = parser.parse_args(); prepare(args.folder, args.source)
