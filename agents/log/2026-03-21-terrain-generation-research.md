# 2026-03-21 — 타 게임 지형/형태 생성 방식 종합 리서치

## research-analyst

---

## 1. Minecraft WorldEdit 형태 생성 시스템

### 1.1 기본 형태 명령어

WorldEdit은 간결한 명령어로 기하 도형을 배치한다:

| 명령어 | 용도 | 예시 |
|--------|------|------|
| `//cyl <pattern> <r> [h]` | 채워진 원기둥 | `//cyl glass 5 10` |
| `//hcyl <pattern> <r> [h]` | 빈 원기둥 | `//hcyl stone 8 3` |
| `//sphere [-r] <pattern> <r>` | 채워진 구 | `//sphere stone 5` |
| `//hsphere [-r] <pattern> <r>` | 빈 구 | `//hsphere glass 10` |
| `//pyramid <pattern> <size>` | 피라미드 | `//pyramid sandstone 6` |
| `//hpyramid <pattern> <size>` | 빈 피라미드 | `//hpyramid stone 4` |

타원형 지원: `//cyl stone 5,8 3` (EW=5, NS=8), `//sphere stone 5,8,3` (3축 독립)

### 1.2 커스텀 형태: //generate (= 우리의 SDF와 동일 패러다임)

`//generate <pattern> <expression>` — 수학 수식이 true(>0)를 반환하면 블록 배치.

- 변수 `x, y, z`는 선택 영역 내에서 [-1, 1]로 정규화됨 → **우리의 [0,1] 정규화와 동일한 접근**
- 플래그: `-r` (raw 좌표), `-c` (중심 원점), `-o` (배치 위치 원점), `-h` (속 빈 형태)

**핵심 예시 — 토러스:**
```
//g stone (0.75-sqrt(x^2+y^2))^2+z^2 < 0.25^2
```

복잡한 형태 예시 (회전 + 조건부):
```
rotate(x,z,-0.33); rotate(z,y,-0.15); data=(y>0?0:8);
(y<0?1.9*x^2+4*y^2+(z>0?1.3*z^2:0.78*z^2):1.75*x^2+9*(y+5)^2+(z>0?1.15*z^2:0.7*z^2)) > 43^2
```

**K3DSurf**: 외부 도구로 시각적으로 3D 형태를 만들면 수식을 자동 생성. WorldEdit에 복사 가능.

### 1.3 브러시 시스템 (지형 스컬프팅)

| 브러시 | 명령어 | 용도 |
|--------|--------|------|
| Sphere | `/brush sphere [-h] <pattern> [r]` | 구형 블록 배치 |
| Cylinder | `/brush cylinder [-h] <pattern> [r] [h]` | 원기둥 배치 |
| Smooth | `/brush smooth [r] [iterations] [mask]` | 지형 평활화 |
| Gravity | `/brush gravity [r] [-h]` | 블록 아래로 떨어뜨림 |
| Deform | `/brush deform <shape> [size] [expr]` | 수식 기반 변형 |
| Raise/Lower | `/brush raise/lower <shape> [size]` | 지형 높이 조절 |
| Height | (고도 변경) | heightmap 기반 |

### 1.4 Region Operations (= 우리의 불리언 연산에 대응)

| 명령어 | 용도 |
|--------|------|
| `//set <pattern>` | 선택 영역 전체를 채움 |
| `//replace [mask] <pattern>` | 조건부 교체 |
| `//overlay <pattern>` | 지형 위에 덮기 (아래로 스캔) |
| `//walls <pattern>` | 벽만 생성 |
| `//faces <pattern>` | 6면 모두 채우기 |
| `//hollow [thickness]` | 내부 비우기 |
| `//smooth [iterations]` | 지형 평활화 |
| `//deform <expression>` | 수식 기반 좌표 변환 |

### 1.5 WorldEdit과 CSG 비교

| 비교 항목 | WorldEdit | 우리 CSG+SDF |
|-----------|-----------|-------------|
| 기본 도형 | sphere, cyl, pyramid | circle, rect, tri, star, heart, poly, ellipse |
| 불리언 연산 | 없음 (수동으로 //set+//replace 조합) | union, sub, inter + smooth 버전 |
| 커스텀 형태 | //generate (수식) | LLM이 도형 조합으로 표현 |
| 좌표계 | [-1,1] 정규화 | [0,1] 정규화 |
| 사용자 인터페이스 | 채팅 명령어 | 자연어 대화 |

**핵심 차이**: WorldEdit은 명시적 CSG 불리언 연산을 지원하지 않는다. 사용자가 선택 영역을 수동으로 조합해야 한다. 우리 시스템은 LLM이 불리언 트리를 자동 생성하므로 훨씬 표현력이 높다.

### 1.6 선택 타입

| 타입 | 명령어 | 설명 |
|------|--------|------|
| Cuboid | `//sel cuboid` | 기본 직육면체 |
| Poly | `//sel poly` | 다각형 (최대 20점) |
| Ellipsoid | `//sel ellipsoid` | 타원체 |
| Convex | `//sel convex` | 볼록 다면체 |

---

## 2. Minecraft AI 빌딩 프로젝트

### 2.1 T2BM — Text to Building in Minecraft (2024, 가장 직접적으로 관련)

**논문**: arxiv 2406.08751

**아키텍처** (3단계 파이프라인):
1. **Input Refining**: 사용자 설명 + 컨텍스트 예시 → LLM이 상세화
2. **Interlayer Generation**: 상세 프롬프트 + 포맷 예시 → LLM이 JSON 생성
3. **Repairing**: 잘못된 블록명, 좌표 오류 등 자동 수정

**JSON Interlayer 스키마:**
```json
{
  "building_name": {
    "structural_component": {
      "position": {
        "start_x": int, "start_y": int, "start_z": int,
        "end_x": int, "end_y": int, "end_z": int
      },
      "material": "block_identifier",
      "hollow": boolean,
      "functional": false
    },
    "functional_component": {
      "position": { "x": int, "y": int, "z": int },
      "material": "block_identifier",
      "functional": true,
      "state": { "facing": "direction", "hinge": "side" }
    }
  }
}
```

- 구조 블록: 6좌표 (start/end → 볼륨 정의) ← 우리의 center+dimensions와 유사
- 기능 블록: 3좌표 (단일 위치)
- hollow 속성 → 우리의 불리언 sub와 기능적 동일

**핵심 발견**: T2BM도 LLM이 직접 좌표를 나열하지 않고 구조적 컴포넌트 단위로 기술. 우리의 "LLM은 무엇을 조합할지만 결정" 원칙과 완전히 일치.

**성능**: GPT-3.5 만족도 8→22%, GPT-4 만족도 12→38% (input refining 후)

### 2.2 BlockGPT (상용 도구)

- 텍스트 설명 → 실제 블록 데이터 (렌더가 아닌 실 구조)
- .schem, .litematic, .schematic 포맷 내보내기
- WorldEdit/Litematica와 호환
- 내부 작동 방식 비공개, 결과물 품질은 단순 구조에서 양호

### 2.3 BuilderGPT (오픈소스)

- GitHub: CyniaAI/BuilderGPT
- Minecraft 버전 선택 → 포맷 선택 → 텍스트 설명 → LLM 호출 → 파일 생성
- 구조: generated/ 폴더에 결과 저장

### 2.4 Voyager (NVIDIA/MineDojo, 2023)

- GPT-4 기반 Minecraft 자율 에이전트
- **구조 건축은 제한적**: 주로 탐험/크래프팅/서바이벌에 최적화
- 인간 피드백 보강 시 네더 포탈, 집 등 건축 가능
- 코드를 action space로 사용 (저수준 명령이 아닌 프로그래밍)
- **교훈**: 건축보다 탐험에 초점. 공간 추론은 LLM의 약점 확인

### 2.5 Mindcraft (2025)

- LLM + Mineflayer 기반 Minecraft 에이전트 플랫폼
- 47개 고수준 액션 명령어
- 커스텀 Mineflayer JS 코드 실행으로 건축 가능
- MineCollab 벤치마크: 협업 건설 태스크 포함
- **교훈**: 자연어 의사소통이 복잡한 건축 태스크의 병목 (성능 15% 하락)

### 2.6 Talking-to-Build (ICMI 2025)

- LLM 보조 인터페이스 vs 명령어 인터페이스 비교 연구 (30명)
- 결과: LLM 인터페이스가 성능, 몰입도, 경험 모두 우수
- 4단계: reflection(해석) → planning(분해) → instruction(명령 생성) → self-check
- **교훈**: 자연어→건축이 사용자 경험에 실제로 긍정적. 우리 접근 방향 검증.

### 2.7 Minecraft Pixel Art 생성기

- 핵심 알고리즘: 각 픽셀 → 가장 가까운 Minecraft 블록 색상 매칭
- RGB→LAB 색공간 변환 + Floyd-Steinberg 디더링
- ~60개 Minecraft 블록 색상으로 매핑
- 에지 감지로 중요 윤곽선 보존
- .schem 파일로 내보내기 가능

### 2.8 Schematic 파일 포맷 (Sponge v3)

```
Schematic (NBT, GZip 압축)
├── Version: 3
├── Width, Height, Length (unsigned short)
├── Blocks
│   ├── Palette: {"minecraft:stone": 0, "minecraft:oak_planks": 1, ...}
│   ├── Data: varint[] (팔레트 인덱스)
│   └── BlockEntities: [{pos, id, nbt}, ...]
├── Biomes (같은 구조)
├── Entities: [{pos, type, nbt}, ...]
└── Offset: [x, y, z]
```

위치 공식: `index = x + z * Width + y * Width * Length` (XZY 순서)

---

## 3. 타 게임 지형/형태 도구

### 3.1 Factorio 블루프린트

**포맷**: Base64(zlib(JSON))

```json
{
  "blueprint": {
    "entities": [
      {
        "entity_number": 1,
        "name": "offshore-pump",
        "position": {"x": 0.5, "y": -1.5},
        "direction": 4,
        "connections": {...}
      }
    ],
    "tiles": [...],
    "icons": [...],
    "label": "My Blueprint",
    "version": 562949954076672
  }
}
```

- 원점 (0,0) = 블루프린트 중심, 부동소수점 좌표
- 엔티티 리스트 방식 (위치+타입+방향+연결)
- Factorio 2.0부터 압축 없는 JSON 직접 임포트 지원
- **교훈**: 우리 CSG JSON과 달리 "모든 엔티티 좌표 나열" 방식. 2D 팩토리에 적합하지만 형태 기술에는 비효율적.

### 3.2 Terraria TEdit

- 페인트 프로그램 UI (연필, 브러시, 모핑 도구)
- 이미지→픽셀아트 변환 기능 내장
- 클립보드 복사/붙이기
- **교훈**: 직접 타일 편집 방식. 프로그래매틱/자연어 접근이 아님. 참고 가치 낮음.

### 3.3 Dwarf Fortress

- Advanced World Generation: 고도, 강수량, 온도 등 5가지 기본 변수로 가중 메쉬
- Perfect World DF: 월드 페인터 스타일 도구
- DFHack: 런타임 모딩 프레임워크
- **교훈**: 매크로 레벨 세계 생성 (대륙/기후). 우리의 맵 레벨 지형 생성과 스케일이 다름.

### 3.4 Cities: Skylines

- heightmap 임포트: 4096x4096, 16비트 그레이스케일 PNG/TIFF
- terrain.party: 실제 지형 데이터 → heightmap 자동 생성
- 삽 도구로 실시간 지형 스컬프팅
- **교훈**: 순수 heightmap 기반. 우리의 CSG는 이보다 표현력이 높음 (불리언 연산).

### 3.5 No Man's Sky

- 복셀 기반 지형 시스템 + 다층 노이즈 생성
- Terrain Manipulator: Mine/Create/Restore/Flatten 4모드
- 구형 영향 범위, 3단계 크기 조절
- 편집 제한: 15,000 edits max
- **교훈**: 실시간 복셀 편집. RimWorld의 2D 타일맵과 근본적으로 다름.

---

## 4. 이미지→지형 접근법

### 4.1 게임 엔진 Heightmap 임포트

**Unity**: 16비트 그레이스케일 RAW → Terrain 컴포넌트에 직접 적용
**Unreal Engine**: 16/32비트 PNG/EXR → Landscape 에디터

외부 도구: World Machine, Terragen, Houdini → heightmap 생성 → 게임 엔진 임포트

### 4.2 RimWorld에 적용 가능성

현재 우리 시스템의 heightmap 타입이 이미 이 역할을 한다:
- LLM이 digit grid를 생성 → C#이 elevation으로 변환
- 9x9~16x16 해상도 (RimWorld 맵 크기 대비 적절)

이미지 API 연동 가능성:
- LLM이 이미지 설명 → DALL-E/Stable Diffusion → 이미지 → grid 변환
- 문제점: 추가 API 호출 비용, 지연시간, 변환 품질 불확실
- 결론: **현재로서는 과잉**. CSG가 더 직접적이고 예측 가능.

---

## 5. 최신 AI 연구 동향

### 5.1 Google DeepMind Genie 3 (2025-2026)

- 텍스트 프롬프트 → 인터랙티브 3D 월드 생성
- 게임 전반(캐릭터, 이펙트, 물리)을 포함하는 범용 월드 모델
- 우리와 스케일이 다름 (전체 월드 vs 맵 내 지형)

### 5.2 MarioGPT (2024)

- 자연어 프롬프트 → Mario 레벨 생성
- "적 많이, 파이프 많이" 등 자연어로 디자인 요소 제어
- **교훈**: 자연어→게임 콘텐츠 생성의 실증. 우리도 같은 패러다임.

### 5.3 PCG + LLM 통합 트렌드

2024년 한 해에만 76편 논문 발표 (총 131편 리뷰). 주요 응용:
1. Procedural Content Generation (PCG)
2. Mixed-initiative Game Design
3. Mixed-initiative Gameplay
4. Playing Games
5. Game User Research

---

## 6. 평가: CSG 접근법의 타당성

### 6.1 대안 비교

| 접근법 | 장점 | 단점 | 우리에게 적합? |
|--------|------|------|---------------|
| **CSG+SDF (현재)** | LLM 강점 활용(기호 추론), 토큰 효율, 매끄러운 결과 | 구현 복잡도 | **최적** |
| Image-based (LLM→이미지→grid) | 시각적 직관 | 추가 API 비용, 불확실한 변환, 지연 | 보완재 (P4) |
| Voxel-paint (Minecraft식) | 정밀 제어 | 토큰 폭발, LLM 좌표 오류 | 부적합 |
| Template library | 안정적, 빠름 | 표현력 제한 | 보완재 (기존 7종 primitive) |
| Direct coordinate listing | 단순 구현 | 토큰 비효율, LLM 공간 추론 약점 | 부적합 |
| Mathematical expression (WorldEdit식) | 무한 표현력 | LLM이 수식 생성하면 오류율 높음 | 부적합 |

### 6.2 우리 CSG 접근법의 위치

**기존 시스템과의 관계:**
```
[Template Library]     → 기존 7종 primitive (slope, radial, split, bump, noise, ring, heightmap)
[CSG+SDF]             → composite 타입 (자유 형태)
[Image-based]         → 미래 확장 가능성 (P4)
```

이 3계층 구조가 가장 합리적:
- 단순 요청 → template (bump position:"center")
- 모양 요청 → CSG (star+circle-circle = 고양이)
- 극도로 복잡한 요청 → heightmap (digit grid) 또는 미래 이미지 기반

### 6.3 T2BM과의 비교

| 항목 | T2BM | 우리 CSG |
|------|------|---------|
| 도메인 | Minecraft 3D 건물 | RimWorld 2D 지형 |
| LLM 출력 | 컴포넌트별 좌표 박스 (JSON) | 도형+불리언 체인 (JSON) |
| 중간 표현 | interlayer (위치+재질) | CSG 트리 (도형+연산) |
| 렌더링 | 블록 직접 배치 | SDF 래스터라이저 |
| 복구 모듈 | repairer (블록명/좌표 수정) | 미구현 (TODO) |
| 표현력 | 직교 박스 단위 | 곡선/유기적 형태 가능 |

**핵심 차이**: T2BM은 건물이라 직교 박스면 충분하지만, 우리는 자연 지형이므로 SDF의 곡선/부드러운 경계가 필수. CSG+SDF가 정확히 맞는 선택.

### 6.4 가져올 수 있는 것

| 출처 | 가져올 것 | 적용 방법 |
|------|----------|----------|
| T2BM | Repairer 모듈 개념 | CSG JSON 자동 검증/수정 단계 추가 |
| T2BM | Input Refining 단계 | LLM 2-pass: 1차 상세화 → 2차 JSON 생성 (토큰 비용 증가와 트레이드오프) |
| WorldEdit | //smooth 개념 | 이미 SDF smooth union으로 구현됨 |
| WorldEdit | 정규화 좌표계 | 이미 [0,1]로 구현됨 |
| Factorio | 압축 블루프린트 공유 | 프리셋 공유 시 Base64 압축 (P4) |
| Sponge Schematic | 팔레트 기반 압축 | 지형 타입이 적으므로 불필요 |
| Talking-to-Build | 4단계 LLM 파이프라인 | reflect→plan→instruct→check 참고 |

### 6.5 결론

**CSG+SDF 접근법을 유지한다.** 근거:

1. **학술적 뒷받침**: Chain-of-Symbol, Don't Mesh with Me, ShapeCraft 등 논문이 LLM+기호적 형태 표현의 우수성을 입증
2. **실무적 검증**: T2BM이 동일한 "LLM→구조화 JSON→렌더링" 패러다임으로 성공
3. **대안 부재**: WorldEdit은 CSG 불리언이 없고, 직접 좌표 나열은 토큰 비효율, 이미지 기반은 과잉
4. **RimWorld 특화**: 2D 타일맵에서 SDF는 픽셀 아트보다 매끄러운 자연 지형을 만듦
5. **기존 시스템과의 조화**: template(7종) + CSG(composite) + heightmap(digit grid) 3계층이 모든 복잡도를 커버

**추가 고려사항:**
- T2BM의 repairer 모듈을 참고하여 CSG JSON 검증 단계 추가 검토
- Talking-to-Build의 self-check 단계 → LLM이 생성한 CSG를 다시 검증하는 2-pass 접근 검토 (단, 토큰 비용 2배)
