## 2026-03-21 21:30 — MapGenParams.Apply() 유닛 테스트 인프라 + 버그 재현

### 맥락
MapGenParams.Apply()에 hills 변경 시 elevation_shapes가 이전 값으로 남는 버그가 보고됨.
게임을 실행하지 않고 C# 유닛 테스트로 버그를 재현하고, 수정 후 회귀 검증에 사용할 테스트 인프라 구축.

### 분석/수행 내용

**1. MapGenParams.cs 의존성 분석**
- `using MapGenAI.LLM` — import만 있고 실제 사용 없음. 빈 namespace 스텁으로 해결.
- `Mathf.Clamp`, `Clamp01`, `Min`, `Repeat` — 순수 수학 함수. 직접 구현.
- `Verse.Log`, `Find.WorldSelector`, `Find.WorldGrid` — Find는 null 반환으로 ApplyMutatorsToWorldTile() early return 유도.
- `DefDatabase<TileMutatorDef>` — null 반환으로 충분 (WorldSelector가 null이라 도달 안 함).
- `MapPreview.WorldInterfaceManager.RefreshPreview()` — no-op 스텁. try-catch로 감싸져 있어 안전.

**2. 테스트 실행 방식**
- 기존 TestBench.cs의 Main에 서브커맨드 분기 추가: `dotnet run -- mdp`
- NUnit 없이 순수 콘솔 출력 방식 (기존 TestBench 패턴 유지)

**3. 버그 근본 원인 분석**

Apply() 코드 흐름 (line 310-348):
```
1. if (data.elevation_shapes != null) → Clear + 교체
2. if (Hills != "none" && ElevationShapes.Count == 0) → 자동 변환
```

문제:
- `elevation_shapes==null` (LLM 생략) → 기존 shapes 무조건 유지
- hills가 left→right로 바뀌어도 shapes는 여전히 slope(left)
- 자동 변환(#2)은 `Count==0`일 때만 발동하므로, 기존 shapes가 있으면 절대 갱신 안 됨

**4. 발견된 FAIL 3건**

| # | 시나리오 | 원인 |
|---|---------|------|
| 2 | hills=left→right, shapes=null | shapes가 null이면 기존 유지 → slope(left) 잔존 |
| 5 | elevation_shapes=[], hills=left | 빈 배열로 Clear 후 자동 변환이 slope(left) 재생성 |
| 6 | hills=left→none, shapes=null | shapes null → 기존 slope(left) 유지, none이어도 안 지워짐 |

#2와 #6은 동일 근본 원인: `elevation_shapes==null`일 때 hills 변경을 반영하지 않음.
#5는 설계 이슈: 명시적 빈 배열 의도가 hills 자동변환으로 덮어씌워짐.

**5. 수정 방향 제안**
- 이전 hills 값을 저장하고, `elevation_shapes==null && hills != previousHills`이면 기존 shapes를 Clear
- 또는 자동 변환된 shapes인지 표시하는 플래그를 두어, 자동 변환된 것만 갱신 대상으로 삼기
- #5는 `elevation_shapes`가 빈 배열(non-null, Count==0)일 때 자동 변환을 스킵하는 boolean 플래그 필요

### 발견/이슈
- 모든 nullable 경고는 MapGenParams.cs 원본 코드의 `Nullable enable` 비호환에서 발생 (기존 코드가 .NET Framework 4.7.2 대상이라 nullable 미지원). 테스트 프로젝트에서는 무해.
- 이전 SDF 테스트 세션의 UnityShim/VerseShim 파일이 디스크에 없었음 (삭제된 것으로 보임). 새로 작성.

---

(아직 작업 기록 없음)

