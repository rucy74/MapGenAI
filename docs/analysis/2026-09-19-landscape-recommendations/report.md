# 맵 구조 중심의 텍스트 추천

2026-09-19. 기준 dev `e0d39c0`. 사용자는 “여러 선택지 (3개정도) 주고 이 중에 골라라”와 “미리 보여주는 거 없이 먼저” 구현을 요청했다. 후보 이미지 표시는 후속 아이디어 논의이며 구현 승인으로 확대하지 않았다.

## 변경한 동작

- 기존 recommend/options, 선택 버튼, 저장된 명령 적용을 재사용한다. 일반 첫 추천은 서로 다른 맵 구조3개를 기본으로 안내하고, 이미 편집한 맵에서는 그 맵을 보완하는 후보를 안내한다. 토양만/두 개만 등 범위·개수 명시 요청이 우선한다. 모델 지침이며 모든 표현에 대한 기계적 보장은 아니다.
- 같은 최종 직렬화 상태를 만드는 중복 후보는 표시 전에 거절하고 기존1회 복구 경로를 사용한다. ID나 목록 순서만 다른 기하학적 중복까지 판별하는 검사는 아니다.
- 한국어/영어로 선택 전 미적용 안내를 표시한다. 선택 대기 중인 추천 문구에만 채팅 전체 폭을 사용한다. 일반 채팅 너비·생성 엔진·저장 형식·모델 설정은 그대로다.
- 실제 composite 명령을 읽어 산·언덕/평탄하게 다듬은 땅을 설명한다. 중간 결과 ID가 있는 연산도 fill을 함께 지정하면 실제 렌더링되는 재료를 표시한다. 모델이 만든 임의 제목이나 효과 설명은 표시 근거로 사용하지 않는다.
- 선택 시 추가 모델 요청은 없다. 일반 추천 응답 하나에 후보 설정들을 받으므로 추천 내용이 길면 응답 토큰은 늘 수 있다. 이미지 입력/해석/팔레트 OFF와 이미지 버튼 숨김, Fable 호출 중단은 유지한다.

## 실제 응답과 화면

최종 한국어 첫 추천은 산속 분지+남쪽 출구, 서쪽 산맥+동쪽 호수, 산 덩어리를 관통하는 협곡이었다. 기존 분지에는 간헐천 특징, 동쪽 출구 추가, 분지 밖 연못을 각각 제안했다. 이는 고정 메뉴가 아니라 이번 실제 응답이다.

- [첫 추천 원문](provider-final-ko/recommend-plain-response.json), [실제 표시 문구](native-final-ko/recommend-plain-choices.txt)
- [기존 분지 추천](provider-final-ko/recommend-existing-response.json), [영어 추천](provider-final-en/recommend-plain-response.json)
- [한국어 화면](ui-verified-ko/recommendations-ui.png), [영어 화면](ui-verified-en/recommendations-ui.png)
- [사용 예시](manual-tests.md)

## 검증

- 변경 전 오프라인142 PASS. 최종 **146 PASS / 0 FAIL**: 기존 회귀, 실제 기록 응답에서 중복 후보 차단, 명령별 설명, 새 한영20후보의 native에서 확인한 상태 재생. [출력](final-tests.log). [제품 빌드](final-build-r2.log) 오류0, 기존 wildcard 버전 경고1.
- 유료 모델 요청은 총16회(첫8+안내 보강 후8), Gemini `gemini-3.8-flash`. 최종8요청은 추천7개와 직접 생성1개. 기본 추천은3개, 명시2개 요청은2개, 직접 생성은 generate. 모든 첫 원문을 보존했다. [KO](provider-final-ko/result.json), [EN](provider-final-en/result.json).
- 실제 원래 타일에서 최종20후보 전부 사전검사·선택 전 불변·각 번호의 정확한 저장 명령 적용·Undo 확인. [KO](native-final-ko/suite-result.json), [EN](native-final-en/suite-result.json). 최종 설치용 DLL에서도 선택/Undo 재생: [KO](native-verified-ko/suite-result.json), [EN 및 재시도·취소](selection-verified-en/result.json).
- 원래 추천받은 타일에15개 지형 후보를 각각 완성맵으로 생성했다. 각 후보번호를 별도 게임에서 실행해 모든 대안이 동일한 원래 타일/seed를 사용하도록 했다. 기존 분지의 산·바닥·출구·70%·폐허2개 설정 보존과 실제 채움 비율·폐허 배치·산 절개를 확인했다. [독립 평가76검사](evaluation.json), [평가 코드](../../../tools/evaluate_landscape_recommendations.py).
- 같은 타일15맵의 DLL은 `5a667222bfe746b5f8fb4ba4599839c7f452e55f1e64c923268772cabcfb7292`. 이후 fill을 가진 중간 연산의 **표시**를1줄 수정한 최종 DLL은 `ba12eb99c43a1528aabc8656ecb1c218dc6bb1284ea18ca6443a46728f216310`이며 전체 오프라인 회귀와 실제 한영 선택/화면을 다시 검증했다. 최종 DLL로15맵을 다시 생성했다고 주장하지 않는다.
- GUI 화면에서3개 설명·3개 적용 버튼과 이미지 버튼 부재를 확인했다. 추가 이미지 생성 API나 후보 미리보기 기능은 호출/구현하지 않았다. 테스트용 맵 캡처는 별도 격리 프로브의 검증 자료다.

## 발견과 한계

1. 처음 해안 후보의 물 다음에 더 큰 흙 영역을 덮는 순서가 원문 검토에서 발견됐다. [원문](provider-ko/recommend-coast-response.json). 토양 먼저/물 나중 또는 흙에 구멍이라는 일반 조합 안내를 추가했다. 완성맵 전체를 미리 계산하는 새 제품 검증기는 추가하지 않았다.
2. 처음 일괄 맵 검사에서 후보마다 다른 타일을 사용했다. 그중 해안434에 옮긴 협곡은 보호되는 물 때문에206칸 장애물을 보고했다. [실패를 보존한 평가](evaluation-relocated-tiles.json). 원래 타일248의 같은 후보는 산 절개 장애물0이었다. 타일을 바꾼 재생을 원래 후보의 성공/실패로 단정하지 않도록 검증 입력을 바로잡았다. 다른 해안 타일에서의 실패는 여전히 유효한 한계다.
3. 원래 해안 타일에서도 바다까지 포함한 전체 경로의12칸 폭 연결은 보장하지 않는다. 산을 깎는 범위는 마르고 걸을 수 있으나, 바깥의 강·해안·기존 지형은 보존한다. 전체 경로 연결 측정은 각 맵 `compoundAudit.routes.dryEndpointConnection`에 남아 있다.
4. 추천의 다양성·자연스러움·모든 모드/타일/모델에 대한 성공률을 측정한 실험은 아니다. 영어 분지의 숨은 바닥에 모델이 별도 굴곡을 준 경우 등 지침을 완벽히 따르지는 않는다. 실제 이번 맵 결과를 검토했고 광범위한 성공 보장으로 확대하지 않는다.
5. 최초 병렬 런처가 같은 밀리초 경로를 골라 EN 실행이 파일 잠금으로 실패했다. 첫 capture-ko는 평가 입력에서 제외하고 새 경로의 순차 런처 호출로 언어별 캡처를 다시 얻었다. 임시 모드는 소유권 마커를 확인하는 기존 cleanup으로 정리했다. 테스트 초안의 ShapeDef 오타에 의한 빌드 실패도 [보존](tests-r1.log)했다.
6. 최종 DLL 선택 재검사에서 옛 RecommendationProbe의 건조관목림 타일로 온대림 응답을 옮겼더니 scoped 후보의 Fertile 특징이 바이옴 제한으로 거절됐다. [실패 기록](selection-verified-ko/result.json). 실제 원래 타일을 사용하는 LandformSuite로 KO 전체를 다시 검사했다. 응답 원문만 같아도 타일 조건이 달라지면 추천 유효성이 달라질 수 있다.

## 재현

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --no-restore --nologo
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore --nologo
python -B -X utf8 tools/evaluate_landscape_recommendations.py docs/analysis/2026-09-19-landscape-recommendations
```

native 캡처는 `launch.ps1 -LandformSuite cases-ko.json -Language Korean -Output <새경로>`, 새 API 응답은 provider-probe의 `landforms <캡처경로>` 명령이다. 모든 실제 옵션을 저장한 원문은 `maps-input-{ko,en}`에 있고 `cases-1.json`~`cases-3.json`을 **각각 새로운 게임에서** 재생한다. launcher는 `-LandformReplies <maps-input 경로>`를 받는다. API 키는 추적하지 않는 기존 개발 설정에서 읽으며 증거/패키지에 포함하지 않는다.

## 후속 후보 미리보기 아이디어

기존 추천은 이미 각 후보의 실행할 설정을 보관한다. 따라서 나중에 사용자가 `미리보기`를 선택했을 때 그 설정을 Map Preview로 로컬 렌더링하는 방향이 유력하다. 이미지 생성 모델은 필요하지 않을 수 있다. 후보3개를 만들기 위한 기존 텍스트 응답의 토큰과, 추가 이미지 생성/분석 호출의 토큰은 구분해야 한다.

제품 구현 전 확인할 항목은 후보별 상태 격리와 원상복구, 순차 생성/취소, 타일·seed·설정별 캐시 무효화, 실제 맵과의 차이, 처리 시간·메모리다. 이번 작업에서는 후보를 렌더링하거나 업로드하는 버튼을 추가하지 않았다.
