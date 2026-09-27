# 표지 v5 — 서로 다른 말이 서로 다른 맵으로

사용자 피드백: “이러면 기존이랑 똑같지 않니”. v4는 기존 전후 비교와 예시를 그대로 재사용하여 새 표지로서 차별성이 부족했다.

이번에는 **서로 다른 요청 3개와 각 요청의 실제 결과**를 나란히 배치한다. 협곡 속 호수, 피오르드, 별 모양 언덕과 초승달 호수. 모드 이름/설명/큰 맵 3개/요청 말풍선으로 구성하고, 기존의 곡선 강 → 직선 강 2칸 구도와 예시는 사용하지 않는다. 생성 일러스트도 사용하지 않는다.

- [표지](out/cover.png): 1440×810.
- [편집 원본](cover.html): 960×540 CSS, 기존 build_images.mjs로 렌더.
- [썸네일](thumb/cover.png): 320×180.

## 실제 이미지 출처

이미 보관된 게임 채팅+Map Preview 캡처 3장의 지도 영역만 CSS로 자르고 확대했다. 지형/색상 리터칭 없음. 새 모델 호출/재촬영/제품 재검증을 한 것으로 주장하지 않는다. 문구는 표지용으로 축약한 것이며 게임 UI 자체를 위조한 것이 아니다.

| 예시 | 원본 캡처 | 원본 실제 요청 | 크롭 x,y,정사각 한 변 |
|---|---|---|---|
| 협곡 속 호수 | [new_example1.png](../../../../assets/new_example1.png) | Diagonal canyon with a large central lake | 637,20,239 |
| 피오르드 | [new_example4.png](../../../../assets/new_example4.png) | Fjord | 635,6,238 |
| 별 언덕·초승달 호수 | [new_example6.png](../../../../assets/new_example6.png) | Star-shaped hill on top, crescent lake on the bottom | 634,6,237 |

원본 SHA256(위 순서):

```text
145DBB98241F901A27B34FFA86098747CDE25E3F6ED988FB7AFA34CED0FC621E
93F1B1A0597648E30CA27860BA052044C04DDF4F6C89283C4908B349B2BC0355
91885CB165E405A5DE1C3C5D8634762577E8CF31E8E9ABFD1B59F2853C1DAF61
```

## 확인 및 상태

세 원본을 직접 관찰해 채팅 문장과 지도 대응을 확인했다. 기존 build_images.mjs로 일반/썸네일 두 크기를 렌더했고 이미지3/3, overflow0, 검출기selfTest=true. 출력 이미지도 직접 관찰했다. 썸네일에서 제목과 세 지형의 차이는 보이며 상세 요청/상단 부제는 작다.

2026-09-27 15:28 사용자 “그래 이게 낫겠다”로 표지 채택. 로컬 upload/2-cover/Preview.png 및 검토 페이지의 선택 표지는 v5로 갱신했다. 공개/설치/dist 표지는 아직 교체하지 않았고, 제품 코드와 이전 시안도 보존했다. 새 캡처가 필요해지면 기존 기록을 현재 빌드의 성공 사례로 자동 간주하지 않는다.

2026-09-27 16:56 후속: 사용자의 모드 목록에 이전 표지가 보인다는 보고로 설치 누락을 확인했다. 채택 원본을 dev/dist 및 로컬 일반판·DEV의 `About/Preview.png`에 복사했다. 네 파일의 SHA256 `3ED88EF35150D59B0EAF1DE7F8612381538CEA0DB7CEC3F785F2FFB6F0D9AF46` 일치, 기존 표지 백업 및 DLL/메타데이터 보존을 확인했다. [설치 기록](../../cover-installation.json). 공개 Workshop 반영은 여전히 미실행이다.
