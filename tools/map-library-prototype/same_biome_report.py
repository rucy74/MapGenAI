"""Actual static-replica and preservation report. No game, API or catalog edits.

Reads completed native runs. Only write_report/CLI creates report artifacts at
the explicitly supplied output directory; collect is read-only.
"""
import argparse
import base64
import html
import io
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image

from same_biome import (AUDIT_FIELDS, compare_pngs, evaluate, hash_string,
                        original_phase_pngs, read, sha256)


GL_IDS = ('gl-lake', 'gl-valley', 'gl-lone-mountain', 'gl-cliff',
          'gl-archipelago', 'gl-oasis', 'gl-cave-entrance', 'gl-secluded-valley')
CORE_IDS = ('core-foothills', 'core-dry-clearing')
DLL_KEYS = ('source_dev', 'installed_dev', 'dist', 'installed_general')


def safe_read(path):
    try:
        value = read(path)
        if not isinstance(value, dict):
            raise ValueError('Expected JSON object')
        return value, None
    except (OSError, ValueError, TypeError) as error:
        return {}, str(error)


def run_evidence(folder):
    folder = Path(folder).resolve()
    result, error = safe_read(folder / 'result.json')
    launch, launch_error = safe_read(folder / 'launch.json')
    cleanup, cleanup_error = safe_read(folder / 'cleanup.json')
    reasons = []
    if error:
        reasons.append('Completed result missing/invalid: ' + error)
    run_ok = result.get('ok') is True and result.get('error') is None
    if not run_ok:
        reasons.append('Whole native run has not completed successfully')
    checks = result.get('checks')
    if not isinstance(checks, list) or not checks or any(not isinstance(c, dict) or c.get('ok') is not True for c in checks):
        reasons.append('Native execution checks missing/failed')
    rows = result.get('results')
    rows = rows if isinstance(rows, list) else []
    ids = [r.get('id') for r in rows if isinstance(r, dict)]
    if len(ids) != len(rows) or len(set(ids)) != len(ids) or any(not isinstance(v, str) or not v for v in ids):
        reasons.append('Native result case identities invalid/duplicated')
    if not rows or any(type(r.get('provider_calls')) is not int or r['provider_calls'] != 0 for r in rows if isinstance(r, dict)):
        reasons.append('Actual zero provider-call counts missing/nonzero')
    if launch_error:
        reasons.append('Launch evidence missing/invalid: ' + launch_error)
    for key in ('productDllSha256', 'probeDllSha256'):
        if not hash_string(str(launch.get(key, '')).lower()):
            reasons.append('Launch DLL hash missing/invalid: ' + key)
    manifest_name = launch.get('manifest')
    manifest_path = Path(manifest_name) if isinstance(manifest_name, str) and manifest_name else folder / '__missing_manifest__'
    manifest, manifest_error = safe_read(manifest_path)
    if manifest_error or not isinstance(manifest.get('cases'), list):
        reasons.append('Actual launch manifest missing/invalid')
    if cleanup_error:
        reasons.append('Cleanup evidence missing/invalid: ' + cleanup_error)
    archive_name, mod_name = cleanup.get('archive'), cleanup.get('mod')
    archive = Path(archive_name) if isinstance(archive_name, str) and archive_name else folder / '__missing_archive__'
    mod = Path(mod_name) if isinstance(mod_name, str) and mod_name else folder / '__missing_mod__'
    if cleanup.get('archived') is not True or not cleanup.get('archive') or not archive.is_dir():
        reasons.append('Probe archive not confirmed on disk')
    if not cleanup.get('mod') or mod.exists() or cleanup.get('mod') != launch.get('mod'):
        reasons.append('Temporary probe removal/identity not confirmed')
    archived_hashes = {}
    for file, key in [('MapGenAI.dll', 'productDllSha256'), ('MapGenAI.MapLibraryProbe.dll', 'probeDllSha256')]:
        path = archive / 'Assemblies' / file
        try:
            archived_hashes[key] = sha256(path)
            if archived_hashes[key] != str(launch.get(key, '')).lower():
                reasons.append('Archived DLL disagrees with actual launch: ' + file)
        except OSError as exc:
            reasons.append('Archived DLL missing: ' + str(exc))
    return {'run': folder.name, 'path': str(folder), 'run_ok': run_ok,
            'pass': not reasons, 'reasons': reasons, 'case_ids': ids,
            'execution_checks': len(checks) if isinstance(checks, list) else 0,
            'result_cases': len(rows), 'full_raw_map_captures': len(list(folder.glob('*-terrain.json'))),
            'rows': rows, 'launch': launch,
            'manifest': manifest, 'cleanup': cleanup, 'archived_dll_hashes': archived_hashes}


def source_observation(folder, ident):
    """Inspect unsafe original evidence too; never remove it from the roster."""
    folder = Path(folder)
    path = folder / (ident + '-replica.json')
    data, error = safe_read(path)
    info = {'id': ident, 'path': str(path), 'present': not bool(error),
            'evidence_pass': False, 'reasons': [], 'images': {}}
    if error:
        info['reasons'].append(error)
        return info
    try:
        w, h = data['width'], data['height']
        if type(w) is not int or type(h) is not int or min(w, h) <= 0 or data['row_order'] != 'south-first':
            raise ValueError('Invalid original dimensions/row order')
        area = w * h
        if data.get('schema_version') != 1 or data.get('mode') != 'source-replica' or data['known_mask'] != '1' * area:
            raise ValueError('Invalid original full observation schema/known mask')
        if data['context']['capture_phase'] != 'post-map-initialized' or data['context']['stage'] != 99999:
            raise ValueError('Original final observation phase missing')
        for key in ('top_indices', 'permanent_indices', 'surface_indices', 'under_indices',
                    'foundation_indices', 'temp_indices', 'color_indices', 'roof_indices',
                    'edifice_indices', 'elevation', 'caves', 'fertility'):
            if not isinstance(data[key], list) or len(data[key]) != area:
                raise ValueError('Original full raw grid missing: ' + key)
        for key in ('elevation', 'caves', 'fertility'):
            if not np.isfinite(np.asarray(data[key], dtype=float)).all():
                raise ValueError('Original non-finite grid: ' + key)
        for key in ('walkable', 'native_roof_supported', 'projected_roof_supported'):
            if not isinstance(data[key], str) or len(data[key]) != area or set(data[key]) - set('01'):
                raise ValueError('Original actual physical mask missing: ' + key)
        info.update(width=w, height=h, biome=data['biome'], context=data['context'],
                    unique_edifices=len(data['edifices']), unsupported=data['unsupported'])
        files, bindings = data['binding_files'], data['source_bindings']
        for key in ('terrain', 'geology', 'map_png', 'map_default_png'):
            name, expected = files[key], bindings[key + '_sha256']
            if not isinstance(name, str) or Path(name).name != name or not hash_string(expected):
                raise ValueError('Invalid original evidence binding: ' + key)
            actual = folder / name
            if sha256(actual) != expected:
                raise ValueError('Original actual file SHA mismatch: ' + key)
            if 'png' in key:
                with Image.open(actual) as image:
                    if image.format != 'PNG' or image.size != (w, h):
                        raise ValueError('Original actual PNG invalid: ' + key)
                info['images'][key] = str(actual)
        for key, suffix in [('early_true', '-map.png'), ('early_default', '-map-default.png')]:
            image = folder / (ident + suffix)
            if not image.is_file():
                raise ValueError('Original 99999 actual PNG missing')
            info['images'][key] = str(image)
        roofs = np.asarray(data['roof_table'], dtype=object)[np.asarray(data['roof_indices'], dtype=int)]
        metadata = data['roof_def_metadata']
        collapsible = np.asarray([False if name == 'None' else metadata[name]['can_collapse'] for name in roofs])
        native = np.asarray(list(data['native_roof_supported'])) == '1'
        projected = np.asarray(list(data['projected_roof_supported'])) == '1'
        info['roof_counts'] = {'roofed': int((roofs != 'None').sum()),
            'collapsible': int(collapsible.sum()),
            'native_unsupported_collapsible': int((collapsible & ~native).sum()),
            'projected_unsupported_collapsible': int((collapsible & ~projected).sum())}
        info['phase_png_comparison'] = {}
        for key, early in [('map_png', 'early_true'), ('map_default_png', 'early_default')]:
            info['phase_png_comparison'][key] = compare_pngs(info['images'][early], info['images'][key], w, h)
        info['snapshot_sha256'] = sha256(path)
        info['evidence_pass'] = True
    except (OSError, ValueError, TypeError, KeyError, IndexError) as exc:
        info['reasons'].append(str(exc))
    return info


def compare_preserved(old_run, new_run, ids=None):
    old, new = run_evidence(old_run), run_evidence(new_run)
    old_rows = {r['id']: r for r in old['rows'] if isinstance(r, dict) and 'id' in r}
    new_rows = {r['id']: r for r in new['rows'] if isinstance(r, dict) and 'id' in r}
    ids = list(ids) if ids is not None else list(old_rows)
    records = []
    for ident in ids:
        record = {'id': ident, 'old_run': str(Path(old_run).resolve()),
                  'new_run': str(Path(new_run).resolve()), 'pass': False,
                  'reasons': [], 'files': {}, 'images': {}}
        a, b = old_rows.get(ident), new_rows.get(ident)
        if a is None or b is None:
            record['reasons'].append('Expected actual case missing from completed result')
        else:
            record['old_profile'] = {key: a.get(key) for key in ('tile', 'biome', 'size', 'world_seed')}
            record['new_profile'] = {key: b.get(key) for key in ('tile', 'biome', 'size', 'world_seed')}
            for field in ('tile', 'biome', 'size', 'world_seed', 'mutators'):
                if field not in a or a.get(field) != b.get(field):
                    record['reasons'].append('Actual preservation profile differs: ' + field)
            for suffix in ('-terrain.json', '-geology.json'):
                before, after = Path(old_run) / (ident + suffix), Path(new_run) / (ident + suffix)
                left, left_error = safe_read(before)
                right, right_error = safe_read(after)
                if left_error or right_error:
                    record['reasons'].append('Actual full raw file missing/invalid: ' + suffix)
                    continue
                changed = sorted(key for key in set(left) | set(right) if key not in left or key not in right or left[key] != right[key])
                byte_equal = sha256(before) == sha256(after)
                record['files'][suffix] = {'all_fields_equal': not changed, 'changed_fields': changed,
                    'byte_equal': byte_equal, 'old_sha256': sha256(before), 'new_sha256': sha256(after)}
                if changed or not byte_equal:
                    record['reasons'].append('Actual full raw preservation differs: ' + suffix)
            for name, suffix in [('map_png', '-map.png'), ('map_default_png', '-map-default.png')]:
                before, after = Path(old_run) / (ident + suffix), Path(new_run) / (ident + suffix)
                try:
                    metric = compare_pngs(before, after, a['size'], a['size'])
                    record['files'][name] = metric
                    record['images'][name] = [str(before.resolve()), str(after.resolve())]
                    if not metric['pixel_equal'] or not metric['byte_equal']:
                        record['reasons'].append('Actual native PNG preservation differs: ' + name)
                except (OSError, ValueError, TypeError, KeyError) as exc:
                    record['reasons'].append('Actual native PNG missing/invalid: ' + str(exc))
        if not old['pass'] or not new['pass']:
            record['reasons'].append('Completed run/launch/archive evidence incomplete')
        if old['launch'].get('productDllSha256') != new['launch'].get('productDllSha256'):
            record['reasons'].append('Product DLL changed between preservation runs')
        record['pass'] = not record['reasons']
        terrain = record['files'].get('-terrain.json', {})
        record['terrain_png_equal'] = (terrain.get('all_fields_equal') is True and terrain.get('byte_equal') is True
            and all(record['files'].get(name, {}).get('pixel_equal') is True
                    and record['files'].get(name, {}).get('byte_equal') is True for name in ('map_png', 'map_default_png')))
        geology = record['files'].get('-geology.json', {})
        record['full_geology_equal'] = geology.get('all_fields_equal') is True and geology.get('byte_equal') is True
        records.append(record)
    return {'old': old, 'new': new, 'expected_cases': len(ids),
            'passed': sum(r['pass'] for r in records), 'pass': bool(ids) and all(r['pass'] for r in records),
            'records': records}


def boundary_evidence(boundary, dll_paths):
    result = {'pass': False, 'files': {}, 'reasons': []}
    data, error = safe_read(boundary) if boundary else ({}, 'Boundary baseline not provided')
    expected = data.get('product_hashes', {})
    if error:
        result['reasons'].append(error)
    for key in DLL_KEYS:
        path, original = (dll_paths or {}).get(key), str(expected.get(key, '')).lower()
        row = {'path': str(path) if path else None, 'expected_sha256': original, 'pass': False}
        if not path or not hash_string(original):
            result['reasons'].append('Actual DLL path/baseline missing: ' + key)
        else:
            try:
                row['actual_sha256'] = sha256(path)
                row['pass'] = row['actual_sha256'] == original
                if not row['pass']:
                    result['reasons'].append('Actual source/installed DLL changed: ' + key)
            except OSError as exc:
                result['reasons'].append('Actual DLL file missing: ' + str(exc))
        result['files'][key] = row
    result['pass'] = not result['reasons']
    return result


def collect(folder, exact_runs, adaptive_pairs=(), legacy_pairs=(), boundary=None, dll_paths=None, diagnostic_pairs=()):
    folder = Path(folder).resolve()
    preparation, prep_error = safe_read(folder / 'same-biome-preparation.json')
    manifest, manifest_error = safe_read(folder / 'source-manifest.json')
    reasons = []
    source_name = preparation.get('source', 'source-native-r1')
    source = (folder / source_name).resolve() if isinstance(source_name, str) else folder / 'source-native-r1'
    if not isinstance(source_name, str) or not source.is_relative_to(folder):
        reasons.append('Original source path escapes/misses experiment')
        source = folder / 'source-native-r1'
    source_run = run_evidence(source)
    if prep_error or manifest_error:
        reasons.append('Preparation/source manifest missing/invalid')
    roster = [c.get('id') for c in manifest.get('cases', []) if isinstance(c, dict)]
    if len(roster) != len(GL_IDS) or set(roster) != set(GL_IDS):
        reasons.append('Fixed original GL8 roster missing/changed')
    selected = preparation.get('admitted_for_native_verification', [])
    if not isinstance(selected, list) or any(not isinstance(ident, str) for ident in selected):
        selected = []
        reasons.append('Invalid prepared original source selection')
    quarantined = {r['id']: r for r in preparation.get('quarantined_sources', []) if isinstance(r, dict) and 'id' in r}
    if len(selected) != len(set(selected)) or set(selected) & set(quarantined) or set(selected) | set(quarantined) != set(GL_IDS):
        reasons.append('Preparation dropped/duplicated original GL sources')
    if type(preparation.get('paid_api_calls')) is not int or preparation['paid_api_calls'] != 0:
        reasons.append('Preparation zero paid-call receipt missing')
    if len(source_run['case_ids']) != 8 or set(source_run['case_ids']) != set(GL_IDS):
        reasons.append('Completed original GL8 actual results missing/changed')
    observations = {ident: source_observation(source, ident) for ident in GL_IDS}
    if not all(row['evidence_pass'] for row in observations.values()):
        reasons.append('Actual original GL8 binding/phase/raw/image evidence incomplete')
    bindings = {r['id']: r for r in preparation.get('bindings', []) if isinstance(r, dict) and 'id' in r}
    catalog, catalog_error = safe_read(folder / 'catalog.json')
    entries = {e['id']: e for e in catalog.get('entries', []) if isinstance(e, dict) and 'id' in e}
    for ident in selected:
        try:
            item, entry = bindings[ident], entries[ident]
            adaptive = folder / entry['command']
            replica = folder / entry['same_biome_replica']['command']
            for path, key in [(adaptive, 'adaptive_command_sha256'), (replica, 'replica_command_sha256'),
                              (source / (ident + '-replica.json'), 'snapshot_sha256'),
                              (source / (ident + '-replica-map.png'), 'true_png_sha256'),
                              (source / (ident + '-replica-map-default.png'), 'default_png_sha256')]:
                if sha256(path) != item[key]:
                    raise ValueError('Preparation actual input SHA differs: ' + key)
        except (OSError, ValueError, TypeError, KeyError) as exc:
            reasons.append(ident + ': ' + str(exc))
    if catalog_error:
        reasons.append('Catalog input missing/invalid')
    exact, statuses = [], []
    seen = set()
    for run in exact_runs:
        target = (Path(run) if Path(run).is_absolute() else folder / run).resolve()
        if target in seen:
            reasons.append('Duplicate exact run input')
            continue
        seen.add(target)
        status = run_evidence(target)
        statuses.append(status)
        requested = {c.get('id') for c in status['manifest'].get('cases', []) if isinstance(c, dict)}
        for ident in GL_IDS:
            raw = evaluate(source / (ident + '-replica.json'), target / (ident + '-replica.json'),
                           target / (ident + '-replica-native-audit.json'),
                           run_ok=source_run['run_ok'] and status['run_ok'])
            raw.update(id=ident, run=target.name, target_run_path=str(target), requested=ident in requested,
                source_reference_run=source.name, quarantined=ident in quarantined,
                quarantine_reason=quarantined.get(ident, {}).get('reason'),
                profile=next((r for r in status['rows'] if r.get('id') == ident), {}),
                report_evidence_pass=source_run['pass'] and status['pass'] and not reasons
                    and observations[ident]['evidence_pass'])
            if ident in quarantined:
                raw['admission_pass'] = False
            raw['verified_pass'] = raw['admission_pass'] and raw['report_evidence_pass']
            raw['images'] = {}
            for name, suffix in [('map_png', '-replica-map.png'), ('map_default_png', '-replica-map-default.png')]:
                image = target / (ident + suffix)
                if image.is_file():
                    raw['images'][name] = str(image)
            exact.append(raw)
    adaptive = [compare_preserved(old, new, GL_IDS) for old, new in adaptive_pairs]
    legacy = [compare_preserved(old, new, CORE_IDS) for old, new in legacy_pairs]
    diagnostics = [compare_preserved(old, new) for old, new in diagnostic_pairs]
    for pair in diagnostics:
        pair['probe_dll_hash_equal'] = (hash_string(str(pair['old']['launch'].get('probeDllSha256', '')).lower())
            and pair['old']['launch'].get('probeDllSha256') == pair['new']['launch'].get('probeDllSha256'))
        pair['scope'] = 'Optional actual diagnostic; full raw differences retained; no automatic attribution or admission'
    boundary_result = boundary_evidence(boundary, dll_paths)
    primary = [r for r in exact if statuses and r['target_run_path'] == statuses[0]['path']]
    preservation_ready = bool(adaptive) and all(p['pass'] for p in adaptive)
    legacy_count = sum(p['expected_cases'] for p in legacy)
    legacy_ready = (legacy_count == 6 and len({p['old']['path'] for p in legacy}) == 3
                    and len({p['new']['path'] for p in legacy}) == 3 and all(p['pass'] for p in legacy))
    expected_product = boundary_result['files'].get('source_dev', {}).get('expected_sha256')
    current_runs = [source_run] + statuses + [p['new'] for p in adaptive + legacy]
    if hash_string(expected_product) and any(str(r['launch'].get('productDllSha256', '')).lower() != expected_product for r in current_runs):
        reasons.append('Actual launched product DLL differs from boundary baseline')
    evidence_ready = source_run['pass'] and all(s['pass'] for s in statuses) and not reasons and boundary_result['pass']
    summary = {'source_total': len(GL_IDS), 'source_available_for_verification': len(selected),
        'source_quarantined': len(quarantined), 'primary_requested': sum(r['requested'] for r in primary),
        'primary_fidelity_passed': sum(r['fidelity_pass'] for r in primary),
        'primary_native_admission_passed': sum(r['admission_pass'] for r in primary),
        'primary_verified_passed': sum(r['verified_pass'] for r in primary),
        'adaptive_compared': sum(p['expected_cases'] for p in adaptive), 'adaptive_preserved': sum(p['passed'] for p in adaptive),
        'legacy_expected': 6, 'legacy_compared': legacy_count, 'legacy_preserved': sum(p['passed'] for p in legacy),
        'adaptive_terrain_png_equal': sum(r['terrain_png_equal'] for p in adaptive for r in p['records']),
        'adaptive_full_geology_equal': sum(r['full_geology_equal'] for p in adaptive for r in p['records']),
        'legacy_terrain_png_equal': sum(r['terrain_png_equal'] for p in legacy for r in p['records']),
        'legacy_full_geology_equal': sum(r['full_geology_equal'] for p in legacy for r in p['records']),
        'source_all_eight_exact_goal_met': len(primary) == 8 and all(r['verified_pass'] for r in primary),
        'available_sources_exact_goal_met': bool(selected) and all(any(r['id'] == ident and r['verified_pass'] for r in primary) for ident in selected),
        'report_evidence_ready': evidence_ready, 'adaptive_preservation_ready': preservation_ready,
        'legacy_preservation_ready': legacy_ready,
        'bundle_verified': evidence_ready and preservation_ready and legacy_ready
            and bool(selected) and all(any(r['id'] == ident and r['verified_pass'] for r in primary) for ident in selected)}
    return {'schema_version': 1, 'folder': str(folder), 'source_reference_run': source.name,
        'preparation': preparation, 'preparation_reasons': reasons, 'source_run': source_run,
        'source_observations': list(observations.values()), 'exact_runs': statuses, 'records': exact,
        'adaptive_preservation': adaptive, 'legacy_preservation': legacy, 'diagnostic_preservation': diagnostics, 'boundary': boundary_result,
        'summary': summary, 'catalog_finalization': 'Separate main task; this report does not register profiles',
        'scope': 'Actual initial static fields/edifices and both native PNGs; no save/pawn/plant/quest survival equivalence; product recommendation UI disconnected'}


def embedded_image(path, caption):
    if not path or not Path(path).is_file():
        return '<figure><p class="absent">실제 결과 그림 없음</p><figcaption>' + html.escape(caption) + '</figcaption></figure>'
    with Image.open(path) as image:
        if image.format != 'PNG':
            raise ValueError('Only actual native PNG images may be embedded')
        image.load()
    payload = base64.b64encode(Path(path).read_bytes()).decode('ascii')
    return '<figure><img alt="' + html.escape(caption, quote=True) + '" src="data:image/png;base64,' + payload + '"><figcaption>' + html.escape(caption) + '</figcaption></figure>'


def review(data):
    summary = data['summary']
    parts = ['<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>실제 same-biome static 비교</title>',
        '<style>body{font:16px/1.65 system-ui,sans-serif;background:#111820;color:#edf2f7;margin:0 auto;max-width:1380px;padding:24px}h1,h2{line-height:1.3}article{border:1px solid #40505e;border-radius:12px;padding:18px;margin:24px 0}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:16px}figure{margin:0}img{width:100%;image-rendering:pixelated;display:block}figcaption,.muted{color:#c0cbd6;font-size:14px}.absent{min-height:180px;display:grid;place-items:center;border:1px dashed #657888}.ok{color:#84dfb1}.fail{color:#ffc298}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{padding:10px;border-bottom:1px solid #455766;text-align:left}details{margin:14px 0}@media(max-width:600px){body{padding:12px}.grid{grid-template-columns:1fr}}</style>',
        '<h1>Prototype · 제품 추천 UI 미연결</h1><p>같은 바이옴·같은 크기의 생성 직후 static 필드와 실제 PNG 2종을 대조합니다. 모든 그림은 실제 native map capture이며 지형 진단 overlay를 넣지 않았습니다. 등록·인덱스 확정은 별도 작업입니다.</p>',
        '<p><b>원본 ' + str(summary['source_total']) + '개 전체:</b> 엄격 native 대조 ' + str(summary['primary_native_admission_passed']) + '/' + str(summary['source_total']) + ', 검증 가능한 원본 ' + str(summary['source_available_for_verification']) + '개, 격리 ' + str(summary['source_quarantined']) + '개. 격리된 원본도 분모와 그림에 유지합니다.</p>',
        '<p>필드 자체 일치 ' + str(summary['primary_fidelity_passed']) + '개 · 완료/해시/cleanup 증거 포함 ' + str(summary['primary_verified_passed']) + '개. 전체 증거 번들 ' + ('확인' if summary['bundle_verified'] else '미완료') + '. PNG 일치는 지붕 안전·통로·실제 edifice 대조를 대신하지 않습니다.</p>']
    observations = {r['id']: r for r in data['source_observations']}
    primary_run = data['exact_runs'][0]['run'] if data['exact_runs'] else None
    primary = {r['id']: r for r in data['records'] if r['run'] == primary_run}
    for ident in GL_IDS:
        source, target = observations[ident], primary.get(ident, {})
        profile = target.get('profile', {})
        status = '격리: ' + str(target.get('quarantine_reason')) if target.get('quarantined') else ('실제 엄격 대조 PASS' if target.get('verified_pass') else '미검증 또는 FAIL')
        parts.append('<article><h2>' + html.escape(ident) + '</h2><p class="' + ('ok' if target.get('verified_pass') else 'fail') + '">' + html.escape(status) + '</p>')
        parts.append('<p>원본 ' + html.escape(data['source_reference_run']) + ' · ' + html.escape(str(source.get('biome', '?'))) + ' · ' + str(source.get('width', '?')) + '×' + str(source.get('height', '?')) + ' → 결과 ' + html.escape(str(primary_run)) + ' · ' + html.escape(str(profile.get('biome', '?'))) + ' · ' + str(profile.get('size', '?')) + '</p><div class="grid">')
        parts.append(embedded_image(source['images'].get('early_true'), data['source_reference_run'] + ' 실제 99999 true PNG'))
        parts.append(embedded_image(source['images'].get('map_png'), data['source_reference_run'] + ' 실제 PostInit true PNG'))
        parts.append(embedded_image(target.get('images', {}).get('map_png'), str(primary_run) + ' 실제 PostInit true PNG'))
        parts.append('</div><details><summary>실제 default PNG와 phase/필드 진단</summary><div class="grid">')
        for path, label in [(source['images'].get('early_default'), '원본 99999 default'), (source['images'].get('map_default_png'), '원본 PostInit default'), (target.get('images', {}).get('map_default_png'), '결과 PostInit default')]:
            parts.append(embedded_image(path, label))
        parts.append('</div><pre>' + html.escape(json.dumps({'phase': source.get('phase_png_comparison'), 'roof_safety': source.get('roof_counts'), 'metrics': target.get('metrics'), 'reasons': target.get('reasons'), 'native_audit': target.get('native_audit')}, ensure_ascii=False, indent=2)) + '</pre></details></article>')
    for label, groups in [('다른 바이옴/크기 adaptive 보존', data['adaptive_preservation']), ('기존 core 생성 6개 보존', data['legacy_preservation']), ('추가 실제 causal 진단 · strict 결과를 그대로 보존', data['diagnostic_preservation'])]:
        parts.append('<h2>' + label + '</h2>')
        if not groups:
            parts.append('<p class="fail">실제 보존 비교 입력 없음. 확인으로 집계하지 않습니다.</p>')
        for pair in groups:
            for row in pair['records']:
                parts.append('<article><h3>' + html.escape(row['id']) + ' · 전체 raw/증거 ' + ('PASS' if row['pass'] else 'FAIL/미완료') + '</h3><p>' + html.escape(Path(row['old_run']).name + ' → ' + Path(row['new_run']).name) + '</p><p>terrain+PNG 동일: ' + str(row['terrain_png_equal']) + ' · 전체 geology 동일: ' + str(row['full_geology_equal']) + '</p><div class="grid">')
                for name in ('map_png', 'map_default_png'):
                    images = row['images'].get(name, [None, None])
                    parts.append(embedded_image(images[0], '기존 실제 native ' + name))
                    parts.append(embedded_image(images[1], '새 실제 native ' + name))
                parts.append('</div><details><summary>실제 biome/크기 · 전체 terrain/geology/PNG 증거</summary><pre>' + html.escape(json.dumps({'old_profile': row.get('old_profile'), 'new_profile': row.get('new_profile'), 'probe_dll_hash_equal_for_optional_diagnostic': pair.get('probe_dll_hash_equal'), 'files': row['files'], 'reasons': row['reasons']}, ensure_ascii=False, indent=2)) + '</pre></details></article>')
    parts.append('<h2>증거 및 설치 경계</h2><pre>' + html.escape(json.dumps({'summary': summary, 'boundary': data['boundary'], 'preparation_reasons': data['preparation_reasons'], 'runs': [{k: r[k] for k in ('run', 'run_ok', 'pass', 'reasons', 'result_cases', 'full_raw_map_captures', 'execution_checks', 'archived_dll_hashes')} for r in [data['source_run']] + data['exact_runs']]}, ensure_ascii=False, indent=2)) + '</pre><p>HTML 이미지 디코딩 검사와 브라우저 렌더 확인은 별개입니다. 생존 상태/save/pawn/plant/quest 동일성, 모든 타일 및 미관을 보장하지 않습니다.</p></html>')
    return ''.join(parts)


def comparison_png(data, path):
    os.environ['MPLCONFIGDIR'] = str(Path(path).resolve().parent / '.matplotlib-cache')
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    observations = {r['id']: r for r in data['source_observations']}
    run = data['exact_runs'][0]['run'] if data['exact_runs'] else None
    primary = {r['id']: r for r in data['records'] if r['run'] == run}
    figure, axes = plt.subplots(len(GL_IDS), 3, figsize=(11, 3.2 * len(GL_IDS)), layout='constrained')
    for z, ident in enumerate(GL_IDS):
        source, target = observations[ident], primary.get(ident, {})
        images = [source['images'].get('early_true'), source['images'].get('map_png'), target.get('images', {}).get('map_png')]
        labels = ['Source: step 99999', 'Source: PostInit', 'Exact: PostInit']
        for x, (image, label) in enumerate(zip(images, labels)):
            ax = axes[z, x]
            if image and Path(image).is_file():
                with Image.open(image) as png:
                    ax.imshow(np.asarray(png.convert('RGB')), interpolation='nearest')
            else:
                ax.text(.5, .5, 'QUARANTINED' if target.get('quarantined') else 'NO ACTUAL RESULT', ha='center', va='center', transform=ax.transAxes)
            ax.set_title(ident + '\n' + label, fontsize=10)
            ax.set_axis_off()
    figure.savefig(path, dpi=160)
    plt.close(figure)


def write_report(data, output):
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    document = review(data)
    (output / 'same-biome-evaluation.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    (output / 'same-biome-review.html').write_text(document, encoding='utf-8')
    comparison_png(data, output / 'same-biome-comparison.png')
    import re
    decoded = []
    for encoded in re.findall(r'src="data:image/png;base64,([^"]+)"', document):
        with Image.open(io.BytesIO(base64.b64decode(encoded, validate=True))) as image:
            image.load()
            if image.format != 'PNG':
                raise ValueError('Embedded actual PNG failed decoding')
            decoded.append(list(image.size))
    rendering = {'embedded_pngs_decoded': len(decoded), 'sizes': decoded, 'browser_render_verified': False,
                 'comparison_png': str(output / 'same-biome-comparison.png'), 'images_are_actual_native_captures': True}
    (output / 'same-biome-render-verification.json').write_text(json.dumps(rendering, indent=2), encoding='utf-8')
    summary = data['summary']
    text = ('# 같은 바이옴 actual static 결과\n\n'
        f"원본 {summary['source_total']}개 전체 중 엄격 native admission {summary['primary_native_admission_passed']}개, "
        f"증거 포함 {summary['primary_verified_passed']}개. 검증 가능한 원본 {summary['source_available_for_verification']}개, "
        f"격리 {summary['source_quarantined']}개이며 격리 사례도 전체 분모에 남긴다.\n\n"
        f"다른 biome adaptive 보존 {summary['adaptive_preserved']}/{summary['adaptive_compared']}, "
        f"기존 core 보존 {summary['legacy_preserved']}/6. "
        f"증거 번들 확인: {summary['bundle_verified']}. 원본 8개 전체 exact 목표 달성: {summary['source_all_eight_exact_goal_met']}.\n\n"
        f"실제 native PNG {len(decoded)}개를 HTML에 포함하고 디코딩했다. 브라우저 렌더는 미검증이다. "
        'PNG는 static 필드/edifice/roof 안전 검사를 대신하지 않는다. 등록·인덱스 확정은 별도이며 제품 추천 UI는 미연결이다. '
        'save/pawn/plant/quest의 생존 상태, 모든 타일 및 미관의 동일성은 범위 밖이다.\n')
    text += '\n| 원본 | 실제 결과 런 | 필드 일치 | strict native/완료 | 격리 |\n|---|---|---|---|---|\n'
    primary = data['exact_runs'][0]['path'] if data['exact_runs'] else None
    for row in data['records']:
        if row['target_run_path'] == primary:
            text += f"| {row['id']} | {row['run']} | {row['fidelity_pass']} | {row['admission_pass']} | {row['quarantine_reason'] or ''} |\n"
    text += '\n원본 99999 → PostInit PNG는 아래와 같이 실제 두 capture를 직접 비교한 진단이며 원본을 바꾸거나 같은 phase라고 가정하지 않았다.\n\n'
    text += '| 원본 | true 차이 픽셀 | default 차이 픽셀 | native/projected 미지지 collapsing roof |\n|---|---:|---:|---|\n'
    for row in data['source_observations']:
        phase, roofs = row.get('phase_png_comparison', {}), row.get('roof_counts', {})
        text += f"| {row['id']} | {phase.get('map_png', {}).get('mismatch_pixels', '?')} | {phase.get('map_default_png', {}).get('mismatch_pixels', '?')} | {roofs.get('native_unsupported_collapsible', '?')}/{roofs.get('projected_unsupported_collapsible', '?')} |\n"
    text += '\n| 보존 대조 | 사례 | terrain+PNG 동일 | 전체 geology 동일 | 전체 raw/증거 PASS | 변경 필드 |\n|---|---|---|---|---|---|\n'
    for pair in data['adaptive_preservation'] + data['legacy_preservation'] + data['diagnostic_preservation']:
        for row in pair['records']:
            changed = sorted({field for metrics in row['files'].values() for field in metrics.get('changed_fields', [])})
            text += f"| {Path(row['old_run']).name}→{Path(row['new_run']).name} | {row['id']} | {row['terrain_png_equal']} | {row['full_geology_equal']} | {row['pass']} | {', '.join(changed)} |\n"
    text += '\n전체 raw가 다르면 terrain/PNG가 같아도 strict 보존 PASS로 집계하지 않는다. 과거 런과 추가로 확보한 동일 환경 causal 대조는 각 pair로 표시되며 자동으로 실패 결과를 제외하지 않는다.\n'
    (output / 'same-biome-report.md').write_text(text, encoding='utf-8')
    return rendering


def assignments(values):
    return dict(value.split('=', 1) for value in values)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', type=Path, required=True)
    parser.add_argument('--exact-runs', nargs='+', required=True)
    parser.add_argument('--adaptive', action='append', default=[], metavar='OLD_RUN=NEW_RUN')
    parser.add_argument('--legacy', action='append', default=[], metavar='OLD_RUN=NEW_RUN')
    parser.add_argument('--diagnostic', action='append', default=[], metavar='OLD_RUN=NEW_RUN')
    parser.add_argument('--boundary', type=Path)
    parser.add_argument('--dll', action='append', default=[], metavar='NAME=ABSOLUTE_DLL_PATH')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    data = collect(args.folder, args.exact_runs, list(assignments(args.adaptive).items()),
                   list(assignments(args.legacy).items()), args.boundary, assignments(args.dll), list(assignments(args.diagnostic).items()))
    rendering = write_report(data, args.output)
    print(json.dumps({'summary': data['summary'], 'rendering': rendering}, ensure_ascii=False))


if __name__ == '__main__':
    main()
