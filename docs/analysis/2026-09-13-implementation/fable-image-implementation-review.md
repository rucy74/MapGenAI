# Claude consultation

Requested: claude-fable-5-1

Observed modelUsage: ["claude-haiku-4-5-20251001", "claude-fable-5-1"]

## 검토 결과 (첨부 코드 기준, 중요 결함 3개)

전반적으로 이전 지적(자기교차 검증, 가려진 영역 경고, RequestGate 버전 처리, 선택 영역 무효화 시점)은 잘 반영되어 있습니다. 좌표계(행 0 = 남쪽)는 로드·리사이즈·프리뷰·클릭 매핑·Apply까지 일관됩니다. 아래 세 가지는 실제 사용 시나리오에서 재현되는 결함입니다.

### 1. JPEG EXIF Orientation 무시 → 사진이 회전된 채 표시·전송됨

`ImageTextureCodec.Load`는 `ImageConversion.LoadImage`만 사용하므로 EXIF 회전 태그가 반영되지 않습니다. 스마트폰 사진은 대부분 센서 원본 방향으로 저장하고 Orientation=6/8 태그로 회전을 지시합니다. 그 결과 원본 패널에 90도 돌아간 사진이 뜨고, 같은 픽셀이 vision 모델에도 전송됩니다. 분류도는 회전된 원본과는 일치하지만, UI의 "위쪽 = 북쪽" 안내와 사용자의 의도(사진 뷰어에서 보던 방향)와는 어긋납니다. 이 기능의 핵심 승인 항목인 "비스듬한 사진 해석" 경로가 정확히 이 조건에 해당합니다.

- 재현: 세로로 찍은 iPhone/Android JPEG(Orientation=6)을 로드 → 원본 패널이 옆으로 누움 → AI 해석 결과도 누운 방향.
- 대응: `ImageHeader`가 이미 JPEG 세그먼트를 순회하므로 APP1(0xE1) Exif의 Orientation을 함께 읽어 `Load`에서 픽셀을 회전하거나, 최소한 status에 "EXIF 회전 미적용" 경고를 표시.

### 2. Vision 전송이 PNG 전용이라 사진은 384px 이하로 급격히 축소됨

`ForVision`은 768→384→192 순서로 PNG 인코딩만 시도하며 1 MiB 초과 시 절반으로 줄입니다. 노이즈가 있는 실제 사진은 768px RGBA PNG가 흔히 1.2~2.5 MB가 되므로, 사진은 거의 항상 384px(경우에 따라 192px)로 내려갑니다. 손그림·지도처럼 색 영역이 단순한 이미지는 768px를 유지하고, 정작 가장 어려운 입력인 사진이 가장 낮은 해상도로 전달되어 좁은 통로·작은 섬 인식이 떨어집니다. "아직 정확도 주장 없음"이라 하셨지만, 예정된 실험 결과가 이 병목에 의해 왜곡될 수 있습니다.

- 재현: 2000px급 야외 사진 로드 → AI로 해석 → 디버그 로그에서 전송 바이트/해상도 확인 시 384px.
- 대응: PNG가 1 MiB를 넘으면 같은 해상도에서 `EncodeToJPG(85)`를 먼저 시도하고, MIME 타입을 `image/jpeg`로 전달. 축소는 그 후에만.

### 3. `Validate()`가 PostLoadInit에서 throw → 무효 데이터가 살아남아 맵 생성 중 예외

`ImageMapData.ExposeData`는 PostLoadInit에서 `Validate()`를 던집니다. RimWorld는 PostLoadInit 예외를 로그로 삼키고 객체를 그대로 두므로, 잘못된 `cells`가 월드 컴포넌트에 남습니다. 이후 해당 타일 생성 시 `Apply` 첫 줄의 `Validate()`가 GenStep 안에서 다시 throw하여 맵 생성이 실패합니다. 라벨 문자를 추가·삭제하는 버전 변경, 저장 파일 부분 손상, 수동 편집이 모두 트리거가 됩니다.

- 재현: 이미지 지형을 적용한 세이브 저장 → `Names`에서 라벨 하나 제거(또는 세이브 XML의 cells 한 글자를 'Q'로 수정) → 로드 후 해당 타일 정착 → 맵 생성 오류.
- 대응: PostLoadInit에서는 throw 대신 `cells=null`(또는 레이어 비활성) 처리 후 경고 로그. 알 수 없는 라벨은 'N'으로 강등하는 관용 로드도 고려. `Apply`는 무효 시 조용히 skip하고 로그.

### 그 외 참고 (결함 아님, 후속 작업 시 확인)

- 75~100 크기 소형 맵에 128폭 분류도를 적용하면 nearest-neighbor 서브샘플링으로 1셀 폭 통로가 사라질 수 있습니다. "좁은 통로 보존" 주석과 상충하므로 게임 probe에 소형 맵 케이스를 포함하세요.
- `apply(...)`가 false를 반환하면(부모 닫힘·RestoreSnapshot 실패) 대화상자는 열린 채 아무 메시지가 없습니다. status에 실패 사유 표시 권장.
- `Correct()`는 `Client()` 실패 시에도 `input`을 먼저 비워 사용자의 질문이 유실됩니다. 순서를 바꾸면 해결됩니다.
- 16MP 허용 상 `GetPixels32()`가 `ForVision` 루프에서 최대 3회(각 64 MB) 호출됩니다. 한 번만 읽어 재사용하면 GC 스파이크를 줄일 수 있습니다.