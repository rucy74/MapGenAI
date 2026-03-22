# rimworld-test-engineer — 현재 상태

## 마지막 작업
- 시점: 2026-03-21 21:30
- 요청: MapGenParams.Apply() 유닛 테스트 인프라 구축 + hills/elevation_shapes 버그 재현
- 수행: Unity/Verse shim 작성, MapGenParams.cs 소스 링크, 7개 시나리오 테스트 작성 및 실행
- 결과: 4 PASS / 3 FAIL — 버그 3건 재현 성공 (#2 hills 변경, #5 빈 배열 덮어쓰기, #6 hills=none 전환)

## 산출물
- `dev/Source/Tests/UnityShim.cs` — Mathf, Vector2 스텁
- `dev/Source/Tests/VerseShim.cs` — Log, Find, DefDatabase, Tile, TileMutatorDef, MapPreview 스텁
- `dev/Source/Tests/MdpApplyTests.cs` — 7개 시나리오 테스트
- `dev/Source/Tests/TextToMap.Tests.csproj` — MapGenParams.cs 소스 링크 추가
- `dev/Source/Tests/TestBench.cs` — `dotnet run -- mdp` 서브커맨드 추가

## 다음 단계
- rimworld-csharp-dev가 Apply() 버그 수정 후 테스트 재실행하여 7/7 PASS 확인
- 수정 방향: elevation_shapes==null일 때 hills 변경 감지 로직 필요

## 다른 에이전트에게
- **rimworld-csharp-dev**: Apply()에서 `elevation_shapes==null && hills가 이전과 다름` 케이스 처리 필요. 현재 코드는 shapes가 null이면 무조건 기존 유지하지만, hills가 바뀌면 기존 shapes를 클리어하고 자동 변환해야 함. #6(hills=none 전환)도 동일 원인.
- **rimworld-ai-dev**: #5 설계 이슈 — LLM이 `elevation_shapes:[]`로 명시적 평지를 보내도 hills 자동변환이 덮어씀. hills="none"도 같이 보내게 프롬프트를 조정하거나, Apply()에서 빈 배열은 자동변환을 스킵하는 플래그가 필요.
