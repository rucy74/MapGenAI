# MapGenAI 맵 저장소 프로토타입 — 2026-10-01

[실제 비교 HTML](review.html) · [도구 실행법](../../../tools/map-library-prototype/README.md) · [카탈로그](catalog.json) · [최종 검증 영수증](verification.json)

## 결과

텍스트 → 로컬 임베딩 검색 → 조건에 맞는 기존 레시피 → 격리된 실제 RimWorld 맵 생성까지 연결했다. GL 원본 6종, 자체 대조 레시피 2종, 이미지에서 물만 읽은 레시피 1종을 준비했다. 실제 윤곽 검사 후 8종을 남기고 작은 오아시스는 격리했다. 현재는 개발자 도구이며 제품 채팅 UI에 연결하지 않았다.

Geological Landforms는 단순한 MapGenAI 숫자 세팅이 아니라 NodeCanvas XML 생성 그래프다. 공개 코드와 설치된 44개 그래프를 확인한 뒤, GL의 원래 worker로 원본을 생성했다. 원본 재현용 seed/타일/GL ID와 이식용 polygon 명령을 분리했다. 원본 그래프를 통째로 다른 엔진으로 번역했다고 부르지 않는다.

| 항목 | 실제 결과 | 해석의 범위 |
|---|---|---|
| 원본 GL | 6종 실제 생성, 36 실행 검사 성공 | GL 원본 override를 사용한 일회용 프로필. 일반 spawn 조건 검증은 아님 |
| 재생 | 9종 × 3조건 = 27 맵 + 동일 타일 baseline 27개 | 다른 시드 250/300 온대림, 건조관목림250, 오아시스는 사막 |
| 재생 상태/실행 검사 | 162/162 성공 | private 후보 무변경·codec·ApplyPatches·snapshot 복원·baseline/전체 맵 캡처 |
| 원본 윤곽 비교 | 18/21 통과 | GL6+이미지1의 3조건. 오아시스3조건은 물 면적 오차로 실패 |
| 자체 대조 레시피 | 6회 실행 성공 | 원본 윤곽 비교 없음. 별도 원본이 없는 자체 산기슭/빈터 |
| 실제 요청 시연 | 검색된 3후보 + baseline3개, 18 실행 검사 성공 | “산 사이로 길게 열린 넓은 골짜기” → 골짜기/산기슭/외딴 산 |
| Python 단위 검사 | 22/22 성공 | 조건 제외·편집 보존·섬 구멍·이미지 거부·캐시 손상·격리 후보 |
| 검색 작은 평가 | 13/15 충족, 답이 있는 요청의 1위 8/11 | 두 coverage gap: 격리된 오아시스, 미검증 기존 강 타일. 절벽은 2위로 내려간 순위 한계도 존재 |
| 모드의 유료 API | 0회 | CPU 로컬 embedding, Claude/Fable/새 LLM 호출 없음. 이 Codex 작업 자체의 사용량과 구분 |

공개판 DLL `9A792FAD…` 및 설치/소스 DEV DLL `6343F311…`은 작업 전후 같다. 일반판·dist·Workshop·제품 소스·사용자 프로필/모델 설정을 변경하지 않았다. 이번 프로토타입만의 어셈블리를 소유된 격리 게임에 로드했고 종료 후 해당 프로브 모드만 archive했다. 기존 기본 기능 전수검사를 새로 수행했다는 주장도 하지 않는다.

## 수집과 변환

- 원본: Lake / Valley / LoneMountain / Cliff / Archipelago / Oasis. GL 설치 v1.7.13.1, 공개 소스 snapshot `e8035e2b2fdb46ceb92aa159e17e72fdc5dcc421`. 설치 XML별 hash와 생성 조건은 [gl-inventory.json](gl-inventory.json), [source-final/result.json](source-final/result.json), [catalog.json](catalog.json)에 있다.
- 실제 물 수심/영구 Water/자연 바위 edifices를 관측한다. 산과 물 외의 일반 토양을 원본에서 통째로 복사하지 않는다. 오아시스만 작은 Soil/SoilRich 영역을 포함한다. seed·biome·원본 mutator·전역 밀도는 재생 명령에서 제외한다.
- contour를 기존 polygon/sub/add로 변환한다. 64 vertices·32 compose/primitives·24 shapes 예산을 지키고, 최소40칸 또는0.15% 미만 조각은 생략하여 점 무더기를 줄인다. 손실은 [conversion-receipt.json](conversion-receipt.json)에 기록한다.
- 깊은 물은 source deep core만 옮긴다. 얕은 footprint를 먼저 칠하고 deep core를 뒤에 적용한다. `details:natural`은 기존 바닥/물가 처리 경로를 사용한다. 실제 얼음 표면은 PNG에 그대로 남기며, 지형 비교만 `TerrainAtIgnoreTemp`로 얼음 아래 영구 Water를 읽는다.
- 같은 타일/seed에서 빈 설정 baseline을 생성해, 원래의 물웅덩이를 새 레시피의 면적 오차로 오인하지 않는다. 원래 바이옴과 바위를 유지하므로 원본과의 전체 산 precision은 낮아질 수 있다. 이를 개선하려고 맵 전체를 깎는 동작을 추가하지 않았다.

## 이미지 → 맵

GL 골짜기/군도 native Map Preview 색으로 팔레트를 보정하고, 보정에 쓰지 않은 GL 호수 PNG를 입력했다. 물의 얕은/깊은 색 분류는 이 한 held-out 사례에서 각각 precision/recall 100%였다. 전체 색 일치율 93.89%는 넓은 바닥의 영향을 받으며 일반 비전 정확도로 해석하면 안 된다. 바위 precision은 22.83%여서 이미지 경로에서 산 이식을 끈다. **현재는 알려진 팔레트의 물 윤곽/수심만 가져오는 fallback**이다. 실제 출력도 GL 없이 다른 seed/바이옴에서 생성했다. [상세 평가](image-evaluation.json).

사진·임의 이미지·UI/글자가 덮인 스크린샷의 해석과 범용 vision adapter는 미구현이다. 제품의 이미지 입력 UI는 계속 OFF다. 이번 사용자 요청은 별도 수집/변환 프로토타입을 승인한 것으로 처리했다.

## 검색과 보존

`intfloat/multilingual-e5-small`, revision `614241f622f53c4eeff9890bdc4f31cfecc418b3`, 384차원, 공식 카드의 query/passage prefix·attention mean pooling·L2를 사용한다. 가중치는 F:의 별도 캐시에 있고 Unity나 배포판에 넣지 않았다. 이 PC의 cold CLI는 약9초이며, 로딩된 프로세스에서의 query-cache는 따로 [retrieval-evaluation.json](retrieval-evaluation.json)에 기록했다. 게임 성능 수치가 아니다.

조건 필터 → 의미 근접 검색 → 가족별 중복 제거/다양성 조정 순서다. 신경망 reranker는 없다. 같은 가족의 직접 윤곽/이미지 윤곽을 둘 다 후보로 채우지 않는다. `verified_profiles`는 실제 통과한 biome/size/hilliness 조합만 기록하며 Cartesian product로 미검증 조건을 확대하지 않는다. 각 다음 타일의 실제 생성 검사는 여전히 필요하다.

부분 수정, 기존 authored map의 묵시적 교체, 정확한 위치/비율/폭, 미검증 바이옴·산악도·크기, 기존 월드 강/해안/도로가 있는 타일은 이번 저장소가 처리하지 않는다. 후보 없음으로 반환해 기존 생성기를 남긴다. 지원하지 않는 요구를 엉뚱한 후보로 채우지 않는다. 기존 자연 물웅덩이는 다른 seed의 기본 생성 결과로 유지된다.

## 중간 실패와 한계

1. 원본 GL 등록: namespace 탐색 오류 후에도 그래프 등록 실패. 실행 설정에 `knownExpansions`가 없어 Odyssey가 자동 활성화된 사실을 확인했다. 알려진 DLC 목록을 명시하고 원래 GL editor의 `LandformData.CommitDirectly` + cache invalidation을 사용하여 생성했다. 두 수정의 인과를 단일 변수 실험으로 분리하지 않았다.
2. 임시 converter: `sub.a`와 `sub.from`을 뒤집어 구멍이 있는 절벽/군도 윤곽이 사라졌다. 실제 기존 SdfComposite의 `from - a` 계약에 맞게 변환기만 수정하고 점 점유/실제 재생으로 재검증했다. 실행 성공만으로 올바른 구도라고 판단하지 않았다.
3. shape-only에는 구조물/통로 authoring report가 없는 경로가 있다. 보고서 부재를 placement PASS로 세지 않는다. 실제 report가 있으면 issues를 검사하며 별도로 silhouette를 관측한다.
4. 동기식 baseline 생성 직후 MapDrawer sections를 초기화하지 않고 deinit하여 NRE가 났다. vanilla `RegenerateEverythingNow` 후 `DeinitAndRemoveMap`을 수행하도록 프로브를 수정했다. baseline과 candidate에 별도 MapParent가 필요했다. 제품 코드 수정이나 Dispose 우회 패치는 없다.
5. 작은 오아시스는 3조건 모두 물 범위 IoU <0.80이었다. 현재 레시피는 원본보다 조금 넓은 물을 만든다. 실패를 숨기거나 검사 기준을 낮추지 않고 자동 추천에서 격리했다. 원본·실패 PNG·레시피는 연구 자료로 보존한다.
6. GL의 절차적 다양성, 동굴/지붕/자원/이벤트/spawn 규칙은 이식하지 않는다. 현재 1개씩 샘플링한 주 윤곽은 반복된다. 좋은 원본 변형과 미관 선정이 다음 단계다. 이번 생성/codec 검사로 인간의 미관 승인·모든 모드 조합·실제 UI Undo 클릭을 보장하지 않는다.

## 재현과 다음 순서

도구 명령은 [README](../../../tools/map-library-prototype/README.md)에 있다. 원시 `*-terrain.json`/state/Player.log/일회용 profile은 로컬 연구 자료로 보존하고 Git에서 제외한다. 레시피·소형 embedding index·검증 JSON·실제 PNG·단일 HTML은 저장한다. 게임 DLL·외부 GL 원본 코드·모델 가중치는 복제하여 배포하지 않는다.

다음은 **이미지에서 마음에 드는 사례 선정 → 각 가족의 원본 변형 추가 → 연결된 정착 공간 검사 → DEV opt-in 후보 UI에 연결**이다. 새 LLM1 + 저장소최대2는 이전 설계의 제안이며 이번 CLI 시연은 저장소 후보3만 생성한다. 공개판 배포나 DEV DLL 설치는 이번 작업에 포함하지 않는다.

HTML은 모든 이미지를 inline으로 포함하고 정적 구조·이미지 decode를 검사했다. 이전 file:// 자동열기 보안 제한은 우회하지 않으며 브라우저의 실제 렌더링 검증은 미실행이다. 실제 지도 그림은 이미지 도구로 직접 확인했다.

## 출처

- GL: [Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=2773943594), [공개 source](https://github.com/m00nl1ght-dev/GeologicalLandforms), [CC BY-NC-SA 4.0 원문](GL-LICENSE.txt). [귀속 범위](ATTRIBUTION.md).
- E5: [공식 model card](https://huggingface.co/intfloat/multilingual-e5-small), MIT. 로컬 pinned model·weights와 실제 encode 경로를 사용했다.
- vanilla RimWorld 1.6의 설치 코드/Defs와 현재 MapGenAI DEV의 공개 계약을 읽었다. 게임 원본 코드/자산의 권리를 재지정하는 선언은 아니다.
