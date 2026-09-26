# DEV 공통 지형 구성·추천 변주 — 2026-09-26

사용자는 분지 종류를 하나씩 늘리기보다 여러 풍경에 공통으로 적용되는 개선, 기존 기능 보존, 다양한 직접 테스트와 Geological Landforms 묘사 안내를 요청했다. 범위는 DEV다. 일반판/dist/main/v1.6은 변경하지 않는다. 이미지 입력은 계속 OFF, 외부 Fable 호출은 0회다.

## 구현

- `LandscapePath`: 순서 있는 2~12개 제어점의 곡선 영역을 산줄기·마른 평지·물에 공통 사용한다. 자연 윤곽을 요청한 새 path만 폭이 완만히 변하고 양끝이 가늘어진다. none/미지정은 일정 폭이며 기존 circle/poly/landform 수식은 바꾸지 않는다.
- `LandscapePlacement`: 기준 영역의 안/안쪽 가장자리/바깥 인접 관계를 저장하고 생성 시 실제 마스크로 해결한다. 전체 feather 포함 직사각형을 보수적으로 검사한다. 형상을 잘라 넣거나 자동 축소하지 않는다. 형제 요소와 다른 산/명시 재료의 충돌을 피한다. 평지에 붙이는 물은 원래 산을 지워 공간을 만들지 않는다.
- `anchor/placement`는 파싱·Clone·Scribe·프리셋에 남는다. 없는 기준/순환/기준 삭제는 사전 거절한다. 기준 지형을 움직이면 관계 요소도 재배치한다. 절대 이동은 관계를 명시 해제해야 한다.
- 자연 평지는 fill 없이 높이만 조정한다. `details:natural`은 composite도 지원하며 명시 켜기/끄기를 안내한다. 모델이 모든 추천에 이를 자동 넣는다는 보장은 없다.
- 새 추천에만 독립 변주 지침: 방위/중심, 하나의 굽은 지형·불균등 군집·분기, 점유 비중/연결된 여백을 조합한다. 사용자 선호와 타일 조건이 우선이다. 월드 시드·현재 지형·후속 편집 variant는 재추첨하지 않는다.
- 문답은 기본 6개, 특별한 풍경의 추가 질문과 기존 구도 유지/새 구도 비교가 있으면 최대 8개다. 질문 자체는 로컬이며 API 비용이 없다. 후보 생성은 기존의 한 번 요청 구조다.
- 위치만 바꾸라는 후속에 관계 설정과 동시에 형상/재료를 바꾸는 응답을 기존 1회 수정 경로 앞에서 차단한다. 후보 revise에도 적용한다. 새 곡선의 한영 방향 설명은 실제 제어점 좌표로 계산한다. 이것은 제한된 방어 검사이며 모든 자연어 의도 오해를 해결하는 시스템은 아니다.

## 검증

실행 명령:

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --no-restore -v minimal
dotnet build tools/natural-landform-probe/NaturalProbe.csproj --no-restore -v minimal
# 실제 모델은 3개 독립 추천만 추가 호출
dotnet tools/provider-probe/bin/Debug/net10.0/ProviderProbe.dll F:/Projects/Rimworld/active/mapgen_ai docs/analysis/2026-09-26-landscape-relations/provider-diversity landscape-diversity docs/analysis/2026-09-26-landscape-relations/native-n2703/system-prompt.txt
# 현재 결과 폴더를 덮어쓰지 않고 Run에 새 번호를 사용한다.
& tools/natural-landform-probe/run.ps1 -Run n2711 -Set composition -Graphics -Evidence 2026-09-26-landscape-relations
& tools/natural-landform-probe/run.ps1 -Run n2710 -Graphics -Evidence 2026-09-26-landscape-relations -StatesDirectory docs/analysis/2026-09-26-landscape-relations/native-replay-states -FullState docs/analysis/2026-09-26-landscape-relations/provider-diversity/diversity-03-guided-option-3-state.json
```

- 최신 순수 회귀 **282 PASS / 0 FAIL**. 기존 도로/다리·70% 영역·기존 저장 응답/도형·문답 검사를 포함한다. 새 검사에는 실제 잘못된 이동 응답, source 누락/순환, 전체 윤곽 맞춤, 부모 이동/형제 분리, 장애물 양성 대조와 실패 시 높이/재료 보존이 있다. [전체 출력](pure-regression.log)
- 제품 빌드 0오류, 기존 버전 문자열 CS7035 경고1. 순수 테스트 링크에는 native에서 할당되는 필드의 미할당 경고가 있다. [빌드](build.log)
- `native-n2711`: 최종 DLL로 공통 fixture4개 Preview와 완성 맵1개, Scribe/프리셋/Dialog/Undo. `native-n2710`: 실제 공급자 후보9개+독립 요청2개 Preview, 문답 후보의 완성 맵1개. 최종 두 실행 합계58개 검사가 통과했다. 자세한 측정은 각 result.json 및 artifact-audit.json을 따른다. 사용자의 전체 모드 구성과 게임 GUI 클릭 재현은 아니다.
- 모든 최종 렌더는 소유한 별도 게임 사본·새 프로필에서 실제 RimWorld/Map Preview가 생성했다. Map Preview 배포 DLL은 수정하지 않았다. 실제 공급자 state의 전역값/특징도 유지하고, Resolve 결과와 전체 state 동등성을 확인한 후 실행한다.
- 실제 Gemini 3.8 Flash 호출 총15회: 입력274899/출력6736, 보고된 thinking0. 세 실험의 first response를 모두 보존했다. 추가 렌더는 모델 호출0이다. [원본1](provider-first/result.json) · [원본2](provider-final/result.json) · [최신3회](provider-diversity/result.json)

## 실패와 보정 과정

1. 초기 순수269PASS/4FAIL: MapStateEditor.Merge의 anchor 참조 검사가 누락된 것1개, 저장 응답 예상값에 새 nullable 필드가 없는 것3개. 참조 검사 추가, 기대값은 새 null 필드만 정규화했다.
2. provider-first는6개 중4개 검사통과. '산 없이'의 hills:none/hill_amount:.1까지 글로벌 불변으로 거절한 평가기준은 과도하여 보정했다. 별도로 평지ID가 없어 후속이 ask로 끝났다. provider-final은6개 중5개이며 후속 이동 때 긴 물 영역을 작은 타원으로 교체한 실제 의도 실패1개를 유지한다. 그 raw 응답을 회귀에 넣었다. 새 guard 이후 실제 모델의 수정 재응답은 추가 호출하지 않았다.
3. 추상적 변주만 준 provider-final의 기본 두 추천은 같은번호 산 구성과 위치가 매우 비슷했다. 구체적 위치/군집 지침을 준 provider-diversity의 두 추천은 동쪽 단일 능선/서쪽 분기/남서 군집에서 남서 능선/남쪽 군집/남동 분기로 바뀌었다. 이것은 1개 온대림 평지 타일에서의 관측이며 일반적 미관 성공률이 아니다.
4. 독립 코드 리뷰로 beside의 다른 산/명시 재료 덮기, 관계 배치 실패의 전역 structures 차단, revise/direction guard 누락을 찾아 수정했다. 관련 실패 양성 대조를 회귀에 추가했다.
5. n2701의 관계 audit가 익명객체 직렬화로 `{}`여서 근거에서 제외하고 Dictionary로 바꿨다. n2703/4는 -nographics로 회색 바닥을 그려 미관 증거에서 제외했다. n2705는 그래픽을 켠 중간 DLL 결과다.
6. 장애물 보호 후 n2706/7 lakeside fixture는 들어갈 공간이 없어 거절됐다. 일반적인 nofit 제한을 해제하지 않았다. 산 없는 호숫가 예제는 원래 native hill 억제를 명시하지 않았으므로 hill_amount:.1을 명시한 새 fixture로 n2708을 실행했다. 실패 원본은 보존한다.
7. 일정 폭 path가 인공적으로 보여 자연 윤곽의 폭 변주를 추가했다. 첫 순수281PASS/1FAIL은 기존 private method reflection의 인자 수 검사로, 한 인자 wrapper를 유지해 해결했다. [첫 출력](pure-profile-first-failure.log)

## 한계와 사용

- 최신 기본 추천도 크게 보면 '한쪽 산과 열린 땅' 계열이 남는다. 다양한 방위·군집·물 유무가 관측됐지만 좋은 풍경이나 원하는 결과를 항상 보장하지 않는다. 실제 geometry 제어와 모델의 설계 선택은 다르다.
- 최신 추천9개는 path7개 사용, anchor 사용0개였다. 관계 기능 검증은 controlled fixture와 별도 순수 검사이며 자연어가 매번 관계를 선택했다는 뜻이 아니다.
- 보수적 직사각형 검사라 불규칙한 좁은 영역에서 거절될 수 있다. 기준/주변 장애물이 바뀌면 종속 요소도 다시 배치된다. 원래 월드 강/해안 수계의 분기·수문, 3D 고도/절벽, 지붕 동굴을 일반화한 구현은 아니다.
- 이전 MG23 native 충돌 미해결, 이전 최종 식물 strict2FAIL은 여전히 별도 미해결이다. 이번 검사로 모두 해결됐다고 표시하지 않는다. 모든 바이옴·제3자 모드 조합/GL 전종은 미검증이다.
- [사용자 테스트64개](../../text-landscape-test-ko.md)는 먼저 핵심10개에서 골라 사용한다. 성공 결과표가 아니라 입력/판정 목록이다. [Geological Landforms 묘사 안내](../../geological-landforms-prompts-ko.md)는 원본과 비슷한 풍경 구성과 실제 설치 특징, 불가능한 월드 조건을 구분한다.

복귀점: `dev-before-landscape-relations-2026-09-26` → `16ceb67a006eb374e30a6acad0d778d5cc5a9e36`. 배포/설치 영수증은 Codex outputs/mapgenai-landscape-relations에 별도 보관한다. 공개 Release/Steam/main 반영은 하지 않는다.
