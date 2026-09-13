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
        'Development preview: cumulative text edits, native material fills and positioned ruins.\n'
        'Precise circles/stars/hearts by default; request natural/irregular outlines to soften their geometry.\n'
        'Preserve world rivers and shores; remove variants while keeping ordinary connections.\n'
        'Add inland lakes/oases and loaded landmark features only when their biome and generator requirements match.\n'
        'Enable this package instead of MapGen AI. Test in a new world.\n'
        'Fill regions with real Odyssey lava or other loaded permanent terrains. Place simple ruined walls/floors inside an area.\n'
        'Image input, interpretation and generation are temporarily paused; stored image data is retained but inactive.\n'
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
            '현재 이미지 입력·해석·팔레트·생성 효과는 일시 중단입니다. 저장된 이미지 데이터는 보존합니다.\n'
            '원형/별/하트는 정확한 형태이며, 자연스러운/울퉁불퉁한 윤곽을 요청하면 선택적으로 굴곡을 추가합니다.\n'
            '살짝/많이로 강도를 바꾸거나 다시 정확한 원형으로 요청해 되돌릴 수 있습니다.\n'
            '월드 강·해안 연결을 보존합니다. 삼각주/피오르드만 제거하면 일반 강/해안이 남습니다.\n'
            '내륙 해안은 만들 수 없으며, 호수·오아시스·확장 모드 특징은 바이옴과 실제 생성 조건에 맞으면 추가할 수 있습니다.\n'
            '용암은 Odyssey의 실제 깊은 용암입니다. 식은 용암·화산암·자갈 등 활성 영구 지형도 채울 수 있습니다.\n'
            '섬/영역/위치 안에 손상된 벽과 바닥의 작은 폐허를 배치할 수 있습니다. 고대 위협 시설 위치는 아직 미지원입니다.\n'
            '건물 전체 면적과 공간을 검사하며, 부족하면 위치 지정 폐허를 일부만 놓지 않고 실패를 표시합니다.\n'
            '미리보기는 벽의 배치 계획을 보여 줍니다. 실제 맵의 다른 건물에 따라 위치가 달라질 수 있어 생성 때 다시 검사합니다.\n')
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
