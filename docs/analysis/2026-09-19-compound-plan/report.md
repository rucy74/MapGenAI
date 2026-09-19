# 복합 요청·연속 수정 검증 — 2026-09-19

기준: `dev-before-compound-2026-09-19` → `4a89b06362d995e8c4ea8ab02808b3c833ce1016`. 기준 DLL `b488514fecd2cea999aebdd1c3d4b0225439ed0b650795589a65a9ecfcd16d5e`.

최종 제품 DLL: `7f01ca9d935a7111184cd6f0e7bf6731f521ba2c9aa2b3dad5a6796cb4437fe4`. RimWorld 1.6, Gemini `gemini-3.8-flash`, 이미지 OFF/버튼 숨김, Fable/Claude 호출 0회.

## 바뀐 동작

- 구조물이 `region_part:enclosed`로 고리 산에 둘러싸인 빈 내부를 참조한다. 기존 생략/inside는 그대로 유지한다. 토양의 선택된 70% 칸과 별개로 산의 원래 내부를 참조하므로 비율 변경이 구조물 영역 자체를 바꾸지 않는다.
- 산 전용 출구의 전체 지정 경로를 위치 지정 구조물이 피한다. 산 밖 토양을 칠하는 범위와 native 생성기의 UsedRects 예약 범위는 바꾸지 않았다. 이전에 접근로에 놓였던 폐허는 이 수정으로 주변의 가능한 위치로 이동할 수 있다.
- 복합 요청은 기존 하나의 `generate.params`에 지형·출구·면적·구조물 변경을 함께 넣는다. 추가 모델 분해 호출이나 별도 생성 엔진을 만들지 않았다. 후속 요청은 필요한 ID/필드만 수정하도록 안내한다.
- 마른 정착지 요청에는 내부 평탄화 영역을 먼저 만들고 그 위에 고리 산을 적용하도록 구체적인 예시를 추가했다. 고리의 구멍만 만드는 것은 원래 물과 돌산을 지우지 않는다.
- 남아 있던 negative bump 통로 예시를 현재 passage 안내와 일치시켰다. 한·영 설명, Clone, Scribe, preset, Undo에 내부 참조를 연결했다.

## 재현된 실패와 수정 근거

1. [첫 실제 응답](provider-baseline/compound-01-response.json)은 폐허를 고리 산벽에 배치하도록 지정했다. [기준 완성맵](baseline-native/suite-result.json)과 Preview 모두 구조물 배치 실패를 보고했다. 이를 산벽에서 내부로 암묵적으로 바꾸지 않고, 명시적인 enclosed 선택과 모델 안내로 해결했다.
2. [구버전 호환 통제 사례](route-inputs/route-response.json)는 폐허 두 개가 산 출구의 내부 접근로를 72칸 점유했다. [이전 결과](route-old/suite-result.json)는 8칸 연결 실패, [수정 결과](route-new/suite-result.json)는 겹침 0칸·8칸 연결 성공이다. 같은 입력/타일/seed이며 모델 재호출은 없다.
3. 첫 안내 수정 뒤 새 7연속 응답은 폐허·면적·폭을 보존했지만 내부 평탄화가 빠졌다. [중간 측정](native-audit/suite-result.json)에 원래 물 71칸/132칸이 남은 사례를 보존했다. 평탄화 예시 보강 후 **새로운** 한·영 10응답을 받았으며, 최종 19맵에서 내부 물은 0칸이다. 중간 결과를 최종 성공 표본에 합산하지 않았다.

## 실제 검증

| 검증 | 결과 | 근거 |
|---|---|---|
| 오프라인 회귀 | 139 PASS / 0 FAIL | [final-tests.log](final-tests.log) |
| 제품 빌드 | 오류 0, 기존 wildcard version 경고 1 | [build-r2.log](build-r2.log) |
| 새 최종 모델 응답 | 한국어 7연속 + 영어 3연속, 10개 모두 실제 Dialog 적용 | [KO](final-provider-ko/sequence-result.json), [EN](final-provider-en/sequence-result.json) |
| 조건 보존 | 폭8→12, 비옥토70→50%, 폐허2→3, 산/내부/출구 이동, 남쪽 닫고 동쪽 열기, 폐허만 삭제; 명시하지 않은 상태 전체 일치 | [evaluation.json](evaluation.json) |
| 최종 완성맵 | KO7×2 seed + EN3 + 일반 native 생성2 = **19맵** | [KO](final-ko/suite-result.json), [seed1](final-ko-seed1/suite-result.json), [EN](final-en/suite-result.json), [native](native/suite-result.json) |
| 실제 배치 | 요청 수의 벽 생성, 전체 면적이 내부에 포함, 지정 통로 겹침0, 면적비 실측 일치 | 위 완성맵 및 독립 compoundAudit |
| 저장/되돌리기 | 각 실제 Dialog의 Undo, native Scribe 및 preset roundtrip 일치 | 각 실행의 before/after JSON과 scribe.xml, suite 결과 |
| 배경 Map Preview | 최종 5개, 내부 배치/면적비/통로 보존 검사 | 각 final 폴더의 preview-result.json과 PNG |
| 기존 성공 사례 보존 | 기존10응답을 이전/최종 DLL로 각각 생성, 20맵의 대응 지형·건물·지붕 **62,500칸 전부 일치** | [old](legacy-old/suite-result.json), [new](legacy-new/suite-result.json), 각 cells.json |
| 검출기 자체 검사 | 비율100칸 오류·폐허 통로 침범1칸을 주입하면 실패함 | evaluation.json detectorPositiveControls |

예: 한국어 첫 맵은 비옥토 **5519/7884칸(약70%)**, 50% 수정은 **3943/7886칸**, 폐허 2개 생성. 분모는 사용 가능한 칸이며 산비탈·건물·월드 연결 등을 제외한다. 다른 타일/seed/구조물 수에 따라 분모가 달라진다.

이번 전체 실험은 새 모델18응답(최종10 + 중간7 + 기준1), 완성맵56개다. 최종 19맵 외에는 회귀 비교20, 접근로 비교2, 기준 실패1, 중간14맵이다. 최종 품질 주장에는 위 표의 해당 집합만 사용한다.

## 재실행

저장된 응답·측정의 검증(유료 호출 없음):

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
python -X utf8 tools/evaluate_compound_plan.py docs/analysis/2026-09-19-compound-plan
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore
./tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-19-compound-plan/final-provider-ko/cases.json -LandformReplies docs/analysis/2026-09-19-compound-plan/final-provider-ko -Language Korean -Output <새 폴더>
```

기존 비교 입력은 `docs/analysis/2026-09-16-mountain-passages/legacy-inputs/cases.json`과 같은 폴더의 기록 응답이다. `-SourceDll <기준 DLL>`로 구버전, 생략 시 현재 빌드를 실행한다. 모델 재실험은 실제 게임에서 캡처한 prompt 폴더와 `inputs/sequence-ko.json`/`sequence-en.json`을 `ProviderProbe`의 `plan-sequence`에 전달한다. 모든 첫 응답을 남기며 기존 응답 파일을 덮어쓰지 않는다. API 설정은 ignored `docs/dev_config.json`에서만 읽는다.

## 한계

- 모델이 자연어를 해석하는 단계의 전수 보장은 아니다. 이 작업은 범용 자연어 의미 검증기나 Geological Landforms 전체 재현 기능을 추가하지 않았다.
- 산만 깎는 scope는 바깥의 물·자연 장애물을 보존한다. 최종 KO seed0의 3·4번은 산 출구 자체는 통과하지만 **맵 가장자리까지 12칸 폭의 연결은 성립하지 않았다**. `dryEndpointConnection` 원본을 남겼다. 사용자 지정 구조물의 통로 겹침0과 산에 낸 출구의 폭을, 전체 평지의 동일 폭 이동 보장으로 확대하지 않는다.
- 산비탈은 사용 가능한 평지 분모에서 제외된다. 내부 평탄화 안내는 명시적 요청에만 적용하며 모든 자연물·식물·바위 덩어리를 제거하는 기능이 아니다.
- 일반 생성2맵은 원래 native 잔해/사당 단계도 포함한다. 나머지 통제 비교는 무작위 기본 폐허·DLC 건물 단계를 제외했다. 다른 모드가 뒤늦게 놓는 구조물이나 다른 공급자·모든 언어·기존 사용자 세이브의 전수 검증은 아니다.
- 연속 요청의 상태는 이전 응답에서 이어받았지만, 완성맵은 각 상태를 서로 다른 고정 타일에 재생했다. 설정 보존과 최종 배치를 확인한 것이며, 같은 타일에서 모든 후속 편집의 자연 생성 결과가 동일하다는 검사는 아니다.

직접 사용 예시는 [테스트 문장](manual-tests.md)에 보관했다.
