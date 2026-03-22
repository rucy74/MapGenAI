# MDP(MapParamsData) 리팩토링 계획

> 작성: 2026-03-21
> 목표: hills 변경 시 elevation_shapes가 갱신되지 않는 버그 수정
> 원칙: **유닛 테스트 먼저 → 검증 후에만 배포**

---

## 버그 분석

### 현상
- "왼쪽에 산" → "오른쪽에 산" 요청 시, 왼쪽 산이 그대로 유지됨
- 때로는 맵 전체에 바위가 깔리거나, 산이 아예 생성 안 됨

### 근본 원인 (MapGenParams.cs:310-348)

```
1. "왼쪽에 산" → Apply(hills="left", elevation_shapes=null)
   → ElevationShapes 비어있음 → auto-convert → slope(left) ✓

2. "오른쪽에 산" → Apply(hills="right", elevation_shapes=null)
   → data.elevation_shapes==null → 기존 shapes 유지 (slope(left) 잔존)
   → ElevationShapes.Count != 0 → auto-convert 안 함
   → 결과: 왼쪽 산 그대로 ✗
```

### 원인 코드

```csharp
// Line 313: null이면 기존 유지 — 이게 문제
if (data.elevation_shapes != null)
{
    ElevationShapes.Clear();
    // ... add new shapes
}

// Line 324: 비어있을 때만 auto-convert — hills 바꿔도 안 먹힘
if (Hills != "none" && ElevationShapes.Count == 0)
{
    // ... auto-convert hills → shapes
}
```

---

## 수정 설계

### 핵심 아이디어
- `hills`가 **변경**되었고 `elevation_shapes`가 null(LLM 생략)이면 → 기존 shapes 클리어 → auto-convert 재실행
- `hills`가 **동일**하고 `elevation_shapes`가 null이면 → 기존 shapes 유지 (다른 파라미터만 변경한 경우)

### 수정 코드 (Apply 메서드)

```csharp
// 이전 hills 값 저장
string previousHills = Hills;

// 새 hills 적용
Hills = ValidHills.Contains(data.hills ?? "") ? data.hills : "none";

// ... (기존 파라미터 적용) ...

// --- ElevationShapes 파싱 ---
if (data.elevation_shapes != null)
{
    // LLM이 명시적으로 shapes를 보냄 → 교체
    ElevationShapes.Clear();
    foreach (var shape in data.elevation_shapes)
    {
        if (shape != null && !string.IsNullOrEmpty(shape.type))
            ElevationShapes.Add(shape);
    }
}
else if (Hills != previousHills)
{
    // hills가 변경됐는데 shapes를 안 보냄 → 기존 shapes 무효화
    ElevationShapes.Clear();
}

// hills → shapes auto-convert (첫 요청이든 hills 변경이든 동일 경로)
if (Hills != "none" && ElevationShapes.Count == 0)
{
    // ... 기존 auto-convert 로직 ...
}
```

### 모든 시나리오 검증

| # | 시나리오 | hills | elevation_shapes | 기대 결과 |
|---|---------|-------|-----------------|----------|
| 1 | 첫 요청 "왼쪽에 산" | "left" | null | slope(left) auto-convert |
| 2 | 변경 "오른쪽에 산" | "right" | null | slope(left) 클리어 → slope(right) auto-convert |
| 3 | 유지 "동물 더 넣어줘" | "left" (동일) | null | 기존 slope(left) 유지 ✓ |
| 4 | LLM이 shapes 직접 지정 | any | [...] | LLM shapes 사용 |
| 5 | LLM이 빈 배열 전달 | "none" | [] | shapes 비움 (평지) |
| 6 | 산 제거 "평지로" | "none" | null | hills 변경 → shapes 클리어 → auto-convert 안 함 (none) |
| 7 | 첫 요청인데 hills="none" | "none" | null | 변화 없음 |

---

## 실행 계획

### Phase 1: 테스트 인프라 (rimworld-test-engineer)
- Tests/ 프로젝트에 MapGenParams.cs + ElevationShape 소스 링크
- Unity shim (Mathf, Vector2), Verse shim (Log, Find) 작성
- `dotnet build` 성공 확인

### Phase 2: 버그 재현 테스트 (rimworld-test-engineer)
- 위 시나리오 표 7개를 유닛 테스트로 작성
- 현재 코드로 실행 → 시나리오 #2가 FAIL 확인 (버그 재현)

### Phase 3: 코드 수정 (rimworld-csharp-dev)
- Apply()에 `previousHills` 비교 로직 추가 (위 설계대로)
- 수정량: ~5줄

### Phase 4: 테스트 통과 (rimworld-test-engineer)
- 7개 시나리오 모두 PASS 확인
- 회귀: 기존 InGameTestRunner 시나리오와 충돌 없는지 확인

### Phase 5: 빌드 + 배포
- `dotnet build` (dev/Source/) 성공
- DLL을 Mods 폴더에 복사
- 게임 테스트 체크리스트 제공 (최종 시각 검증)

---

## 에이전트 배정

| Phase | 담당 에이전트 | 병렬 가능 |
|-------|-------------|----------|
| 1 | rimworld-test-engineer | - |
| 2 | rimworld-test-engineer | Phase 1 직후 |
| 3 | rimworld-csharp-dev | Phase 2 직후 |
| 4 | rimworld-test-engineer | Phase 3 직후 |
| 5 | rimworld-csharp-dev | Phase 4 직후 |

순차 실행 — 각 Phase가 이전 Phase 결과에 의존.

---

## 리스크

1. **shim 누락**: MapGenParams.Apply()가 참조하는 RimWorld API가 생각보다 많을 수 있음 → 컴파일 오류로 즉시 발견, shim 추가
2. **ToSnapshot 연쇄**: Dialog_TextToMap의 undo stack도 이 변경에 영향받을 수 있음 → 시나리오 #3(유지)이 커버
3. **LLM 프롬프트**: hills/elevation_shapes 관계가 바뀌면 시스템 프롬프트도 업데이트 필요할 수 있음 → Phase 3에서 확인
