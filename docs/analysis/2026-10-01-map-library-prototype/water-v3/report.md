# 원본의 물 위치까지 함께 옮기는 저장소 프로토타입

2026-10-01 · 기준 dev5cada7f · 제품 채팅/설치 DLL/배포판은 변경하지 않았다.

## 달랐던 이유와 수정

이전 변환은 호수/군도/오아시스만 물을 옮기고 계곡/절벽/외딴 산의 연못은 제외했다. 현재 타일에서 생성된 연못도 남겼고, 작은 조각을 polygon 기준으로 생략했다. 산/물/바닥을 같은 구도로 보려는 목적과 맞지 않았다.

지금은 원본의 영구 TerrainDef 관측에서 모든 작은 일반 담수 연못과 수심을 `water_layer` RLE로 저장한다. 산의 polygon 명령과 바닥 `ground_layer`를 함께 적용하고 단계403에서 원본의 물 위치를 확정한 뒤 단계405에서 바닥을 적용한다. 원본에서 확인된 마른 칸의 일반 연못만 정리한다. 원본의 AncientConcrete 등은 마른 칸이라는 근거로 쓸 수 있지만, 그 바닥/건물을 복사하지 않는다.

대상의 강·바다·온천 같은 특별한 물과 거기에 연결된 물, 도로/다리·건설 가능한 바닥·건물은 보호한다. 원본 구도가 보호 대상과 충돌하면 원래 대상을 남기고 후보를 제외한다. 모르는 이미지 픽셀은 그대로 남는다. 보통 Marsh는 온천/강으로 오인하지 않는다. 일반 연못 정리에는 현재 바이옴의 기본 흙/사막 모래를 사용하며 지원한 원본 바닥은 이어지는 바닥 단계에서 적용한다.

## 실제 새 결과

- [전후 HTML](review.html): 원본 → 이전 바닥 이식 결과 → 물 위치 포함 결과, 원본6종·크기/바이옴·이미지·보호 대조군 총23개 실제 PNG. [직접 표시한 그림](comparison.png).
- GL 원본6종×3조건 **18/18에서 물 위치와 수심 IoU100%**, 추가 물/빠진 물0. 조건은 온대림250/300, 건조관목림250이며 오아시스는 사막250/300 및 다른 사막seed250으로 검사했다. 지형 전체 복사율·미관 점수는 아니다.
- 이미지 기반 호숫가3조건도 water IoU **98.835~100%**. 300칸에서는 이미지의 불명 영역에 native 물138칸이 남았다. 이미지에 없던 물을 모두 없애려면 불명 칸을 추측해야 하므로 보존하고 수치/한계로 기록했다. 원본 데이터가 있는6종과 이미지-only를 구분한다.
- 기존 작은 오아시스의 물 면적 실패도 이번 원본 물 mask 적용에서 해소됐다. 현재 레시피9개 모두 실제 통과 profile만 검색 가능하다. 신규 원본/임의 타일의 성공이나 미관을 보증하는 뜻은 아니다.
- Python **45/45**, 프로브 빌드0경고/0오류, 최종 실제 생성 검사 **308/308**, 새 생성 맵 **58개**(baseline 포함). GL 원본6개는 직전 실제 캡처의20파일을 해시로 대조해 재사용했으며 새 원본 생성이라고 세지 않았다.
- 바닥 적용21/21 보호·미지정 칸·고도 변경0, 같은 바이옴 바닥15조건 검사 통과. 원본 작은 물 조각 중 계곡19칸/절벽123칸/외딴 산54칸도 물 데이터에 포함된다. polygon의 작은 조각 생략과 구분한다.
- 실제 보호 양성 대조군: 물252칸 추가·일반 연못560칸 정리·수심1칸 변경이 일어나면서 강1/바다1/온천1/연결된 담수3/길1/바닥2/벽1과 불명 연못을 보호했다. 보호충돌6칸은 원형을 유지했다. 바닥/물/높이 보호 위반0.
- 입력 전체가 불명인 대조군은 TerrainDef 전체와 PNG 전체가 동일 타일 baseline과 같다. 바닥/물 sidecar 없는 기존 core2종×3조건도 이전 지도와 칸별 지형·바닥 계수·PNG 전체가6/6 동일하다.
- 원본 직접 비교가 승인 기준이다. 현재 타일의 연못을 원본 mask에 합친 값은 진단만 하며 후보 승인을 부풀리는 근거로 쓰지 않는다. geometry27/27, 물21/21, 보호 대조군2/2. 보호충돌/부재인 water 근거는 카탈로그에서 제외하는 행동 검사 포함.
- 로컬 E5 인덱스9×384를 캐시만 사용해 재생성하고 catalog SHA/벡터 정규화/ID 순서를 대조했다. 새 검색 품질 benchmark/LLM 호출은 하지 않았다. 유료API/Claude/Fable0.
- [검증](verification.json), [물 위치 검사](water-evaluation.json), [지형 검사](transfer-evaluation.json), [바닥 검사](ground-evaluation.json), [원본 재사용](source-reuse-receipt.json). 제품 source/설치DEV/일반판/dist와 사용자 설정 불변, 소유 프로브6개 archive 완료. 다른 세션의 게임은 종료하지 않았다.

## 재현

제품 repo 루트에서 실행한다. 각 game 출력은 새 폴더여야 하고 전용 프로세스가 끝나면 같은 run.ps1 인자에 `-Archive`를 붙인다. 실행 DLL은 설치DEV6343F311… 기준이다. 원본 terrain 원자료와 Player.log/launch/cleanup은 로컬에 보존하며 기존 .gitignore 규약으로 Git에서 제외한다. 새 PC에는 실제 원본을 다시 캡처해야 한다.

```powershell
python -X utf8 tools/map-library-prototype/water_report.py --folder <새 자료 폴더> --prepare
dotnet build tools/map-library-prototype/PrototypeProbe.csproj --nologo
tools/map-library-prototype/run.ps1 -Manifest <자료>/transfer-a-manifest.json -Output <자료>/transfer-a-native-r2
# B/C manifest와 water-controls-manifest.json도 같은 방식으로 실행하고 Archive한다.
python -X utf8 tools/map-library-prototype/water_report.py --folder <자료> --runs transfer-a-native-r2 transfer-b-native-r2 transfer-c-native-r2 water-controls-native-r1
python -X utf8 tools/map-library-prototype/finalize_catalog.py --folder <자료>
python -X utf8 tools/map-library-prototype/embedding.py --catalog <자료>/catalog.json --index <자료>/index
python -X utf8 tools/map-library-prototype/verify_water.py --folder <자료> --runs transfer-a-native-r2 transfer-b-native-r2 transfer-c-native-r2 water-controls-native-r1
```

## 반증과 남은 범위

- 초기 준비 스크립트의 예약어 키워드/import 오류는 게임 실행 전에 수정했다. transfer-a/b-native-r1은 원본의 관측된 dry floor를 물 처리에서도 불명 취급하여 작은 물 조각이 남은 중간 결과다. A의 절벽 IoU97.619%는 실패로 남기고 원본 재료 이식 가능성과 dry 위치 관측을 분리해 r2에서 다시 생성했다. initial-import에 당시 레시피를 보존했고 최종 증거로 사용하지 않는다.
- 새 원본 mask가 정확해도 옛 native pond 합집합 recall 검사가 남아6사례를 거짓 실패로 표시한 것을 발견했다. source-composition에서는 원본 직접 비교만 승인 기준으로 삼도록 고치고 행동 검사로 확인했다. 실제 지도와 원본 IoU98% 기준은 바꾸지 않았다.
- 이 기능은 개발자 probe만 해석하는 sidecar다. 제품 `TileMapState`/Preview/Undo/저장/추천창은 미연결이다. 일반 `ApplyPatches`만 호출하면 산/물 polygon만 적용되고 정확한 물 mask·바닥 sidecar는 누락된다. 제품 연결 때 이 차이를 회귀 검사해야 한다.
- 산의 작은 암석·동굴·지붕·자원·건물·이벤트와 GL의 절차적 다양성은 이식하지 않는다. 기존 강/해안/월드 도로 타일·부분 편집·정확한 방향/비율과 지원 밖 바이옴은 계속 제외한다. 이번 보호 검사는 실제 local fixture이며 모든 세계지도 수계/모드 조합의 이식 검사로 확대하지 않는다.
- 원래 무창 런타임의 Plant.Print/GetSnowDepth 정리 경고는 이전에도 존재했고 해결 범위가 아니다. 전체 실플레이/모든 자연어 요청의 재검증은 하지 않았다. HTML 내장 PNG23개는 디코드 검사·실제 비교 그림 직접 관찰, 브라우저 렌더는 로컬 자동열기 제한을 우회하지 않아 미검증이다.
- 원본/파생 그림과 레시피의 [귀속](../ATTRIBUTION.md)은 m00nl1ght · CC BY-NC-SA4.0 조건을 유지한다. 게임/GL DLL·새 모델 가중치는 저장소나 모드 배포에 넣지 않았다. 설치·공개판 반영은 이번 범위가 아니다.
