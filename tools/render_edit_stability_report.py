"""Make a self-contained Korean review from native captures, not generated artwork."""
import base64
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "docs/analysis/2026-09-30-edit-stability"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def figure(run, filename, caption, wide=False):
    raw = (ROOT / run / filename).read_bytes()
    src = "data:image/png;base64," + base64.b64encode(raw).decode("ascii")
    return f'<figure class="{"wide" if wide else ""}"><button onclick="show(this.firstElementChild.src)"><img alt="{html.escape(caption)}" src="{src}"></button><figcaption>{html.escape(caption)}</figcaption></figure>'


def group(title, text, figures):
    return f'<section><h2>{title}</h2><p>{text}</p><div class="maps">{"".join(figures)}</div></section>'


def main():
    comparison = read(ROOT / "comparison.json")
    installed = read(ROOT / "installation-receipt.json")
    basic = read(ROOT / "installed-basic-runtime-final/result.json")
    assert comparison["ok"] and basic["ok"]
    content = group("1. 툴바를 꺼도 버튼을 누를 수 있음",
        "공개 v1.7.2 DLL 그대로, 시작 지점 화면에서 OS 마우스 입력으로 클릭했습니다. 합성 GUI 이벤트 0회, 유료 호출 0회. 사람의 물리 마우스 검사는 아닙니다.", [
            figure("os-entry-runtime", "00-world-entry.png", "툴바 OFF — AI MapGen 버튼 표시", True),
            figure("os-entry-runtime", "01-os-input-dialog.png", "OS 클릭 뒤 열린 대화창", True)])
    for title, prefix, message in [
        ("2-1. 동굴을 지워도 강·해안의 생성 위치 유지", "river", "수정 전에는 동굴 삭제만으로 강 중심이 [101,131]에서 [174,101]로 바뀌었습니다. 수정 후에는 동굴 삭제·대동굴 교체 모두 원래 강 중심과 강 경로가 유지됩니다."),
        ("2-2. 해안도 같은 기준 유지", "coast", "미리보기의 해안 물 칸은 원래 13,225칸에서 삭제 후 11,213칸으로 줄었습니다. 수정 후에는 13,225칸을 유지합니다. 실제 완성 맵에서도 해안 noise 62,500개 값이 그대로입니다. 동굴 변경으로 암석 바닥이 달라지면, 기본 게임의 바위 보존 규칙 때문에 최종 물 칸 수까지 같지는 않을 수 있습니다.")]:
        content += group(title, message, [
            figure("baseline-runtime-r5", f"captures/{prefix}-caves-preview.png", "원래 동굴이 있던 타일"),
            figure("baseline-runtime-r5", f"captures/{prefix}-removed-preview.png", "수정 전 — 동굴만 삭제했는데 물 위치가 변함"),
            figure("final-runtime-r9", f"captures/{prefix}-removed-preview.png", "수정 후 — 동굴 삭제, 물 위치 유지")])
    content += group("2-3. 다른 타일 미리보기 격리", "A 타일을 편집하는 중 B 타일을 눌러도, B 타일의 강은 바뀌지 않습니다. 전체 지형 해시가 원래 B 타일과 같습니다.", [
        figure("baseline-runtime-r5", "captures/other-before-preview.png", "B 타일 원본"),
        figure("baseline-runtime-r5", "captures/other-editor-active-preview.png", "수정 전 — A 설정이 B에 적용됨"),
        figure("final-runtime-r9", "captures/other-editor-active-preview.png", "수정 후 — B 원본 유지")])
    content += group("2-4. 추천 그림과 실제 적용의 생성 순서 일치", "추천 후보도 기본 게임과 같은 genOrder로 정렬합니다. 같은 후보를 선택하기 전과 적용한 뒤, 전체 지형과 미리보기 62,500픽셀이 일치했습니다.", [
        figure("final-runtime-r9", "captures/candidate-unapplied-preview.png", "선택 전 후보"),
        figure("final-runtime-r9", "captures/candidate-applied-preview.png", "선택 후 일반 미리보기 — 다른 픽셀 0개")])
    content += group("3-1. ‘강을 북쪽으로 옮겨줘’", "예전에 실패한 실제 모델 응답 <code>river_position: 0.85</code>를 그대로 재생했습니다. 방향이 자동인 강도 세계 타일의 연결 방향을 읽습니다. 강의 방향 설정이나 다른 지형을 바꾸지 않고 중심을 [174,101] → [174,212]로 옮겼습니다.", [
        figure("baseline-runtime-r5", "captures/north-before-preview.png", "이동 전"),
        figure("baseline-runtime-r5", "captures/north-after-preview.png", "수정 전 — 동서 좌표만 변경"),
        figure("final-runtime-r9", "captures/north-after-preview.png", "수정 후 — 북쪽 이동")])
    content += group("3-2. 산에 걸린 우회도로 경유점", "같은 실패 응답을 재생했습니다. 우회도로의 막힌 내부 경유점만 가까운 연결된 평지로 옮깁니다. 끝점·이미 가능한 경유점·정확한 직선 도로는 그대로이고 산을 깎지 않습니다. 이 사례는 미리보기와 완성 맵 모두 도로 1개, 막힌 경로 0칸입니다. 통행 가능한 고대 소화전을 장애물로 세던 오진도 수정했습니다.", [
        figure("baseline-runtime-r5", "captures/road-before-preview.png", "도로 요청 전"),
        figure("baseline-runtime-r5", "captures/road-after-preview.png", "수정 전 — 경로 실패, 도로 없음"),
        figure("final-runtime-r9", "captures/road-after-preview.png", "수정 후 — 산을 우회해 출구에 연결")])
    checks = "".join(f'<li>{html.escape(c["name"])}</li>' for c in comparison["checks"])
    content += f'''<section><h2>검증과 범위</h2><p>회귀 테스트 <strong>345 PASS / 0 FAIL</strong>. 실제 수정 전후 비교 <strong>{comparison['passed']} PASS / {comparison['failed']} FAIL</strong>. 미편집 강·해안과 온천 추가 4사례 × 미리보기·완성 맵 = 8결과의 전체 지형이 이전 DLL과 동일했습니다. 설치된 DEV DLL로 기본·저장·되돌리기·실제 도넛 등 <strong>{len(basic['checks'])}개 검사</strong>도 통과했습니다.</p>
    <details><summary>전후 비교 검사 {comparison['passed']}개</summary><ul>{checks}</ul></details>
    <p>외부 모드 특징이나 지형에 따라 난수 소비가 달라지는 일부 native 동굴 변형의 삭제는 아직 완전한 물 위치 보존을 보장하지 않습니다. 이 경우 기존 생성 경로와 경고를 유지합니다. 임의 모드 조합·모든 자연어 문장을 보장하는 검사는 아닙니다.</p>
    <p>우회 경유점 이동은 짧은 맵 변 길이의 12% 또는 도로 여유폭의 최소 범위까지입니다. 범위 밖·닫힌 지형·막힌 끝점은 실패를 알리고 기존 지형을 보존합니다.</p></section>
    <section><h2>설치 및 선택적 확인</h2><p><strong>MapGen AI [DEV]</strong> 설치 완료. 코드 commit <code>{installed['sourceCommit'][:7]}</code>. 공개 v1.7.2·일반판·Workshop·dist는 변경하지 않았고, 새 유료 호출은 0회입니다.</p>
    <p>추가로 확인하고 싶다면 본인 모드 조합에서 다음 두 문장만 시도하면 됩니다. 같은 타일에서 강 이동 전후와 도로를 살펴보세요.</p><ol><li>강을 북쪽으로 조금 옮겨 줘.</li><li>동쪽 가장자리에서 산의 남쪽 출구로 흙길을 이어 줘. 산은 우회해 줘.</li></ol>
    <details><summary>재현 자료와 한계</summary><p>원문 실패 응답은 fixtures/north-response.json, road-response.json. 비교 명령: <code>python -X utf8 tools/evaluate_edit_stability.py docs/analysis/2026-09-30-edit-stability/baseline-runtime-r5 docs/analysis/2026-09-30-edit-stability/final-runtime-r9 --output docs/analysis/2026-09-30-edit-stability/comparison.json</code></p><p>기본 생성 코드는 로컬 RimWorld 1.6.4871을 참조했습니다. 화면 이미지는 실제 RimWorld/Map Preview에서 캡처했으며 AI로 다시 그리거나 보정하지 않았습니다. 외부 모델 자문과 새 API 요청은 하지 않았습니다.</p></details></section>'''
    doc = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MapGen AI — 편집 안정성 검증</title>
    <style>:root{color-scheme:dark}*{box-sizing:border-box}body{margin:0;background:#12171d;color:#e4e9ed;font:16px/1.65 system-ui,sans-serif}main{max-width:1080px;margin:auto;padding:32px 20px}header{padding:20px 0 32px}h1{font-size:30px;line-height:1.3;margin:10px 0}h2{font-size:22px;line-height:1.4}p{color:#c6cfd8}section{border:1px solid #303b46;border-radius:14px;padding:22px;margin-bottom:22px;background:#19212a}.badge{display:inline-block;padding:4px 12px;border-radius:99px;background:#183e32;color:#9ce1bf;margin:4px}code{word-break:break-word;color:#9ccfff}.maps{display:flex;flex-wrap:wrap;gap:18px;justify-content:center}figure{margin:0;flex:1;min-width:230px;max-width:310px}figure.wide{max-width:none;min-width:100%}figure button{border:0;background:none;padding:0;width:100%;cursor:zoom-in}img{display:block;width:100%;image-rendering:pixelated;border-radius:5px}figure.wide img{image-rendering:auto}figcaption{font-size:14px;color:#c6cfd8;padding-top:9px}summary{cursor:pointer;color:#9ccfff}dialog{max-width:95vw;max-height:94vh;padding:8px;border:1px solid #526477;background:#151b22}dialog img{max-width:90vw;max-height:85vh;width:auto;object-fit:contain;image-rendering:pixelated}dialog::backdrop{background:#000b}dialog button{background:#314252;border:0;color:white;padding:7px 20px;cursor:pointer}a{color:#9ccfff}@media(max-width:600px){main{padding:16px 10px}section{padding:16px}h1{font-size:26px}.maps{gap:22px}figure{max-width:100%;flex:100%;min-width:0}figure img{max-width:400px;margin:auto}}</style>
    <main><header><span class="badge">DEV 설치 완료</span><span class="badge">새 API 호출 0회</span><h1>MapGen AI 편집 안정성 검증</h1><p>2026-09-30 · 버튼 확인 → 특징/미리보기 격리 → 강 위치/도로 수정<br>이미지를 누르면 확대합니다.</p></header>''' + content + '''</main><dialog id="zoom"><button onclick="this.parentElement.close()">닫기</button><img alt="확대 캡처"></dialog><script>function show(src){const d=document.getElementById('zoom');d.querySelector('img').src=src;d.showModal()}document.getElementById('zoom').addEventListener('click',function(e){if(e.target===this)this.close()})</script></html>'''
    (ROOT / "review.html").write_text(doc, encoding="utf-8")
    print("Wrote self-contained review.html from 19 native captures")


if __name__ == "__main__":
    main()
