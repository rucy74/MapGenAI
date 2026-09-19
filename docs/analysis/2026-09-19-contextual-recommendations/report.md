# 타일에 맞춘 추천과 숫자 없는 시작 문구

2026-09-19 / 기준 dev `9eea3f6f5862b37d0cb33cef33072f1f8f3348a2`. 복귀 태그 `dev-before-contextual-recommendations-2026-09-19`.

사용자가 서로 다른 타일에서도 비슷한 도넛 산·측면 산과 호수·직사각형 협곡이 반복된다고 지적했다. 저장된 세 맵을 꺼내는 구현은 아니었다. 매번 모델이 추천을 생성하지만, 추천 지침과 지형 예제에 제시한 구성이 강하게 반복됐다. 실제 타일의 바이옴·산악도·강/해안·특징 정보는 이전부터 전달되고 있었다. [사용자 로그에서 추출한 두 원문](reported-recommendations.json).

## 변경

- 일반 추천은 선택 타일의 산악도·건조함·물 연결과 기존 지형을 반영하고, 넓게 연결된 정착 공간을 남기도록 지침을 수정했다. 평지의 거대한 산 덩어리, 닫힌 고리, 좁은 틈만 있는 협곡을 기본 추천에서 피한다. 기존에 만든 맵에는 작은 보완을 제안한다.
- 지하 맵 생성용 `UndergroundCave`를 일반 지상 맵 추천에 새로 넣지 않도록 했다. 기존 특징이나 명시적 동굴 편집의 처리 규칙은 유지한다.
- 정북/정남을 잘못된 방향 필드에 넣거나 구조물을 지형 도형 목록에 넣지 않도록 스키마 안내를 보강했다.
- 시작 예시: **“이 타일에 어울리는 지형 추천해 줘”**, 영어 **“Suggest landscapes for this tile.”** 후보 수정 예시는 “2번을 좀 더 자연스럽게 해 줘”. 코드 내장 문자열과 한·영 XML이 일치한다.
- 후보 개수는 여전히 **최대 3개**, 보통 3개이며 1~2개 요청도 지원한다. 4개 지원은 이번 범위에 없다. 생성 엔진·병합·저장·후보 선택·Undo·모델 선택 UI는 수정하지 않았다. 정확한 원형/직사각형/직선은 명시적으로 요청할 수 있다.

이는 모델에 주는 추천 지침 개선이다. 결과의 미관이나 건축 가능 공간을 자동 점수화해서 다시 뽑는 품질 필터는 구현하지 않았다. 실제 결과에도 둥근 토양 패치가 남아 있다. 산악 타일 세 번째 후보는 산 비율이 높아 여전히 난도가 높으며, 모든 추천이 사용자 취향에 맞는다고 주장하지 않는다.

## 실제 검증

[그림 비교](gallery.html)에서 테스트에 사용한 모든 추천을 볼 수 있다. 이미지의 지형 색은 시험 환경에서 얻은 실제 결과이며 사용자 모드 조합과 다를 수 있다. Map Preview는 일부 유적을 표시하지 않으므로 완성 맵 그림도 함께 제공한다.

- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **150 PASS / 0 FAIL**. [출력](tests-final.log).
- `dotnet build dev/Source/MapGenAI.csproj --no-restore --nologo`: 최종 제품 오류 0 / 기존 버전 경고 1. [출력](build-r3.log). 최종 DLL SHA256 `f265b99feb95e86a289dedefed4a5aa518af7177db9fbde5f9b5c023dbc1eda2`.
- 실제 Gemini 3.8 요청 **17회**. 처음 8회 중 한국어 평지 방향 필드와 해안 구조물 필드 오류 2건은 기존 검증기가 거부했다. 보강 후 8회 응답은 모두 설정 적용에 통과했지만 평지 후보의 `UndergroundCave`가 실제 생성에서 100회 시도 초과를 일으켰다. 최종 지침으로 평지 1회만 새로 요청했다. 실패 원문과 로그를 보존했다.
- 최종 선택 원문은 **한국어 평지 1응답만 마지막 지침**, 나머지 7응답은 직전 지침의 원문이다. [출처 대조](accepted-response-sources.json). 전부 마지막 지침에서 새로 뽑은 결과로 집계하지 않는다.
- 최종 DLL에서 한·영 **후보 18개의 저장된 설정 적용·선택 전 무변경·Undo** 확인. 한국어 평지·사막·산악·해안·기존 도넛 맵, 영어 평지 추천이다.
- **완성 맵 20개 + 실제 배경 Map Preview 20개**. 추천 18개와 명시적 완전 원형 도넛/12칸 직선 협곡 2개를 포함한다. 기존 도넛/통로/폐허 설정 보존, 내부 토양 70% 오차 0.1%p 미만, 명시적 협곡의 12칸 폭·마른 통로 연결 확인. 최종 맵 로그에 지하 동굴 시도 초과가 재발하지 않았다.
- 한국어 평지 후보의 마른 보행 가능 칸은 약 **79.5~83.4%**, 사막 **70.4~93.4%**, 산악 **31.2~46.5%**였다. 이 비율 자체는 미관·모든 건축 조건·전체 연결성을 보증하지 않는다. 산악 타일은 여전히 많은 산을 보존한다.
- `python -X utf8 tools/evaluate_contextual_recommendations.py`: 원문 선택·Undo, 맵/미리보기 결과, 기존 도형/구조물/70% 보존, 두 명시적 기하 대조, 한영 문구 대조 통과. [전체 평가](evaluation.json).

## 시험 도구의 실패와 재검증

`LandformSuiteProbe`에 실제 바이옴/산악도 선택과 `previewOnly`를 추가했다. 처음 산악 타일 조회는 “특징 없는 산악”이 시험 월드에 없어 실패했고, 원래 특징을 허용하여 다시 캡처했다. 이 실패는 `context-ko`에 남아 있다.

다중 완성 맵 생성 후 미리보기의 다음 작업/시간 제한이 진행되지 않는 시험 도구 대기 상태가 총 6실행에서 발생했다. `maps-ko-1..3`와 `accepted-maps-ko-1..3`의 완성 맵 단계는 끝났지만 미리보기 단계는 끝나지 않았다. 소유 마커·PID를 확인하고 해당 시험만 종료했으며 `interrupted.json`을 보존했다. 이를 미리보기 성공으로 집계하지 않았다.

시험 도구의 후속 작업을 `Root.Update`에서도 진행하도록 고친 뒤, 최종 DLL의 완료된 한국어 맵 16개에 대해 `previews-ko-1..3`으로 미리보기만 다시 실행해 전부 완료했다. 영어 4개 맵과 미리보기는 `accepted-maps-en-1..3`에서 함께 완료했다. 시험 도구 최종 SHA256 `ec1ee196169e930292aa7e09752f1f3d1e8c053348e07521f43451da89db6821`; 제품에는 시험 도구를 포함하지 않는다.

실행 예시(새 출력 디렉터리를 사용):

```powershell
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore --nologo
tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-19-contextual-recommendations/cases-ko.json -LandformReplies docs/analysis/2026-09-19-contextual-recommendations/accepted-provider-ko -Language Korean -Output <새절대경로>
tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-19-contextual-recommendations/maps-input-accepted-ko/cases-1.json -LandformReplies docs/analysis/2026-09-19-contextual-recommendations/maps-input-accepted-ko/1 -Language Korean -Output <새절대경로>
```

맵 입력은 후보 1~3 및 ko/en 조합으로 보관했다. 선택 검증은 전체 추천 원문, 개별 맵 생성은 원문의 해당 후보 설정을 그대로 추출한 입력을 사용한다. 실제 테스트는 고정 월드 `mapgenai-landforms-v1` / 250×250 / 저장된 타일 조합이며 모든 seed·모드·모델을 검증한 것은 아니다. 통제되지 않은 기본 유적 산포는 시험 입력에서 제외했고, 명시적 특징/배치 유적은 유지했다.

**이전 MG23의 native 충돌 1회는 여전히 원인 미확정이다.** 이번 시험 도구의 대기 문제와 같은 원인으로 간주하지 않는다. Fable 호출 0회, 이미지 입력 OFF. 이번 그림들은 로컬 맵 생성 결과이며 이미지 모델 호출은 없었다.

## 직접 확인할 문장

서로 다른 평지·사막·산악 타일에서 새 대화로 “이 타일에 어울리는 지형 추천해 줘”. 마음에 들지 않는 후보에는 “3번은 산을 줄이고 넓은 정착 공간이 생기도록 바꿔 줘” 또는 “1번의 둥근 토양을 불규칙한 자연스러운 모양으로 바꿔 줘”. 수정이 원하는 모양으로 이어지는지는 실제 그림을 기준으로 확인한다.
