## 2026-03-21 24:30 -- LLM 시스템 프롬프트 slope/split -> ridge 전면 업데이트

### 맥락
PLAN_ELEVATION_REDESIGN.md에서 설계한 ridge shape를 LLM 시스템 프롬프트에 실제 반영하는 작업.
프롬프트에 slope/split이 남아있으면 LLM이 여전히 slope을 생성할 수 있으므로 완전 제거 필수.

### 분석/수행 내용

**수정 범위 (Dialog_TextToMap.cs, BuildSystemPrompt 메서드 내)**:

1. **JSON schema enum** (한/영 각 1곳):
   - `slope|radial|split|bump|noise|ring` → `ridge|radial|bump|noise|ring`
   - `gap` 필드 제거 (split 전용이었음), `fade`/`noise_amount` 필드 추가

2. **elevation_shapes 가이드** (한/영 각 1곳):
   - slope 설명 삭제, split 설명 삭제
   - ridge 설명 추가: direction, strength, fade, noise_amount 파라미터 설명
   - 조합 예시: "양쪽에 산" = [ridge(left), ridge(right)]
   - 산맥 패턴: ridge(fade=small, noise_amount=high)

3. **규칙 섹션** (한/영 각 1곳):
   - `산맥=split+negative_strong+gap:medium` → `산맥=ridge(fade=small, noise_amount=high)`
   - `경사면=slope` → `한쪽에 산=ridge`

4. **few-shot 예시** (한/영 x 내륙/해안 = 4곳):
   - 내륙 예시5: split(대각선 산맥) → ridge(왼쪽 산)
   - 해안 예시2: slope(left) → ridge(left, fade=medium, noise_amount=medium)

5. **JSON 파싱** (1곳, line ~1046):
   - ElevationShape 생성 시 `fade`, `noise_amount` 필드 파싱 추가

6. **modExample 직렬화** (2곳, firstShape + others):
   - 동적 수정 예시 생성 시 fade/noise_amount 포함

**BuildCurrentParamsText (MapGenParams.cs)**:
- 확인 결과 이미 fade/noise_amount 포함 (line 684-685). 변경 불필요.

### 발견/이슈
- JSON schema에서 `gap` 필드는 split 전용이었으나, 파싱 코드(line 1053)에는 남겨둠 (레거시 호환). 프롬프트 schema에서만 제거.
- composite 타입은 이전 세션에서 추가되었으나 이번 수정 범위에 포함되지 않음 (composite는 slope/split과 무관).
- 대각선 산맥 few-shot은 ridge 1개로는 정확히 표현 불가 (ridge는 8방향 + 각도 지원이므로 direction=top_left로 대각선 산 가능하나, split의 "축 분할" 개념과는 다름). 설계 문서상 ridge 조합으로 대체하는 것이 의도이므로 예시를 "왼쪽에 산"으로 변경.

---

## 2026-03-21 23:50 -- 산/elevation 아키텍처 재설계 (ridge shape 도입)

### 맥락
MapGenAI의 산/언덕 생성 시스템에 3가지 근본적 문제가 있었음:
1. slope 상쇄: slope(left) + slope(right) = 0 (선형 함수의 합은 선형)
2. split의 Max 덮어쓰기: grid[cell] = Max(grid[cell], 0.85f)가 Perlin 패턴 소실
3. 바닐라 비유사성: 바닐라는 Perlin+DistFromAxis(Add), MapGenAI는 slope(선형)/bump(가우시안)/radial(거리)

### 분석/수행 내용

#### 접근법 분석 (4개)

**A (DistFromEdge)**: 바닐라 DistFromAxis 확장. 거리 기반이라 상쇄 불가. 하지만 디테일 부족 -- 산이 "벽"처럼 보일 위험.

**B (Perlin 마스크)**: Perlin * mask(x). 바닐라와 가장 유사하나, Postfix에서 구현 불가 (base Perlin은 이미 GenStep에서 생성됨). Transpiler 필요해서 호환성/복잡도 문제.

**C (하이브리드)**: DistFromEdge로 위치 + 별도 Perlin으로 디테일. 가장 자연스러운 결과. Postfix에서 구현 가능. **채택**.

**D (sigmoid)**: slope의 선형을 sigmoid로 변경. 그러나 sigmoid(x) + sigmoid(-x) = 1.0 (대칭성). 상쇄가 "상수"로 바뀔 뿐, 평지 문제 해결 안 됨. **탈락**.

#### 핵심 통찰: 왜 C가 상쇄를 해결하는가

기존 slope의 문제는 `f(x) + f(-x) = 0`인 선형 함수를 사용한 것.
ridge의 핵심은 smoothstep 프로파일이 **0~1 범위** (비음수)이라는 점:
```
ridge(left):  profile = smoothstep(t)     -- 0~1, 왼쪽이 높음
ridge(right): profile = smoothstep(-t)    -- 0~1, 오른쪽이 높음
합산: smoothstep(t) + smoothstep(-t)      -- 양쪽이 높고 가운데가 낮음 (골짜기)
```
비음수 함수 2개를 더하면 절대로 0이 될 수 없으므로 상쇄가 구조적으로 불가능.

#### 새로운 shape: ridge

slope과 split을 모두 대체하는 단일 프리미티브:
- 수학: smoothstep(edge0, edge1, t) 프로파일 * Perlin noise 변조
- 새 파라미터: fade (산 범위), noise_amount (디테일 정도)
- 기존 direction/strength 파라미터 그대로 사용

#### 바닐라 코드 참조

GenStep_ElevationFertility.cs:
- Perlin(freq=0.021, lac=2.0, pers=0.5, oct=6)
- hilliness factor: Flat*0.8 ~ Impassable*1.2
- DistFromAxis(span=0.42) -> Clamp -> Invert -> **Add** (Max가 아님!)

Map Designer MountainSettingsPatch.cs:
- Side (slope): AxisAsValueX + Rotate -- 우리 slope과 유사하나 다른 수학
- Split: abs(axis) - gap -- 양쪽 산, Add 방식
- Radial: distance - centerSize -- 우리 radial과 동일
- Donut: abs(distance - donutRadius) -- 우리 ring과 유사

#### 레거시 호환 설계

slope/split을 코드에서 삭제하지 않고 레거시 디스패처로 변환:
- slope -> ridge(같은 direction/strength, fade=medium, noise_amount=medium)
- split(산맥) -> 축에 수직으로 2개 ridge
- split(협곡) -> 2개 ridge

### 발견/이슈
- sigmoid 접근(D)은 언뜻 상쇄를 해결할 것 같지만, sigmoid(x)+sigmoid(-x)=1이라 여전히 실패. 이는 모든 대칭 단조함수에 해당하는 수학적 성질.
- Map Designer의 Split은 abs(axis) - gap으로 Add 방식이라 Perlin 패턴을 보존함. 우리 split의 Max 방식과 근본적으로 다름.
- ridge의 Perlin noise는 Verse.Noise.Perlin(freq=0.035, 4옥타브)를 사용. freq=0.035는 바닐라 freq=0.021보다 약간 높아 더 세밀한 디테일 제공 (바닐라 base noise와 중첩 시 자연스럽도록).
- fade 파라미터의 profileStart 매핑: fade=0.5 -> profileStart=0.0 (중앙부터 산). 이 매핑이 직관적인지 게임 내 테스트로 확인 필요.

---

## 2026-03-21 23:30 -- hills/elevation_shapes 상호작용 아키텍처 설계

### 맥락
hills 변경 시 LLM이 시스템 프롬프트의 기존 elevation_shapes를 그대로 복사해서 보내는 버그. Apply()는 elevation_shapes가 non-null이면 그대로 사용하므로, hills를 바꿔도 이전 지형이 유지됨. 코드 레벨에서 이를 보장하는 아키텍처가 필요.

### 분석/수행 내용

#### 옵션 분석
- **A (hills 변경 시 전부 삭제)**: 복합 지형(호수 등) 파괴. 탈락.
- **B (auto-generated 플래그)**: ElevationShape에 boolean 추가 필요. 직렬화/undo/프롬프트 표시 전부 영향. 복잡성 대비 이점 부족. 탈락.
- **C (패턴 매칭으로 auto-generated shapes만 교체)**: 유망하지만 "어떤 shape가 auto-generated인지" 판별이 핵심.
- **D (hills 항상 우선)**: 근본적으로 맞지만, LLM이 보낸 shapes와 충돌 시 처리가 모호.

#### 하이브리드 시도 (D+)
hills가 항상 base shape를 주입하되, LLM shapes에서 OLD hills auto-shape를 제거하는 방식. 문제: "LLM이 복사한 stale shape"와 "유저가 의도적으로 만든 동일 shape" 구분 불가.

#### 최종 결론: Option C(refined)
핵심 통찰: `slope(left)는 그것이 누가 만들었든 "hills=left"와 동일`. auto-generated와 user-specified의 구분은 이 특정 패턴에서는 무의미. hills-slot shapes는 6개 패턴으로 정의 가능:
- slope(cardinal, no-fill) -- 4개
- bump(center, no-fill) -- 1개
- radial(no-fill) -- 1개

대각선 slope(top_left 등), bump+water, split, noise, ring은 절대 hills-slot이 아님.

#### Apply() 로직 설계
1. shapes 명시: Clear + LLM shapes 추가
2. hills 변경+shapes 미전송: RemoveAll(IsHillsSlotShape) -- 커스텀만 보존
3. hills 미변경+shapes 미전송: 아무것도 안 함
4. Auto-inject: hills != "none"이고 (hillsChanged || shapesExplicit)이면 RemoveAll + Insert(0, autoShape). 아니면 hills-slot shape가 없을 때만 Insert.

#### 시스템 프롬프트 변경안
1. BuildCurrentParamsText()에서 hills-slot shapes만 있으면 elevation_shapes 섹션 비표시 (LLM 복사 원천 차단)
2. elevation_shapes 가이드에 "hills가 base layer, shapes는 custom layer" 설명 추가
3. 수정 예시에서 hills-slot shapes를 포함하지 않도록 변경

### 발견/이슈
- 대각선 slope(top_left 등)은 hills="left"와 전혀 다른 지형이므로 hills-slot에 포함시키면 안 됨
- radial은 항상 hills-slot으로 처리. 유저가 radial을 직접 만들더라도 hills="edges"와 동일 의미
- auto-inject에서 "hills 미변경 + shapes 미전송" 경로는 기존 shapes를 건드리지 않아야 함 (시나리오 #10)
- IsHillsSlotShape()의 판별 기준이 전체 설계의 정확성을 결정하는 핵심 요소

---

## 2026-03-21 -- composite(v2) anchor 기반 프롬프트 추가

### 맥락
PLAN_SHAPES_V2.md 기반 작업. v1 CSG는 LLM이 [0.28, 0.64] 같은 숫자 좌표를 직접 출력해야 해서 정밀도 부족 문제가 있었음.
v2는 LLM이 pos("center"), anchor("face"), at("top_left"), size("large") 같은 의미적 값만 출력하고, 코드가 실제 좌표를 계산하는 방식.
프리미티브는 4종(circle, rect, ngon, star)으로 축소. 기존 코드는 한 글자도 수정하지 않고 추가만 수행.

### 분석/수행 내용
1. params 스키마: 기존 elevation_shapes type enum `slope|radial|split|bump|noise|ring` 뒤에 `|composite` 추가. composite 전용 필드(shapes, compose) 스키마 추가. 한/영 모두.
2. composite 가이드: 기존 ring 설명 바로 뒤, "단순 요청" 줄 바로 앞에 삽입. 도형 4종 + 위치(pos/anchor+at) + 크기(의미적) + 연산 설명 + 하트/고양이 조합 예시.
3. few-shot 예시:
   - 내륙 한국어: 예시8(별 모양 언덕) + 예시9(하트 모양 호수) 추가
   - 내륙 영어: Ex8(star-shaped hill) + Ex9(heart-shaped lake) 추가
   - 해안 한국어: 예시6(별 모양 언덕) 추가
   - 해안 영어: Ex6(star-shaped hill) 추가
4. 검증: straight_river 카운트 7(불변), hills 카운트 31(불변), 빌드 성공.

### 발견/이슈
- v1과 v2 composite 프롬프트가 현재 공존하는 상태. v1 composite 프롬프트(좌표 직접 지정 방식)가 이전 세션에서 추가되어 있음. 향후 v1 제거 또는 통합 필요.
- ParseParams()에서 composite JSON의 shapes/compose 배열을 아직 파싱하지 않음. C# 백엔드(Stage 1) 구현 시 함께 추가 필요.

---

## 2026-03-21 -- composite(CSG) 프롬프트 엔지니어링

### 맥락
PLAN_SHAPES.md Stage 2 작업. heightmap(digit grid) 방식은 LLM이 grid를 정확하게 그리지 못하는 문제가 있었음.
CSG(Constructive Solid Geometry) 방식으로 전환: LLM은 기본 도형(원/삼각형/별 등)의 좌표와 불리언 연산(union/sub)만 출력하고, C#이 SDF로 래스터라이즈.

### 수행 내용

#### 1. params 스키마 업데이트 (한/영)
- type enum: `heightmap` 뒤에 `|composite` 추가
- 새 필드: `shapes`(도형 배열), `compose`(연산 체인 배열) 추가
- shapes: `{id, prim, center, r, r2, n, w, h, size, verts, rot}`
- compose: `{op, s, a, b, from, out, k, e, f}`

#### 2. heightmap 가이드 -> composite 가이드 (한/영)
- 기존 heightmap 설명(grid 문자열, bilinear 보간, grid 크기 가이드) 전부 삭제
- composite 설명으로 교체: 도형 7종, 연산 4종, k(smooth), 좌표계 설명
- grid 크기 가이드(8~16행)도 삭제 (composite에 해당 없음)

#### 3. 예시 교체 (한/영)
기존 heightmap 예시 5종 -> composite 예시 5종:
- 원형 언덕 -> 별 언덕 (star prim, add op)
- 하트 호수 -> 하트 호수 (heart prim, add op, e:-0.5)
- 대각선 산맥 -> 초승달 호수 (circle 2개, sub+add)
- 삼각형 산 -> L자 산맥 (rect 2개, union+add)
- 별 모양 언덕 -> 삼각형 산 (tri prim, add op)

#### 4. few-shot 교체
내륙:
- 예시8: 별 모양 언덕 (heightmap grid -> composite star)
- 예시9: 하트 모양 호수 -> 고양이 얼굴 호수 (5도형 + union/sub 체인) -- 복합 예시
- 예시10: 삼각형 산 (heightmap grid -> composite tri)

해안:
- 예시6: 삼각형 산 -> 별 모양 언덕 (composite star)

영어도 동일 패턴.

#### 5. 규칙 업데이트 (한/영)
- "heightmap으로 최대한 비슷하게 시도하라" -> "composite로 최대한 비슷하게 시도하라"
- "고양이 얼굴 윤곽 grid" -> "고양이 얼굴 composite"

#### 6. 보존 확인
- 기존 primitive 6종 (slope/radial/split/bump/noise/ring) 가이드: 변경 없음
- heightmap은 type enum에 여전히 존재 (기존 호환성). 가이드만 비활성화.
- C# 파싱 코드: 수정 없음 (별도 에이전트 담당)

### 발견/이슈
- heightmap이 type enum에 남아 있는 것은 의도적: 기존 세이브/설정 호환성 + 고급 사용자가 직접 grid 지정 가능
- Stage 1(C# SDF 래스터라이저)이 완료되어야 composite가 실제로 동작함
- LLM이 composite JSON을 올바른 구조로 생성하는지 Stage 3 테스트 필요

---

