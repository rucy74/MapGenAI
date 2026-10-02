"""Prepare isolated exact same-biome replay beside unchanged adaptive recipes.

Source captures are actual final maps, not inferred from preview colors.  This
does not alter the product state, installed DLLs, or any previous experiment.
"""
import argparse
import hashlib
import json
from pathlib import Path

from build_catalog import build, write
from same_biome import prepare_replica


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(folder, source):
    folder, source = folder.resolve(), source.resolve()
    if folder not in source.parents:
        raise ValueError('Keep actual source captures inside this new experiment')
    if (folder / 'same-biome-preparation.json').exists():
        raise ValueError('Fresh preparation required; preserve previous results')
    # This writes the old adaptive recipes verbatim from the same observations.
    build(folder, source, details=True, caves=True)
    catalog = json.loads((folder / 'catalog.json').read_text(encoding='utf-8'))
    captures = json.loads((source / 'result.json').read_text(encoding='utf-8'))
    sources = {scene['id']: scene for scene in captures['results']}
    exact, excluded, hashes = {}, [], []
    for entry in catalog['entries']:
        ident = entry['id']
        if ident not in sources:
            continue
        adaptive = folder / entry['command']
        command = json.loads(adaptive.read_text(encoding='utf-8'))
        replica = source / (ident + '-replica.json')
        true_png = source / (ident + '-replica-map.png')
        default_png = source / (ident + '-replica-map-default.png')
        try:
            # A file reference keeps the model command parser's 8MiB bound.
            # It is resolved relative to the recipe, confined to artifact root.
            exact_command, receipt = prepare_replica(
                command, replica, source / (ident + '-terrain.json'),
                source / (ident + '-geology.json'), true_png, default_png,
                target_biome=sources[ident]['biome'],
                width=sources[ident]['size'], height=sources[ident]['size'],
                fresh_unedited=True,
                snapshot_file='../' + replica.relative_to(folder).as_posix())
            if not receipt['replica_selected']:
                raise ValueError('Exact source lacks a complete matching four-sidecar recipe')
            layer = exact_command['replica_layer']
        except (ValueError, FileNotFoundError) as error:
            excluded.append({'id': ident, 'reason': str(error),
                             'source_retained': True,
                             'exact_replay_selected': False})
            entry['same_biome_replica'] = {'status': 'quarantined',
                                         'reason': str(error)}
            continue
        exact_path = folder / 'recipes' / (ident + '.replica.json')
        write(exact_path, exact_command)
        write(exact_path.with_suffix('.receipt.json'), receipt)
        entry['same_biome_replica'] = {
            'status': 'unverified',
            'command': exact_path.relative_to(folder).as_posix(),
            'source_biome': layer['source_biome'],
            'width': layer['width'], 'height': layer['height'],
            'scope': 'Initial static fields and edifices; no save/pawn/plant lifetime clone',
            'verified_profiles': []}
        exact[ident] = entry['same_biome_replica']
        hashes.append({'id': ident,
                       'adaptive_command_sha256': digest(adaptive),
                       'replica_command_sha256': digest(exact_path),
                       'snapshot_sha256': digest(replica),
                       'true_png_sha256': digest(true_png),
                       'default_png_sha256': digest(default_png)})
    # Candidate manifests choose exact only for matching biome AND dimensions.
    # B and C keep all existing case positions and adaptive recipes, allowing
    # byte/actual-map regression against the previous code on identical inputs.
    for suffix in ('a', 'b', 'c'):
        original = folder / ('transfer-' + suffix + '-manifest.json')
        manifest = json.loads(original.read_text(encoding='utf-8'))
        selected, unavailable = [], []
        for case in manifest['cases']:
            info = exact.get(case['id'])
            scene = sources.get(case['id'])
            matching = scene is not None and case['biome'] == scene['biome'] and case['size'] == scene['size']
            if matching:
                if info is None:
                    unavailable.append(case['id'])
                    # An exact request is not silently presented as an adaptive
                    # success. Quarantined source evidence remains available.
                    continue
                case['command'] = info['command']
                case['capture_replica'] = True
                selected.append(case['id'])
        if unavailable:
            manifest['cases'] = [case for case in manifest['cases'] if case['id'] not in unavailable]
        manifest['exact_selected'] = selected
        manifest['exact_unavailable'] = unavailable
        write(folder / ('same-biome-' + suffix + '-manifest.json'), manifest)
    a = json.loads((folder / 'same-biome-a-manifest.json').read_text(encoding='utf-8'))
    smoke = next((case for case in a['cases'] if case['id'] == 'gl-cliff'), None)
    if smoke is not None:
        write(folder / 'same-biome-smoke-manifest.json',
              {'world_seed': a['world_seed'], 'cases': [smoke]})
    catalog['runtime_contract'] += '; optional validated same-biome exact static replica'
    write(folder / 'catalog.json', catalog)
    receipt = {'schema_version': 1, 'paid_api_calls': 0,
               'source': source.relative_to(folder).as_posix(),
               'admitted_for_native_verification': list(exact),
               'quarantined_sources': excluded, 'bindings': hashes,
               'policy': 'Same biome and dimensions on fresh full recipes only; other-biome/size adaptive bytes unchanged; no final success before actual raw and both PNGs match'}
    write(folder / 'same-biome-preparation.json', receipt)
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.folder, args.source), ensure_ascii=False))
