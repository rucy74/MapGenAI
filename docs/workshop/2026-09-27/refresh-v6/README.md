# 창작마당 본문 v6 — 실제 캡처로 기능을 나눠 보여주기

2026-09-27. 사용자가 채택한 표지 v5에 맞춘 본문·설명·갤러리 정리. 공개 게시 전 로컬 산출물이다.

## 현재 사용할 파일

- 전체 검토: [../review.html](../review.html). 이미지 주소 없이 로컬 파일로 표시한다.
- 새 카드 4장: [../upload-v6/1-imgur](../upload-v6/1-imgur).
- 채택 표지: [../upload-v6/2-cover/Preview.png](../upload-v6/2-cover/Preview.png).
- 갤러리에 추가할 실제 화면 4장: [../upload-v6/3-steam-gallery](../upload-v6/3-steam-gallery).
- 게시용 설명: [영어](../description-en.txt), [한국어](../description-ko.txt).
- 출처 파일과 SHA256: [../upload-v6/manifest.json](../upload-v6/manifest.json).

`upload/`와 `cards/out/`은 이전 자료다. 이번 작업에 사용할 새 묶음은 **upload-v6/**다. 사용자가 올린 새 카드 네 장의 Imgur 직접 주소를 한·영 설명에 반영했다. HTTP 200/image/png 및 디코딩한 RGBA 픽셀 일치4/4 확인; PNG 파일의 바이트 해시는 다르다. [호스팅 확인 기록](../image-hosting.json). 검토 HTML은 동일한 로컬 이미지로 표시한다. 공개 Steam 페이지는 아직 변경하지 않았다.

도로·다리 갤러리는 **gallery-04-road-bridge.jpg**를 업로드한다. 원본 1920×1080 PNG 3,714,444 bytes를 동일 해상도의 JPEG(quality 94, 4:4:4) 1,070,342 bytes로 변환했다. 리사이즈·크롭·리터칭 없음. 원본 PNG는 `captures/showcase-04-road-replay/14-map-2.png`에 유지한다. [변환 기록](gallery-compression.json). 갤러리 생성 시 각 파일이 2,000,000 bytes 미만인지 검사한다.

## 구성과 출처

| 카드 | 목적 | 실제 이미지 출처 |
|---|---|---|
| describe | 한 문장으로 지형을 만드는 기능 | `docs/assets/new_example1.png` 기존 협곡/호수 캡처. x637,y20,239×239 Map Preview 부분 |
| editing | 섬 추가 → 북쪽 이동 | `cards/assets/c2-step1..3.png`; `showcase-03-replay`의 실제 게임 미리보기 |
| ideas | 후보 선택·수정·재추천·선택 안 함 | `cards/assets/c3-candidates.png`; `showcase-02/12-recommend.png` 실제 추천 화면 |
| roads | 실제 생성되는 도로/나무 다리 | `showcase-04-road-replay/14-map-2.png`; x500,y70,1000×653 크롭 |

- 지도 내용은 새로 그리거나 AI로 보완하지 않았다. HTML/CSS로 원본을 자르고 레이아웃·제목을 붙였다.
- 협곡은 이전 공개용 캡처이며 현재 빌드의 새 모델 성공 증거가 아니다.
- editing/roads는 같은 날 받은 모델 응답을 그대로 재생해 생성·촬영한 결과다. 자유로운 새 문장의 성공률을 검증한 것은 아니다.
- 추천/문답/후보 수정 갤러리는 같은 날 실제 UI 캡처다. 이 홍보물 수정 중 새 LLM/Fable 호출은 0회다.
- 현재 추천 캡처의 비슷한 후보 모양은 그대로 보여준다. 홍보용으로 추천 결과를 꾸며 바꾸지 않았다.

## 재생성

키트 루트에서:

```powershell
node build_images.mjs 'C:/Program Files/Google/Chrome/Application/chrome.exe' refresh-v6/cards.html refresh-v6/out describe,editing,ideas,roads 640 1.5
python -X utf8 build_review.py
python -X utf8 check_bbcode.py description-en.txt description-ko.txt
```

## 검증 및 한계

- 카드 4/4 렌더, 원본 이미지 6/6 로딩, 가로 넘침 0, 넘침 검출기 양성 대조 성공. `out/render-checks-cards.json`.
- 카드 네 장을 이미지 도구로 직접 확인했다. 소개글 서식 오류 EN/KO 각각 0.
- 업로드 묶음 9개 파일은 생성 원본과 SHA256이 일치한다.
- 검토 HTML은 외부 이미지 주소에 의존하지 않는다. 로컬 링크와 자원 존재 여부를 별도 검사했다.
- Codex 브라우저가 `file:///.../review.html` 자동 열기를 URL 보안 정책으로 차단했다. 다른 브라우저/HTTP 서버 등으로 우회하지 않았다. 최종 검토 페이지의 앱 브라우저·모바일 실표시는 검증하지 못했다.
- 새 이미지 호스팅, 공개 Steam 설명·갤러리·표지 반영은 미실행이다. 기존 영상은 유지 대상이다.
