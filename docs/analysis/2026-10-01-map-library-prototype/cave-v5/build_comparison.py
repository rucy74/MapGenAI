"""Compose existing actual native captures; no generated art or altered map pixels."""
import json
import pathlib
from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent


def main():
    report = json.loads((ROOT / 'cave-evaluation.json').read_text(encoding='utf-8'))
    font_path = 'C:/Windows/Fonts/malgun.ttf'
    font = ImageFont.truetype(font_path, 22)
    small = ImageFont.truetype(font_path, 17)
    title = ImageFont.truetype(font_path, 29)
    canvas = Image.new('RGB', (1320, 1220), '#101820')
    draw = ImageDraw.Draw(canvas)
    draw.text((30, 20), '동굴 복제 전후 — 실제 Map Preview 지도', font=title, fill='#edf3f5')
    draw.text((30, 64), '가운데·오른쪽은 같은 타일·seed·제품 상태. 왼쪽은 별도 GL 원본.', font=small, fill='#bbc8d0')
    for row, (ident, name) in enumerate((('gl-cave-entrance', '산자락의 동굴 입구'),
                                       ('gl-secluded-valley', '동굴로 이어진 외딴 골짜기'))):
        record = next(r for r in report['records'] if r['id'] == ident and r['run'] == 'cave-a-native-r3')
        run = ROOT / record['run']; source = ROOT / record['source_reference_run']
        y = 108 + row * 520
        draw.text((30, y), name + ' · 전체 검사는 탈락', font=font, fill='#f3cb98')
        panels = ((source / (ident + '-map.png'), '원본 GL · source-native-r2'),
                  (run / (ident + '-rock-only-map.png'), '동굴 데이터 없이 바위만 복제'),
                  (run / (ident + '-map.png'), '높이·동굴·자연 지붕 함께 복제'))
        for col, (path, label) in enumerate(panels):
            x = 30 + col * 430
            draw.text((x, y + 42), label, font=small, fill='#d2dce3')
            with Image.open(path) as image:
                image = image.convert('RGB').resize((400, 400), Image.Resampling.NEAREST)
                canvas.paste(image, (x, y + 76))
        missing = record['metrics']['passage']['missing_cells']
        unsafe = record['final_audit']['unsafe_roof_cells']
        draw.text((30, y + 482), f'새 결과: 고도·동굴 수치 차이 0 / 통로 {missing}칸 막힘 / 무지지 지붕 {unsafe}칸 → 후보 제외',
                  font=small, fill='#dfb0a4')
    draw.text((30, 1170), '그림은 실제 생성 지도. 지붕·보행 연결은 별도 원자료 검사로 확인하며, 미관 승인과는 구분합니다.',
              font=small, fill='#bbc8d0')
    canvas.save(ROOT / 'comparison.png')


if __name__ == '__main__':
    main()
