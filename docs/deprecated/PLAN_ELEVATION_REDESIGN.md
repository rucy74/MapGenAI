# MapGenAI -- Elevation/Mountain Architecture Redesign

> 작성: 2026-03-21
> 작성자: rimworld-ai-dev
> 목표: slope 상쇄, split Max 덮어쓰기, 바닐라 비유사성 3대 문제 해결

---

## 1. 문제 요약

### 문제 1: slope 상쇄
slope(left, medium) + slope(right, medium)을 적용하면:
```
left:  elevation += -0.0042 * dx   (cos(180) = -1)
right: elevation += +0.0042 * dx   (cos(0)   = +1)
합계:  0
```
선형 함수의 합은 선형이므로, 반대 방향 slope 두 개는 완벽히 상쇄되어 평지가 된다.
바닐라 Perlin 노이즈는 비선형이므로 이런 상쇄가 구조적으로 불가능하다.

### 문제 2: split의 Max 덮어쓰기
ApplySplit에서 strength < 0 (산맥 모드)일 때:
```csharp
grid[cell] = Mathf.Max(grid[cell], 0.85f - falloff * 0.3f);
```
이것은 Add가 아니라 Max이므로, 기존 Perlin 패턴의 계곡-봉우리 변화가 소실된다.
바닐라의 DistFromAxis는 기존 elevation에 Add하여 자연스러운 패턴을 보존한다.

### 문제 3: 바닐라와 다른 수학
바닐라 산 생성:
1. Perlin(freq=0.021, lacunarity=2.0, persistence=0.5, octaves=6)
2. ScaleBias(0.5, 0.5) -> 0~1 범위로 정규화
3. hilliness factor 적용: Flat*0.8, SmallHills*0.9, LargeHills*1.0, Mountainous*1.1, Impassable*1.2
4. Mountainous/Impassable: DistFromAxis(span=0.42) -> Clamp(0,1) -> Invert -> Add로 산맥 추가

MapGenAI: slope(선형 경사), bump(가우시안), radial(유클리드 거리) -- 전혀 다른 수학적 기반.

---

## 2. 접근법 분석

### 접근법 A: DistFromEdge 기반 (바닐라 DistFromAxis 확장)

**개요**: 바닐라의 DistFromAxis처럼, 특정 edge에서의 거리를 elevation에 Add.

**수학**:
```
"왼쪽에 산" = 1.0 - clamp(distFromLeftEdge / fadeWidth, 0, 1)
"오른쪽에 산" = 1.0 - clamp(distFromRightEdge / fadeWidth, 0, 1)
"양쪽에 산" = max(leftProfile, rightProfile)  또는  leftProfile + rightProfile
```

**장점**:
- Add 방식이라 Perlin 패턴 보존
- 바닐라 DistFromAxis와 동일한 철학
- 두 개 조합해도 상쇄 불가 (거리 함수는 비음수)
- 구현이 단순

**단점**:
- `leftProfile + rightProfile`일 때 가운데가 과도하게 높아질 수 있음
- 산의 형태가 "벽"에 가까움 (edge에서 부드럽게 감소하는 프로파일)
- slope의 "한쪽이 높고 반대쪽이 낮음" 의미와 미묘하게 다름

**상쇄 검증**:
```
left:  1.0 - clamp(x / W, 0, 1)     -- 왼쪽=1, 오른쪽=0
right: 1.0 - clamp((mapW-x) / W, 0, 1)  -- 오른쪽=1, 왼쪽=0
합계:  가운데=0, 양쪽=1   <-- 골짜기 형성. 상쇄 아님!
```

### 접근법 B: Perlin 마스크 기반

**개요**: 사용자 요청을 Perlin 노이즈의 bias/mask로 변환.

**수학**:
```
"왼쪽에 산" = perlin(x,z) * leftMask(x)   (leftMask: 왼쪽=1, 오른쪽=0)
"양쪽에 산" = perlin(x,z) * (leftMask + rightMask)
```

**장점**:
- 결과가 바닐라와 가장 유사 (Perlin 기반)
- 자연스러운 산 형태 (노이즈 기반)

**단점**:
- 곱셈 방식이므로 Postfix에서 구현 불가 (base Perlin은 이미 생성됨)
- Transpiler 필요 (복잡도 높음, 호환성 낮음)
- 마스크 자체가 노이즈를 완전히 억제하므로 마스크 경계가 부자연스러울 수 있음

### 접근법 C: 하이브리드 (DistFromEdge + Perlin noise 추가)

**개요**: DistFromEdge로 대략적 위치 + 별도 Perlin으로 디테일 추가.

**수학**:
```
"왼쪽에 산":
  edgeProfile = smoothstep(fadeEnd, fadeStart, distFromLeftEdge)   -- 0~1
  noise = perlin(x, z, freq=0.035) * 0.5 + 0.5                    -- 0~1
  elevation += strength * edgeProfile * noise
```

**장점**:
- 가장 자연스러운 결과 (위치 제어 + 자연스러운 디테일)
- Add 방식이라 Perlin 패턴 보존
- 두 개 조합해도 자연스러움 (각각 독립적)
- Postfix에서 구현 가능

**단점**:
- 구현이 가장 복잡
- Perlin 시드 관리 필요
- 튜닝 파라미터가 많음 (fadeWidth, noiseFreq, noiseAmplitude)

**상쇄 검증**:
```
left:  strength * edgeProfileLeft * noiseL     -- 항상 >= 0
right: strength * edgeProfileRight * noiseR    -- 항상 >= 0
합계:  왼쪽=높음, 오른쪽=높음, 가운데=0 또는 약간   <-- 골짜기 형성!
```

### 접근법 D: 기존 slope만 수정 (sigmoid/smoothstep)

**개요**: slope의 선형 경사를 sigmoid로 변경.

**수학**:
```
기존: elevation += strength * SlopeMultiplier * dot(offset, direction)
수정: elevation += strength * sigmoid(dot(offset, direction) / scale)
sigmoid(x) = 1 / (1 + exp(-x))  -- 또는 smoothstep
```

**장점**:
- 최소 변경 (slope 함수 한 줄 수정)
- 기존 인터페이스 완전 보존

**단점**:
- sigmoid(left) + sigmoid(right)의 합이 상수 1이 됨 -> 여전히 평지!
  ```
  sigmoid(x) + sigmoid(-x) = 1.0    -- sigmoid의 대칭성
  ```
- 근본적으로 선형/비선형과 무관하게, 반대 방향의 단조 함수 합은 상수

**결론**: slope 상쇄 문제를 해결하지 못함. **탈락**.

---

## 3. 최종 선택: 접근법 C (하이브리드)

### 선택 이유

1. **상쇄 문제 근본 해결**: DistFromEdge는 비음수(0~1)이므로 두 개를 더해도 상쇄 불가
2. **바닐라 유사성**: Perlin noise 레이어 추가로 자연스러운 산 형태
3. **기존 구조 보존**: Postfix에서 additive로 적용 -- 아키텍처 변경 없음
4. **split 문제도 해결**: Max 대신 Add 방식으로 산맥 생성
5. **LLM 인터페이스 최소 변경**: 기존 direction/strength 파라미터 유지 가능

### 탈락 사유 요약

| 접근법 | 탈락 사유 |
|--------|----------|
| A | 디테일 부족 (벽 같은 산) -- C가 A의 상위호환 |
| B | Transpiler 필요, Postfix 불가 |
| D | sigmoid 대칭성으로 상쇄 미해결 |

---

## 4. 새로운 Shape 시스템 설계

### 4.1 기존 Shape 타입 판정

| 타입 | 판정 | 사유 |
|------|------|------|
| **slope** | **제거 -> ridge로 교체** | 선형 경사면은 구조적으로 상쇄 문제. 새로운 ridge가 완전 대체 |
| **radial** | **유지** | edge에서의 거리 기반. 상쇄 문제 없음. 분지/요새에 적합 |
| **split** | **제거 -> ridge로 교체** | Max 덮어쓰기 문제. ridge + 방향 조합으로 동일 표현 가능 |
| **bump** | **유지** | 가우시안 돌출. 호수 생성에 필수. 상쇄 문제 없음 |
| **noise** | **유지** | Perlin 추가 노이즈. 문제 없음 |
| **ring** | **유지** | 도넛 산맥. 상쇄 문제 없음 |
| composite | 유지(미구현) | CSG/SDF 자유 형태. 별도 시스템 |

### 4.2 새로운 Shape: `ridge` (능선)

slope과 split을 모두 대체하는 단일 프리미티브. 핵심 아이디어:
**"edge에서의 거리 기반 프로파일 + Perlin noise 디테일"**

#### 수학적 정의

```
ridge(cell, direction, strength, fade, noiseAmt):
  // 1. 방향 벡터로 edge까지 거리 계산
  dirVec = (cos(direction), sin(direction))   -- 산이 높은 방향
  offset = (cell - mapCenter) / mapHalf
  t = dot(offset, dirVec)                     -- -1 (반대편) ~ +1 (높은 쪽)

  // 2. 거리 -> 프로파일 변환 (smoothstep)
  //    fade는 산이 시작되는 위치 (0=가장자리, 1=중앙까지 전체)
  profileStart = 1.0 - fade                   -- fade=0.5 -> profileStart=0.5
  profile = smoothstep(profileStart - 0.2, profileStart + 0.2, t)  -- 0~1

  // 3. Perlin noise 디테일 (선택)
  if noiseAmt > 0:
    noise = perlin(cell.x * 0.035, cell.z * 0.035)  -- -1~1
    profile *= max(0, 1.0 + noiseAmt * noise)        -- 노이즈로 프로파일 변조

  // 4. 최종 적용 (additive)
  elevation[cell] += strength * profile
```

#### 핵심 특성

1. **비음수 프로파일**: profile은 항상 0~1이므로, `strength * profile`은 항상 같은 부호
2. **상쇄 불가**: ridge(left) + ridge(right)는 양쪽 모두 양수를 더하므로 골짜기가 형성됨
3. **자연스러운 전환**: smoothstep으로 산 가장자리가 부드러움
4. **Perlin 디테일**: noiseAmt > 0이면 산 윤곽이 불규칙해져 자연스러움
5. **additive**: 기존 Perlin 지형 위에 더하므로 패턴 보존

#### 파라미터

| 파라미터 | 타입 | 기본값 | 설명 |
|----------|------|--------|------|
| direction | string | "left" | 산이 높은 방향. 기존 8방향 + 숫자(0-360) |
| strength | string | "medium" | 산의 최대 높이. weak/medium/strong 또는 숫자 |
| fade | string | "medium" | 산이 맵의 얼마나 안쪽까지 들어오는지. small(가장자리만)/medium(절반)/large(중앙까지) |
| noise_amount | string | "medium" | Perlin 디테일 정도. none/low/medium/high |

#### fade 파라미터 값

| 값 | 숫자 | 의미 |
|----|------|------|
| small | 0.3 | 가장자리에만 산 (좁은 산맥) |
| medium | 0.5 | 맵 절반까지 산 (기본) |
| large | 0.7 | 맵 대부분이 산 |
| 숫자 | 0.0~1.0 | 직접 지정 |

#### noise_amount 파라미터 값

| 값 | 숫자 | 의미 |
|----|------|------|
| none | 0.0 | 노이즈 없음 (깨끗한 경계) |
| low | 0.3 | 약간의 불규칙함 |
| medium | 0.6 | 자연스러운 산 (기본) |
| high | 1.0 | 매우 불규칙한 산 |
| 숫자 | 0.0~1.5 | 직접 지정 |

### 4.3 기존 시나리오가 ridge로 어떻게 표현되는지

| 기존 | 신규 | 설명 |
|------|------|------|
| slope(left, medium) | ridge(left, medium) | 왼쪽 산 |
| slope(right, medium) | ridge(right, medium) | 오른쪽 산 |
| slope(left) + slope(right) | ridge(left) + ridge(right) | 양쪽 산 + 가운데 골짜기 |
| split(direction=0, strength=negative) | ridge(left) + ridge(right) with fade=small | 양쪽 산맥 |
| split(direction=90, strength=positive) | bump(negative) 또는 ridge 조합 | 협곡 |

### 4.4 hills -> shape 자동변환 매핑 (업데이트)

```csharp
"left"   -> ridge(direction=left, strength=medium, fade=medium, noise_amount=medium)
"right"  -> ridge(direction=right, strength=medium, fade=medium, noise_amount=medium)
"top"    -> ridge(direction=top, strength=medium, fade=medium, noise_amount=medium)
"bottom" -> ridge(direction=bottom, strength=medium, fade=medium, noise_amount=medium)
"center" -> bump(position=center, size=large, strength=medium)  // 유지
"edges"  -> radial(strength=medium, size=medium)                // 유지
```

center와 edges는 기존 bump/radial이 잘 동작하므로 변경 없음.

### 4.5 IsHillsSlotShape 업데이트

기존 slope/bump(center)/radial 판별에서 ridge를 추가:

```csharp
private static bool IsHillsSlotShape(ElevationShape s)
{
    if (s == null || string.IsNullOrEmpty(s.type)) return false;
    if (!string.IsNullOrEmpty(s.fill)) return false;
    switch (s.type)
    {
        case "ridge":  return true;   // slope 대체
        case "bump":
            var pos = (s.position ?? "center").Trim().ToLower();
            return pos == "center" || pos == "";
        case "radial": return true;
        // slope은 더 이상 auto-generate하지 않지만,
        // 이전 세이브 호환을 위해 여전히 hills-slot으로 인식
        case "slope":  return true;
        default:       return false;
    }
}
```

---

## 5. LLM 프롬프트 영향 분석

### 5.1 시스템 프롬프트 변경 사항

#### elevation_shapes 가이드 (한국어)

**기존**:
```
- slope: 한쪽이 높은 경사면. direction으로 높은 방향 지정.
- split: 축 방향 분할. positive strength=협곡, negative strength=산맥.
```

**신규**:
```
- ridge: 한 방향에 산맥. direction으로 산이 높은 방향. fade로 산 범위(small=가장자리만, large=맵 대부분).
  noise_amount로 자연스러움 조절(none=깨끗한 경계, high=매우 불규칙).
  "양쪽에 산" = [ridge(left), ridge(right)] -> 양쪽 높고 가운데 골짜기.
  "산맥" = ridge(fade=small, noise_amount=high).
```

#### elevation_shapes 가이드 (영어)

**기존**:
```
- slope: A slope where one side is higher. Use direction to set the high side.
- split: Axis-based split. Positive strength=canyon. Negative strength=mountain range.
```

**신규**:
```
- ridge: Mountains on one side. Use direction to set which side is high. fade controls range
  (small=edge only, large=most of map). noise_amount controls naturalness (none=clean, high=very rough).
  "mountains on both sides" = [ridge(left), ridge(right)] -> both sides high, valley in center.
  "mountain range" = ridge(fade=small, noise_amount=high).
```

### 5.2 JSON Schema 변경

#### ElevationShape type enum

**기존**: `slope|radial|split|bump|noise|ring|composite`
**신규**: `ridge|radial|bump|noise|ring|composite`

- slope, split 제거
- ridge 추가

#### ElevationShape 필드 추가

| 필드 | 타입 | 적용 대상 | 설명 |
|------|------|-----------|------|
| fade | string | ridge | small/medium/large 또는 0.0~1.0 |
| noise_amount | string | ridge | none/low/medium/high 또는 0.0~1.5 |

기존 필드 중 direction, strength은 ridge에서도 동일하게 사용.

### 5.3 Few-shot 예시 변경

모든 slope 예시를 ridge로 교체:

**기존**:
```json
{"type":"slope", "direction":"left", "strength":"medium"}
```

**신규**:
```json
{"type":"ridge", "direction":"left", "strength":"medium", "fade":"medium", "noise_amount":"medium"}
```

split 예시는 ridge 조합으로 교체:

**기존** (산맥):
```json
{"type":"split", "direction":"top_left", "strength":"negative_strong", "gap":"tiny"}
```

**신규** (산맥):
```json
[
  {"type":"ridge", "direction":"left", "strength":"strong", "fade":"small", "noise_amount":"high"},
  {"type":"ridge", "direction":"right", "strength":"strong", "fade":"small", "noise_amount":"high"}
]
```

### 5.4 규칙 변경

기존 규칙:
```
- 산맥=split+negative strength(가운데 높음). 협곡=split+positive strength(가운데 낮음).
```

신규 규칙:
```
- 산맥=ridge(fade=small, noise_amount=high). "양쪽 산맥"=2개 ridge 조합.
- 협곡=2개 ridge + bump(negative strength, center).
```

---

## 6. 구현 가이드 (의사코드)

### 6.1 수정 대상 파일

| 파일 | 변경 내용 |
|------|----------|
| `GenStepPatches.cs` | ApplySlope 삭제, ApplySplit 삭제, ApplyRidge 추가, Shape 디스패처 수정 |
| `MapGenParams.cs` | ElevationShape에 fade/noise_amount 필드 추가, ParseFade/ParseNoiseAmount 추가, GetAutoShapeForHills 업데이트 |
| `Dialog_TextToMap.cs` | 시스템 프롬프트 가이드/예시/few-shot 업데이트 |

### 6.2 ElevationShape 확장 (MapGenParams.cs)

```csharp
public class ElevationShape
{
    // 기존 필드 유지
    public string type;
    public string direction;
    public string strength;
    public string position;
    public string size;
    public string gap;
    public string fill;

    // 신규 필드
    public string fade;           // ridge용: small/medium/large 또는 0~1
    public string noise_amount;   // ridge용: none/low/medium/high 또는 0~1.5

    // 신규 파서
    public static float ParseFade(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0.5f;
        val = val.Trim().ToLower();
        switch (val)
        {
            case "small":  return 0.3f;
            case "medium": return 0.5f;
            case "large":  return 0.7f;
            default:
                return float.TryParse(val, ..., out float f)
                    ? Mathf.Clamp(f, 0f, 1f) : 0.5f;
        }
    }

    public static float ParseNoiseAmount(string val)
    {
        if (string.IsNullOrEmpty(val)) return 0.6f;
        val = val.Trim().ToLower();
        switch (val)
        {
            case "none": return 0f;
            case "low":  return 0.3f;
            case "medium": return 0.6f;
            case "high": return 1.0f;
            default:
                return float.TryParse(val, ..., out float f)
                    ? Mathf.Clamp(f, 0f, 1.5f) : 0.6f;
        }
    }
}
```

### 6.3 ApplyRidge (GenStepPatches.cs)

```csharp
/// <summary>
/// ridge: 한 방향에 산맥. smoothstep 프로파일 + Perlin noise 디테일.
/// 기존 slope(선형 상쇄)와 split(Max 덮어쓰기)를 모두 대체.
///
/// 핵심: profile은 0~1이므로 strength * profile은 항상 같은 부호.
///        ridge(left) + ridge(right)는 양쪽 다 양수 -> 골짜기 형성.
///        기존 slope(left) + slope(right)의 상쇄 문제가 구조적으로 불가능.
/// </summary>
private static void ApplyRidge(ElevationShape shape, Map map, MapGenFloatGrid grid)
{
    float strength = ElevationShape.ParseStrength(shape.strength);
    float angleDeg = ElevationShape.ParseDirection(shape.direction);
    float fade = ElevationShape.ParseFade(shape.fade);
    float noiseAmt = ElevationShape.ParseNoiseAmount(shape.noise_amount);

    float thetaRad = angleDeg * Mathf.Deg2Rad;
    float cosTheta = Mathf.Cos(thetaRad);
    float sinTheta = Mathf.Sin(thetaRad);

    float centerX = map.Center.x;
    float centerZ = map.Center.z;
    float mapHalfX = map.Size.x / 2f;
    float mapHalfZ = map.Size.z / 2f;
    float mapHalf = Mathf.Max(mapHalfX, mapHalfZ);

    // smoothstep 경계: fade가 클수록 산이 맵 안쪽까지 들어옴
    // profileStart: 이 t값부터 산이 시작됨 (-1~1 범위)
    // fade=0.5 -> profileStart=0.0 (중앙부터 산이 시작)
    // fade=0.3 -> profileStart=0.4 (가장자리 쪽에서만 산)
    // fade=0.7 -> profileStart=-0.4 (맵 대부분이 산)
    float profileStart = 1.0f - fade * 2f;
    float transitionWidth = 0.3f;  // smoothstep 전환 구간 폭
    float edgeStart = profileStart - transitionWidth;
    float edgeEnd = profileStart + transitionWidth;

    // Perlin noise (디테일용)
    Verse.Noise.ModuleBase noiseModule = null;
    if (noiseAmt > 0.01f)
    {
        noiseModule = new Verse.Noise.Perlin(
            0.035, 2.0, 0.5, 4, Rand.Range(0, 2147483647),
            Verse.Noise.QualityMode.Medium);
    }

    foreach (var cell in CellRect.WholeMap(map))
    {
        // 1. 방향 벡터로 정규화된 위치 계산 (-1 ~ +1)
        float dx = (cell.x - centerX) / mapHalf;
        float dz = (cell.z - centerZ) / mapHalf;
        float t = dx * cosTheta + dz * sinTheta;  // -1 (반대편) ~ +1 (높은 쪽)

        // 2. smoothstep 프로파일 (0 ~ 1)
        float profile = Smoothstep(edgeStart, edgeEnd, t);

        // 3. Perlin noise 디테일
        if (noiseModule != null && profile > 0.01f)
        {
            float noise = (float)noiseModule.GetValue(cell);  // approx -1~1
            // 프로파일에 노이즈 변조: 산 윤곽을 불규칙하게
            profile *= Mathf.Max(0f, 1f + noiseAmt * noise);
        }

        // 4. additive 적용
        grid[cell] += strength * profile;
    }
}

/// <summary>Hermite smoothstep: edge0에서 0, edge1에서 1, 매끄러운 전환.</summary>
private static float Smoothstep(float edge0, float edge1, float x)
{
    float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
    return t * t * (3f - 2f * t);
}
```

### 6.4 Shape 디스패처 수정 (GenStepPatches.cs)

```csharp
private static void ApplyShape(ElevationShape shape, Map map, MapGenFloatGrid grid)
{
    switch (shape.type)
    {
        case "ridge":  ApplyRidge(shape, map, grid);  break;
        case "radial": ApplyRadial(shape, map, grid); break;
        case "bump":   ApplyBump(shape, map, grid);   break;
        case "noise":  ApplyNoise(shape, map, grid);  break;
        case "ring":   ApplyRing(shape, map, grid);   break;
        // 하위 호환: 이전 세이브에서 slope/split이 올 수 있음
        case "slope":  ApplyRidgeFromLegacySlope(shape, map, grid); break;
        case "split":  ApplyRidgeFromLegacySplit(shape, map, grid); break;
        default:
            Log.Warning($"[MapGenAI] 알 수 없는 ElevationShape type: {shape.type}");
            break;
    }
}
```

### 6.5 레거시 호환 (slope/split -> ridge 자동 변환)

```csharp
/// <summary>이전 slope shape를 ridge로 변환하여 적용.</summary>
private static void ApplyRidgeFromLegacySlope(ElevationShape shape, Map map, MapGenFloatGrid grid)
{
    // slope(direction=X, strength=Y) -> ridge(direction=X, strength=Y, fade=medium, noise_amount=medium)
    var ridgeShape = new ElevationShape
    {
        type = "ridge",
        direction = shape.direction,
        strength = shape.strength,
        fade = "medium",
        noise_amount = "medium"
    };
    ApplyRidge(ridgeShape, map, grid);
}

/// <summary>이전 split shape를 2개 ridge로 변환하여 적용.</summary>
private static void ApplyRidgeFromLegacySplit(ElevationShape shape, Map map, MapGenFloatGrid grid)
{
    float strength = ElevationShape.ParseStrength(shape.strength);
    float angleDeg = ElevationShape.ParseDirection(shape.direction);

    if (strength < 0)
    {
        // 산맥 모드: 양쪽에 산 -> 2개 ridge
        // 원래 축에 수직 방향으로 2개 ridge 배치
        float perpAngle1 = angleDeg + 90f;
        float perpAngle2 = angleDeg - 90f;
        float absStrength = Mathf.Abs(strength);
        float gap = ElevationShape.ParseGap(shape.gap);

        var ridge1 = new ElevationShape
        {
            type = "ridge",
            direction = perpAngle1.ToString(CultureInfo.InvariantCulture),
            strength = absStrength.ToString(CultureInfo.InvariantCulture),
            fade = (0.5f - gap).ToString(CultureInfo.InvariantCulture),
            noise_amount = "high"
        };
        var ridge2 = new ElevationShape
        {
            type = "ridge",
            direction = perpAngle2.ToString(CultureInfo.InvariantCulture),
            strength = absStrength.ToString(CultureInfo.InvariantCulture),
            fade = (0.5f - gap).ToString(CultureInfo.InvariantCulture),
            noise_amount = "high"
        };
        ApplyRidge(ridge1, map, grid);
        ApplyRidge(ridge2, map, grid);
    }
    else
    {
        // 협곡 모드: 기존 split 로직을 유지하되 Add 방식으로
        // (협곡은 양쪽을 높이는 것이므로 2개 ridge와 동일)
        float perpAngle1 = angleDeg + 90f;
        float perpAngle2 = angleDeg - 90f;

        var ridge1 = new ElevationShape
        {
            type = "ridge",
            direction = perpAngle1.ToString(CultureInfo.InvariantCulture),
            strength = strength.ToString(CultureInfo.InvariantCulture),
            fade = "medium",
            noise_amount = "medium"
        };
        var ridge2 = new ElevationShape
        {
            type = "ridge",
            direction = perpAngle2.ToString(CultureInfo.InvariantCulture),
            strength = strength.ToString(CultureInfo.InvariantCulture),
            fade = "medium",
            noise_amount = "medium"
        };
        ApplyRidge(ridge1, map, grid);
        ApplyRidge(ridge2, map, grid);
    }
}
```

### 6.6 GetAutoShapeForHills 수정

```csharp
private static ElevationShape GetAutoShapeForHills(string hills)
{
    switch (hills)
    {
        case "left":
            return new ElevationShape { type = "ridge", direction = "left",
                strength = "medium", fade = "medium", noise_amount = "medium" };
        case "right":
            return new ElevationShape { type = "ridge", direction = "right",
                strength = "medium", fade = "medium", noise_amount = "medium" };
        case "top":
            return new ElevationShape { type = "ridge", direction = "top",
                strength = "medium", fade = "medium", noise_amount = "medium" };
        case "bottom":
            return new ElevationShape { type = "ridge", direction = "bottom",
                strength = "medium", fade = "medium", noise_amount = "medium" };
        case "center":
            return new ElevationShape { type = "bump", position = "center",
                size = "large", strength = "medium" };
        case "edges":
            return new ElevationShape { type = "radial",
                strength = "medium", size = "medium" };
        default:
            return null;
    }
}
```

---

## 7. 시나리오 검증표

### 시나리오 1: "왼쪽에 산"

**입력**: hills="left" 또는 elevation_shapes=[ridge(direction=left)]

**ridge 동작**:
```
direction = 180도 (left)
t = dx*cos(180) + dz*sin(180) = -dx/mapHalf
  왼쪽 가장자리: t = +1 (높음)
  오른쪽 가장자리: t = -1 (낮음)
profile = smoothstep(0~1): 왼쪽=1, 오른쪽=0, 매끄러운 전환
elevation += strength * profile
```

**결과**: 왼쪽이 높고 오른쪽이 낮은 자연스러운 산. Perlin 디테일로 윤곽 불규칙. 바닐라 Mountainous의 DistFromAxis+Add와 유사한 결과.

### 시나리오 2: "양쪽에 산"

**입력**: elevation_shapes=[ridge(left), ridge(right)]

**ridge(left) 동작**:
```
t = -dx/mapHalf
왼쪽=1, 오른쪽=0
```

**ridge(right) 동작**:
```
t = +dx/mapHalf
왼쪽=0, 오른쪽=1
```

**합산**:
```
왼쪽: 1 + 0 = 1 (높음)
오른쪽: 0 + 1 = 1 (높음)
가운데: 0 + 0 = 0 (낮음 -> 골짜기)
```

**결과**: 양쪽에 산이 있고 가운데에 골짜기. 상쇄 없음. 기존 slope(left)+slope(right)=평지 문제 완전 해결.

### 시나리오 3: "가운데에 산"

**입력**: hills="center" -> bump(position=center, size=large, strength=medium)

**변경 없음**: bump은 가우시안이므로 중앙이 높고 가장자리로 갈수록 낮아짐. 기존과 동일.

### 시나리오 4: "산맥이 동서로"

**입력**: elevation_shapes=[ridge(direction=top, strength=strong, fade=small, noise_amount=high)]

**ridge(top) 동작**:
```
direction = 90도 (top)
t = dz/mapHalf
  상단 가장자리: t = +1 (높음)
  하단 가장자리: t = -1 (낮음)
fade=small -> profileStart=0.4 -> 상단 40%만 높음
noise_amount=high -> 산 윤곽 매우 불규칙
```

**결과**: 맵 상단에 좁은 산맥. Perlin 노이즈로 자연스러운 산 형태. 동서(horizontal) 방향의 산맥이 됨.

**대안 (맵 중앙에 동서 산맥이 필요하면)**:
```json
[
  {"type":"ridge", "direction":"top", "fade":"small", "strength":"strong", "noise_amount":"high"},
  {"type":"ridge", "direction":"bottom", "fade":"small", "strength":"strong", "noise_amount":"high"}
]
```
-> 상단과 하단에서 산이 겹치며 중앙에 능선 형성.

### 시나리오 5: "산이 많은 지형"

**입력**: hill_amount=1.4 (전역 오프셋)

**변경 없음**: hill_amount은 전체 elevation에 오프셋을 더하는 기존 시스템. ridge와 독립적으로 동작. HillAmount=1.4 -> offset=0.4 -> 전체 elevation += 0.4.

### 시나리오 6: "산 + 호수"

**입력**:
```json
{
  "elevation_shapes": [
    {"type":"ridge", "direction":"left", "strength":"medium"},
    {"type":"bump", "position":"center", "size":"medium", "strength":"negative_medium", "fill":"water"}
  ]
}
```

**동작**:
1. ridge(left)이 먼저 적용 -> 왼쪽 elevation 높음
2. bump(water)가 나중 적용 -> 중앙의 물 지형 (fill=water이므로 물 shapes 뒤에 적용)
3. bump의 fertility 음수 설정으로 물 생성
4. 산 위에도 호수 가능 (bump은 elevation을 Min으로 낮춤)

**결과**: 왼쪽 산 + 가운데 호수. 기존과 동일한 동작.

---

## 8. 하위 호환성

### slope -> ridge 마이그레이션

- 기존 프리셋이나 세이브에 `type:"slope"`이 있을 수 있음
- ApplyShape 디스패처에서 slope을 감지하면 자동으로 ridge로 변환하여 적용
- LLM 프롬프트에서 slope은 더 이상 노출하지 않음 (ridge만 노출)

### split -> ridge 마이그레이션

- 기존 프리셋에 `type:"split"`이 있을 수 있음
- ApplyShape 디스패처에서 split을 감지하면 2개 ridge로 변환하여 적용
- 산맥 모드(negative strength): 축에 수직으로 2개 ridge
- 협곡 모드(positive strength): 축에 수직으로 2개 ridge

### 프리셋 파일 (.json)

PresetManager에서 기존 프리셋 로드 시 slope/split -> ridge 자동 변환 레이어 추가 가능.
또는 레거시 호환 디스패처가 처리하므로 프리셋 파일 수정 불필요.

---

## 9. 구현 순서 제안

| 단계 | 담당 | 작업 |
|------|------|------|
| 1 | rimworld-csharp-dev | ElevationShape에 fade, noise_amount 필드 + 파서 추가 |
| 2 | rimworld-csharp-dev | ApplyRidge 구현 + Smoothstep 함수 |
| 3 | rimworld-csharp-dev | ApplyRidgeFromLegacySlope, ApplyRidgeFromLegacySplit 레거시 변환 |
| 4 | rimworld-csharp-dev | Shape 디스패처 수정, GetAutoShapeForHills 수정 |
| 5 | rimworld-csharp-dev | ApplySlope, ApplySplit 코드 삭제하지 않고 주석 처리 (롤백 대비) |
| 6 | rimworld-ai-dev | 시스템 프롬프트 업데이트 (slope/split -> ridge) |
| 7 | rimworld-test-engineer | 6개 시나리오 유닛 테스트 + 상쇄 검증 테스트 |
| 8 | 빌드 + 게임 테스트 | 시각적 검증 |

---

## 10. 참고 자료

- [RW-Decompile GenStep_ElevationFertility.cs](https://github.com/josh-m/RW-Decompile/blob/master/RimWorld/GenStep_ElevationFertility.cs) -- 바닐라 elevation 생성 코드
- [Map Designer (Zylleon)](https://github.com/Zylleon/MapDesigner) -- MountainSettingsPatch의 Side/Split/Radial/Donut 패턴
- Map Designer의 Side 패턴 = AxisAsValueX + Rotate (slope과 유사하나 다른 수학)
- Map Designer의 Split 패턴 = abs(axis) - gap (양쪽 산, 상쇄 없음)
- 바닐라 DistFromAxis: span=0.42, Clamp(0,1), Invert, Add
- PLAN_SHAPES.md, PLAN_SHAPES_V2.md -- CSG/SDF composite 시스템 (독립적, ridge와 공존)
