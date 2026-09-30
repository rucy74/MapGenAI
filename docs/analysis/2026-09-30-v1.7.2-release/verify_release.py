"""Read-only release comparison; keeps source, install and Steam download evidence separate."""
from pathlib import Path
import datetime
import hashlib
import json
import re
import sys
import zipfile

repo = Path(__file__).resolve().parents[3]
output = Path(__file__).resolve().parent
workspace = repo.parent.parent
evidence = workspace / 'agents/main/log/2026-09-30-2057-mapgenai-evidence'
package = workspace / 'work/mapgenai-packages/2026-09-30-entry-button-v1.7.2/release/MapGenAI.zip'
normal = Path('G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI')
subscribed = Path('G:/SteamLibrary/steamapps/workshop/content/294100/3685385453')
expected_dll = '9a792fad321743582ee767548082b0ae72d23643d4f8e26358a42bee5b1052b5'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def tree(root):
    return {p.relative_to(root).as_posix(): sha(p.read_bytes())
            for p in root.rglob('*') if p.is_file()}


def matches(candidate, expected):
    return candidate == expected


with zipfile.ZipFile(package) as z:
    assert z.testzip() is None
    expected = {p.removeprefix('MapGenAI/'): sha(z.read(p)) for p in z.namelist()
                if p.startswith('MapGenAI/') and not p.endswith('/')}
assert len(expected) == 9
assert expected['Assemblies/MapGenAI.dll'] == expected_dll
# A changed DLL must make the same equality check fail.
broken = dict(expected)
broken['Assemblies/MapGenAI.dll'] = 'deliberately-different'
assert not matches(broken, expected)
copies = {}
for label, root in [('repository_dist', repo / 'dist'), ('installed', normal), ('subscribed', subscribed)]:
    actual = tree(root)
    id_path = 'About/PublishedFileId.txt'
    if label != 'repository_dist':
        assert (root / id_path).read_text(encoding='utf-8').strip() == '3685385453'
        actual.pop(id_path)
    assert matches(actual, expected), f'{label}: files differ'
    copies[label] = {'payloadFiles': len(actual), 'matchesPackage': True,
                    'dllSha256': actual['Assemblies/MapGenAI.dll']}

upload = json.loads((evidence / 'steam-upload.json').read_text(encoding='utf-8-sig'))
assert upload['success'] and upload['result'] == 'k_EResultOK'
assert not upload['ioFailure'] and not upload['legalAgreementRequired']
assert upload['returnedItem'] == '3685385453'
preserved = ['title', 'descriptionSha256', 'tags', 'visibility', 'previewSize']
assert all(upload['before'][key] == upload['after'][key] for key in preserved)
public = json.loads((evidence / 'public-item-after.json').read_text(encoding='utf-8-sig'))['response']['publishedfiledetails'][0]
assert public['result'] == 1 and public['publishedfileid'] == '3685385453'
assert public['hcontent_file'] == '4012711468297338724'
assert int(public['time_updated']) == 1790769943 and int(public['file_size']) == 911208
assert sum(p.stat().st_size for p in normal.rglob('*') if p.is_file()) == int(public['file_size'])

runtime = json.loads((output / 'installed-site-off/result.json').read_text(encoding='utf-8-sig'))
launch = json.loads((output / 'installed-site-off/launch.json').read_text(encoding='utf-8-sig'))
assert launch['sourceDllSha256'].lower() == expected_dll
assert runtime['ok'] and runtime['dryRun'] and runtime['paidCallsThisRun'] == 0
assert runtime['entryAudit']['startingSiteScreen']
assert runtime['mapPreviewToolbarOff']['toolbarOpenAfterRefresh'] is False
assert runtime['entryAudit']['buttonDrawCalls'] > 0
assert not runtime['entryAudit']['windowsOverlappingButton']
assert runtime['entryAudit']['click']['dialogOpenedByClick']
cleanup = json.loads((output / 'installed-site-off/cleanup.json').read_text(encoding='utf-8-sig'))
assert cleanup['archived'] and cleanup['originalAbsent']
assert not Path(cleanup['mod']).exists() and Path(cleanup['destination']).is_dir()

# The runtime loads model settings; ensure the files intended for Git contain no API key strings.
key_detector = re.compile(r'AIza[0-9A-Za-z_-]{20,}|sk-(?:proj-)?[0-9A-Za-z_-]{20,}')
positive_controls = ['AIza' + 'A' * 35, 'sk-proj-' + 'b' * 30]
assert all(key_detector.search(value) for value in positive_controls)
inspected = [p for p in output.rglob('*') if p.is_file() and p.suffix.lower() in ('.md', '.json', '.log', '.xml', '.html', '.py', '.txt', '.cs', '.csproj')]
hits = [str(p.relative_to(output)) for p in inspected if key_detector.search(p.read_text(encoding='utf-8-sig', errors='replace'))]
with zipfile.ZipFile(output / 'installed-site-off/Player.log.zip') as logs:
    assert logs.namelist() == ['Player.log']
    raw_log = logs.read('Player.log')
    assert not key_detector.search(raw_log.decode('utf-8-sig', errors='replace'))
    local_log = output / 'installed-site-off/Player.log'
    if local_log.exists():
        assert local_log.read_bytes() == raw_log
assert not hits, f'API key strings detected in: {hits}'
report = {
    'verifiedAt': datetime.datetime.now().astimezone().isoformat(timespec='seconds'),
    'item': '3685385453', 'version': '1.7.2', 'copies': copies,
    'server': {'manifest': public['hcontent_file'], 'timeUpdated': public['time_updated'],
               'fileSize': int(public['file_size']), 'uploadResult': upload['result']},
    'preservedMetadata': preserved, 'changedDllNegativeControlRejected': True,
    'runtime': {'startingSite': True, 'toolbarOff': True, 'buttonVisible': True,
                'syntheticClickOpensDialog': True, 'paidCalls': 0, 'probeArchived': True},
    'secretScan': {'positiveControlsDetected': len(positive_controls), 'inspectedFiles': len(inspected), 'hits': hits},
    'limits': ['Real mouse input was not tested.', 'Non-default preview-window positions and sizes were not tested.']
}
(output / 'release-verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report, ensure_ascii=False, indent=2))
