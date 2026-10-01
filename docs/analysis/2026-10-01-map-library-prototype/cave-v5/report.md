# 실제 동굴·자연 지붕·고도 재현 실험

원본 동굴을 실제 칸별 데이터로 가져오도록 개발자 프로토타입을 확장했다. 원본 높이와 Caves 수치는 8종×3조건 모두 차이0이지만, **실제 동굴이 있는 15조건 중 통로·지붕·연결·안전 검사까지 통과한 것은2조건**, 물·바위·바닥까지 합쳐 등록할 수 있는 동굴 조건은1개다. 전체 GL 지형의 완전 복제가 끝났다는 결과가 아니다. 제품 추천창/codec/저장/Undo/설치 DLL에는 아직 연결하지 않았다.

[실제 그림·진단 HTML](cave-review.html) · [검증 영수증](verification.json) · [지질/통로 검사](cave-evaluation.json) · [카탈로그](catalog.json) · [전후 그림](comparison.png)

## 바뀐 동작

- native99999에서 working grid가 폐기되기 전에 실제 elevation/Caves/자연·인공 지붕/보행/건축 바닥/건물을 관측한다. 기존 terrain schema2는 유지하고 geology schema1을 따로 저장한다.
- 명시적인 전체 관측 이식에서 source199 높이/Caves, 물403, 바위404, 자연 바닥405, 자연 지붕1601을 적용한다. 불명 원본 영역·현재 보호 대상은 보존한다. 마지막99999 감사와 원본 raw 비교가 최종 판정이다.
- 전과 같이 바위 점유만 .71/.5로 평탄화하지 않고 관측 float 값을 유지한다. 최근접 칸 대응으로250→300 확대하되 Caves 강도 자체는 확대/반올림하지 않는다.
- 완전 관측 모드의 `params`는 비운다. 물 polygon을 함께 실행하면 제품 Authoring400이 원본 고도를 .3 이하로 낮추는 중복 적용이 있었다. polygon은 진단 손실·SHA만 남긴다. 기존 detailsOnly/이미지/core/일반 제품 명령은 유지한다. 제품의 빈 추천 거절을 바꾼 것이 아니다.
- 실제 자연 지붕/None, 동굴 양성 마스크와 값, 걸을 수 있는 통로,4방향 연결, 원래 입구가 바깥으로 이어지는지를 독립 검사한다. 건물·길·특수 물·인공 지붕을 지워서 원본 일치를 만들지 않는다. 무지지 자연 지붕을 적용하거나 전역 붕괴 처리를 우회하지 않는다.

## 최종 실제 결과

정본 원본은 새 `source-native-r2` GL8지도다. target A는250온대림, B는300온대림, C는250건조관목림이며 오아시스는 세 조건 모두 사막이다. 원본과 다른 world seed·타일에서 생성했다. 현재 타일의 자원 종류·기후·바이옴 적용을 남기고 source seed/save/월드 설정·자원 배치·사건을 이식하지 않는다.

| 실제 원본 종류 | A 250온대림 | B 300온대림 | C 250건조관목림 |
| --- | --- | --- | --- |
| 호숫가(C=0) | 지질 통과/전체 제외 | 지질 통과/전체 제외 | 지질 통과/전체 제외 |
| 골짜기 | 지질 제외 | 지질 제외 | 지질 제외 |
| 외딴 산 | 지질 제외 | **동굴 통과/물 검사로 전체 제외** | 지질 제외 |
| 절벽 | 지질 제외 | 지질 제외 | **동굴·전체 통과** |
| 군도(C=0) | 지질 통과/전체 제외 | 지질 통과/전체 제외 | 지질·전체 통과 |
| 오아시스(C=0, 사막) | 지질·전체 통과 | 지질·전체 통과 | 지질·전체 통과(동일 바이옴/크기 profile은 두 seed 모두 통과해야 등록) |
| 동굴 입구 | 지질 제외 | 지질 제외 | 지질 제외 |
| 외딴 동굴 골짜기 | 지질 제외 | 지질 제외 | 지질 제외 |

- B 외딴 산: 동굴 통로753/753·지붕 아래 통로729/729·자연 지붕12810/12810, 연결2components·입구7/7·입구edge24/24 모두 정확하다. 하지만 source 불명 구역의 native 물88칸을 남겨 전체 물 IoU95.2586%로98% 기준 미달이다. 동굴 통과를 전체 등록 통과로 바꾸지 않았다.
- C 절벽: 통로1341/1341·지붕 아래1330/1330·자연 지붕16312/16312, 연결6components·입구12/12·edge15/15 정확하다. 지질·물·암석·바닥 보호와 해당 조건의 바이옴 조정 기준도 통과했다. 다른 타일 보장이나 원본 온대림 바닥 재료100% 복제라는 뜻은 아니다.
- 나머지 동굴 조건에는 native 후반 Wall/AncientFence/인공 지붕 충돌이나 무지지 자연 지붕이 남는다. 실제 source Caves 배열이 맞아도 통로가1칸 막히면 거부한다. 원본 건물을 복사하거나 현재 건물을 삭제하지 않는다.
- 원본 바위·전체 지원 자연 바닥의 기존 독립 분모/98%·95% 기준도 유지했다. 바위/바닥 엄격 기준13/24, 지질11/24(실제 동굴2/15), 물21/27(이미지3조건 포함), 중간 바닥 적용 보호27/27, 윤곽20/33이다. GL24표본 중5개가 모든 기준을 통과하며 중복 바이옴/크기를 묶으면 통과 profile은4개다. 카탈로그11항목 중6항목에만 측정된 통과 profile이 있다. **이 cave-v5 카탈로그는 실험용이며 기존 rock-v4 카탈로그를 대체하지 않았다.**

## 실제로 검증한 범위

- Python124/124, 프로브 빌드 경고0/오류0. 실제 CLR constructor의 잘못된 수치/타입/배열/K-N 거부12/12도 최종 DLL에서 확인했다.
- 최종 source49 + target173×3 + controls77 = 실제 실행645/645. 정본 source8 + target/baseline/rock-only72 + controls10 = 새90지도. 지붕 지지 알고리즘의 별도 source-r3 진단8지도/49검사는 따로 센다. 실행 검사 통과는 지도 재현/등록 통과가 아니다.
- target GL24개의 E/C 오차0, 직접 protected/unknown 변경0. native 실제 roof-support와 projected 판정 차이0. 전 단계 보호 대조군3/3은 **기대 동작 검사**이며 의도적으로 충돌/unsafe 후보를 탈락시키는 검사다.
- source 지붕 support의4방향 flood/6.9칸/인접 지지대 규칙과 얇고 두꺼운 자연 지붕의 collapse 정의를 실제 게임 코드와 비교했다. 별도 source-r3의 CaveEntrance742·SecludedValley15 무지지 지붕은 **Flat·GL stable override=false인 이 별도 표본의 진단값**이다. 모든 GL 원래 사용 조건의 안전성에 대한 판정이 아니다.
- r2/r3 원본은 E/C/roof/PNG가 같지만 건물·보행·바닥 등의 물리 관측이 다르다. r3 수치를 r2 전체 물리/안전성 정본으로 합치지 않는다. 이전 rock-v4 원본과도 Cliff 바닥193칸 차이/PNG동일이 관측됐다. 과거 source 전체 동일 주장 대신 새 raw source를 정답으로 썼다.
- 기존 sidecar 없는 core6결과는 이전 전체 terrain JSON과 true/default PNG 모두 동일하다. 강화 all-unknown은 별도 물900칸/비옥한 바닥10000칸 요청에도 전체 실제 terrain/geology/edifice와 두 PNG가 baseline과 동일했다. unsafe400 fixture는 적용/승격을 거부했다.
- 소스/설치 DEV SHA `6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6`, dist/일반판 `9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5` 그대로다. 제품 source diff0, 유료 API/Claude/Fable0, 사용자 프로필/다른 게임 변경0. 최종 프로브 DLL은 `55F196AACF7D73673F9B0570E8DB14A96EF6F6C825C9C233C15D950613E8AC32`.
- 로컬 offline E5 index11×384의 ID/L2/카탈로그SHA를 대조했다. 자연어 검색 품질 평가를 새로 반복한 것은 아니다. HTML은 실제 PNG와 지붕/보행 진단을 내장하며 PNG decode/정적 구조를 검사한다. 브라우저 렌더·미관/플레이 승인은 미검증이다.

## 실패와 재현

`source-native-r1`은 개발자 추가 geology 데이터가 기존1MiB reader/writer 제한을 넘어서 실패했다. 프로브만8MiB bounded reader/streaming float writer로 바꾸고 source-r2에서 새로 확인했다. 제품/모델 응답 제한은 그대로다. 잘못된 CLR helper resolver의 recursive load 실패 뒤, 실제 C# resolver에서 최종12검사를 완료했다.

`cave-controls-native-r1`의 unknown은 nonrock_edifice2칸 차이를 관측했고 원인은 확정하지 않았다. 정규화로 숨기지 않았다. 새 강화r2는 전체 물리·PNG가 정확히 같은 별도 성공 표본이다. A-r1/r2는 중복 authored 물 적용으로 고도가 달라진 중간 실패다. 당시 입력35파일은 [보존 영수증](intermediate-polygon-inputs/receipt.json), 생성 당시 제품 상태와 실패 raw/audit은 해당 폴더에 남겼다. 새 A-r3/B-r1/C-r1이 최종 표본이다. 다른 seed를 반복해 통과 결과를 골라내지 않았다.

도구 정본은 [README](../../../../tools/map-library-prototype/README.md), [관측 계약](../../../../tools/map-library-prototype/cave-contract.md)이다. 실제 생성은 `cave-a-manifest.json`, `cave-b-manifest.json`, `cave-c-manifest.json`, `cave-controls-manifest.json`을 `run.ps1`의 새 소유 출력 폴더에서 실행하고 자연 종료 후 Archive한다. `measure_transfer` → source 지정 `ground_report(strict=False)` → water/rock/cave 보고서 → finalize → offline embedding → verify_caves 순서다. 실패 profile은 최종 카탈로그에서 제외한다.

원시 terrain/geology/Player.log/소유 launch/cleanup은 이 PC에 남는다. 새 geology도 기존 terrain과 같은 원시 관측 방침으로 이 실험 폴더에서만 Git 제외한다. 원문을 삭제하거나 정규화하지 않는다. Git 파일만으로 과거 실행 전체를 재검증할 수 없으며 SHA/파일 경로 영수증은 현장 재검증용이다. 레시피·독립 평가·그림·영수증은 dev에서 코드와 분리해 저장한다. 모든 소유 프로브는 Archive했고 다른 게임은 종료하지 않았다.

기준 commit은 `e288a0c04af7826a4f56bb388155e8b8c2958b7c`, 이번 구현은 DEV `2d7606d`이다. 별도 검증 자료 commit으로 cave-v5/MAP만 저장하며 기존 미추적126개를 stage하지 않는다. 일반판·설치 DEV 승격·배포·태그 변경은 수행하지 않았다.

다음 제품 작업은 새로 생성되는 폐허가 동굴 통로를 막지 않도록 배치 제약을 공유하고, 원래 있는 보호 대상과 충돌하면 후보를 거부하는 흐름이다. 큰 동굴은 원본 사용 조건/지붕 지지 정책을 확인한 뒤 안전한 구도 변형을 따로 설계해야 한다. 그 이후에 제품 codec/Preview/Undo/저장/DEV opt-in 연결을 검토한다. 현재 공개판이나 설치 DEV에 이 실험을 자동 적용하지 않는다.

## 출처

Geological Landforms 원작 m00nl1ght, [저장소](https://github.com/m00nl1ght-dev/GeologicalLandforms), revision `e8035e2b2fdb46ceb92aa159e17e72fdc5dcc421`, 설치 v1.7.13.1. GL 파생 레시피·GL 지도 그림·카탈로그는 원본 [CC BY-NC-SA 4.0](https://github.com/m00nl1ght-dev/GeologicalLandforms/blob/master/LICENSE) 출처/조건을 유지한다. RimWorld 원본 자산의 권리를 변경하지 않는다. E5는 [intfloat/multilingual-e5-small](https://huggingface.co/intfloat/multilingual-e5-small), MIT, revision `614241f622f53c4eeff9890bdc4f31cfecc418b3`.
