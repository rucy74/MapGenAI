# MapGenAI — 자유 형태 지형 시스템 설계 (CSG+SDF)

## 핵심 원칙 (절대 위반 금지)

> **LLM은 "무엇을 조합할지"만 결정한다. 렌더링은 코드가 한다.**

- LLM 출력: 기본 도형 목록 + 불리언 조합 순서 (CSG 트리)
- LLM 출력 금지: digit grid, 픽셀 배열, 수학 수식, SDF 함수
- 좌표: 정규화 [0,1] 범위만. 맵 크기는 코드가 처리
- 렌더링: C#이 SDF 래스터라이저로 매끄러운 지형 생성

리서치 근거:
- Chain-of-Symbol (2023): 기호 표기 시 LLM 공간 추론 정확도 31%→93% (60%p 향상)
- LayoutGPT (NeurIPS 2023): 구조화 JSON이 텍스트 대비 20-40% 우수
- AIDL (2025): LLM은 의미 추론, solver는 정밀 기하 — 역할 분리 필수
- Don't Mesh with Me (2024): LLM CSG 생성 98% 구문 정확, 93% 논리 정확
- ShapeCraft (NeurIPS 2025): 구조 그래프 분해 > 직접 좌표 생성

---

## 아키텍처

```
사용자 입력 ("고양이 얼굴 호수 만들어줘")
        ↓
[Phase 1] LLM → CSG JSON (~200-400 토큰)
  - shapes: 기본 도형 목록 (id + type + params)
  - compose: 불리언 연산 체인 (union/subtract + 최종 elevation)
        ↓
[Phase 2] C# 파이프라인
  ParseCSG       → JSON 파싱 → ShapeTree 객체
  BuildSdfTree   → 도형별 SDF 함수 생성 + 연산 체인 조합
  RasterizeToMap  → 맵 셀마다 SDF 평가 → smoothstep → elevation 적용
  ApplyWater     → elevation < 0 영역을 물 지형으로 변환
```

기존 시스템과의 관계:
- elevation_shapes 배열에 `type:"composite"` 추가
- 기존 7종 primitive (slope/radial/split/bump/noise/ring/heightmap) 100% 보존
- composite와 기존 primitive는 additive로 조합 가능

---

## LLM 출력 포맷

### 최소 예시 (단일 도형)
```json
{
  "type": "composite",
  "shapes": [
    {"id": "s", "prim": "star", "center": [0.5, 0.5], "r": 0.35, "r2": 0.15, "n": 5}
  ],
  "compose": [
    {"op": "add", "s": "s", "e": 0.8}
  ]
}
```
→ 약 100 토큰. 5각 별 언덕.

### 복합 예시 (고양이 얼굴 호수)
```json
{
  "type": "composite",
  "shapes": [
    {"id": "f", "prim": "circle", "center": [0.5, 0.45], "r": 0.25},
    {"id": "eL", "prim": "tri", "verts": [[0.28,0.64],[0.35,0.78],[0.2,0.78]]},
    {"id": "eR", "prim": "tri", "verts": [[0.72,0.64],[0.65,0.78],[0.8,0.78]]},
    {"id": "yL", "prim": "circle", "center": [0.4, 0.5], "r": 0.05},
    {"id": "yR", "prim": "circle", "center": [0.6, 0.5], "r": 0.05}
  ],
  "compose": [
    {"op": "union", "a": "f", "b": "eL", "out": "h1", "k": 0.03},
    {"op": "union", "a": "h1", "b": "eR", "out": "h2", "k": 0.03},
    {"op": "sub", "a": "yL", "from": "h2", "out": "h3"},
    {"op": "sub", "a": "yR", "from": "h3", "out": "cat"},
    {"op": "add", "s": "cat", "e": -0.5, "f": 0.04}
  ]
}
```
→ 약 350 토큰. 머리(원) + 귀x2(삼각형) - 눈x2(원) = 고양이 얼굴 호수.

---

## 필드 정의

### ElevationShape (기존 확장)

기존 ElevationShape에 추가되는 필드:

| 필드 | 타입 | 설명 |
|------|------|------|
| `shapes` | ShapePrimitive[] | CSG 기본 도형 목록 |
| `compose` | ComposeOp[] | 불리언 연산 체인 |

type="composite"일 때만 사용. 기존 필드(direction, strength 등)는 무시.

### ShapePrimitive 필드

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `id` | string | ✓ | 고유 식별자 (compose에서 참조) |
| `prim` | string | ✓ | 도형 타입 (아래 목록) |
| `center` | float[2] | △ | 중심 [x, z]. 정규화 [0,1]. circle/ellipse/rect/star/heart에 필수 |
| `r` | float | △ | 반지름. circle/star에 필수 |
| `r2` | float | - | 내부 반지름. star에 사용 (없으면 r*0.4) |
| `n` | int | - | 꼭지점 수. star에 사용 (기본 5) |
| `w` | float | △ | 너비. rect/ellipse에 필수 |
| `h` | float | △ | 높이. rect/ellipse에 필수 |
| `size` | float | △ | 크기. heart에 필수 |
| `verts` | float[N][2] | △ | 꼭짓점 배열. tri/poly에 필수 |
| `rot` | float | - | 회전 (도 단위, 기본 0) |

### 도형 타입 (prim)

| prim | 필수 파라미터 | SDF 수식 | 설명 |
|------|-------------|---------|------|
| `circle` | center, r | `length(p-c) - r` | 원 |
| `ellipse` | center, w, h | 스케일 보정 원 | 타원 |
| `rect` | center, w, h | Quilez box SDF | 직사각형 |
| `tri` | verts (3개) | Quilez triangle SDF | 삼각형 |
| `poly` | verts (N개) | 일반 다각형 SDF | 다각형 |
| `star` | center, r, n | 접힌 세그먼트 SDF | N각 별 |
| `heart` | center, size | Quilez heart SDF | 하트 |

### ComposeOp 필드

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `op` | string | ✓ | 연산 타입 (아래 목록) |
| `s` | string | △ | 단일 도형/결과 ID (add에 사용) |
| `a` | string | △ | 첫 번째 피연산자 (union/sub/inter에 사용) |
| `b` | string | △ | 두 번째 피연산자 (union에 사용) |
| `from` | string | △ | 빼기 대상 (sub에 사용: a를 from에서 뺌) |
| `out` | string | - | 결과 ID (다음 연산에서 참조. 마지막이면 생략 가능) |
| `k` | float | - | smooth 블렌딩 반경 (기본 0, 0이면 하드 엣지) |
| `e` | float | △ | elevation 값. 양수=언덕, 음수=호수. 마지막 연산에 필수 |
| `f` | float | - | falloff 반경 (기본 0.05). 가장자리 부드러움 |

### 연산 타입 (op)

| op | 의미 | SDF 수식 | 용도 |
|----|------|---------|------|
| `add` | 단일 도형 적용 | identity | 도형 하나만 쓸 때 |
| `union` | 합집합 | `min(a, b)` | 두 도형 합치기 |
| `sub` | 빼기 | `max(-a, b)` | 도형에서 구멍 뚫기 |
| `inter` | 교집합 | `max(a, b)` | 겹치는 부분만 |

k > 0이면 자동으로 smooth 버전 사용 (smooth union 등).

---

## 연산 체인 규칙

1. `compose` 배열은 **순서대로** 실행
2. 각 연산의 `out`은 이후 연산에서 참조 가능
3. 마지막 연산에 반드시 `e` (elevation) 지정
4. `f` (falloff)는 마지막 연산에만 의미 있음 (전체 형태 가장자리)
5. `sub` 연산: `a`를 `from`에서 뺌 → `from`의 형태에서 `a` 영역이 빠짐

### 연산 체인 예시 (고양이 얼굴)

```
① union(face, earL) → h1        // 얼굴 + 왼쪽 귀
② union(h1, earR) → h2          // + 오른쪽 귀
③ sub(eyeL, from: h2) → h3      // - 왼쪽 눈
④ sub(eyeR, from: h3) → cat     // - 오른쪽 눈
⑤ add(cat, e: -0.5, f: 0.04)    // 최종: 호수 (음수 elevation)
```

---

## C# 래스터라이저 (의사코드)

### SDF 프리미티브

```
SdfCircle(p, center, r):
  return length(p - center) - r

SdfRect(p, center, halfW, halfH):
  d = abs(p - center) - (halfW, halfH)
  return length(max(d, 0)) + min(max(d.x, d.y), 0)

SdfTriangle(p, v0, v1, v2):
  // Quilez 공식: 각 변까지 거리 + winding sign
  e0=v1-v0, e1=v2-v1, e2=v0-v2
  w0=p-v0, w1=p-v1, w2=p-v2
  각 변에 대한 projection clamp → 최소 거리
  sign = cross(e0,w0) * cross(e1,w1) * cross(e2,w2)
  return sqrt(minDist) * sign(sign)

SdfStar(p, center, outerR, innerR, points):
  p -= center
  segAngle = π / points
  angle = atan2(p.y, p.x) mod (2*segAngle) 접기
  → outer tip과 inner valley 사이 선분 거리 계산

SdfHeart(p, center, size):
  p = (p - center) / size, p.x = abs(p.x)
  → Quilez heart 공식 적용

SdfPolygon(p, vertices[N]):
  각 변까지 최소 거리 + winding number → sign
```

### 불리언 연산

```
OpUnion(a, b):       min(a, b)
OpSubtract(a, b):    max(-a, b)
OpIntersect(a, b):   max(a, b)

OpSmoothUnion(a, b, k):
  k *= 4
  h = max(k - abs(a-b), 0)
  return min(a,b) - h*h*0.25/k

OpSmoothSubtract(a, b, k):
  return -OpSmoothUnion(a, -b, k)
```

### 메인 래스터라이저

```
ApplyComposite(shape, map, elevGrid):
  1. shapes[] 파싱 → Dictionary<string, SdfFunc> 생성
  2. compose[] 순회:
     - 각 op에 따라 SDF 함수 조합
     - out ID로 결과 저장 → 다음 연산에서 참조
  3. 최종 SDF 함수 확정
  4. 맵 전체 순회:
     for each cell (x, z) in map:
       p = (x/mapW, z/mapH)           // 정규화
       d = finalSdf(p)                 // SDF 평가
       t = smoothstep(falloff, 0, d)   // 거리→마스크 (0~1)

       if elevation < 0 (호수):
         if t > 0.5: fertilityGrid[cell] = -2005 (깊은물)
         elif t > 0.1: fertilityGrid[cell] = -1025 (얕은물)
         elif t > 0.05: fertilityGrid[cell] = 1 (해변)
         elevGrid[cell] = min(elevGrid[cell], 0.3)
       else (언덕):
         elevGrid[cell] += t * elevation
```

### Smoothstep 함수

```
smoothstep(edge0, edge1, x):
  t = clamp((x - edge0) / (edge1 - edge0), 0, 1)
  return t * t * (3 - 2*t)
```

이 함수가 핵심 — 경계에서 0, 내부에서 1, 그 사이는 매끄러운 곡선.

---

## heightmap과의 관계

### 교체 결정: heightmap은 유지하되 비활성 권장

- heightmap C# 코드(ApplyHeightmap)는 그대로 유지 (삭제 불필요)
- 프롬프트에서 heightmap 가이드를 composite 가이드로 **교체**
- LLM에게 "모양 요청 → composite 사용"으로 가이드
- heightmap은 고급 사용자가 직접 grid를 지정할 때만 사용 (프롬프트에서 비표시)

### 기존 primitive와의 공존

composite는 elevation_shapes 배열의 한 항목:
```json
{
  "elevation_shapes": [
    {"type": "slope", "direction": "left", "strength": "medium"},
    {"type": "composite", "shapes": [...], "compose": [...]}
  ]
}
```
→ slope로 왼쪽 경사 + composite로 별 모양 언덕 = additive 조합.

---

## 프롬프트 가이드 (요약)

### LLM에게 제공할 가이드 (한국어)
```
- composite: ★자유 형태★ 기본 도형(원/삼각형/사각형/별/하트)을 조합하여 어떤 모양이든 표현.
  shapes: 도형 목록. compose: 합치기(union)/빼기(sub) 연산 체인 → 최종 형태.
  e>0 = 언덕, e<0 = 호수. 단순 요청에는 bump, 자유 형태에만 composite.

  도형: circle(center,r), rect(center,w,h), tri(verts 3개), star(center,r,r2,n), heart(center,size), poly(verts)
  연산: add(단일), union(합치기), sub(빼기, 구멍)
  k>0이면 매끄럽게 연결.

  예시:
  별 언덕: shapes:[{id:"s",prim:"star",center:[0.5,0.5],r:0.35,r2:0.15,n:5}], compose:[{op:"add",s:"s",e:0.8}]
  하트 호수: shapes:[{id:"h",prim:"heart",center:[0.5,0.45],size:0.3}], compose:[{op:"add",s:"h",e:-0.5}]
  초승달: shapes:[{id:"a",prim:"circle",center:[0.5,0.5],r:0.3},{id:"b",prim:"circle",center:[0.6,0.55],r:0.28}],
    compose:[{op:"sub",a:"b",from:"a",out:"c"},{op:"add",s:"c",e:-0.4}]
```

---

## 구현 단계

### Stage 1: C# 백엔드 (rimworld-csharp-dev)
1. SDF 프리미티브 7종 구현 (circle, ellipse, rect, tri, poly, star, heart)
2. 불리언 연산 6종 (union, sub, inter + smooth 3종)
3. ApplyComposite() 래스터라이저
4. JSON 파싱 (shapes[], compose[])
5. 빌드 검증

### Stage 2: 프롬프트 (rimworld-ai-dev)
1. BuildSystemPrompt()에 composite 가이드 추가 (한/영)
2. heightmap 가이드를 composite으로 교체
3. few-shot 예시: 별/하트/고양이/L자/초승달
4. 규칙: "모양 요청 = composite. 거절 금지."

### Stage 3: 테스트 (rimworld-test-engineer)
1. 빌드 검증
2. 회귀 테스트 (기존 6종 primitive)
3. 신규 테스트 (별/하트/고양이/L자/초승달)
4. 기존+신규 조합 테스트

---

## 참고 문헌

- Quilez, I. "2D Distance Functions" — SDF 프리미티브 정의서 (iquilezles.org)
- Quilez, I. "Smooth Minimum" — smooth boolean 연산 (iquilezles.org)
- Hu et al. "Chain-of-Symbol" (2023) — LLM 기호 추론 정확도 93%
- Feng et al. "LayoutGPT" (NeurIPS 2023) — 구조화 JSON > 텍스트
- "Don't Mesh with Me" (2024) — LLM CSG 생성 98% 구문 정확
- "ShapeCraft" (NeurIPS 2025) — 그래프 기반 형태 분해
- "AIDL" (2025) — LLM 의미 추론 + solver 기하 분리
