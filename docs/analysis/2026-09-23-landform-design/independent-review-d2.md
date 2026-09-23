# 독립 재검토 — 2026-09-23

범위: `DesignBench.OnlyBasinChange` 보완 및 `OrganicLandformGeometry`의 곡선 gully/SoftMin 변경. 제품 코드와 기존 기록은 수정하지 않았다. 부모 작업자가 제공한 제품 DLL 식별자는 `fd1327816da1a171f347bed08577cd7aac27d898954d70edb0b8104b93cfd284`이며, 이 검토에서 제품 빌드나 설치를 다시 실행하지 않았다.

**결론: 앞선 P2 검증 누락은 해소되었고, 이번 변경에서 새 확정 결함은 찾지 못했다.**

- `OnlyBasinChange`는 이전 상태를 복제하고 허용한 `gap` 또는 `direction`만 변경한 뒤 전체 상태와 비교한다. 다른 지형·구조물·전역 설정·ID·프로필의 변경을 통과시키던 경로가 닫혔다. 크기 증가 및 동쪽 방향 조건도 호출부에서 별도로 확인한다.
- 곡선 gully의 선분 길이는 생성 범위에서 0이 되지 않는다. SoftMin 반경은 양수 상수이며, 현재 거리값을 낮추는 방향으로만 합성하므로 기존 평지를 줄이지 않는다. 기존 classic 분기의 계산은 변경하지 않는다.

이번 재검토에서 실행한 확인:

1. `dotnet C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/mapgenai-design-provider-d2/ProviderProbe.dll C:/nonexistent-review-config docs/analysis/2026-09-23-landform-design/review-provider-audit design-audit-selftest`
   - 종료 코드 0. 정상 2건 및 부정 대조군 10건 PASS. 존재하지 않는 설정 경로로 실행했으며, selftest 분기가 설정 로드보다 앞에 있음을 코드에서도 확인했다.
   - 원문 출력: [audit-selftest.txt](review-provider-audit/audit-selftest.txt).
2. PowerShell에서 `mapgenai-design-provider-d2/TextToMap.Tests.dll`을 `Assembly.LoadFrom`으로 읽고 `OrganicLandformTests.RunAll`을 reflection으로 호출한 뒤 `CoreRegressionTests.passed/failed`를 확인했다.
   - 종료 코드 0, `passed=7`, `failed=0`. 기본 1,200개와 경계 300개 표본 전부 통과. 의도적으로 끊은 평지 검출 대조군도 통과했다.
   - 기본 표본의 최소 정사각 평지: 분지 25, 골짜기 19, 산기슭 28칸(80×80 검사 기준).

한계: 위 지형 검사는 중앙 배치와 중립 바닥의 순수 계산을 확인한다. 실제 타일의 물·기존 지형과 결합한 결과, 새 gully의 네이티브 외관, 장기 런타임 안정성 및 미적 만족을 입증하지 않는다. live provider 품질도 이 검토에서 확인하지 않았다. 원본 게임·API·외부 Claude 호출은 실행하지 않았다.
