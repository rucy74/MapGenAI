# MapGenAI — 자유 형태 지형 시스템 v2 (Anchor 기반 설계)

## 1. 핵심 원칙 (절대 위반 금지)

> **LLM은 함수를 호출하는 결정자다. 좌표 계산은 절대 하지 않는다.**

| 역할 | LLM이 하는 일 | 코드가 하는 일 |
|------|--------------|---------------|
| **도형 선택** | 어떤 프리미티브를 쓸지 결정 | SDF 함수 생성 |
| **위치** | 의미적 위치(`center`, `top_left`) 또는 anchor 참조 | 정규화 좌표 [0,1] 계산 |
| **크기** | 의미적 크기(`tiny`, `small`, `medium`, `large`, `huge`) | 맵 비율 → 실제 반지름 변환 |
| **조합** | union/sub/inter 연산 순서 결정 | SDF boolean 평가 |
| **고도** | 양수(언덕)/음수(호수) 의미 지정 | smoothstep → elevation/water 적용 |

**LLM 출력 금지 항목:**
- 숫자 좌표 (`[0.28, 0.64]` 등)
- 수학 수식, SDF 함수
- 픽셀 배열, digit grid

**이전 실패와 교훈:**

| 시도 | 방식 | 실패 원인 |
|------|------|----------|
| v0 | heightmap digit grid | LLM이 그림을 "그리게" 함 → 공간 추론 불가 |
| v1 | CSG + 좌표 직접 지정 | LLM이 `[0.28, 0.64]` 계산 → 정밀도 부족 |
| v1.1 | 모양별 전용 함수 (SdfHeart) | 확장 불가, 모든 모양마다 SDF 추가 필요 |

**해법:** blueprint_ai v2의 attach 패턴을 terrain에 적용.
LLM은 `pos("center")`, `anchor("face")`, `at("top_left")` 같은 **의미적 참조**만 출력.
코드가 참조를 해석하여 실제 좌표를 계산.

리서치 근거:
- Chain-of-Symbol (2023): 기호 표기 시 LLM 공간 추론 정확도 31%→93%
- LayoutGPT (NeurIPS 2023): 구조화 JSON이 텍스트 대비 20-40% 우수
- Don't Mesh with Me (2024): LLM CSG 생성 98% 구문 정확, 93% 논리 정확
- ShapeCraft (NeurIPS 2025): 구조 그래프 분해 > 직접 좌표 생성

---

## 2. 아키텍처 다이어그램

```
사용자 입력 ("고양이 얼굴 호수 만들어줘")
        ↓
[Phase 1] LLM → Composite JSON (~150-350 토큰)
  - shapes: 프리미티브 목록 (id + prim + pos/anchor/at + size)
  - compose: boolean 연산 체인 (union/sub + 최종 elevation)
  ※ 숫자 좌표 없음. 의미적 위치/크기만.
        ↓
[Phase 2] ShapeBuilder (C#)
  ResolvePositions  → pos/anchor/at → 실제 [0,1] 중심 좌표 계산
  ResolveSize       → size enum → 실제 반지름 계산
  BuildSdfTree      → 프리미티브별 SDF 함수 + boolean 체인 조합
        ↓
[Phase 3] SdfRasterizer (C#)
  RasterizeToMap    → 맵 셀마다 SDF 평가 → smoothstep → elevation 적용
  ApplyWater        → elevation < 0 영역을 물 지형으로 변환
```

기존 시스템과의 관계:
- elevation_shapes 배열에 `type:"composite"` 항목 추가
- 기존 6종 primitive (slope/radial/split/bump/noise/ring) 100% 보존
- composite와 기존 primitive는 additive로 조합 가능

---

## 3. LLM 출력 포맷

### 최소 예시 (단일 도형)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "s", "prim": "star", "pos": "center", "size": "large", "n": 5}
  ],
  "compose": [
    {"op": "add", "s": "s", "e": 0.8}
  ]
}
```
→ 약 80 토큰. 맵 중앙에 큰 5각 별 언덕. 좌표 없음.

### 복합 예시 (하트 호수)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "lobeL", "prim": "circle", "pos": "center", "size": "medium", "offset": "left"},
    {"id": "lobeR", "prim": "circle", "pos": "center", "size": "medium", "offset": "right"},
    {"id": "body", "prim": "ngon", "n": 3, "pos": "center", "size": "large", "rot": 180}
  ],
  "compose": [
    {"op": "union", "a": "lobeL", "b": "lobeR", "out": "top", "k": 0.03},
    {"op": "union", "a": "top", "b": "body", "out": "heart", "k": 0.05},
    {"op": "add", "s": "heart", "e": -0.5}
  ]
}
```
→ 약 250 토큰. center에 medium 원 2개(좌/우) + large 역삼각형 = 하트 호수.

### Anchor 참조 예시 (고양이 얼굴)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "face", "prim": "circle", "pos": "center", "size": "large"},
    {"id": "earL", "prim": "ngon", "n": 3, "anchor": "face", "at": "top_left", "size": "small"},
    {"id": "earR", "prim": "ngon", "n": 3, "anchor": "face", "at": "top_right", "size": "small"},
    {"id": "eyeL", "prim": "circle", "anchor": "face", "at": "inner_left", "size": "tiny"},
    {"id": "eyeR", "prim": "circle", "anchor": "face", "at": "inner_right", "size": "tiny"}
  ],
  "compose": [
    {"op": "union", "a": "face", "b": "earL", "out": "h1", "k": 0.02},
    {"op": "union", "a": "h1", "b": "earR", "out": "h2", "k": 0.02},
    {"op": "sub", "a": "eyeL", "from": "h2", "out": "h3"},
    {"op": "sub", "a": "eyeR", "from": "h3", "out": "cat"},
    {"op": "add", "s": "cat", "e": -0.5}
  ]
}
```
→ 약 350 토큰. LLM은 "face의 top_left에 작은 삼각형" 정도만 결정. 좌표는 코드가 계산.

---

## 4. 필드 정의

### ShapePrimitive 필드

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `id` | string | ✓ | 고유 식별자 (compose에서 참조) |
| `prim` | string | ✓ | 도형 타입: `circle`, `rect`, `ngon`, `star` |
| `pos` | string | △ | 의미적 위치 (anchor 미사용 시 필수). 아래 Position 테이블 참조 |
| `anchor` | string | △ | 참조할 도형 id (pos 대신 사용) |
| `at` | string | △ | anchor 기준 배치 위치 (anchor 사용 시 필수). 아래 At 테이블 참조 |
| `offset` | string | - | pos 기준 미세 이동. `left`, `right`, `up`, `down`. 아래 Offset 테이블 참조 |
| `size` | string | ✓ | 의미적 크기: `tiny`, `small`, `medium`, `large`, `huge` |
| `n` | int | △ | 꼭지점 수. `ngon`에 필수 (3=삼각형, 5=오각형, ...). `star`에 선택 (기본 5) |
| `rot` | float | - | 회전 (도 단위, 기본 0). 시계 방향 |
| `aspect` | float | - | 종횡비 (기본 1.0). `rect`에서 w/h 비율. 예: 2.0=가로가 세로의 2배 |

**위치 결정 우선순위:**
1. `anchor` + `at`가 있으면 → anchor 시스템으로 좌표 계산
2. `pos`가 있으면 → 의미적 위치 프리셋으로 좌표 결정
3. 둘 다 없으면 → 기본값 `"center"` 적용

### Position (pos) 값

기존 `ElevationShape.ParsePosition()`과 동일한 9개 프리셋:

| pos 값 | 정규화 좌표 (x, z) | 설명 |
|---------|-------------------|------|
| `center` | (0.50, 0.50) | 맵 중앙 |
| `top` | (0.50, 0.80) | 상단 중앙 |
| `bottom` | (0.50, 0.20) | 하단 중앙 |
| `left` | (0.20, 0.50) | 좌측 중앙 |
| `right` | (0.80, 0.50) | 우측 중앙 |
| `top_left` | (0.20, 0.80) | 좌상단 |
| `top_right` | (0.80, 0.80) | 우상단 |
| `bottom_left` | (0.20, 0.20) | 좌하단 |
| `bottom_right` | (0.80, 0.20) | 우하단 |

### Offset 값

pos 기준으로 미세하게 이동. 같은 pos에서 2개 도형을 나란히 배치할 때 사용:

| offset 값 | 이동량 (정규화) | 설명 |
|-----------|----------------|------|
| `left` | x -= sizeRadius * 0.6 | 왼쪽으로 도형 반지름의 60% |
| `right` | x += sizeRadius * 0.6 | 오른쪽으로 도형 반지름의 60% |
| `up` | z += sizeRadius * 0.6 | 위로 도형 반지름의 60% |
| `down` | z -= sizeRadius * 0.6 | 아래로 도형 반지름의 60% |

→ 하트의 원 2개(lobeL, lobeR)를 center에서 left/right로 벌리는 데 사용.

### Size 값

맵 크기 대비 비율로 변환. 반지름(r) 기준:

| size 값 | 맵 비율 (r) | 250셀 맵에서 반지름 | 용도 |
|---------|------------|-------------------|------|
| `tiny` | 0.05 | ~6셀 | 눈, 작은 점 |
| `small` | 0.10 | ~13셀 | 귀, 작은 부분 |
| `medium` | 0.20 | ~25셀 | 일반 부품 |
| `large` | 0.35 | ~44셀 | 메인 형태 |
| `huge` | 0.50 | ~63셀 | 맵 전체 덮는 형태 |

```
실제 반지름 = sizeRatio * min(mapWidth, mapHeight) / 2
```

### 프리미티브 타입 (prim)

| prim | 필수 파라미터 | 설명 |
|------|-------------|------|
| `circle` | (없음) | 원. size가 반지름 |
| `rect` | (없음) | 직사각형. size가 반변 길이. aspect로 종횡비 조절 (기본 1.0=정사각형) |
| `ngon` | `n` | 정N각형. n=3 삼각형, n=5 오각형, n=6 육각형 등 |
| `star` | (없음) | N각 별. n으로 꼭지점 수 (기본 5). 내부 반지름은 자동 계산 (r*0.4) |

4개 프리미티브만으로 모든 형태를 조합:
- **하트** = circle×2(lobeL, lobeR) + ngon(3, rot:180) → union
- **십자가** = rect(aspect:3) + rect(aspect:0.33, rot:90) → union
- **초승달** = circle - circle(offset) → sub
- **화살표** = ngon(3) + rect → union

### ComposeOp 필드

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `op` | string | ✓ | 연산 타입: `add`, `union`, `sub`, `inter` |
| `s` | string | △ | 단일 도형/결과 ID (`add`에 사용) |
| `a` | string | △ | 첫 번째 피연산자 (`union`/`sub`/`inter`에 사용) |
| `b` | string | △ | 두 번째 피연산자 (`union`/`inter`에 사용) |
| `from` | string | △ | 빼기 대상 (`sub`에 사용: `a`를 `from`에서 뺌) |
| `out` | string | - | 결과 ID (다음 연산에서 참조. 마지막이면 생략 가능) |
| `k` | float | - | smooth 블렌딩 반경 (기본 0, 0이면 하드 엣지) |
| `e` | float | △ | elevation 값. 양수=언덕, 음수=호수. **마지막 연산에 필수** |
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

## 5. Anchor 시스템 (엄밀 정의)

### 포맷

```
위치 결정 방법 A: pos (절대적 의미 위치)
  {"pos": "center", "size": "large"}

위치 결정 방법 B: anchor + at (상대적 참조 위치)
  {"anchor": "face", "at": "top_left", "size": "small"}
```

- `anchor`: 앞서 정의된 도형의 id (자신보다 앞에 선언되어야 함)
- `at`: anchor 도형의 bounding box 기준 배치 위치

### At 값 목록

anchor 도형의 bounding box를 기준으로 새 도형의 중심 좌표를 결정:

```
            top_left    top     top_right
               ┌─────────────────────┐
               │                     │
       left    │  inner_    inner_   │  right
               │  left      right    │
               │                     │
               │  inner_    inner_   │
               │  top       bottom   │
               │                     │
               └─────────────────────┘
          bottom_left  bottom  bottom_right
```

| at 값 | 좌표 계산 (anchor bbox 기준) | 설명 |
|--------|---------------------------|------|
| `top` | (cx, top + newR * 0.5) | 상단 중앙, 반쯤 걸침 |
| `bottom` | (cx, bottom - newR * 0.5) | 하단 중앙, 반쯤 걸침 |
| `left` | (left - newR * 0.5, cz) | 좌측 중앙, 반쯤 걸침 |
| `right` | (right + newR * 0.5, cz) | 우측 중앙, 반쯤 걸침 |
| `top_left` | (left, top + newR * 0.3) | 좌상단 모서리 |
| `top_right` | (right, top + newR * 0.3) | 우상단 모서리 |
| `bottom_left` | (left, bottom - newR * 0.3) | 좌하단 모서리 |
| `bottom_right` | (right, bottom - newR * 0.3) | 우하단 모서리 |
| `inner_left` | (cx - anchorR * 0.4, cz) | 내부 좌측 (눈 위치 등) |
| `inner_right` | (cx + anchorR * 0.4, cz) | 내부 우측 (눈 위치 등) |
| `inner_top` | (cx, cz + anchorR * 0.4) | 내부 상단 |
| `inner_bottom` | (cx, cz - anchorR * 0.4) | 내부 하단 |

여기서:
- `cx`, `cz`: anchor 도형의 중심 좌표
- `anchorR`: anchor 도형의 반지름
- `newR`: 새 도형의 반지름
- `top` = cz + anchorR, `bottom` = cz - anchorR
- `left` = cx - anchorR, `right` = cx + anchorR

### Bounding Box 계산 규칙

각 프리미티브의 bounding box (AABB):

| prim | bounding box |
|------|-------------|
| `circle` | center ± r |
| `rect` | center ± (halfW, halfH), 회전 시 회전된 AABB |
| `ngon` | center ± r (외접원 기준) |
| `star` | center ± r (외부 반지름 기준) |

---

## 6. ShapeBuilder 의사코드

### ResolvePositions (pos/anchor/at → 실제 좌표)

```
데이터 구조:
  ResolvedShape {
    id:       string
    prim:     string      // circle, rect, ngon, star
    center:   float[2]    // 정규화 [0,1] 좌표
    r:        float       // 정규화 반지름
    n:        int         // ngon/star 꼭지점 수
    rot:      float       // 회전각 (도)
    aspect:   float       // 종횡비 (rect용)
    bbox:     AABB        // bounding box (anchor 참조용)
  }

  AABB {
    minX, minZ, maxX, maxZ: float
    cx:  float = (minX + maxX) / 2
    cz:  float = (minZ + maxZ) / 2
  }

함수 ResolveAll(shapes: ShapePrimitive[]) → ResolvedShape[]:

  resolved = {}   // id → ResolvedShape

  for each shape in shapes:

    //── 1. 크기 해석 ─────────────────────────────
    r = ParseSizeToRadius(shape.size)
    // tiny=0.05, small=0.10, medium=0.20, large=0.35, huge=0.50
    // 이 값은 정규화 좌표 기준 반지름 (mapSize의 비율)

    //── 2. 중심 좌표 결정 ────────────────────────
    if shape.anchor != null:
      // Anchor 모드: 참조 도형 기준 상대 배치
      anchorShape = resolved[shape.anchor]
      if anchorShape == null: ERROR("anchor 참조 실패: " + shape.anchor)

      (cx, cz) = ComputeAnchorPosition(anchorShape, shape.at, r)

    else:
      // Pos 모드: 절대적 의미 위치
      pos = shape.pos ?? "center"
      (cx, cz) = ParsePosition(pos)   // 기존 ElevationShape.ParsePosition() 재사용

      // Offset 적용
      if shape.offset != null:
        (cx, cz) = ApplyOffset(cx, cz, shape.offset, r)

    //── 3. ResolvedShape 생성 ────────────────────
    rs = ResolvedShape {
      id     = shape.id
      prim   = shape.prim
      center = (cx, cz)
      r      = r
      n      = shape.n ?? (shape.prim == "star" ? 5 : 0)
      rot    = shape.rot ?? 0
      aspect = shape.aspect ?? 1.0
      bbox   = ComputeBBox(shape.prim, cx, cz, r, shape.aspect ?? 1.0)
    }

    resolved[shape.id] = rs

  return resolved.Values
```

### ComputeAnchorPosition (at → 실제 좌표)

blueprint_ai의 `ComputePosition()` 패턴을 2D terrain에 적용:

```
함수 ComputeAnchorPosition(anchor: ResolvedShape, at: string, newR: float) → (float, float):

  cx = anchor.bbox.cx
  cz = anchor.bbox.cz
  aR = anchor.r          // anchor 반지름
  top    = anchor.bbox.maxZ
  bottom = anchor.bbox.minZ
  left   = anchor.bbox.minX
  right  = anchor.bbox.maxX

  switch at:
    // ── 외부 배치 (도형이 anchor 가장자리에 걸침) ──
    case "top":
      return (cx, top + newR * 0.5)

    case "bottom":
      return (cx, bottom - newR * 0.5)

    case "left":
      return (left - newR * 0.5, cz)

    case "right":
      return (right + newR * 0.5, cz)

    case "top_left":
      return (left, top + newR * 0.3)

    case "top_right":
      return (right, top + newR * 0.3)

    case "bottom_left":
      return (left, bottom - newR * 0.3)

    case "bottom_right":
      return (right, bottom - newR * 0.3)

    // ── 내부 배치 (도형이 anchor 내부에 위치) ──
    case "inner_left":
      return (cx - aR * 0.4, cz)

    case "inner_right":
      return (cx + aR * 0.4, cz)

    case "inner_top":
      return (cx, cz + aR * 0.4)

    case "inner_bottom":
      return (cx, cz - aR * 0.4)

    default:
      WARNING("알 수 없는 at 값: " + at + ", 기본값 center 사용")
      return (cx, cz)
```

### ApplyOffset

```
함수 ApplyOffset(cx: float, cz: float, offset: string, r: float) → (float, float):
  shift = r * 0.6

  switch offset:
    case "left":   return (cx - shift, cz)
    case "right":  return (cx + shift, cz)
    case "up":     return (cx, cz + shift)
    case "down":   return (cx, cz - shift)
    default:       return (cx, cz)
```

### ComputeBBox

```
함수 ComputeBBox(prim: string, cx: float, cz: float, r: float, aspect: float) → AABB:

  switch prim:
    case "circle":
      return AABB(cx - r, cz - r, cx + r, cz + r)

    case "rect":
      halfW = r * sqrt(aspect)
      halfH = r / sqrt(aspect)
      // 회전은 무시하고 외접 AABB 계산 (anchor 참조용이므로 대략적이어도 충분)
      return AABB(cx - halfW, cz - halfH, cx + halfW, cz + halfH)

    case "ngon":
      return AABB(cx - r, cz - r, cx + r, cz + r)  // 외접원 기준

    case "star":
      return AABB(cx - r, cz - r, cx + r, cz + r)  // 외부 반지름 기준
```

### ParseSizeToRadius

```
함수 ParseSizeToRadius(size: string) → float:
  switch size:
    case "tiny":    return 0.05
    case "small":   return 0.10
    case "medium":  return 0.20
    case "large":   return 0.35
    case "huge":    return 0.50
    default:
      // 숫자 직접 입력도 허용 (fallback)
      if float.TryParse(size, out f): return clamp(f, 0.02, 0.6)
      return 0.20  // 기본: medium
```

---

## 7. SDF 프리미티브 4종

모든 SDF 함수는 **정규화 좌표 [0,1]** 기준으로 평가.
d < 0 = 내부, d > 0 = 외부, d = 0 = 경계.

### SdfCircle

```
SdfCircle(p, center, r):
  return length(p - center) - r
```

### SdfRect

```
SdfRect(p, center, r, aspect, rotDeg):
  halfW = r * sqrt(aspect)
  halfH = r / sqrt(aspect)

  // 회전 적용
  q = Rotate(p - center, -rotDeg)

  // Quilez box SDF
  d = abs(q) - (halfW, halfH)
  return length(max(d, 0)) + min(max(d.x, d.y), 0)
```

### SdfNgon

```
SdfNgon(p, center, r, n, rotDeg):
  q = Rotate(p - center, -rotDeg)

  // 정N각형 SDF (Quilez 참조)
  // 각 꼭지점 사이 변까지의 거리 → 최소값
  segAngle = 2π / n
  halfAngle = π / n

  // 현재 각도를 한 세그먼트로 접기
  angle = atan2(q.y, q.x)
  angle = ((angle % segAngle) + segAngle) % segAngle  // [0, segAngle)
  angle = angle - halfAngle                             // [-halfAngle, halfAngle)

  // 접힌 좌표에서 변까지 거리
  dist = length(q)
  foldedX = dist * cos(angle)
  foldedY = dist * sin(angle)

  // 변의 위치: x = r * cos(halfAngle)
  edgeX = r * cos(halfAngle)

  // 변까지의 signed distance
  return foldedX - edgeX
```

### SdfStar

```
SdfStar(p, center, outerR, n, rotDeg):
  innerR = outerR * 0.4     // 내부 반지름 = 외부의 40%

  q = Rotate(p - center, -rotDeg)

  // N각 별 SDF (Quilez 참조)
  segAngle = π / n           // 별의 세그먼트 반각
  angle = atan2(q.y, q.x)
  angle = ((angle % (2*segAngle)) + 2*segAngle) % (2*segAngle)

  // outer tip (outerR, 0)과 inner valley (innerR*cos(segAngle), innerR*sin(segAngle))
  // 사이의 선분까지 거리 계산
  tipX = outerR
  tipY = 0
  valX = innerR * cos(segAngle)
  valY = innerR * sin(segAngle)

  foldedP = (length(q) * cos(angle), length(q) * sin(angle))
  // 선분 projection + clamp → signed distance
  return PointToSegmentDist(foldedP, (tipX, tipY), (valX, valY))
```

### 회전 유틸리티

```
Rotate(p, angleDeg):
  rad = angleDeg * π / 180
  c = cos(rad)
  s = sin(rad)
  return (p.x * c - p.y * s, p.x * s + p.y * c)
```

---

## 8. Boolean 연산

### 하드 엣지

```
OpUnion(a, b):       min(a, b)           // 합집합
OpSubtract(a, from): max(-a, from)       // a를 from에서 뺌
OpIntersect(a, b):   max(a, b)           // 교집합
```

### Smooth 변형 (k > 0)

```
OpSmoothUnion(a, b, k):
  k *= 4
  h = max(k - abs(a - b), 0)
  return min(a, b) - h * h * 0.25 / k

OpSmoothSubtract(a, from, k):
  return -OpSmoothUnion(a, -from, k)

OpSmoothIntersect(a, b, k):
  return -OpSmoothUnion(-a, -b, k)
```

k 값 가이드:
- 0: 하드 엣지 (날카로운 경계)
- 0.01~0.03: 약간 둥근 경계 (대부분의 경우)
- 0.05~0.10: 부드럽게 녹아드는 경계 (하트의 lobe-body 연결 등)

---

## 9. Rasterizer 의사코드

### 메인 래스터라이저

```
함수 ApplyComposite(compositeShape, map, elevGrid):

  //── 1. ShapeBuilder: 의미적 → 실제 좌표 ──────────
  resolvedShapes = ResolveAll(compositeShape.shapes)
  // 결과: Dictionary<string, ResolvedShape>

  //── 2. SDF 함수 생성 ───────────────────────────────
  sdfFuncs = {}   // id → SdfFunc(p) → float

  for each rs in resolvedShapes:
    switch rs.prim:
      case "circle": sdfFuncs[rs.id] = (p) => SdfCircle(p, rs.center, rs.r)
      case "rect":   sdfFuncs[rs.id] = (p) => SdfRect(p, rs.center, rs.r, rs.aspect, rs.rot)
      case "ngon":   sdfFuncs[rs.id] = (p) => SdfNgon(p, rs.center, rs.r, rs.n, rs.rot)
      case "star":   sdfFuncs[rs.id] = (p) => SdfStar(p, rs.center, rs.r, rs.n, rs.rot)

  //── 3. Compose 체인 실행 ────────────────────────────
  float elevation = 0
  float falloff = 0.05

  for each op in compositeShape.compose:
    switch op.op:
      case "add":
        finalSdf = sdfFuncs[op.s]
        elevation = op.e
        falloff = op.f ?? 0.05

      case "union":
        funcA = sdfFuncs[op.a]
        funcB = sdfFuncs[op.b]
        if op.k > 0:
          combined = (p) => OpSmoothUnion(funcA(p), funcB(p), op.k)
        else:
          combined = (p) => OpUnion(funcA(p), funcB(p))
        sdfFuncs[op.out] = combined
        if op.e != null:
          finalSdf = combined
          elevation = op.e
          falloff = op.f ?? 0.05

      case "sub":
        funcA = sdfFuncs[op.a]        // 빼는 도형
        funcFrom = sdfFuncs[op.from]  // 빼기 대상
        if op.k > 0:
          combined = (p) => OpSmoothSubtract(funcA(p), funcFrom(p), op.k)
        else:
          combined = (p) => OpSubtract(funcA(p), funcFrom(p))
        sdfFuncs[op.out] = combined
        if op.e != null:
          finalSdf = combined
          elevation = op.e
          falloff = op.f ?? 0.05

      case "inter":
        funcA = sdfFuncs[op.a]
        funcB = sdfFuncs[op.b]
        if op.k > 0:
          combined = (p) => OpSmoothIntersect(funcA(p), funcB(p), op.k)
        else:
          combined = (p) => OpIntersect(funcA(p), funcB(p))
        sdfFuncs[op.out] = combined
        if op.e != null:
          finalSdf = combined
          elevation = op.e
          falloff = op.f ?? 0.05

  //── 4. 맵 래스터라이징 ──────────────────────────────
  mapW = map.Size.x
  mapH = map.Size.z
  fertilityGrid = MapGenerator.Fertility

  for each cell (x, z) in map:
    p = (x / mapW, z / mapH)           // 정규화
    d = finalSdf(p)                     // SDF 평가
    t = smoothstep(falloff, 0, d)       // 거리 → 마스크 (0~1)

    if elevation < 0:                   // 호수
      if t > 0.5:
        fertilityGrid[cell] = -2005     // 깊은 물
        elevGrid[cell] = min(elevGrid[cell], 0.3)
      elif t > 0.1:
        fertilityGrid[cell] = -1025     // 얕은 물
        elevGrid[cell] = min(elevGrid[cell], 0.3)
      elif t > 0.05:
        fertilityGrid[cell] = 1         // 해변
    else:                               // 언덕
      elevGrid[cell] += t * elevation
```

### Smoothstep 함수

```
smoothstep(edge0, edge1, x):
  t = clamp((x - edge0) / (edge1 - edge0), 0, 1)
  return t * t * (3 - 2 * t)
```

이 함수가 핵심 — 경계(d=falloff)에서 0, 내부(d=0)에서 1, 그 사이는 매끄러운 S-커브.

---

## 10. 프롬프트 가이드 초안

### LLM에게 제공할 가이드 (한국어)

```
- composite: 자유 형태 지형. 기본 도형(원/사각형/정다각형/별)을 조합하여 어떤 모양이든 표현.

  [도형]
  circle (원), rect (사각형), ngon (정다각형, n=꼭지점수), star (별)
  4개만 사용. 복잡한 모양은 조합으로 만듦:
  - 하트 = circle×2 + ngon(3, rot:180) → union
  - 초승달 = circle - circle → sub
  - 십자가 = rect + rect(rot:90) → union

  [위치 — 좌표 금지, 의미만]
  pos: center, top, bottom, left, right, top_left, top_right, bottom_left, bottom_right
  anchor: 다른 도형 id + at: top, bottom, left, right, top_left, top_right, ..., inner_left, inner_right, ...
  offset: left, right, up, down (pos 기준 미세 이동)

  [크기 — 숫자 금지, 단어만]
  tiny, small, medium, large, huge

  [연산]
  add(단일), union(합치기), sub(빼기, 구멍), inter(교집합)
  k>0이면 매끄럽게 연결. e>0=언덕, e<0=호수.

  [규칙]
  1. 좌표 숫자([0.3, 0.5] 등) 절대 쓰지 마.
  2. pos 또는 anchor+at으로만 위치 지정.
  3. 복잡한 모양은 4개 프리미티브 조합으로.
  4. compose 마지막 연산에 반드시 e(elevation) 지정.

  [예시]
  별 언덕: shapes:[{id:"s",prim:"star",pos:"center",size:"large",n:5}],
           compose:[{op:"add",s:"s",e:0.8}]

  하트 호수: shapes:[
    {id:"lL",prim:"circle",pos:"center",size:"medium",offset:"left"},
    {id:"lR",prim:"circle",pos:"center",size:"medium",offset:"right"},
    {id:"b",prim:"ngon",n:3,pos:"center",size:"large",rot:180}],
           compose:[{op:"union",a:"lL",b:"lR",out:"t",k:0.03},
                    {op:"union",a:"t",b:"b",out:"h",k:0.05},
                    {op:"add",s:"h",e:-0.5}]
```

### LLM에게 제공할 가이드 (영어)

```
- composite: Free-form terrain. Combine basic shapes (circle/rect/ngon/star) to create any form.

  [Shapes]
  circle, rect, ngon (regular polygon, n=vertices), star
  Only 4 primitives. Complex shapes are combinations:
  - Heart = circle×2 + ngon(3, rot:180) → union
  - Crescent = circle - circle → sub
  - Cross = rect + rect(rot:90) → union

  [Position — NO coordinates, semantic only]
  pos: center, top, bottom, left, right, top_left, top_right, bottom_left, bottom_right
  anchor: other shape id + at: top, bottom, left, right, top_left, ..., inner_left, inner_right, ...
  offset: left, right, up, down (minor shift from pos)

  [Size — NO numbers, words only]
  tiny, small, medium, large, huge

  [Operations]
  add (single), union (merge), sub (cut hole), inter (intersection)
  k>0 = smooth blend. e>0 = hill, e<0 = lake.

  [Rules]
  1. NEVER use numeric coordinates like [0.3, 0.5].
  2. Use pos OR anchor+at for positioning.
  3. Compose complex shapes from 4 primitives.
  4. Last compose op MUST have e (elevation).
```

---

## 11. 예시 5개

### 예시 1: 별 언덕 (단순 — add)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "s", "prim": "star", "pos": "center", "size": "large", "n": 5}
  ],
  "compose": [
    {"op": "add", "s": "s", "e": 0.8}
  ]
}
```

해석: 맵 중앙에 큰 5각 별 모양 언덕 (elevation +0.8).

### 예시 2: 하트 호수 (조합 — union + ngon)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "lobeL", "prim": "circle", "pos": "center", "size": "medium", "offset": "left"},
    {"id": "lobeR", "prim": "circle", "pos": "center", "size": "medium", "offset": "right"},
    {"id": "body", "prim": "ngon", "n": 3, "pos": "center", "size": "large", "rot": 180}
  ],
  "compose": [
    {"op": "union", "a": "lobeL", "b": "lobeR", "out": "top", "k": 0.03},
    {"op": "union", "a": "top", "b": "body", "out": "heart", "k": 0.05},
    {"op": "add", "s": "heart", "e": -0.5}
  ]
}
```

해석:
1. center에서 left/right로 벌린 원 2개 → smooth union → 하트 윗부분
2. 역삼각형(ngon 3, rot 180)과 합침 → 하트 전체 형태
3. elevation -0.5 → 호수

### 예시 3: 고양이 얼굴 호수 (anchor 참조)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "face", "prim": "circle", "pos": "center", "size": "large"},
    {"id": "earL", "prim": "ngon", "n": 3, "anchor": "face", "at": "top_left", "size": "small"},
    {"id": "earR", "prim": "ngon", "n": 3, "anchor": "face", "at": "top_right", "size": "small"},
    {"id": "eyeL", "prim": "circle", "anchor": "face", "at": "inner_left", "size": "tiny"},
    {"id": "eyeR", "prim": "circle", "anchor": "face", "at": "inner_right", "size": "tiny"}
  ],
  "compose": [
    {"op": "union", "a": "face", "b": "earL", "out": "h1", "k": 0.02},
    {"op": "union", "a": "h1", "b": "earR", "out": "h2", "k": 0.02},
    {"op": "sub", "a": "eyeL", "from": "h2", "out": "h3"},
    {"op": "sub", "a": "eyeR", "from": "h3", "out": "cat"},
    {"op": "add", "s": "cat", "e": -0.5}
  ]
}
```

해석:
1. center에 큰 원 (얼굴)
2. face의 top_left/top_right에 작은 삼각형 (귀) → union
3. face의 inner_left/inner_right에서 tiny 원 빼기 (눈) → sub
4. elevation -0.5 → 호수

### 예시 4: L자 산맥 (rect 조합)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "vert", "prim": "rect", "pos": "left", "size": "large", "aspect": 0.3},
    {"id": "horiz", "prim": "rect", "anchor": "vert", "at": "bottom", "size": "large", "aspect": 3.0}
  ],
  "compose": [
    {"op": "union", "a": "vert", "b": "horiz", "out": "L", "k": 0.02},
    {"op": "add", "s": "L", "e": 0.9}
  ]
}
```

해석:
1. 맵 좌측에 세로로 긴 사각형 (aspect 0.3 = 세로가 가로의 3.3배)
2. vert의 bottom에 가로로 긴 사각형 (aspect 3.0 = 가로가 세로의 3배)
3. union → L자 형태, elevation +0.9 → 산맥

### 예시 5: 초승달 호수 (sub)

```json
{
  "type": "composite",
  "shapes": [
    {"id": "outer", "prim": "circle", "pos": "center", "size": "large"},
    {"id": "cut", "prim": "circle", "pos": "center", "size": "large", "offset": "right"}
  ],
  "compose": [
    {"op": "sub", "a": "cut", "from": "outer", "out": "crescent"},
    {"op": "add", "s": "crescent", "e": -0.4}
  ]
}
```

해석:
1. center에 큰 원
2. 같은 크기 원을 right로 offset → 오른쪽으로 이동
3. sub → 겹치는 부분 제거 → 왼쪽에 초승달 형태
4. elevation -0.4 → 호수

---

## 12. 기존 시스템과의 통합

### elevation_shapes에 type:"composite" 추가

```json
{
  "elevation_shapes": [
    {"type": "slope", "direction": "left", "strength": "medium"},
    {"type": "composite", "shapes": [...], "compose": [...]},
    {"type": "bump", "position": "center", "strength": "strong", "size": "medium"}
  ]
}
```

- composite는 elevation_shapes 배열의 한 항목
- 기존 6종 primitive(slope/radial/split/bump/noise/ring)와 additive로 조합
- 처리 순서: 비-물 shapes 먼저 → 물 shapes 나중 (기존 규칙 유지)
- composite의 e < 0은 fill="water"와 동일하게 물 처리

### 기존 primitive 보존 (변경 없음)

| type | 설명 | 변경 |
|------|------|------|
| `slope` | 한쪽이 높은 경사면 | 없음 |
| `radial` | 중심/가장자리 방사형 | 없음 |
| `split` | 축 기준 양분 | 없음 |
| `bump` | 가우시안 돌출/함몰 | 없음 |
| `noise` | 펄린 노이즈 불규칙 지형 | 없음 |
| `ring` | 도넛 형태 산맥 | 없음 |
| **`composite`** | **자유 형태 (신규)** | **추가** |

### ApplyShape 디스패처 확장

기존 switch에 `case "composite"` 추가:
```csharp
switch (shape.type)
{
    case "slope":     ApplySlope(shape, map, grid);     break;
    case "radial":    ApplyRadial(shape, map, grid);    break;
    case "split":     ApplySplit(shape, map, grid);     break;
    case "bump":      ApplyBump(shape, map, grid);      break;
    case "noise":     ApplyNoise(shape, map, grid);     break;
    case "ring":      ApplyRing(shape, map, grid);      break;
    case "composite": ApplyComposite(shape, map, grid); break;  // 신규
}
```

### ElevationShape 클래스 확장

기존 필드에 추가:
```csharp
public class ElevationShape
{
    // 기존 필드 (100% 보존)
    public string type;
    public string direction;
    public string strength;
    public string position;
    public string size;
    public string gap;
    public string fill;

    // composite 전용 필드 (신규)
    public List<ShapePrimitive> shapes;
    public List<ComposeOp> compose;
}
```

---

## 13. 연산 체인 규칙

1. `compose` 배열은 **순서대로** 실행
2. 각 연산의 `out`은 이후 연산에서 참조 가능
3. **마지막 연산에 반드시** `e` (elevation) 지정
4. `f` (falloff)는 마지막 연산에만 의미 있음 (전체 형태 가장자리)
5. `sub` 연산: `a`를 `from`에서 뺌 → `from`의 형태에서 `a` 영역이 빠짐
6. 중간 연산의 `out`은 필수 (다음 연산에서 참조해야 하므로)
7. 마지막 연산의 `out`은 생략 가능

### 연산 체인 예시 (고양이 얼굴)

```
① union(face, earL, k=0.02) → h1     // 얼굴 + 왼쪽 귀
② union(h1, earR, k=0.02) → h2       // + 오른쪽 귀
③ sub(eyeL, from: h2) → h3           // - 왼쪽 눈
④ sub(eyeR, from: h3) → cat          // - 오른쪽 눈
⑤ add(cat, e: -0.5)                  // 최종: 호수
```

---

## 14. 구현 단계

### Stage 1: C# 데이터 모델 + ShapeBuilder

**파일:** `MapGen/ShapeBuilder.cs` (신규), `MapGen/MapGenParams.cs` (확장)

1. `ShapePrimitive` 데이터 클래스 정의 (id, prim, pos, anchor, at, offset, size, n, rot, aspect)
2. `ComposeOp` 데이터 클래스 정의 (op, s, a, b, from, out, k, e, f)
3. `ResolvedShape` 중간 표현 (center, r, bbox 등)
4. `ShapeBuilder.ResolveAll()` — pos/anchor/at → 실제 좌표 계산
5. `ComputeAnchorPosition()` — at 값 → 좌표 의사코드 구현
6. `ParseSizeToRadius()` — size enum → 반지름
7. `ElevationShape` 클래스에 shapes/compose 필드 추가
8. JSON 파싱 (기존 LitJSON 사용)

### Stage 2: SDF 엔진 + Rasterizer

**파일:** `MapGen/SdfEngine.cs` (신규)

1. SDF 프리미티브 4종 구현 (SdfCircle, SdfRect, SdfNgon, SdfStar)
2. Boolean 연산 6종 (union, sub, inter + smooth 3종)
3. `ApplyComposite()` 래스터라이저 — GenStepPatches.cs에 추가
4. smoothstep + elevation/water 적용
5. 기존 ApplyShape 디스패처에 `case "composite"` 추가

### Stage 3: 프롬프트 통합

**파일:** `UI/Dialog_TextToMap.cs`, `LLM/PromptBuilder.cs`

1. BuildSystemPrompt()에 composite 가이드 추가 (한/영)
2. few-shot 예시: 별/하트/고양이/L자/초승달
3. 규칙: "모양 요청 = composite. 좌표 금지. pos/anchor+at만."
4. 기존 heightmap 가이드를 composite으로 교체

### Stage 4: 테스트 + 튜닝

1. 빌드 검증
2. 회귀 테스트 (기존 6종 primitive)
3. 신규 테스트: 별/하트/고양이/L자/초승달
4. anchor 좌표 계산 정확도 확인 (고양이 귀/눈 위치)
5. 크기 매핑 튜닝 (tiny~huge 실제 결과 확인)
6. smooth 블렌딩 k 값 튜닝

---

## 15. 참고 문헌

- Quilez, I. "2D Distance Functions" — SDF 프리미티브 수식 (iquilezles.org/articles/distfunctions2d/)
- Quilez, I. "Smooth Minimum" — smooth boolean 연산 (iquilezles.org/articles/smin/)
- Hu et al. "Chain-of-Symbol" (2023) — LLM 기호 추론 정확도 93%
- Feng et al. "LayoutGPT" (NeurIPS 2023) — 구조화 JSON > 텍스트
- "Don't Mesh with Me" (2024) — LLM CSG 생성 98% 구문 정확
- "ShapeCraft" (NeurIPS 2025) — 그래프 기반 형태 분해
- blueprint_ai PLAN_V2.md — attach/ComputePosition 패턴 원본
