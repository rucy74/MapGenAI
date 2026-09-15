# 대표 지형 검증과 기존 기능 보존

2026-09-15 시작한 작업. 기준 `dev=4b05726`, 구현 커밋 `15cb9cd`. 기존 기능을 최대한 보존해 달라는 사용자 지시를 우선했다. **이번 제품 변경은 명시적으로 연결·칸 수 폭을 요청한 마른 통로에 한정한다.** 이미지 기능은 계속 OFF이며 Fable/Claude 호출은 없다.

현재 버전은 [dev-landform-baseline-2026-09-15](https://github.com/rucy74/MapGenAI/tree/dev-landform-baseline-2026-09-15) 태그로 먼저 보존했다. 기준 DLL SHA256은 `91955863bab902bbfc70b061025aa80476d82d7ec1e4a5d696c160e933ca8596`, 최종 검증 DLL은 `a3edda95b8b31fc064f870796675979ef844331057bda71ca14f0d0d4ba9ad60`이다. 원본 모드·main·v1.6은 수정하지 않는다.

## 확인한 문제와 변경

기존 모델은 통로를 사각형이나 여러 다각형으로 조합했다. 도넛 출구가 중앙보다 일찍 끝나 내부의 원래 바위에 막히거나, 꺾이는 지점의 폭이 부족한 경우가 있었다. 예를 들어 [기존 출구 결과](baseline-r2-s1/valley-exit-2-generated-preview.png)와 [기존 꺾인 길](baseline-r2-s0/bent-canyon-1-generated-preview.png)을 보관했다.

새 `passage`는 순서대로 통과할 점과 실제 맵의 칸 수를 받는다. 중심선을 4방향으로 연결하고 요청 폭의 사각형을 따라 넓혀, 굽은 지점에서도 폭이 줄지 않게 한다. Unity/Mono에서 소수 좌표를 정수 칸으로 변환할 때 생긴 한 칸 차이도 보정했다. 기존 SDF 도형·자연스러운 윤곽 계산은 그대로다.

처음부터 산을 낮추고 기존 재료 적용 경로로 마른 땅을 만든다. 통로를 예약 영역으로 등록해 이를 따르는 기본 생성기가 피해 가게 한다. 이후 남은 물·위험 지형·장애물·산 높이를 검사한다. 바다·강·건물을 무조건 지워 성공시키지 않는다. 일부 외부/기본 생성기는 예약을 무시할 수 있으므로 장애물 안내가 남을 수 있다. 후속 면적 채움처럼 더 늦게 실행하는 편집도 통로와 충돌할 수 있다.

폭/경로 변경, 이동, 삭제, Undo, 프리셋, 실제 Scribe 저장·불러오기를 연결했다. 한국어·영어 설명은 내부 ID 대신 통로 폭, 바닥 재료와 연결 방향을 표시한다. 예전 통로의 재료만 바꾸는 요청이나 일반 도형을 자동 변환하지 않는다.

## 검증 결과

| 항목 | 결과와 범위 |
|---|---|
| 기존 회귀 + 새 통로 검사 | [132 PASS / 0 FAIL](final-tests.log), 기존 128개 포함 |
| 대표 지형 요청 | [10종 × 한국어 2표현](cases.json), Gemini 3.8 Flash 새 첫 응답 20개: 생성18, 동굴 미지원 안내2 |
| 기준판 실제 생성 | 같은 응답을 3개 시드에서 재생: 54개 완성 맵 |
| 수정 후 모델 | 한국어 통로6 + 기존 지형4, 영어 통로2: 새 응답12개. 전체 새 API 요청32개, 시드 반복에는 API를 호출하지 않음 |
| 수정 후 대표 맵 | 새 응답10개와 유지 응답10개를 같은 타일·3시드에서 재생: 54개 완성 맵 |
| 통로의 8칸 연결·꺾임 지점 | **13/18 → 18/18**. 실제 건물/바위·물과 독립적인 8×8 바닥 여유 검사로 판정 |
| 통로 생성 경고 | 최종18개 중1개는 연결·지정점 검사를 통과했지만 계획한 바닥 일부에 장애물이 남아 경고. 모든 칸에 장애물이 없다는 보장은 아님 |
| 기존 성공 사례 대조 | **8개 × 62,500칸 × 지형/건물·바위/지붕 층이 전부 일치**. 아래 통제 조건 적용 |
| 면적 채움 회귀 | [116검사 PASS](coverage-final/result.json), 13개 완성 맵 + 실제 배경 미리보기2개. 70/50/100%, 소스 이동·자연 윤곽·구조물 이후 비율·Scribe/Undo |
| 실제 Map Preview | 한국어2 + 영어2. 산 높이까지 반영해 통로 연결·폭 검사, 이미지 출력 확인 |
| 월드 해안 보호 | 강제 통로 진단에서 기존 바다 **8,624칸 재료가 그대로**, 막힌 통로라는 결과 안내 확인 |

집계는 [evaluation.json](evaluation.json)에 있다. `python -X utf8 tools/evaluate_landforms.py docs/analysis/2026-09-15-landform-suite`로 재계산할 수 있다. `complete:true`나 API의 `generate`만으로 지형 요구사항 통과를 판정하지 않는다.

기존 기능 대조는 D04 마른 통로, F03 용암 교체, 도넛70%/후속50%/기존 잘못된 채움 교체, 섬 가장자리·물가·산기슭 폐허8개다. [이전 응답 원본과 출처](control-inputs/cases.json)를 그대로 사용했다. 초기 전체 맵 비교에서 고대 잔해·사당 배치 차이가 나와, 정확한 칸 비교에서는 `Ancient*`, `ScatterRuinsSimple`, `ScatterShrines`, `ScatterRoadDebris`, `ScatterCaveDebris`, `MechanoidRemains`만 제외했다. **사용자가 지정한 폐허·기본 지형 생성은 포함한다.** 제외하지 않은 전체 맵 실행도 별도로 보관했으며, 모든 기본 잔해까지 같다는 주장은 하지 않는다.

최종 미리보기:

- [한국어 도넛 출구](previews-ko-final/valley-exit-1-background-preview.png), [꺾인 통로](previews-ko-final/bent-canyon-1-background-preview.png), [검사 결과](previews-ko-final/preview-result.json)
- [영어 도넛 출구](previews-en-final/valley-exit-1-background-preview.png), [꺾인 통로](previews-en-final/bent-canyon-1-background-preview.png), [검사 결과](previews-en-final/preview-result.json)

## 대표 지형별 남은 범위

이 표는 [Geological Landforms 게시글](https://steamcommunity.com/sharedfiles/filedetails/?id=2773943594)의 모든 지형을 구현했다는 뜻이 아니다. 기본 배치의 가능성과 품질·제약을 구별했다.

| 지형 | 이번 확인과 남은 작업 |
|---|---|
| 외딴 원형 산 | 기본 원형 배치 가능. 주변 자연 지형까지 완전히 평탄하다는 보장은 별도 |
| 칼데라·닫힌 고리 산 | 고리와 내부 공간 가능. 내부 원래 바위·물의 정리 정책은 후속 |
| 출구가 있는 분지 | 명시적 중앙→남쪽 통로로 연결 개선 |
| 직선 협곡 | 실제 칸 수 폭의 마른 통로 적용 |
| 꺾인 협곡 | 순서대로 지정한 꺾임 지점·폭 적용 |
| 호수 속 섬 | 기본 물 고리+섬 배치 가능. 자연 해안의 세부 품질은 별도 |
| 오아시스 형태 | 작은 물+비옥토+모래 구성 가능. native Oasis 특징의 바이옴 조건과 구별 |
| 반도 | **미해결.** 모델의 배치 방향과 실제 해안 위치가 어긋나며 바다 보호 때문에 원하는 돌출부가 생기지 않음 |
| 군도 | **미해결.** 섬 개수·분리·얕은물 접근로를 실제 해안 생성기와 함께 제어해야 함 |
| 지붕 있는 동굴 입구 | **미지원 안내와 기존 상태 보존 확인.** 통로가 두꺼운 산 지붕을 유지하는 동굴 기능은 아님 |

다음 단계는 해안 방향·물/육지 경계·접근로와 내부 장애물 정리처럼 실제 측정에서 드러난 문제를 각각 명시적 기능으로 추가하는 것이다. Geological Landforms의 전체 생성기 연동, 복잡한 강, 모든 외부 모드/언어/모델, 오래된 사용자 세이브, RimWorld1.5는 이번 검증에 포함하지 않았다. 이미지 기능을 다시 켜지 않는다.

## 실행과 실패 이력

제품 빌드 `dotnet build dev/Source/MapGenAI.csproj --no-restore`: 오류0, 기존 버전 형식 CS7035 경고1. 마지막 제품 빌드 뒤 같은 DLL로 위 검증을 수행했다. 테스트 실행은 `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`다.

실제 맵은 `tools/runtime-probe/launch.ps1 -LandformSuite <cases.json> -LandformReplies <응답폴더> -LandformSample 0 -Output <새폴더> -SourceDll <검사DLL> -Language Korean`으로 재생한다. 시드1/2도 별도 폴더로 실행한다. 정밀 대조에는 `control-inputs/controlled-cases.json`을 사용한다. 임시 모드는 별도 표시한 프로필에만 설치하고 종료 후 소유권을 확인해 정리한다.

실패도 보존했다. 초기 도구의 익명 객체 직렬화로 타일 메타데이터가 `{}`가 된 문제, 전체 칸 JSON1MB 제한 초과는 dictionary와 무손실 RLE 기록으로 수정했다. 테스트 shim의 물/위험 속성 누락을 수정했다. 최초 통로는 Mono 좌표 반올림과 나중에 놓인 잔해 때문에 일부 실패하여 좌표 변환과 통로 예약을 추가했다. 배경 미리보기에서는 실제 바위를 생성하지 않는다는 점을 반영해 고도/동굴 데이터로 산을 판별하도록 검사 도구를 고쳤다. 초기 실패 폴더를 최종 성공 근거로 세지 않는다.

현재 테스트 문장은 [직접 테스트 안내](manual-tests.md)에 있다. 개발 패키지 설치·Git 저장 완료 여부와 정확한 최종 커밋은 함께 전달한 installation/completion 영수증을 따른다.
