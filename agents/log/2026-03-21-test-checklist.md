# 인게임 테스트 체크리스트 — 2026-03-21

**작성자**: rimworld-test-engineer
**목적**: elevation_shapes 프롬프트 개편 + BuildCurrentParamsText 강화 + Ring Gaussian 수정 후 회귀 검증
**전제**: 빌드 성공 확인됨. 아래는 게임 실행 없이 검증 불가능한 항목만 포함.
**테스트 순서**: 단순 → 복합 순서로 진행. 앞 항목 실패 시 뒤 항목 진행 의미 없음.

---

## 사전 준비

- 온대림(TemperateForest) 내륙 타일 1개 선택 (강 없음, 해안 없음, 소규모 언덕)
- 온대림(TemperateForest) 내륙 타일 1개 선택 (강 있음, 해안 없음, 소규모 언덕) — #5, #8용
- Gemini 또는 GPT-4o 프로바이더로 테스트 시작
- 각 테스트 후 **맵 프리뷰**와 **게임 로그(`[MapGenAI]` 접두사)** 둘 다 확인

---

## 그룹 A — 단순 요청 회귀 (가장 중요한 그룹)

### TC-01 [P1] 단순 산 요청 → single bump

**입력**: `산 하나 추가해줘`
**타일**: 내륙, 강 없음

기대 결과:
- `action: generate`
- `elevation_shapes` 배열에 shape가 **1개** (bump 또는 radial 하나)
- `type: "bump"` 또는 `type: "radial"` 중 하나, **여러 개의 bump 조합 없음**

실패 판정:
- `elevation_shapes`에 3개 이상의 shape가 포함된 경우
- bump 5개로 "별 모양"처럼 복잡한 패턴이 나오는 경우
- `action: ask`가 아닌 상황에서 `elevation_shapes` 자체가 없는 경우

---

### TC-02 [P1] 단순 호수 요청 → single bump with fill:water

**입력**: `가운데 호수 하나 만들어줘`
**타일**: 내륙, 강 없음

기대 결과:
- `action: generate`
- `elevation_shapes`: `[{"type":"bump","position":"center","fill":"water",...}]`
- shape 수 **1~2개** (호수 하나 + 선택적 지형 보완 정도)

실패 판정:
- `fill:"water"` 없이 bump만 나오는 경우
- 3개 이상의 shape로 복잡한 패턴이 생성되는 경우
- `mutators`에 `Lake`를 넣고 `elevation_shapes`를 누락하는 경우 (mutator Lake와 elevation_shapes bump+fill은 별개)

---

## 그룹 B — Ring 버그 수정 확인 (핵심 버그 픽스)

### TC-03 [P1] 빈 맵에서 ring 요청 → 완전한 원형 능선

**입력**: `가운데 원형 산맥 만들어줘` (이전 맵 파라미터 없는 초기 상태)
**타일**: 내륙, 강 없음

기대 결과:
- `action: generate`
- `elevation_shapes`: `[{"type":"ring","position":"center",...}]`
- 맵 프리뷰에서 중앙 기준 **대칭적인 원형** 능선 확인
- 한쪽만 높은 초승달 형태가 **아님**

실패 판정:
- 맵 프리뷰에서 한쪽만 솟아오른 초승달/반달 형태
- `elevation_shapes`에 ring 대신 bump 여러 개로 조합하는 경우

---

### TC-04 [P1] 기존 산이 있을 때 ring 추가 → 기존 산 유지

**전제**: TC-01 또는 TC-03 이후 — 이미 `elevation_shapes`에 bump 1개가 적용된 상태
**입력**: `여기에 도넛 모양 산맥도 추가해줘`

기대 결과:
- `action: generate`
- `elevation_shapes`: 기존 bump **그대로 포함** + 새 ring **추가** (총 2개)
- 맵 프리뷰에서 기존 bump 언덕이 **사라지지 않음**
- ring 추가 후에도 bump 위치에 지형이 남아있음

실패 판정:
- `elevation_shapes`에 ring만 있고 기존 bump가 **삭제된** 경우
- 맵 프리뷰에서 기존 지형이 평탄해지거나 사라진 경우
- ring이 기존 지형을 음수 elevation으로 덮어쓰는 경우 (로그에서 `elevation_shapes=1개`로 줄어들면 실패)

---

## 그룹 C — 기존 shapes 보존 (BuildCurrentParamsText 강화)

### TC-05 [P1] 맵 수정 시 기존 shapes 유지 + 새 shape 추가

**전제**: 먼저 `bump([0.5,0.5], fill=water, medium)`이 적용된 상태 (TC-02 결과 또는 수동 생성)
**입력**: `왼쪽에 언덕도 추가해줘`

기대 결과:
- `elevation_shapes`: 기존 호수 bump **포함** + 새 언덕 bump **추가** (총 2개)
- 로그에서 `elevation_shapes=2개` 확인
- 맵 프리뷰에서 호수와 언덕 **둘 다** 표시됨

실패 판정:
- `elevation_shapes`에 새 언덕 bump만 있고 기존 호수가 **없는** 경우
- `elevation_shapes`가 아예 없고 기존 shapes가 유지되지 않는 경우
- 로그에서 `elevation_shapes=1개`로 줄어드는 경우

---

### TC-06 [P1] 특정 shape 제거 요청

**전제**: TC-05 이후 — 호수와 언덕 bump 2개가 있는 상태
**입력**: `호수만 없애줘`

기대 결과:
- `action: generate`
- `elevation_shapes`: 언덕 bump **만** 남고 호수 bump **제거**
- 로그에서 `elevation_shapes=1개`
- 맵 프리뷰에서 호수 사라지고 언덕만 남음

실패 판정:
- 둘 다 사라져 `elevation_shapes:[]`인 경우
- 둘 다 유지된 경우 (요청 무시)
- `elevation_shapes` 필드 자체가 없는 경우 (기존 shapes 전체 유지됨 — 이건 C# 쪽에서 null 처리로 유지하지만, 요청을 무시한 것이므로 실패)

---

### TC-07 [P2] 완전 초기화 요청

**전제**: elevation_shapes가 3개 이상 있는 상태
**입력**: `지형을 처음부터 다시 해줘, 평평하게`

기대 결과:
- `action: generate`
- `elevation_shapes: []` (빈 배열로 명시)
- `hills: "none"`, `hill_amount: 0.1`
- 맵 프리뷰에서 지형이 평탄해짐

실패 판정:
- `elevation_shapes`가 없고 기존 shapes가 유지된 경우
- `elevation_shapes: []` 없이 `hills: none`만 있는 경우 (평탄화 불완전)

---

## 그룹 D — 복합 형태 (명시적 요청에만 multi-bump)

### TC-08 [P2] U자형 산 명시 요청 → multi-bump

**입력**: `U자 모양으로 산을 배치해줘, 왼쪽 오른쪽 위쪽에 산이 있고 아래는 트여있게`
**타일**: 내륙, 강 없음

기대 결과:
- `action: generate`
- `elevation_shapes`: 3개 bump — 대략 `[0.15,0.5]`, `[0.5,0.85]`, `[0.85,0.5]` 근처
- 프롬프트 가이드의 U자 레시피와 유사한 배치

실패 판정:
- bump 1개로만 나오는 경우 (단순화 과적용)
- 5개 이상으로 과도하게 복잡해진 경우
- `type: "ring"`이나 `type: "radial"`만으로 처리한 경우

---

### TC-09 [P2] 초승달 호수 명시 요청

**입력**: `초승달 모양 호수 만들어줘`

기대 결과:
- `action: generate`
- `elevation_shapes`: 2개
  - bump + `fill:"water"` (큰 원형 호수)
  - bump + `strength: "negative_..."` (한쪽을 깎아 초승달 형태)
- 프롬프트 가이드의 초승달 레시피 참조

실패 판정:
- `fill:"water"` 없는 경우
- shape 1개만 나와 단순 원형 호수가 된 경우
- `negative_strength` 없이 모양이 안 나오는 경우

---

## 그룹 E — 프로바이더별 일관성

### TC-10 [P1] Gemini 기준 통과한 TC-01 입력을 다른 프로바이더로 반복

**입력**: `산 하나 추가해줘` (TC-01과 동일)
**테스트 대상**: 설정된 두 번째 프로바이더 (GPT-4o 또는 DeepSeek)

기대 결과:
- TC-01과 동일한 판정 기준 적용
- single bump 또는 single radial

실패 판정:
- TC-01은 통과했으나 이 프로바이더에서 multi-bump 복잡 패턴이 나오는 경우
- JSON 파싱 오류 또는 `action` 필드 없는 응답

---

### TC-11 [P1] Gemini 기준 통과한 TC-05 입력을 다른 프로바이더로 반복

**전제**: 해당 프로바이더로 먼저 TC-02를 실행해 호수 상태 만들기
**입력**: `왼쪽에 언덕도 추가해줘`

기대 결과:
- TC-05와 동일한 판정 기준
- 기존 shapes 보존 + 새 shape 추가

실패 판정:
- 기존 호수가 삭제된 경우
- `elevation_shapes` 없이 `hills` 파라미터만 사용하는 경우

---

## 그룹 F — 경계 케이스 (기존 테스트 보완)

### TC-12 [P2] elevation_shapes 있는 상태에서 elevation_shapes 무관한 수정

**전제**: bump 1개가 적용된 상태
**입력**: `나무를 좀 더 많이 해줘`

기대 결과:
- `action: generate`
- `elevation_shapes`: 기존 bump **그대로 유지** (변경 없음)
- `vegetation_density > 1.0` 증가

실패 판정:
- `elevation_shapes`가 `null`이 되거나 `[]`로 초기화된 경우
- C# 쪽에서 `data.elevation_shapes == null`이면 기존 유지 로직 동작해야 함 — LLM이 `elevation_shapes` 키를 **완전히 생략**해야 정상

---

### TC-13 [P2] 분화구 호수 요청 (ring + bump 조합)

**입력**: `분화구 모양의 호수 만들어줘`

기대 결과:
- `action: generate`
- `elevation_shapes`: `ring` 1개 + `bump+fill:water` 1개 (총 2개)
- 맵 프리뷰: 원형 능선 + 중앙 호수

실패 판정:
- ring만 나오고 호수(fill:water)가 없는 경우
- bump만 나오고 ring 능선이 없는 경우
- shape 1개만 나오는 경우

---

## 판정 기준 요약

| 그룹 | 핵심 확인 사항 | 확인 방법 |
|------|----------------|-----------|
| A (TC-01, 02) | 단순 요청 = single shape | LLM JSON의 elevation_shapes 배열 길이 |
| B (TC-03, 04) | Ring Gaussian 수정 동작 | 맵 프리뷰 시각 확인 + elevation_shapes 배열 |
| C (TC-05, 06, 07) | 기존 shapes 보존/제거 | 로그의 `elevation_shapes=N개` + 맵 프리뷰 |
| D (TC-08, 09) | 명시적 복합 요청만 multi-bump | elevation_shapes 배열 길이 + 위치값 |
| E (TC-10, 11) | 프로바이더 간 일관성 | 동일 입력 → 동일 판정 기준 |
| F (TC-12, 13) | 무관한 수정 시 shapes 유지 | C# 로그 `elevation_shapes=N개` |

---

## 빠른 실행 순서 (시간 부족 시)

P1만 먼저: **TC-01 → TC-04 → TC-05 → TC-10**
4개면 핵심 회귀 90% 커버 가능.

---

## 버그 발견 시 기록 양식

```
TC-XX 실패
- 프로바이더: [Gemini/GPT-4o/DeepSeek/...]
- 입력: "..."
- 실제 LLM JSON 응답 (elevation_shapes 부분):
  ...
- 로그 출력:
  [MapGenAI] 파라미터 적용: ... elevation_shapes=N개
- 판정: [단순 요청에 multi-bump / shapes 삭제 / ring 초승달 / ...]
```
