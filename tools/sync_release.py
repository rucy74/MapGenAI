"""Promote a verified DEV archive to dist and a normal MapGenAI test archive.

Does not publish, install, rebuild C#, or change user settings.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def promote(dev_package: Path, output: Path):
    repo = Path(__file__).resolve().parents[1]
    archive = output / 'MapGenAI.zip'
    if archive.exists():
        raise SystemExit(f'Refusing to overwrite {archive}')
    payload = {}
    with zipfile.ZipFile(dev_package) as dev:
        if dev.testzip() is not None:
            raise SystemExit('Invalid DEV archive')
        manifest = json.loads(dev.read('MapGenAI-Dev/build.json'))
        if manifest['packageId'] != 'Choco.MapGenAI.Dev' or manifest['workingTreeDirtyAtPackaging']:
            raise SystemExit('Expected a clean, identified DEV build')
        paths = ['Assemblies/MapGenAI.dll'] + [f'Languages/{lang}/Keyed/MapGenAI.xml'
                 for lang in ['English', 'Korean', 'Japanese', 'ChineseSimplified']]
        for path in paths:
            data = dev.read('MapGenAI-Dev/' + path)
            if data != (repo / 'dev' / path).read_bytes():
                raise SystemExit(f'DEV archive differs from current development payload: {path}')
            payload[path] = data
        metadata = ET.fromstring(dev.read('MapGenAI-Dev/About/About.xml'))
    source = manifest['sourceCommit']
    subprocess.run(['git', 'merge-base', '--is-ancestor', source, 'HEAD'], cwd=repo, check=True)
    changed = subprocess.check_output(['git', 'diff', source, '--', 'dev/Source', 'dev/Languages',
                                      'dev/Defs', 'dev/Textures', 'dev/Patches'], cwd=repo)
    if changed:
        raise SystemExit('Development source differs from the verified DEV source commit')
    dll_hash = hashlib.sha256(payload['Assemblies/MapGenAI.dll']).hexdigest()
    if dll_hash != manifest['dllSha256']:
        raise SystemExit('DEV DLL does not match its build manifest')
    metadata.find('name').text = 'MapGen AI'
    metadata.find('packageId').text = 'Choco.MapGenAI'
    metadata.find('description').text = (
        'Describe and refine RimWorld maps in natural language, with live Map Preview.\n'
        'Includes cumulative edits, natural outlines, partial soil/material fills, positioned ruins, '
        'hot springs, local roads with automatic wooden bridges, and selectable recommendation previews.\n'
        'Recommendations can be dismissed, refreshed or collapsed to leave more room for chat.\n'
        'Current build targets RimWorld 1.6. Image input is temporarily disabled.\n'
        'Configure your provider and API key in Mod Settings. Enable MapGen AI without MapGen AI [DEV].'
    )
    versions = metadata.find('supportedVersions')
    versions.clear(); ET.SubElement(versions, 'li').text = '1.6'
    incompatible = metadata.find('incompatibleWith')
    if incompatible is None:
        incompatible = ET.SubElement(metadata, 'incompatibleWith')
    incompatible.clear(); ET.SubElement(incompatible, 'li').text = 'Choco.MapGenAI.Dev'
    payload['About/About.xml'] = ET.tostring(metadata, encoding='utf-8', xml_declaration=True)
    # Keep the existing release artwork; no Workshop identity or local configuration in the ZIP.
    payload['About/Preview.png'] = (repo / 'dist/About/Preview.png').read_bytes()
    release = dict(sourceCommit=source, dllSha256=dll_hash, packageId='Choco.MapGenAI',
                   gameVersion='1.6', promotedFrom='Choco.MapGenAI.Dev',
                   developmentArchiveSha256=hashlib.sha256(dev_package.read_bytes()).hexdigest(),
                   createdUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
    payload['build.json'] = (json.dumps(release, indent=2) + '\n').encode('utf-8')
    payload['README.txt'] = (
        'MapGen AI — RimWorld 1.6\n\n'
        'Harmony and Map Preview are required. Copy MapGenAI to RimWorld/Mods.\n'
        'Enable MapGen AI without MapGen AI [DEV]. Configure your provider in Mod Settings.\n'
        'This package contains the same DLL and four language files as the verified DEV build.\n'
        'Image input remains disabled. No API keys, saves or settings are included.\n\n'
        '현재 검증된 DEV와 같은 코드와 번역을 사용하는 일반 MapGen AI 패키지입니다.\n'
        'RimWorld 1.6 기준이며 Harmony와 Map Preview가 필요합니다.\n'
        '기존 폴더를 백업한 뒤 MapGenAI 폴더를 RimWorld/Mods에 복사하세요.\n'
        'MapGen AI [DEV]와 동시에 활성화하지 마세요.\n'
        '추천 그림/취소/재추천/접기, 자연스러운 지형, 부분 채움, 유적, 온천, 지역 도로와 자동 다리가 포함됩니다.\n'
        '이미지 입력은 계속 중단 상태입니다. Steam 업로드 여부와는 별개의 설치 패키지입니다.\n'
    ).encode('utf-8')
    dist = repo / 'dist'
    actual = {p.relative_to(dist).as_posix() for p in dist.rglob('*') if p.is_file()}
    if actual - set(payload):
        raise SystemExit('Unexpected files in dist; inspect them before changing the release')
    for path, data in payload.items():
        target = dist / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    output.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as package:
        for path, data in sorted(payload.items()):
            package.writestr('MapGenAI/' + path, data)
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None
        assert all(package.read('MapGenAI/' + path) == (dist / path).read_bytes() for path in payload)
    print(archive)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dev_package', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    promote(args.dev_package, args.output)
