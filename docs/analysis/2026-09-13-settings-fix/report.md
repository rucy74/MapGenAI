# MapGenAI 설정 UI와 모델 선택 수정

2026-09-13. 기준 `dev / 7e75cf5`, 수정 DLL SHA256 `9ad0711d1ee7ff5667e835be8e0a69f543e5ce6daf24bd27f4b8eb07d37de0d7`.

## 사용자 지적과 원인

“이거 모드 설정에서 UI같은 거 왜 깨져있지. 그리고 LLM 모델 고르는 것도 왜 택2야? 원래 mapgenai에서 안 그랬잖아.”

고급 설정에서 `DrawAdvancedSettings`가 `Listing_Standard.End()`를 호출한 뒤 부모 `DoWindowContents`가 다시 호출했다. 사용자 게임 로그의 `Invalid GUIClip stack popping` 반복과 같은 오류를 기존 설치 DEV DLL로 실제 Verse 설정 창에서 재현했다. 이중 종료 코드는 보존판에도 있던 코드이며, 앞선 개발 검증에서 설정 화면까지 렌더하지 않아 놓쳤다. [기존 DLL 재현](baseline/result.json).

두 항목의 선택 메뉴는 앞선 개발 과정에서 Codex가 간편 모드에 추가한 제한이었다. 기존 고급 설정의 API 모델 조회는 남아 있었지만, 간편 화면에서는 3.8/2.5만 보였다. 사용자 요청 없이 모델 선택 폭을 좁힌 판단을 바로잡았다.

## 변경

- 설정 헤더의 Begin/End를 부모 한 곳의 try/finally로 관리한다. 고급 본문은 헤더 그룹을 닫은 후 그리며 고급→간편 전환도 같은 종료 규칙을 따른다. 색상·폰트·정렬도 복원한다.
- 간편 화면에 모델명 직접 입력과 **모델 목록 불러오기**를 제공한다. 실제 API 모델 목록을 고급 화면과 같은 메뉴로 열고 선택을 `simpleGeminiModel`에 저장한다. 목록 새로고침도 가능하다.
- 기존 고급 공급자 선택·API 구성·현재 모델을 유지한다. 기본 모델 3.8 Flash는 기본값이며 선택 제한이 아니다.
- 비동기 조회 결과를 요청별 큐로 전달한다. 캐시는 공급자·주소·API 키별로 구분하고, 설정이 바뀌거나 행이 삭제된 뒤의 메뉴 선택은 적용하지 않는다. 실패를 무조건 `No API key`라고 표시하던 안내를 고쳤다.
- 설명 글의 높이를 실제 줄 수에 맞추고 새 오류 문구를 한국어·영어·일본어·중국어 간체에 추가했다. 사용자 설명서와 MAP도 갱신했다.

## 실제 검증

- `dotnet build dev/Source/MapGenAI.csproj --nologo`: 오류 0, 기존 CS7035 버전 문자열 경고 1. 이 턴의 도구 실행 결과 기준이다.
- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **62 PASS / 0 FAIL**. 기존 지형·이미지·저장 회귀이며, 이 결과만으로 설정 UI를 검증했다고 보지 않는다.
- `dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo`: 오류 0, 경고 0.
- `tools/runtime-probe/launch.ps1 -Render -Settings`: 기존 설치 DLL에서는 고급 설정 GUIClip 오류 재현, 수정 DLL은 간편·고급 클라우드·로컬 화면 모두 해당 오류 0건. [수정 후 결과](fixed-render/result.json).
- `-Language Korean`을 지정한 별도 프로필에서 세 한국어 화면을 렌더하고 육안 확인했다. [한국어 결과](korean-render/result.json), [간편](korean-render/settings-simple.png), [고급](korean-render/settings-advanced-cloud.png), [로컬](korean-render/settings-advanced-local.png).
- `-ModelConfig <ignored docs/dev_config.json>`: 실제 production Gemini 목록 조회에서 **33개** 확인. production 비동기 요청→큐→메뉴, 전체 목록 포함, 두 기본값 외 모델 선택, 오래된 메뉴의 변경 차단, 고급 선택, 임의 모델 ID 유지까지 **7개 검사 PASS**. [최종 결과](catalog-integration/result.json), [조회 모델](catalog-integration/live-gemini-models.json).
- 마지막 모델 검사는 실제 API 응답과 production 메뉴 action을 사용한다. 요청 시작과 완료 처리 일부는 리플렉션으로 호출했으며 물리적 마우스 클릭 전 과정 자동화는 아니다. 화면 렌더와 모델 경로를 분리해서 확인했다.
- 언어 XML·알려진 테스트 API 키 유출 검사·diff·DLL·임시 폴더 확인은 [검증 요약](verification.json)에 기록한다. 모든 비밀 형식을 탐지하는 검사는 아니다.

실제 Fable 자문은 [요청](fable-request.md)과 [답변](fable-review-current.md)에 보관했다. 응답 modelUsage에서 `claude-fable-5-1`을 확인했고 해당 검토 범위의 P0/P1 지적은 없었다. 자문 의견은 위 실행 증거와 별개다.

## 실패한 검사 도구 시도와 남은 범위

최초 프로브는 Root의 WindowStack 생성 전에 창을 열어 실패했다(`before`). 이후 실제 프레임까지 지연해 기존 DLL 오류를 재현했다. 위젯 버튼을 가로채는 검사에는 실행 시점과 메뉴 관찰 누락 문제가 있었다. 앞에서 패치를 설치한 `ui-model-flow-final`은 조회가 끝나기 전 결과가 없어 정지로 오인했으나, 최종 결과에서는 **실제 간편 설정 버튼→조회→메뉴→세 번째 모델 선택 등 5개가 통과**했다. 고급 선택 검사만 테스트 설정의 `useCloudProviders=false` 때문에 실패했다. 원본 [결과](ui-model-flow-final/result.json)와 자동 [종료 정리](ui-model-flow-final/cleanup.json)가 남아 있으며 강제 종료 영수증은 없다. 앞선 정지 추정을 최종 사실로 취급하지 않는다. 최종 검사에서는 고급 클라우드 모드를 명시하고 GUI 가로채기 없이 큐 경로를 별도로 확인했다.

메뉴가 포인터 밖에서 자동으로 닫히기 전에 검사하지 못한 실패(`async-menu-final`, `verified`)도 보존했다. 최종 검사는 production 큐를 명시적으로 처리하고 메뉴 action을 즉시 실행해 결과를 확인했다. 실패 런을 성공 점수에 섞지 않는다. 모든 소유 임시 모드 폴더는 종료 후 삭제했으며 프로필과 증거는 보존했다.

Fable의 P2 중 비동기 경로의 검사 부족은 최종 큐 검사로 보완했다. **조회 중 설정 창을 닫았다 재열 때 메뉴가 뜰 수 있는 동작**, **기존 HTTP 클라이언트의 긴 기본 대기 시간과 Local 주소 누락 안내**는 후속 항목이다. 이번에는 이미지 생성 품질·새 모델별 맵 생성·다양한 모드 조합을 다시 평가하지 않았다.

기존 `v1.6`·`main`·`dist`는 보존한다. 사용자에게 설치하는 대상은 별도 **MapGen AI [DEV]**이며, API 키·활성 모드 목록·사용자 세이브를 패키지에 포함하지 않는다. 실제 설치 커밋과 파일 검증은 새 패키지의 `build.json` 및 출력 `installation-receipt.json`을 따른다.
