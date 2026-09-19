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
        'Development preview: cumulative text edits, native material fills, positioned ruins and ancient dangers.\n'
        'Precise circles/stars/hearts by default; request natural/irregular outlines to soften their geometry.\n'
        'Preserve world rivers and shores; remove variants while keeping ordinary connections.\n'
        'Add inland lakes/oases and loaded landmark features only when their biome and generator requirements match.\n'
        'Native hot springs can also be added on flat inland tiles. Enabled mods supply the feature and material catalog automatically.\n'
        'Enable this package instead of MapGen AI. Test in a new world.\n'
        'Fill regions with real Odyssey lava or other loaded permanent terrains. Place simple ruined walls/floors inside an area.\n'
        'Place structures near rivers, water, mountains or inner region edges with minimum gaps; rotate simple ruins in 90-degree steps.\n'
        'Native ancient temples follow game content and difficulty rules. Their orange preview outline reserves space; interiors generate in the full map.\n'
        'Fill a requested percentage of usable cells inside an existing shape or enclosed mountain ring.\n'
        'Connect explicit dry passage waypoints with a width in map cells; remaining obstructions are reported.\n'
        'Mountain exits cut only mountain cells and preserve open ground; explicitly request a soil path across the entire route to paint the plains.\n'
        'Combine ring mountains, exits, interior soil fractions and ruins. Ruins can reference an enclosed interior and avoid the complete planned passage route.\n'
        'Describe calderas, open basins and canyons; edit their interiors and exits using existing terrain components. This is not a Geological Landforms generator integration.\n'
        'Ask for three distinct landscape options, or complementary ideas for your edited map. Validated candidates automatically show local Map Preview images; click to enlarge and select by number or button without another model call.\n'
        'Refine an option by number before selecting it; only its preview refreshes. Natural passage edges preserve the route and minimum clear width; precise edges remain the default.\n'
        'Image input, interpretation and generation are paused, and the image button is hidden. Stored image data is retained but inactive.\n'
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
            '도넛 산 안에 70%만 비옥한 토양처럼, 기존 영역의 사용 가능한 칸 수를 기준으로 일부만 채울 수 있습니다.\n'
            '중앙에서 남쪽 끝까지 최소8칸 폭의 마른 통로처럼, 연결 지점과 실제 칸 수로 길을 지정할 수 있습니다. 남은 장애물은 안내합니다.\n'
            '산에 출구를 뚫는 요청은 산 부분만 깎고 평지를 유지합니다. 평지에도 흙길을 원하면 전체 경로에 길을 이어달라고 요청하세요.\n'
            '고리 산·출구·내부 비옥토 비율·폐허를 함께 요청하고 폭·비율·개수만 따로 수정할 수 있습니다. 폐허는 산에 둘러싸인 빈 내부를 참조하고 지정한 통로 전체를 피합니다.\n'
            '칼데라·열린 분지·협곡의 구성 안내를 보강했습니다. 내부 평지 확장·출구 변경·여러 분지 중 하나만 수정하거나 삭제할 수 있습니다. GL 원본 생성기 연동은 아닙니다.\n'
            '추천은 기본적으로 서로 다른 지형3개, 편집 중에는 기존 맵에 어울리는 보완안을 제안합니다. 후보 지도 미리보기를 자동으로 표시하며 클릭하면 확대됩니다. 로컬 렌더와 번호/버튼 선택은 추가 모델 호출이 없습니다.\n'
            '선택 전에는 3번 통로만 자연스럽게처럼 번호를 지정해 후보를 수정할 수 있습니다. 해당 그림만 갱신하며, 자연스러운 통로는 경로와 최소 폭을 유지하고 가장자리만 확장합니다. 반듯한 통로는 기본값입니다.\n'
            '현재 이미지 입력·해석·팔레트·생성 효과는 일시 중단이며 버튼도 숨깁니다. 저장된 이미지 데이터는 보존합니다.\n'
            '원형/별/하트는 정확한 형태이며, 자연스러운/울퉁불퉁한 윤곽을 요청하면 선택적으로 굴곡을 추가합니다.\n'
            '살짝/많이로 강도를 바꾸거나 다시 정확한 원형으로 요청해 되돌릴 수 있습니다.\n'
            '월드 강·해안 연결을 보존합니다. 삼각주/피오르드만 제거하면 일반 강/해안이 남습니다.\n'
            '내륙 해안은 만들 수 없으며, 호수·오아시스·확장 모드 특징은 바이옴과 실제 생성 조건에 맞으면 추가할 수 있습니다.\n'
            '기본 온천은 평지에도 직접 추가할 수 있습니다. 강·해안 충돌 제한은 유지합니다.\n'
            '모드 특징과 재료는 활성 모드에서 자동으로 읽습니다. VLE의 비옥한 화산 토양 등을 사용하려면 VLE와 필수 선행 모드를 활성화한 뒤 재시작해야 합니다.\n'
            '용암은 Odyssey의 실제 깊은 용암입니다. 식은 용암·화산암·자갈 등 활성 영구 지형도 채울 수 있습니다.\n'
            '섬/영역/위치 안에 작은 폐허나 게임 기본 고대 위협을 배치할 수 있습니다.\n'
            '강가·물가·산기슭·섬 안쪽 가장자리와의 거리/방향, 구조물 사이 최소 간격을 지정할 수 있습니다.\n'
            '단순 폐허는 90도 단위 회전이 가능합니다. 고대 위협은 회전 미지원이며 가로·세로15~20칸, 계획별1~2개, 전체최대4개입니다.\n'
            '건물 전체 면적과 공간을 검사하며, 부족하면 위치 지정 구조물을 일부만 놓지 않고 실패를 표시합니다.\n'
            '미리보기는 폐허 벽 계획과 고대 위협의 주황색 예약 테두리를 보여 줍니다. 실제 건물에 따라 최종 위치가 달라질 수 있습니다.\n'
            '고대 위협 내부 내용물은 실제 맵에서 게임의 무작위/DLC/난이도 규칙에 따라 생성됩니다. 임의 모드·퀘스트 단지는 미지원입니다.\n')
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
