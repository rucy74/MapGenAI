# Image-to-Map v2 — 설계 스펙 (v2)

> 이미지에서 RimWorld 맵을 생성하는 파이프라인 재설계. 기존 LLM 문자 그리드 방식을 폐기하고, Gemini 네이티브 마스크 segmentation + 멀티 Analyzer 아키텍처로 전환.
> 
> v2: Multi-Role Review 반영 (7건 apply).

## 1. 설계 원칙

1. **원본에 최대한 충실, 불가능한 부분은 AI가 합리적으로 채움** — 목표는 (a) 충실한 변환이되, 실사/복잡한 이미지에서는 AI 해석으로 자연스럽게 전환
2. **매핑 불가 요소는 가장 가까운 terrain으로 강제 매핑 + 로그** — 도로→모래, 건물→산 등. 유저에게 변환 내역 알림
3. **유저가 결과를 교정 가능** — 영역 편집 UI + 채팅 보정 둘 다 지원
4. **분석 방법과 출력 레이어 분리** — IImageAnalyzer 추상화로 분석 방법 교체 가능, Palette 인프라 재사용
5. **분석 전환/실패를 유저에게 알림** — fallback 발생, 분석 방식 전환 시 유저에게 안내 메시지 표시 *(v2 추가: review #5)*

## 2. 전체 아키텍처

```
유저 이미지 입력
    |
[ImageTypeClassifier] -- 이미지 유형 + 시점 판별 (LLM 1회 호출)
    |
    +-- topdown/unclear --> [GeminiMaskAnalyzer]
    |                           | 품질 체크 실패?
    |                           +-- yes --> [ClusteringAnalyzer] (fallback)
    |                           |           + 유저 알림: "다른 분석 방식으로 전환했습니다"
    |
    +-- front/quarter -----> [LLMSemanticAnalyzer] + [ImageGenAnalyzer] 병렬
    |                            | 유저에게 2개 미니맵 비교 제시
    |                            | + 설명: "AI가 두 가지 방식으로 해석했습니다"
    |
    v
PaletteLabel[,] (맵 크기)
    |
[편집 UI] -- 색상 미니맵 + 툴바 팔레트 + 영역 클릭
    |
[채팅 보정] -- 자연어로 그리드 수정 (선택) + LLM 해석 요약 표시
    |
[GridBuilder] --> elevation/fertility 그리드
    |
[PendingImageMap] --> [InjectPaletteGrid] --> 맵 반영
```

## 3. IImageAnalyzer 인터페이스

```csharp
interface IImageAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(
        byte[] imageBytes,
        int mapWidth,
        int mapHeight,
        string biomeDefName,
        CancellationToken ct);
}

class AnalysisResult
{
    PaletteLabel[,] Grid;         // 맵 크기
    int[,] SegmentIdGrid;          // 맵 크기, 각 셀이 속한 segment ID (v2: review #2)
    SegmentInfo[] Segments;        // 영역 메타데이터
    string[] MappingLogs;          // 매핑 로그
    float Coverage;                // 마스크 커버리지
    AnalyzerUsed AnalyzerUsed;     // 어떤 Analyzer가 사용됐는지 (v2: review #5)
    string FallbackReason;         // fallback 발생 시 사유 (v2: review #5)
}

class SegmentInfo
{
    int Id;                        // segment 고유 번호
    PaletteLabel Label;            // 현재 라벨
    string OriginalLabel;          // Gemini 원본 라벨 ("dense forest" 등)
    // Mask는 별도 저장하지 않음 — SegmentIdGrid에서 Id로 조회 (v2: review #2)
}
```

4개 구현체:

| Analyzer | 용도 | 입력 | 핵심 로직 |
|---|---|---|---|
| GeminiMaskAnalyzer | 탑다운 이미지 1순위 | 이미지 바이트 | Gemini 2.5 segmentation API → 마스크 업스케일 → 라벨 매핑 → 겹침 해소 → 빈 영역 채우기 |
| ClusteringAnalyzer | GeminiMask fallback | 이미지 바이트 | K-means L*a*b (k=7~8) → LLM 클러스터 라벨링 → 리사이즈 |
| LLMSemanticAnalyzer | 정면/쿼터뷰 | 이미지 바이트 | LLM에 "탑다운 terrain 배치 추론" 요청 → JSON 영역 기술 → 그리드 생성 |
| ImageGenAnalyzer | 정면/쿼터뷰 비교 대상 | 이미지 바이트 + 레퍼런스 | 이미지 생성 API로 탑다운 변환 → GeminiMaskAnalyzer에 넘김 |

## 4. GeminiMaskAnalyzer 상세

### 4.1 Gemini Segmentation API 호출

프롬프트:
```
Segment this image into terrain types. Return JSON list:
[{"box_2d": [y_min, x_min, y_max, x_max], "mask": "<base64 PNG>", "label": "<terrain type>"}]

Terrain types to identify: water, sand, grass, rich soil/forest, marsh/swamp, mountain/rock, ice/snow.
Also identify any other distinct regions.
```

- 모델: `gemini-2.5-flash` (thinkingBudget: 0)
- 이미지 전처리: 긴 변 2048px 캡, JPEG 재인코딩

**API 스키마 검증 (v2 추가: review #1):**
- 구현 전 반드시 실제 API 호출로 응답 스키마 확인
- 역직렬화 어댑터 레이어 추가 — API 응답 형식이 변경되어도 Analyzer 내부 로직은 영향 없도록
- 확인할 항목: box_2d 좌표 체계 (0-1000 vs 0-1), mask 인코딩 방식 (base64 PNG vs 다른 형식), label 필드 존재 여부

### 4.2 마스크 합성 (맵 크기에서 직접)

1. 각 마스크의 box_2d 좌표를 이미지 픽셀로 변환 (0-1000 정규화 → 실제 좌표)
2. 마스크 PNG 디코딩 → grayscale 확률맵 (0-255)
3. 확률맵을 **맵 크기로 bilinear 업스케일** (바운딩 박스 영역에 맞춰 배치)
4. 전체 마스크를 합성:
   - 셀마다 확률값이 가장 높은 마스크의 라벨 채택
   - 어떤 마스크에도 속하지 않는 셀 → 가장 가까운 이웃 라벨로 채움
   - **합성 결과는 `int[,] SegmentIdGrid`로 저장** — 각 셀에 segment ID 기록 *(v2: review #2)*
5. 커버리지 = (하나 이상의 마스크에 속한 셀 수) / (전체 셀 수)

### 4.3 라벨 의미론적 매핑

Gemini가 반환하는 라벨은 자유 텍스트. PaletteLabel로 매핑 필요:

| Gemini 라벨 패턴 | PaletteLabel |
|---|---|
| water, lake, ocean, river, pond, sea | Water |
| sand, beach, desert, dune | Sand |
| grass, field, plain, meadow, lawn | Grass |
| forest, dense vegetation, rich soil, farmland, crop | RichSoil |
| marsh, swamp, wetland, bog | Marsh |
| mountain, rock, cliff, boulder, stone, rocky | Mountain |
| ice, snow, glacier, frozen | Ice |
| sky, cloud, air | (제외 — 주변 라벨로 채움) |
| road, building, structure, person, vehicle | 가장 유사한 terrain (로그 남김) |

매핑은 키워드 매칭 + LLM fallback (매칭 실패 시 LLM에게 1회 질문).

### 4.4 품질 기반 fallback (v2 수정: review #7, #8)

기존 coverage 단일 기준 → **복합 기준**으로 변경:

1. **커버리지 체크**: 마스크가 전체 면적의 70% 미만 → fallback
2. **라벨 다양성 체크**: 단일 라벨이 전체의 90% 이상 차지 → fallback (Gemini가 "vegetation" 하나로 뭉친 경우 감지)
3. 둘 중 하나라도 해당 → ClusteringAnalyzer fallback + 유저 알림

- 로그: `[MapGenAI] Gemini mask quality check failed (coverage: 45% / dominant label: 95%), falling back to clustering`
- **유저 알림**: Dialog에 "분석 품질이 낮아 다른 방식으로 전환했습니다" 표시 *(v2: review #5)*
- 임계값은 구현 후 골든 데이터셋으로 튜닝

## 5. ClusteringAnalyzer 상세

1. 이미지를 L*a*b 색공간으로 변환
2. K-means 클러스터링 (k=7~8)
3. 원본 이미지 + 클러스터별 대표색 팔레트를 LLM에게 전송
4. LLM 프롬프트: "이 이미지의 각 색상 클러스터가 어떤 terrain type인지 JSON으로 반환"
5. 각 클러스터에 PaletteLabel 부여
6. 클러스터 맵을 맵 크기로 리사이즈 (Nearest-Neighbor)
7. LLM 라벨링 실패 시 HSV 규칙 기반 자동 매핑 (최후 방어)

## 6. LLMSemanticAnalyzer 상세

정면/쿼터뷰 이미지용. 픽셀 분석이 아닌 의미론적 해석.

1. 이미지를 Gemini에게 전송
2. 프롬프트: "이 풍경을 위에서 내려다보면 어떤 terrain 배치인지 JSON으로 기술. 각 영역의 위치(left/center/right, top/middle/bottom), terrain type, 대략적 비율을 포함."
3. 응답: `[{region: "left-top", terrain: "mountain", coverage_pct: 30}, ...]`
4. 영역 기술 → PaletteLabel[,] 그리드 생성 (위치/크기 비율 기반 배치, 경계 노이즈 추가)

## 7. ImageGenAnalyzer 상세

정면/쿼터뷰 이미지를 이미지 생성 모델로 탑다운 변환 후 segmentation.

1. 림월드 탑다운 맵 레퍼런스 이미지 (모드에 번들, 다양한 biome별 1장씩)
2. 유저 이미지 + 레퍼런스를 이미지 생성 API에 전송
3. 프롬프트: "Transform the second image into a top-down terrain map in the style of the first image"
4. 생성된 탑다운 이미지를 GeminiMaskAnalyzer.AnalyzeAsync()에 넘김
5. GeminiMask 결과를 그대로 반환

- 이미지 생성 Provider: 구현 시 조사 결정 (Flux, NanoBanana 등) — **의도적 defer**
- 비용 안내: 유저에게 "2가지 방식을 비교합니다 (추가 비용 발생)" 표시

## 8. ImageTypeClassifier

LLM 1회 호출로 이미지 유형/시점 분류:

```
Classify this image. Return JSON:
{
  "type": "photo" | "map" | "sketch" | "screenshot",
  "perspective": "topdown" | "front" | "quarter" | "unclear",
  "confidence": 0.0-1.0
}
```

라우팅:
- topdown → GeminiMaskAnalyzer
- front / quarter → LLMSemanticAnalyzer + ImageGenAnalyzer 병렬 실행
- unclear → GeminiMaskAnalyzer (탑다운 취급, 품질 체크 후 필요 시 fallback)

분류 실패 시 → perspective=unclear로 간주.

## 9. 편집 UI

### 9.1 색상 미니맵

- Dialog_TextToMap 내 별도 영역, 약 200x200px (맵 비율 유지)
- PaletteLabel[,] → Texture2D (라벨별 고정 색상)
- 라벨 색상: W=파랑, S=노랑, G=초록, R=진초록, M=갈색, X=회색, I=흰색
- Analyzer 완료 즉시 표시

### 9.2 툴바 팔레트

- 미니맵 옆/아래에 라벨 버튼 7개 (색상 사각형 + 라벨명)
- 현재 선택 라벨 하이라이트
- 기본: 선택 없음

### 9.3 영역 클릭 편집

- 미니맵 위 클릭 → 해당 좌표의 segment ID 조회 (`SegmentIdGrid[x,y]`) → 같은 ID를 가진 셀 전체를 선택 라벨로 변경 *(v2: review #2 — bool[,] Mask 대신 SegmentIdGrid 사용)*
- 툴바에서 라벨 선택 상태 → 해당 영역 전체를 선택 라벨로 변경
- 미니맵 즉시 갱신
- undo 1단계 지원

### 9.4 승인 흐름

미니맵 표시 → 유저 편집 (선택) → [맵 생성] 버튼 → GridBuilder → Map Preview 갱신

### 9.5 정면/쿼터뷰 비교 (v2 수정: review #4)

- 미니맵 2개 나란히 표시
- **유저 친화적 라벨** *(v2 추가)*:
  - 방식 A: "AI 해석" (부제: "풍경을 분석해서 배치를 추론했습니다")
  - 방식 B: "이미지 변환" (부제: "이미지를 맵 스타일로 변환했습니다")
- 유저가 하나 선택 → 해당 PaletteLabel[,]로 편집 UI 진입

## 10. 채팅 보정 (v2 수정: review #6)

- 기존 Dialog_TextToMap 입력창 사용
- 이미지 분석 완료 후 텍스트 입력 시 보정 모드
- LLM에게 현재 라벨 분포 요약 (예: "Mountain 25% left-top, Grass 40% center, Water 15% right") + 유저 텍스트 전송
- LLM 응답: 수정 지시 (변경할 영역 위치 + 새 라벨 + 비율)
- **피드백 표시 (v2 추가):**
  - LLM 해석 요약 1줄 표시: "물 영역을 30%→15%로 축소, 모래를 15%→30%로 확대합니다"
  - 미니맵 즉시 갱신 (수정 전/후 비교는 undo로 대체)
- 반복 가능

## 11. 에러 처리

| 단계 | 에러 | 처리 | 유저 알림 |
|---|---|---|---|
| ImageTypeClassifier | LLM 호출 실패 | perspective=unclear, GeminiMask 시도 | (없음 — 내부 전환) |
| GeminiMaskAnalyzer | API 실패/타임아웃 | ClusteringAnalyzer fallback | "분석 방식을 전환했습니다" |
| GeminiMaskAnalyzer | 품질 체크 실패 (커버리지 < 70% 또는 단일 라벨 > 90%) | ClusteringAnalyzer fallback | "분석 품질이 낮아 다른 방식으로 전환했습니다" |
| ClusteringAnalyzer | LLM 라벨링 실패 | HSV 규칙 기반 자동 매핑 | "자동 색상 매핑을 사용했습니다" |
| LLMSemanticAnalyzer | 영역 기술 파싱 실패 | GeminiMask로 전환 시도 | "분석 방식을 전환했습니다" |
| ImageGenAnalyzer | 이미지 생성 API 실패 | 해당 경로 포기, LLMSemantic 결과만 | "이미지 변환에 실패했습니다. AI 해석 결과만 표시합니다" |
| 편집 UI | segment 데이터 없음 | 셀 단위 자동 전환 | (없음 — 자동) |
| 채팅 보정 | LLM 수정 파싱 실패 | "수정을 적용할 수 없습니다" 안내 | 해당 메시지 표시 |

## 12. 기존 인프라 재사용

### 유지 (Palette 인프라)

| 파일 | 역할 | 변경사항 |
|---|---|---|
| PaletteDefinition.cs | 라벨 enum + 색상/fertility 매핑 | 확장 가능 구조로 리팩터링 (데이터 드리븐) |
| GridBuilder.cs | PaletteLabel[,] → elevation/fertility 변환 | 입력이 이미 맵 크기이면 리사이즈 스킵. mountain transition(2-ring) + water erosion 로직은 유지 |
| BiomeValidator.cs | 바이옴별 제약 검증 | 변경 없음 |
| PendingImageMap.cs | 변환 결과 transient holder | 변경 없음 |
| GenStepPatches.InjectPaletteGrid | 맵 주입 Harmony 패치 | 변경 없음 |

### 삭제 (LLMSegmenter)

| 파일 | 이유 |
|---|---|
| LLMImageSegmenter.cs | 문자 그리드 방식 폐기 |
| GridParser.cs | 문자 그리드 파싱 불필요 |
| GridSizeCalculator.cs | 마스크 기반에서 그리드 크기 계산 불필요 |
| ImageDownscaler.cs | 전처리 로직은 새 Analyzer 내부로 이동 |
| BiomeContextProvider.cs | 바이옴 힌트는 새 프롬프트에 통합 |

### 신규

| 파일/모듈 | 역할 |
|---|---|
| IImageAnalyzer.cs | 분석 인터페이스 |
| GeminiMaskAnalyzer.cs | Gemini 마스크 segmentation |
| ClusteringAnalyzer.cs | K-means + LLM 라벨링 |
| LLMSemanticAnalyzer.cs | 의미론적 terrain 해석 |
| ImageGenAnalyzer.cs | 이미지 생성 모델 경유 |
| ImageTypeClassifier.cs | 유형/시점 분류 라우터 |
| MaskCompositor.cs | 마스크 업스케일 + 합성 + 겹침 해소 |
| GeminiResponseAdapter.cs | Gemini API 응답 역직렬화 어댑터 *(v2: review #1)* |
| LabelMapper.cs | Gemini 라벨 → PaletteLabel 매핑 |
| QualityChecker.cs | 커버리지 + 라벨 다양성 복합 품질 체크 *(v2: review #7, #8)* |
| SegmentEditor.cs | 영역 편집 로직 (UI와 분리) |
| ChatCorrector.cs | 채팅 보정 LLM 호출 + delta 적용 |

## 13. 라벨 체계

1차는 7종 (W/S/G/R/M/X/I). 확장 가능 구조:

- PaletteDefinition을 enum 하드코딩 → 데이터 드리븐으로 전환
- 라벨 추가 시: 정의 파일에 항목 추가 + GridBuilder에 elevation/fertility 매핑 추가
- 후보 확장 라벨: GravelBeach, River, ShallowWater

## 14. 확정 사항 (discuss 외부)

- **반복 보정**: 맵 생성 후 "산을 더 크게" 같은 텍스트 보정 지원 (채팅 보정으로 구현)
- **Provider 의존성**: Gemini 2.5 전용 (segmentation). Gemini 3에서 제거됨. IImageAnalyzer 추상화로 향후 교체 대비

## 15. 스코프 외 (향후 과제)

- 브러시 편집 (드래그로 여러 셀에 라벨 칠하기)
- River 후처리 (W 라벨의 선형 요소 → 바닐라 river GenStep 정합)
- Provider 확장 (OpenAI Vision, Anthropic Vision segmentation)
- Cross-platform 파일 다이얼로그
- 고해상도 서브샘플링
- 이미지 생성 모델 Provider 선정 (Flux, NanoBanana 등)

## User Utterance Mapping

| # | User utterance (verbatim quote) | Main's interpretation | Where in design |
|---|---|---|---|
| 1 | "a에 가까운 b, c의 경우에는 이미 텍스트로 가능하기 때문에 a를 하고 싶은데 실사 이미지나 따라할 수 없는 이미지가 나올 경우들이 많을 수 있기 때문에 b가 될 수밖에 없겠찌" | 핵심 가치: 최대한 원본에 충실하되 불가능하면 AI가 합리적으로 채움 | §1 설계 원칙 #1 |
| 2 | "b" (Q2 매핑 불가 요소) | 강제 매핑 + 변환 내역 로그 | §1 설계 원칙 #2, §4.3 라벨 매핑, §11 에러 처리 |
| 3 | "d" (Q3 분석 단위) | Gemini 마스크 우선, 클러스터링 fallback | §2 전체 아키텍처, §4 GeminiMask, §5 Clustering |
| 4 | "a 이고 라벨의 경우 의미론적으로 비슷한 레이블로 매핑해야 될 거 같은데" | 커버리지 기반 fallback + 라벨은 의미론적 유사도 매핑 | §4.3 라벨 매핑, §4.4 품질 기반 fallback |
| 5 | "c" (Q5 유저 개입) + "c면 좋겠는데 어떻게 '그리드를 보여준다는 거지?'" | 색상 미니맵 + 영역 편집. 텍스트 그리드는 의미 없음 | §9 편집 UI |
| 6 | "일단 b로 하고 나중에 c 도전" (Q6 편집 단위) | 영역 단위 편집 1차, 브러시는 후속 | §9.3 영역 클릭 편집, §15 스코프 외 |
| 7 | "a 이전 코드가 프레임화 되서 구현에 제약이 있게 하고 싶진 않고 싶어" | LLMSegmenter 전부 교체 | §12 삭제 목록 |
| 8 | "그래 추천대로 갈게" (Q8 Palette 인프라) | Palette 인프라 재사용 (메인 추천 수락) | §12 유지 목록 |
| 9 | "c가 괜찮나?" + "추천하는 대로 갈게" (Q9 Provider 전략) | IImageAnalyzer 추상화 레이어 | §3 인터페이스 |
| 10 | "b가 좋을 거 같네" (Q10 이미지 유형) | 자동 분류 + 유형별 프롬프트 분기 | §8 ImageTypeClassifier |
| 11 | "임의의 이미지를 보내서 바로 mapping 시키지 말고, 이미지 생성 모델에 두 이미지를 보내서..." | 정면/쿼터뷰에서 이미지 생성 모델로 탑다운 변환 경로 추가 | §7 ImageGenAnalyzer |
| 12 | "c 둘 다 구현하고 비교해보고 싶은데" (시점별 전략) | LLMSemantic + ImageGen 둘 다 구현, 유저에게 비교 제시 | §2 아키텍처 (front/quarter 분기), §9.5 비교 |
| 13 | "a" (Q11 클러스터링 LLM 역할) | 클러스터 라벨링만 담당 | §5 ClusteringAnalyzer |
| 14 | "c" (Q13 라벨 체계) | 확장 가능 구조, 1차 7종 | §13 라벨 체계 |
| 15 | "b가 나으려나?" + "ㅇㅇ 그거로" (Q14 해상도) | 맵 크기에서 직접 합성 (확률맵 상태에서 업스케일) | §4.2 마스크 합성 |
| 16 | "d는 어려워서 그런거야?" + "ㅇㅇ 그래" (Step 4) | Ideal + Creative 혼합: 영역 편집 UI + 채팅 보정 둘 다 | §9 편집 UI, §10 채팅 보정 |
| 17 | "ㅇㅇ 그렇게 해 줘" (Q12 편집 선택 방식) | 툴바 팔레트 (메인 추천 수락) | §9.2 툴바 팔레트 |

---
## 작성 이력
- 2026-04-12 18:30 — 초안 작성 (discuss Step 6)
- 2026-04-12 19:00 — v2 리비전 (Multi-Role Review 7건 apply: #1 API 스키마 검증 어댑터, #2 bool[,]→int[,] SegmentIdGrid, #4 비교 UI 설명, #5 fallback 유저 알림, #6 채팅 피드백 표시, #7 라벨 다양성 체크, #8 복합 fallback 기준)
