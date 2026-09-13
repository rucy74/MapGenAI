"""Package the built development mod separately, without touching dist or the game."""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def package(output: Path) -> Path:
    repo = Path(__file__).resolve().parents[1]
    dll = repo / 'dev/Assemblies/MapGenAI.dll'
    if not dll.is_file():
        raise SystemExit('Build dev/Source/MapGenAI.csproj before packaging.')
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    status = subprocess.check_output(['git', 'status', '--porcelain'], cwd=repo, text=True)
    root = ET.parse(repo / 'dev/About/About.xml').getroot()
    root.find('name').text = 'MapGen AI [DEV]'
    root.find('packageId').text = 'Choco.MapGenAI.Dev'
    versions = root.find('supportedVersions')
    versions.clear()
    ET.SubElement(versions, 'li').text = '1.6'
    ET.SubElement(ET.SubElement(root, 'incompatibleWith'), 'li').text = 'Choco.MapGenAI'
    root.find('description').text = (
        'Development preview: cumulative terrain edits and image terrain workspace.\n'
        'Enable this package instead of MapGen AI. Test in a new world.\n'
        'Image interpretation is experimental; region correction currently changes terrain labels only.\n'
        'Gemini default: gemini-3.8-flash. Requires your own provider configuration.'
    )
    manifest = {
        'sourceCommit': revision,
        'workingTreeDirtyAtPackaging': bool(status.strip()),
        'createdUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'dllSha256': hashlib.sha256(dll.read_bytes()).hexdigest(),
        'packageId': 'Choco.MapGenAI.Dev',
        'testedGameVersion': '1.6',
        'preservedTag': 'v1.6',
    }
    output.mkdir(parents=True, exist_ok=True)
    archive = output / 'MapGenAI-Dev.zip'
    if archive.exists():
        raise SystemExit(f'Refusing to overwrite an existing package: {archive}')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        prefix = 'MapGenAI-Dev/'
        z.writestr(prefix + 'About/About.xml', ET.tostring(root, encoding='utf-8', xml_declaration=True))
        z.write(dll, prefix + 'Assemblies/MapGenAI.dll')
        for path in sorted((repo / 'dev/Languages').rglob('*.xml')):
            ET.parse(path)
            z.write(path, prefix + path.relative_to(repo / 'dev').as_posix())
        z.writestr(prefix + 'build.json', json.dumps(manifest, indent=2))
        z.writestr(prefix + 'README.txt',
            'MapGenAI 개발 미리보기 / RimWorld 1.6\n\n'
            '압축 안의 MapGenAI-Dev 폴더를 RimWorld/Mods 아래에 복사하세요.\n'
            'Harmony와 Map Preview가 필요합니다. 기존 MapGen AI를 끄고 MapGen AI [DEV]를 켭니다.\n'
            '새 테스트 월드에서 시작하고 모드 설정에 API 키를 직접 입력하세요.\n'
            '기존 배포판·설정·세이브는 이 패키지에 포함하지 않습니다.\n'
            'Image terrain에서 배치가 읽히는 Map Preview/탑다운/AI 맵 PNG/JPEG를 불러옵니다. 설명은 선택 사항입니다.\n'
            '기본 해석은 원본 윤곽을 보존하고 AI가 지형 종류를 분류합니다. 높이 우선 옵션으로 월드 지형과의 적용 순서를 선택합니다.\n'
            '분류도에서 영역 클릭 후 팔레트/채팅으로 지형 종류를 바꾸고 적용합니다.\n'
            '이 분류도는 실제 맵 미리보기가 아닙니다. 적용 후 Map Preview를 확인하세요.\n'
            '외곽선 이동·브러시는 미지원이며, 비슷한 색상/가는 지형/삽입 그림은 오해할 수 있습니다.\n')
    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None
        assert len(z.namelist()) == len(set(z.namelist()))
        assert hashlib.sha256(z.read('MapGenAI-Dev/Assemblies/MapGenAI.dll')).hexdigest() == manifest['dllSha256']
        assert not any('PublishedFileId' in p or 'dev_config' in p for p in z.namelist())
    (output / 'package-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print(archive)
    return archive


if __name__ == '__main__':
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument('output', type=Path)
    package(args.parse_args().output)
