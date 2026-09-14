# 특징 충돌과 불분명한 대안 동의 — 공통 사전검증

2026-09-15 00:01 KST. 기준 dev eac097e, 검증 DLL SHA256 `77ad21c9c89f4a4cd6cb753694a9ddc3bfda871bb62c7f9d33e8aedaf444c636`.

## 원인

사용자 원문 “이런 거 일일이 하나하나 다 발견하면서 수정할 수밖에 없나?”에 따라 개별 온천/연못 예외를 추가하지 않고 후보 안내와 적용 전 검증을 공통화했다.

실제 로그에는 오아시스 대신 **Pond 특징 또는 직접 물·모래 도형**을 제안한 뒤, 사용자의 “그래”에 두 방식을 모두 생성한 응답이 남았다. Pond는 기본 Lake 정의를 상속해 Lake/Groundwater에 속하며 HotSprings도 Lake에 속하므로 현재 조합 규칙에서 충돌한다. 백엔드는 전체 요청을 거부해 온천과 이전 상태를 지켰지만, 앞선 제안과 오류 안내가 부적절했다. [원본 제안](user-evidence/ambiguous-offer.json), [거부된 명령](user-evidence/rejected-response.json).

## 공통 수정

1. 후보를 현재 특징 유지하며 추가 가능 / 기존 특징 교체 필요 / 조건상 불가로 구분한다. category와 overrideCategory 충돌 규칙을 후보표와 실제 계획에서 공유하며, 단순 추가 후보도 현재 전체 계획으로 dry-run한다. 월드 River/Coast 같은 기본 연결을 삼각주 등의 변형으로 표현하는 경우는 보호 연결 삭제를 요구하지 않는다.
2. `MapGenParams.ValidatePatch`는 적용과 같은 요청 파싱 후 검사·후보 병합·모양/재료·월드 계획 검증을 재사용한다. 검증용 후보만 복제하고 타일/상태/캐시/미리보기/Undo/안내 기록은 수정하지 않는다. 실제 API 요청과 UI 적용 사이에서 UI thread로 실행한다.
3. 부적합한 변경 명령이면 사용자 요청과 카탈로그를 유지하고 한 번 설명을 다시 요청한다. **이 재요청에서는 ask 설명만 허용**한다. AI가 온천 삭제 등 다른 generate 명령으로 우회하면 적용하지 않는다. 취소·닫기는 RequestGate로 이전 요청을 무효화한다. 정상 명령은 추가 설명 요청 없이 진행한다.
4. 여러 대안 뒤의 막연한 동의를 임의 선택이나 둘 다의 허가로 해석하지 않도록 안내한다. 충돌 안내의 프로그램 키를 사용자에게 직접 입력하라고 요구하지 않는다. 명시적 “온천을 없애고 연못으로 교체”는 정상 적용·Undo된다.

## 실제 검증

- [오프라인](final-tests.log):110 PASS/0FAIL. 이름이 다른 합성 특징의 category/override 충돌, 후보 검증 무변경, 명시적 교체, 미지원 지리/정의/도형 참조를 포함한다.
- [최종 빌드](final-build.log):오류0, 기존 자동버전 CS7035 경고1. 이 DLL을 최종 실제 게임 검사와 패키지에 사용한다.
- [VLE 실제 UI 검사](native-verified/result.json):후보 107개에서 카탈로그 표시와 backend dry-run 일치, 보호된 기본 강→삼각주 처리, 실제 모델6응답 재생/Undo, 비동기 부적합명령→설명, 설명 대신 임의교체명령 거부, 재요청 중 닫기, 정상명시교체 확인. 후보 검사는 각 특징의 완성맵 생성 검사가 아니다.
- [실제 생성](maps-final/result.json):기존 VLE7특징/평지 온대·열대 온천/비옥화산토양 채움을 최종 DLL에서 완성맵5개와 배경Preview1개로 재확인. 두 최종 runtime 검사 합계 162 PASS. 앞선 native-r1/maps-verified는 수정 전 DLL이라 합산하지 않는다.
- [Gemini 3.8 한국어6종](provider-r1/results.json):오아시스 대안, 두 대안 뒤 “그래”, 온천+연못 동시요청, 명시교체, 온천+화창함 추가, 원본 실패 명령에 대한 재설명. 질문4/변경2를 실제 Dialog에 재생했다. 응답 원문과 히스토리를 보존한다.

## 범위와 한계

기계적으로 차단하는 것은 부적합한 명령의 적용과 설명 재요청에서의 generate 우회다. AI가 질문에서 제안하는 모든 문장, 암묵적 사용자 의도, 좋은 위치/모양까지 검증하지는 않는다. 실제 답변 일부에는 여전히 기술 용어가 섞인다. 일반 단계의 명시적 삭제 요청 해석과 대안 질문 표현은 모델 판단이 포함된다. 뒤의 “그래”도 불명확하면 다시 어느 쪽인지 묻는다.

형식 복구(StructuredChat)와 이번 검증 실패 설명 재요청은 별개이며 추가 API 호출이 생길 수 있다. 검증 후 실제 외부 worker 실행 중 발생하는 실패는 자동 재요청하지 않는다. 모든 모드·타일·날씨 빈도를 검증한 것은 아니다. 기존 70% 후속의 잘못된 대상/정확 면적비와 F03 첫 물 잔존 원인은 후속으로 남는다. 이미지/Fable 중단과 사용자 Config 보존, 월드 연결 보존 정책 유지.

테스트 도구 첫 빌드에는 Hilliness 네임스페이스 누락1건이 있었고 using 추가로 해결했다. 코드 검토 중 기본 River/Coast를 교체 대상에 포함하면 보호 연결 삭제를 요구할 수 있음을 발견해 일반 관리 특징을 후보 교체 목록에서 제외하고 실제 삼각주 dry-run으로 확인했다.

## 재현

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --no-restore
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore
./tools/runtime-probe/launch.ps1 -EditPreflight docs/analysis/2026-09-14-edit-preflight/user-evidence -EditReplies docs/analysis/2026-09-14-edit-preflight/provider-r1 -Landmarks -Language Korean -Output <새폴더>
./tools/runtime-probe/launch.ps1 -FeatureFeedback -Landmarks -FeedbackResponses docs/analysis/2026-09-14-feature-feedback/provider-vle-r1 -Language Korean -Output <새맵검사폴더>
dotnet run --project tools/provider-probe/ProviderProbe.csproj -- . <새모델응답폴더> preflight <native-verified폴더> docs/analysis/2026-09-14-edit-preflight/user-evidence
```

---
## 작성 이력
- 2026-09-15 00:01 — 현재 조합 후보표·공통 dry-run·설명 전용 재요청과 실제 UI/모델/생성 검증 기록.
