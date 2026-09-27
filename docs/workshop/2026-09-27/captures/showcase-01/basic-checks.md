# 기본 6종 실게임 점검 — showcase-01

- 실행: `showcase-01` (2026-09-27 10:00, 격리 런타임 1.6.4871, 영어 UI, 1920×1080, UI 배율 1.5)
- 검사 대상 DLL: `dev/Assemblies/MapGenAI.dll` SHA256 `4908863336BEFAC9…` (출시 후보, `launch.json`의 `sourceDllSha256`)
- 모델: `gemini-3.8-flash` 실제 호출 11회 (`../model-call-ledger.json`, 상한 16)
- 타일: A = 281 (TemperateForest, Flat, 강 1개가 서→동으로 지나감, 바다·호수 이웃 없음) / B = 83 (TemperateForest, Flat, Coast, 서쪽 바다). 선택 규칙과 근거는 `tiles.json`

## 판정 방법

- **상태**: 각 단계 `NN-name.json`의 `stateBefore`/`stateAfter`/`changedFields`, 원시 모델 응답(`attempts[].responsesHandled`, `modelCalls[].rawModelText`)
- **미리보기**: Map Preview가 실제로 만든 250×250 맵의 텍스처(`NN-name-preview.png`, 북쪽이 위)와 같은 맵의 칸별 지형 덤프(`NN-name-preview-cells.txt`). 수치는 `../measure-previews.py`로 뽑아 `preview-measurements.json`에 저장
- PASS = 요청한 변화가 상태와 미리보기 양쪽에 있다. 보기 좋은지(미관)는 판정에 넣지 않았다.

## 판정

| 점검 | 판정 | 이유 |
|------|------|------|
| 03-ring (도넛 산) | PASS | 상태에 고리 산(바깥 r 0.35, 안쪽 r 0.20, 높이 0.95) + 안쪽 호수(r 0.12 물) + 중심→남쪽 가장자리 통로(폭 8)가 추가됐다. 미리보기의 암반 칸이 1,569 → 20,791로 늘었고, 중심에서 2° 간격으로 쏜 광선 중 암반을 만나지 않는 방향은 270–272°(정남) 하나뿐이다. 중심부의 고인 물 비율 0.987. **단서**: 이 타일의 월드 강(제품이 일부러 보존)이 고리 남쪽 부분과 통로를 가로질러, 남쪽 통로 외에 강물 수로가 고리 서·동쪽 벽을 지난다. 제품도 대화창에 "The requested dry passage footprint has 52 obstructed cells"와 "Fill cells excluded to preserve existing rivers, ocean or roads: 52"를 알렸다. |
| 04/05 (지정 위치 추가·이동) | PASS | 04: 호수 가운데에 흙 원(r 0.04)이 생기고 호수가 그 둘레의 물길로 바뀌었다. 미리보기에서 물에만 둘러싸인 육지 덩어리 465칸, 중심 (0.502, 0.503) = 호수 중심. 05: 섬 중심이 (0.5, 0.56)로 옮겨졌고, 미리보기 섬 372칸의 중심이 (0.502, 0.562)로 호수 중심보다 북쪽(약 15칸)이다. 여전히 사방이 물이다. 섬은 호수(z ≈ 0.38–0.62)의 북쪽 절반에 있고 북쪽 물가에 붙지는 않았다. |
| 06 (강 위치) | FAIL | 모델은 `river_position: "left"`를 보냈고 상태는 `riverXPosition` 0.5 → 0.2로 바뀌었다. 그런데 이 타일의 월드 강은 서→동으로 흐르므로 x 기준점을 옮겨도 강이 서쪽으로 가지 않는다. 미리보기 강 중심 x는 0.516 → 0.526으로 그대로이고, 강은 여전히 지도 전체를 가로지르며 오히려 고리 안 호수 한가운데를 지나게 됐다(중심 z 0.318 → 0.457). 대화창은 "Move the river toward the west."라고 답해 결과와 안내 문구가 어긋난다. |
| 07 (직선 강) | PASS | `straight_river: true`. 미리보기 강 칸들을 한 직선에 맞췄을 때 직선에서 벗어난 거리(RMS)가 6.97칸 → 1.73칸으로 줄었다(축 약 15°의 곧은 선). 06의 위치 문제는 그대로 이어져 고리와 호수를 가로지른다. |
| 08 (온천) | PASS | 타일 특징이 [River] → [HotSprings, River]로 바뀌었고, 미리보기에 HotSpring 지형이 0 → 843칸 생겼다. **부작용**: 요청하지 않은 강 이동이 함께 일어났다. 강 관련 상태 값은 하나도 바뀌지 않았는데(`changedFields` = `mutators`만) 생성 로그의 강 기준점이 (50, 101) → (50, 140)으로 바뀌어 강이 북쪽으로 옮겨졌다(미리보기 강 중심 z 0.486 → 0.643). 온천 웅덩이 둘레의 고리 산이 암반에서 바위 바닥(Granite/Marble/Limestone_Rough, +3,187칸)으로 낮아져 암반 칸이 21,199 → 18,015로 줄었다. |
| 13 (해안 방향) | PASS | 타일 B(83)에서 `coast_direction` auto → north. 미리보기 바다 칸 중심이 (0.101, 0.583)(서쪽) → (0.446, 0.922)(북쪽)로 옮겨졌고, 그림에서도 바다가 위쪽 가장자리를 따라 놓였다. |

## 6종 밖에서 발견한 것

- **09-road 도로가 실제로 놓이지 않았다.** 도로 계획은 상태(`localRoads`)에 저장됐지만 제품이 "No supported road and bridge route"를 알렸고 미리보기에 도로가 없다. 모델이 고른 경유점 (0.75, 0.3)이 고리 산 띠(중심에서 반지름 0.20–0.35) 안에 있다. 09 캡처는 실패 안내를 보여 주므로 도로 홍보 사진으로는 맞지 않는다. 14-map(09 상태로 실제 맵 생성)에서도 `Player.log`에 같은 도로 경로 실패가 기록됐고 화면에 도로가 없다.
- **영어 UI에 한국어 문장이 나온다.** 통로 막힘 안내(`dev/Source/MapGen/PassageGeneration.cs`, `AuthoringGeneration.Fail` 문구)와 도로 경로 실패 안내(`dev/Source/MapGen/RoadRouting.cs`)가 한·영 두 언어를 붙인 고정 문장이라, 03–10 캡처에 한국어가 보인다.
- **11-recommend는 모델 호출 2회**: 첫 응답을 제품이 표시 전에 거절("Missing composite operand")하고 자동 복구 요청 1회로 유효한 후보 3개를 받았다. 후보 미리보기 3장 모두 렌더링됐다.
- **12-refine은 2번 후보만 바꿨다**: 두 산줄기의 `edge_roughness` medium → high. 2번 텍스처 해시만 바뀌고 1·3번은 같다.
- **02-guide**는 모델 호출 없이 열리고 닫혔다(`guideClosedWithoutModelCall: true`).

## 한계

- 판정 수치는 Map Preview의 250×250 맵 기준이다. 14-map(실제 맵 생성)은 시각 확인만 했다.
- 06·08의 원인 코드는 찾지 않았다(판정만 함).
- 한 번의 실행 결과다. 같은 요청에 모델이 다르게 답할 수 있다.

---
## 작성 이력
- 2026-09-27 10:07 — 초안 작성 (showcase-01 결과 판정)
