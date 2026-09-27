# MapGen AI 창작마당 영상 시안

> 목적: 창작마당 갤러리 첫 칸의 옛 영상을 바꿀 새 YouTube 영상의 구성안.
> 길이 약 80초, 1920×1080, 30fps. 내레이션 없이 영어 자막. 음악은 선택.

## 원칙

- **실제 화면만 쓴다.** 대화창·AI 답변·Map Preview는 게임에서 실제로 돌린 결과다. 가짜 UI 합성이나 AI 생성 그림을 쓰지 않는다.
- **AI 대기 시간은 잘라 낸다.** 요청을 보낸 뒤 1초쯤 기다리는 모습만 남기고 바로 결과로 넘어간다. 편집으로 줄였다는 사실은 숨기지 않아도 되는 일반적인 편집이다.
- **글씨는 읽혀야 한다.** 대화창 부분은 1.3~1.6배 확대해 보여 준다. 자막은 화면 아래 3분의 1, 한 줄 7단어 안팎.
- **약속하지 않는 것:** 모든 요청의 완벽한 해석, 모든 모드·바이옴 호환, 항상 아름다운 결과.

## 장면 구성

| # | 시간 | 화면 | 자막 (영어) | 캡처 단계 |
|---|---|---|---|---|
| 0 | 0:00–0:04 | 제목. 완성된 맵 위로 천천히 확대하며 MAPGEN AI 로고 | Describe your map. MapGen AI builds it. | 14-map 또는 03-ring |
| 1 | 0:04–0:10 | 월드맵에서 타일 선택, ✦ AI Map Gen 을 눌러 대화창이 열림 | Pick a tile and open MapGen AI. | 01-open |
| 2 | 0:10–0:22 | 도넛 산 요청을 입력하고 보냄. AI 답변, Map Preview가 다시 그려짐 | Describe the map you want. | 03-ring |
| 3 | 0:22–0:31 | 호수 한가운데 섬 추가, 이어서 섬을 북쪽으로 이동 | Keep editing. Earlier changes stay. | 04-island, 05-island-move |
| 4 | 0:31–0:40 | 강을 서쪽으로 옮기고 곧게 펴기 | Move a river. Straighten it. | 06-river-west, 07-river-straight |
| 5 | 0:40–0:46 | 온천 추가 | Add tile features like hot springs. | 08-hot-springs |
| 6 | 0:46–0:53 | 동쪽 끝에서 출구까지 흙길. 물을 건너면 다리가 놓임 | Roads bridge the water for you. | 09-road |
| 7 | 0:53–0:57 | 되돌리기 한 번 | Undo any step. | 10-undo |
| 8 | 0:57–1:10 | 초기화 후 Quick suggestions. 후보 3장이 차례로 그려짐. 2번 다듬기 | Or ask for ideas. Refine one before you pick. | 11-recommend, 12-refine |
| 9 | 1:10–1:16 | 이 설정으로 맵 생성 후 실제 맵을 멀리서 둘러봄 | Generate with these settings, then settle. | 14-map |
| 10 | 1:16–1:21 | 끝 화면 | RimWorld 1.6 · Requires Harmony and Map Preview · Use your own AI key | 없음 |

해안 방향 바꾸기(13-coast)는 다른 타일이라 흐름이 끊긴다. 영상에서는 빼고 갤러리 사진으로만 쓴다.

## 만드는 방법

1. **촬영**: 사진용 캡처 도구에 연속 프레임 저장을 더한다. 각 단계에서 입력창에 글자가 한 글자씩 들어가는 모습, 보내기, 답변, Map Preview 갱신을 초당 30장으로 저장한다. 요청과 답변은 실제 모델 호출 그대로다.
2. **편집**: ffmpeg로 이어 붙이고 대기 구간을 자른 뒤 자막과 짧은 전환을 넣는다. 이 PC에 편집 도구가 이미 있다.
3. **게시**: YouTube 업로드와 창작마당 영상 교체는 유저님 계정으로 직접 한다. 창작마당 편집 화면의 이미지·영상 추가에서 YouTube 링크를 넣고, 옛 영상은 내린다.

## 이번 시안에서 확인할 것

- 장면 순서와 길이, 자막 문장.
- 사진용 캡처가 나오면 그 프레임으로 정지 화면 시안 영상(자막 포함)을 먼저 만든다. 실제 연속 녹화는 그 시안을 확인한 뒤 진행한다.

## 시안 영상 (2026-09-27 실제 캡처로 제작)

`animatic-draft.mp4` — 61초, 1920×1080, 30fps. 사진용 캡처(`../captures/showcase-01`)의 정지 화면과 실제 Map Preview 이미지로 만든 **순서·자막 확인용 시안**이다. 움직이는 녹화가 아니다.

| # | 길이 | 화면 | 출처 |
|---|---|---|---|
| 0 | 3.5초 | 제목 | 없음 |
| 1 | 5초 | Pick a tile. Open AI Map Gen. | 01-open |
| 2 | 5.5초 | Describe the map you want | 03-ring 대화 부분(한·영 병기 안내 아래는 잘라 냄) |
| 3 | 5초 | Map Preview shows the result | 01·03 미리보기 전후 |
| 4 | 4.5초 | Keep editing. Earlier changes stay. | 04-island 미리보기 + 요청 문장 |
| 5 | 4.5초 | Move what you made | 05-island-move 미리보기 + 요청 문장 |
| 6 | 5초 | Turn a coast. World connections stay. | 13-coast 전후 (다른 타일) |
| 7 | 6초 | Or ask for ideas. Up to three, drawn by Map Preview. | 11-recommend |
| 8 | 5초 | Refine one before you pick | 11·12의 2번 후보 전후 |
| 9 | 5초 | Not sure? A few questions, no AI calls | 02-guide |
| 10 | 7초 | Generate with these settings. Then play. | 14-map-1 실제 맵 |
| 11 | 5초 | 끝 화면 | 없음 |

- **뺀 장면**: 강 옮기기, 온천, 도로. 이번 실행에서 강 옮기기 요청은 흐름 방향과 맞지 않는 요청이었고, 온천 추가는 강 위치를 함께 바꿨고, 도로는 경로를 찾지 못했다. 제품 수정 여부가 정해진 뒤 다시 찍는다.
- **자막 정책**: 합성한 장면에는 제목 글씨를 화면 위에 넣고, UI 화면은 잘라 내지 않고 통째로 보여 준다. 글자 없는 기호는 도형으로 그린다(글꼴에 ▶·✦ 글리프가 없어 네모로 나왔던 실패를 고침).
- **만드는 명령**: `py make_frames.py` 뒤 `py build_animatic.py shots.json animatic-draft.mp4`.
- **다음**: 제품 수정 뒤 같은 캡처 도구로 다시 찍고, 연속 프레임 저장을 더해 실제 움직이는 영상으로 바꾼다.

---
## 작성 이력
- 2026-09-27 09:20 — 초안. 장면 10개, 사진용 캡처 단계와 연결.
- 2026-09-27 10:25 — 실제 캡처로 만든 61초 시안 영상 절 추가, 뺀 장면과 이유 기록.
