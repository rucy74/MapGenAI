# 같은 바이옴 초기 정적 지형 재현 — 결과

사용자 결정: “다른 바이옴이면 타일을 지금처럼 그 바이옴에 맞게 하는 게 맞는 거 같아. 같은 바이옴이면 동일하게 나오개 하고”. 개발자 맵 저장소에서 같은 BiomeDef·같은 크기의 새 후보를 원본과 같은 초기 정적 지형으로 재현하고, 다른 바이옴/크기·이전 명령은 기존 경로로 처리한다. 제품 UI·DEV 설치 DLL·공개판은 변경하지 않았다.

## 실제 결과

- 고정 GL8 원본을 새로 생성했다. 호수·계곡·외딴산·절벽·군도·오아시스6은 같은 바이옴250×250에서13grid의62500칸 전체·고유 건물 기록·실제 Map Preview 두 PNG 모두100%일치. world seed·타일이 다른 Odyssey 프로필(GL 미로드)에서도 원본을 재현했다. 정확 재현 허용 오차는0이다.
- CaveEntrance/SecludedValley2는 원본 지붕 지지가742/15칸 실패해 격리했다. 6/8을8/8로 표시하지 않았으며 무지지 지붕·기존 건물 삭제·임의seed 탐색으로 맞추지 않았다.
- 수정 전124에서 최종162 Python검사, 프로브 빌드0경고/0오류. 실제9실행776/776 실행검사·118생성지도(원본/후보/paired baseline/rock-only 포함). 모두 소유 프로브Archive 확인. 실행 검사 성공과 정확 재현 admission은 다른 판정이다.
- 기본 core6은 이전과 전체 terrain 및 실제PNG2 모두같다. 다만 전체geology의 후반 비암석 건물 기록은 달라 strict raw 보존0/6이다. 다른 바이옴/크기 GL16 strict전체raw 보존0/16, terrain+PNG보존10/16, PNG2단독보존15/16이다. 이 실패를 분모에서 빼거나 기준을 완화하지 않았다.
- 수정 전 실제55F… 프로브를 현재 환경에서 동일 입력·순서로 재실행했다. B에서 수정 전 DLL끼리도 후반 건물/일부바닥/보행 기록이 달랐다. C 첫3사례 대조에서도 수정 전 외딴산의PNG85/16픽셀이 달랐고, 새DLL의역사적C대조는108/16픽셀차이다. 기존native변동의 존재를 확인했지만 모든차이의 인과를 단정하지 않는다. 새로운 두Harmony prefix는 replica=null일 때 즉시반환하고 기존14생성pass 본문은 그대로다. **전체회귀 번들 PASS는 주장하지 않는다.** 정확 복제6의독립검증은 통과했으며 제품설치/전체호환완료와구분한다.
- 기존 보호·unknown·위험지붕 대조군77/77, 기존Wall 복제거절7/7. Wall있는빈후보는배치시작전거절하고grid/건물/제품state변경0. 독립 검토의 기존replica_layer+fresh_unedited=False 재사용 문제는ValueError로수정하고38개helper검사/전체162검사로 확인했다.

## 경계와 사용

초기 바닥모든층·색·실제높이/Caves/비옥도·지붕/보행·바위/광석/건물고유배치를 재현한다. pawn·식물·아이템·퀘스트·게임진행 상태의 세이브복제는아니다. 관측이후외부모드 지연callback 및 원본99999보다늦은genstep는범용보장밖이다. 현재선택자는일회용새Map 경로에만있으며 제품TileMapState/추천창/Preview/Undo/저장과 미연결이다. 일반ApplyPatches만으로replica가적용되지는않는다.

같은바이옴6종의same_biome_replica metadata만 verified-static과 실제Aprofile로등록했다. 다른adaptive entry필드/이전index는변경하지않았다. unsafe2는quarantined. 범용검색/제품UI에연결된등록이라는뜻은아니다.

원시terrain/geology/replica·Player.log·launch/cleanup은이PC의같은폴더에남고Git제외다. Git의exact레시피만으로즉시재생할수없으며 새환경에서는source-manifest를실제로생성하고same_biome_prepare.py로snapshot/해시를새로묶는다. 과거원자료를덮어쓰지않는다.

## 산출물과 재현

[실제 그림 전체 HTML](same-biome-review.html) · [간단한 전후 그림](compact-comparison.png) · [전체 독립 대조](same-biome-evaluation.json) · [카탈로그 등록 영수증](registration-receipt.json) · [실행·보호·한계 계약](../../../../tools/map-library-prototype/same-biome-contract.md).

실행정본은 tools/map-library-prototype/README.md. 새소유profile/source capture→prepare→run.ps1 fresh output→정상종료후Archive→same_biome_report.py로actualrun_ok/raw/nativeaudit/PNG 전체대조. `same-biome-a-manifest.json`이같은바이옴복제를자동선택하며 B/C가조건불일치적응이다. 회귀는기존cave-v5 레시피를그대로참조한별도adaptive-b/c-regression-manifest.json으로대조했다. 선택거절시실패와이유를보존하며 unverified후보를성공으로사용하지않는다.

HTML에는실제nativePNG188개를포함하고모두디코딩했다. 비교판은Matplotlib로실제그림만배치했으며 직접view_image로검토/표시했다. 브라우저렌더·사용자미관승인은미검증이다. DEVsource/설치6343…·dist/일반설치9A792… 모두해시불변. 유료API/Claude/Fable호출0, 다른게임종료/설정/Workshop변경0.
