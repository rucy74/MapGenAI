"""Fresh developer cave replay evidence; final fidelity failures stay quarantined."""
import argparse
import base64
import datetime
import hashlib
import io
import json
import pathlib
import re
import subprocess
import numpy as np
from PIL import Image
from verify_ground import Document
from measure_transfer import measure
from ground_report import evaluate as ground_evaluate
from water_report import evaluate as water_evaluate
from rock_report import evaluate as rock_evaluate
from cave_report import evaluate as cave_evaluate
from finalize_catalog import finalize

ROOT = pathlib.Path(__file__).resolve().parents[2]


def read(path):
    return json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def verify(folder, runs, commands=True):
    folder = folder.resolve(); runs = [r.resolve() for r in runs]
    source = folder/read(folder/'source-capture-receipt.json')['reference_run']
    def command(args, name):
        result = subprocess.run(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                encoding='utf-8', errors='replace')
        (folder/name).write_text(result.stdout, encoding='utf-8')
        if result.returncode:
            raise ValueError('Fresh verification failed: '+repr(args))
        return result.stdout
    if commands:
        units = command(['python', '-X', 'utf8', '-m', 'unittest', 'discover',
                         '-s', 'tools/map-library-prototype', '-p', 'test_*.py', '-v'], 'unit-output.txt')
        command(['dotnet', 'build', 'tools/map-library-prototype/PrototypeProbe.csproj', '--nologo'], 'build-output.txt')
    else:
        units = (folder/'unit-output.txt').read_text(encoding='utf-8')
    unit_count = int(re.search(r'Ran (\d+) tests', units).group(1))
    checks = maps = 0
    for run in [source, *runs]:
        result = read(run/'result.json')
        assert result['ok'] and all(c['ok'] for c in result['checks']), str(run)
        assert all(s['provider_calls'] == 0 for s in result['results'])
        assert read(run/'cleanup.json')['archived']
        checks += len(result['checks']); maps += len(list(run.glob('*-terrain.json')))
        for path in run.glob('*-map*.png'):
            with Image.open(path) as image: image.verify()
        for path in run.glob('*-geology.json'):
            raw = read(path); terrain = path.with_name(path.name.replace('-geology.json', '-terrain.json'))
            assert raw['source_terrain_sha256'] == digest(terrain)
    for file in read(folder/'source-capture-receipt.json')['files']:
        assert digest(source/file['name']) == file['sha256']
    target_runs = runs[:3]
    geometry = measure(folder, target_runs)
    ground = ground_evaluate(folder, target_runs, source=source, strict=False)
    water, _ = water_evaluate(folder, target_runs)
    rock = rock_evaluate(folder, runs, source=source)
    cave = cave_evaluate(folder, runs)
    assert len(cave['records']) == 24 and len(rock['records']) == 24
    assert cave['final_audit_missing'] == cave['final_audit_invalid'] == 0
    assert len(rock['guards']) == 2 and all(g['pass'] for g in rock['guards'])
    assert cave['nonempty_source_cave_checks'] >= 15
    assert cave['guard_checks'] == cave['guard_passed'] == 3 and cave['guard_failed'] == 0
    # A validated rejection is evidence, not a successful cave reproduction.
    # Keep the intended fidelity goal explicit even if all such profiles fail.
    nonempty_passed = sum(r['pass'] and r.get('source', {}).get('portable_cave_cells', 0) > 0
                          for r in cave['records'])
    assert ground['applications'] == ground['passing_applications']
    # Water failures are retained and combined in admission, not hidden behind
    # successful generator execution or changed denominators.
    finalize(folder)
    catalog = read(folder/'catalog.json')
    accepted_gl_rows = []
    for row in cave['records']:
        entry = next(e for e in catalog['entries'] if e['id'] == row['id'])
        if any(p['biome'] == row['biome'] and p['map_size'] == row['size']
               for p in entry['verified_profiles']):
            accepted_gl_rows.append({'id': row['id'], 'run': row['run'],
                                     'biome': row['biome'], 'size': row['size']})
    for entry in catalog['entries']:
        for profile in entry['verified_profiles']:
            for flag, rows, result_key in (
                    ('requires_rock_sidecar', rock['records'], 'pass'),
                    ('requires_cave_sidecar', cave['records'], 'pass'),
                    ('requires_water_sidecar', water, 'pass')):
                if entry.get(flag):
                    matching = [r for r in rows if r['id'] == entry['id'] and r['biome'] == profile['biome']
                                and r['size'] == profile['map_size']]
                    assert matching and all(r[result_key] for r in matching)
    legacy = []
    for run, previous_name in zip(target_runs, ('detail-a-native-r2', 'detail-b-native-r2', 'detail-c-native-r2')):
        previous = folder.parent/'rock-v4'/previous_name
        for ident in ('core-foothills', 'core-dry-clearing'):
            before = read(previous/(ident+'-terrain.json')); after = read(run/(ident+'-terrain.json'))
            assert before == after
            for suffix in ('-map.png', '-map-default.png'):
                assert digest(previous/(ident+suffix)) == digest(run/(ident+suffix))
            legacy.append({'id': ident, 'run': run.name, 'whole_terrain_true_and_default_png_identical': True})
    previous_sources = folder.parent/'rock-v4/source-native-final'
    source_equivalence = []
    for ident in ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff', 'gl-archipelago', 'gl-oasis'):
        before = read(previous_sources/(ident+'-terrain.json')); after = read(source/(ident+'-terrain.json'))
        fields = {field: before[field] == after[field] for field in ('cells', 'fertile_cells', 'terrain_defs')}
        differences = {'cells': sum(a != b for a, b in zip(before['cells'], after['cells']))}
        for field in ('terrain_indices', 'surface_indices'):
            a = np.asarray(before['terrain_table'])[before[field]]
            b = np.asarray(after['terrain_table'])[after[field]]
            fields[field] = bool(np.array_equal(a, b)); differences[field] = int(np.sum(a != b))
        for suffix in ('-map.png', '-map-default.png'):
            fields[suffix] = digest(previous_sources/(ident+suffix)) == digest(source/(ident+suffix))
        # Fresh native sources are the new measured truth, not an assertion of
        # bit-identical physical maps across separate historical captures. PNG
        # equality must never conceal a differing rock/floor/building capture.
        source_equivalence.append({'id': ident, 'prior_actual_terrain_and_both_pngs_identical': all(fields.values()),
                                   'fields_identical': fields, 'physical_cell_differences': differences,
                                   'prior_caves_roofs_elevation': 'Not captured; no equality claim'})
    safety = read(folder/'source-roof-safety-evaluation.json')
    support_run = folder/safety['independent_support_capture']
    support_result = read(support_run/'result.json')
    assert safety['source_primary'] == source.name and safety['independent_support_capture'] != source.name
    assert support_result['ok'] and all(c['ok'] for c in support_result['checks'])
    assert read(support_run/'cleanup.json')['archived'] and len(safety['rows']) == 8
    assert all(r['native_projected_support_mismatches'] == 0 for r in safety['rows'])
    parser = read(folder/'native-parser-checks-r2.json')
    assert parser['passed'] == parser['total'] == len(parser['checks']) == 12
    assert all(c['pass'] for c in parser['checks'])
    assert parser['probe_sha256'].upper() == digest(ROOT/'tools/map-library-prototype/bin/Debug/net472/MapGenAI.MapLibraryProbe.dll').upper()
    document = Document(); document.feed((folder/'cave-review.html').read_text(encoding='utf-8'))
    assert document.images
    for src in document.images:
        assert src.startswith('data:image/png;base64,')
        with Image.open(io.BytesIO(base64.b64decode(src.split(',', 1)[1], validate=True))) as image: image.verify()
    dev = '6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6'
    stable = '9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5'
    hashes = {}
    for name, path, expected in (
            ('source_dev', ROOT/'dev/Assemblies/MapGenAI.dll', dev),
            ('installed_dev', 'G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll', dev),
            ('dist', ROOT/'dist/Assemblies/MapGenAI.dll', stable),
            ('installed_general', 'G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI/Assemblies/MapGenAI.dll', stable)):
        hashes[name] = digest(path).upper(); assert hashes[name] == expected
    assert not command(['git', 'diff', '--name-only', 'e288a0c', '--', 'dev', 'dist'], 'product-diff.txt').strip()
    index = read(folder/'index/space.json'); vectors = np.load(folder/'index/vectors.npy', allow_pickle=False)
    assert index['catalog_sha256'] == digest(folder/'catalog.json')
    assert index['entry_ids'] == [e['id'] for e in catalog['entries']]
    assert vectors.shape == (11, 384) and np.isfinite(vectors).all()
    assert np.allclose(np.linalg.norm(vectors, axis=1), 1, atol=1e-5)
    receipt = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
               'unit_tests': unit_count, 'native_execution_checks': checks,
               'fresh_actual_maps_including_sources_baselines_and_rock_only': maps,
               'fresh_gl_sources': 8, 'source_common_fields_equivalence': source_equivalence,
               'independent_support_capture': {'run': support_run.name,
                   'checks': len(support_result['checks']), 'actual_maps': len(support_result['results']),
                   'native_projected_support_mismatches': 0, 'observations': safety['rows'],
                   'scope': 'Separate fresh source samples; not substituted for primary reference physical data'},
               'native_parser_checks': parser['total'],
               'cave_passed': cave['passed'], 'cave_cases': cave['checks'],
               'cave_nonempty_actual_source_cases': cave['nonempty_source_cave_checks'],
               'cave_nonempty_portable_passed': nonempty_passed,
               'nonempty_fidelity_goal_met': bool(nonempty_passed),
               'cave_controls': cave['guards'],
               'all_gates_passing_gl_cases': len(accepted_gl_rows),
               'all_gates_passing_gl_rows': accepted_gl_rows,
               'rock_ground_strict_passed': rock['passed'], 'rock_ground_strict_cases': rock['checks'],
               'water_passed': sum(r['pass'] for r in water), 'water_cases': len(water),
               'geometry_passed': geometry['passed'], 'geometry_cases': geometry['checks'],
               'legacy_controls': legacy, 'product_hashes': hashes,
               'available_entries': [e['id'] for e in catalog['entries'] if e['verified_profiles']],
               'quarantined_entries': [e['id'] for e in catalog['entries'] if not e['verified_profiles']],
               'local_index_shape': list(vectors.shape), 'local_index_catalog_hash_matches': True,
               'html_embedded_pngs': len(document.images), 'html_rendered_in_browser': False,
               'paid_api_calls': 0, 'archived_runs': [p.name for p in [source, *runs]],
               'scope': 'Observed inland developer geology only. Failed actual profiles stay excluded; no source resource/world/save transplant, product integration or beauty certification.'}
    (folder/'verification.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: receipt[k] for k in ('unit_tests', 'native_execution_checks',
        'fresh_actual_maps_including_sources_baselines_and_rock_only', 'cave_passed', 'cave_cases',
        'cave_nonempty_portable_passed', 'all_gates_passing_gl_cases', 'html_embedded_pngs', 'paid_api_calls')}, ensure_ascii=False))
    return receipt


if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--folder', type=pathlib.Path, required=True)
    parser.add_argument('--runs', nargs='+', required=True); parser.add_argument('--skip-commands', action='store_true')
    args = parser.parse_args(); verify(args.folder, [args.folder/name for name in args.runs], commands=not args.skip_commands)
