# 칸별 바위·바닥 디테일 이식

2026-10-01 22:18 KST · 개발자 저장소 프로토타입 · 기준 dev `21a218f`

사용자 “아직 타일 디테일 바위 디테일 잘 못 따라하는거 같은데”에 따른 후속이다. 작은 조각을 생략하거나 윤곽을 닫는 대신, 실제 원본의 모든 자연 바위·광석 점유 칸을 `rock_layer`에 저장한다. 지원하는 자연 바닥은 한 칸짜리 무늬까지 저장한다. 제품 UI·설치 DEV·일반판·Workshop·사용자 프로필은 변경하지 않았다.

[실제 전후 그림 6종](review.html) · [최종 검증](verification.json) · [독립 원본 대조](rock-evaluation.json) · [카탈로그](catalog.json)

## 결과

- 호숫가 원본 자연 바위는 **1,143칸**이다. 이전 물/바닥 이식은4,970칸, 이번250칸 온대림 결과는1,143칸이며 추가·누락0이다. 원본의 작은 바위425칸/30조각 모두 포함한다. 작은 바닥 패치637칸의 재료 이름은636칸 일치한다. 이 사례의 전체 지원 자연 바닥 이름 일치는98.844%로, 전체 맵100% 복제를 뜻하지 않는다.
- GL6종×3조건18결과 중 **14결과의 raw 바위 추가·누락0**. 엄격한 원본 점유·바닥·최종 충돌 검사를 함께 통과한 것은12/18이다. 원본/그림/실행의 성공을 후보 적합성 성공으로 바꾸어 말하지 않는다.
- 최종 native 고대 건물·곤충 지형이 원본 바위 자리를 점유한4조건과, 원본 자연 바닥 일치95% 미달인 군도2조건은 검색 가능 profile에서 제외했다. 같은 레시피도 통과한 조건에서만 사용한다. 9항목 모두 최소1개의 관측된 통과 profile을 가지며, 아무 타일에나 성공하는9항목은 아니다.
- Python **79/79**, 실제 생성 실행 검사 **383/383**, 새 지도58개(29후보+29동일 타일 baseline). 기존 sidecar 없는6결과는 이전 전체 지형/PNG와 동일하다. 원본 GL6개는 이전 실제 관측20파일의 SHA를 대조해 재사용했으며 이번에 새로 만든 원본으로 세지 않았다.
- 물 배치21/21, 바닥 적용 보호21/21, 중간 윤곽25/27. 전체 원본 자연 바닥을 분모로 한 같은 바이옴 재료 검사11/13. 중간 바닥 보고서의15/15는 결과에서 비교 가능한 G칸을 거르는 종전 분모로서 최종95% 승인 판정과 다르다.
- 보호 대조군은 실제 강·바다·특수 물·일반 물·길/다리·건축 바닥·건물·특수 바위·기존 바위·기존 자원·다른 물체·위험/비지원 바닥12종을 양성 확인했다. 실제 바위1칸 추가, 새 일반 바위1칸 제거, 새 광석1칸 제거를 행사했으며 보호/unknown/그리드 위반0. AncientConcrete(31,20)는 category-null 특수 바닥 그대로 남았다. 전체 unknown 대조군10,000칸은 baseline의 지형과 실제/일반PNG가 바이트까지 같다.
- 유료 API/Claude/Fable0. 로컬E5 9×384 인덱스의 정규화/ID/카탈로그SHA를 대조했다. 자연어 검색 품질 평가를 새로 반복한 것은 아니다. 7개 소유 프로브(r1 3개+r2 4개)는 전부Archive했으며, final4개 영수증을 재검증했다.

## 끝까지 확인해서 거부한 조건

| 조건 | 원본 바위 누락 | 최종 보호 충돌 | 판정 |
|---|---:|---:|---|
| A250 온대림 · 골짜기 |256|256|고대 건물/바닥 점유 · 제외|
| A250 온대림 · 외딴 산 |251|251|고대 건물/바닥 점유 · 제외|
| B300 온대림 · 절벽 |434|418|고대 건물·곤충 지형 · 제외. 나머지16칸은 InsectSludge|
| C250 건조관목림 · 외딴 산 |381|381|고대 건물/바닥 점유 · 제외|
| A/B 온대림 · 군도 |0|0|전체 원본 자연 바닥 이름93.791%/92.660% · 제외|

골짜기A와 절벽B는 바위IoU가98% 문턱을 넘더라도 최종 보호 충돌 때문에 거부한다. 기존 고대 건물을 지워 사진을 맞추지 않았다. 다른 바이옴에서는 원래 토양·기후와 물가 정책을 따르므로 원본 바닥 이름의 단순 일치율을 승인 조건으로 사용하지 않는다.

## 구현과 native 참조

`rock.py`는 unknown0/관측된 비바위1/자연 바위 점유2를 south-first RLE로 저장한다. 원본 자원·바위 종류는 복제하지 않는다. `RocksFromGrid` 직전199에서 Elevation/Caves를 맞추고 물403 뒤404에서 바위를 보정한다. 새로 생성된 광석이 원본 전체 점유 밖으로 튀어나오면 제거될 수 있으므로 자원량 유지 보장은 아니다. 지원하는 기존/특수 자원과 보호 대상은 유지한다.

native750 `ScatterShrines`→`SymbolResolver_AncientTemple`는 `Sketch.Spawn`으로 바위를 치우고 건물/바닥을 놓는다. 그래서 마지막99999 캡처에서도 점유/보호 충돌을 읽기 전용으로 감사한다. 지붕을 붙이려고 .71을 .75/.8로 높이는 우회는 해당 생성기가 지붕을 허용하여 해결책이 아니고 채광·붕괴 의미를 바꾸므로 채택하지 않았다. 원본 지붕·동굴·이벤트/건물의 이식은 미구현이다.

`rock_report.py`는 RLE가 아닌 원본 전체 M/지원 자연 G칸을 정답 분모로 읽는다. raw 바위 precision/recall/IoU≥98%, 같은 바이옴 전체 자연 바닥 이름≥95%, 중간 보호 불변조건과 최종 보호 충돌0을 요구한다. 최종 감사가 없거나 원본 관측과 수치가 맞지 않아도 후보를 거부한다. `--details`에서 native palette가 없으면 polygon fallback으로 조용히 우회하지 않고 거부한다. 정확한 바위 재현 문구는 실제 rock sidecar가 있는 항목에만 붙는다.

## 재현

```powershell
python -X utf8 tools/map-library-prototype/detail_prepare.py --folder <새 실험 폴더>
dotnet build tools/map-library-prototype/PrototypeProbe.csproj --nologo
# 새 실험 폴더의 detail-a/b/c-manifest와 rock-controls-manifest를 run.ps1로 순차 실행.
# 자연 종료와 result.json을 확인한 뒤 각 실행을 같은 인자 -Archive로 보관.
python -X utf8 tools/map-library-prototype/measure_transfer.py --folder <폴더> --runs <A> <B> <C>
python -X utf8 tools/map-library-prototype/rock_report.py --folder <폴더> --runs <A> <B> <C> <control>
```

이번 최종 폴더는 `rock-v4`, 실행은 `detail-a-native-r2`, `detail-b-native-r2`, `detail-c-native-r2`, `rock-controls-native-r2`다. `rock_report` CLI 종료1은 실제 실패6조건을 검출한 결과다. 결과를 숨기거나 기준을 낮추지 않고 `finalize_catalog.py`로 실패 profile만 제외했다. 바닥/물 별도평가→finalize→offline embedding→`verify_details.py` 순서의 영수증이 최종검증에 있다. 마지막 palette prerequisite/문구 수정은 생성 입력·receipt20파일이 byte-identical임을 [대조](catalog-rebuild-receipt.json)했다. 추가 게임 생성은 필요하지 않았다. 원시 `*-terrain.json`·Player.log·소유 실행/cleanup 영수증은 기존Git 제외 정책에 따라 이 PC에 남으며, Git에서 받은 파일만으로 과거 실행 전체를 재검증할 수는 없다.

## 보호된 배포 경계·후속

소스/설치 DEV DLL SHA256 `6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6`, dist/일반 설치 DLL `9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5` 그대로다. 제품 source diff도0이다. 이번 프로브 DLL은 `E34AAB357FFAD2BFD091A0A2093A6B490B732B8C4FAFBB253A8CECFFB5219028`다.

다음은 원본 지형과 부속 구조물의 배치를 함께 계획하거나, 실제 타일에서 충돌이 적은 변형을 생성하는 단계다. 그 뒤 DEV opt-in으로 sidecar를 TileMapState/Preview/저장/Undo에 연결해야 한다. 현재 제품 채팅에서 이 칸별 레시피가 자동 적용되는 기능은 아니다. 일반 사진/임의 모드 팔레트/기존 강·해안·도로 타일/절차적 GL 그래프 변형은 미검증이며 그대로 지원 범위 밖이다. PNG36개를 직접 decode하고 비교PNG를 관찰했지만 브라우저 렌더와 사용자 미적 승인은 별개다.

GL: m00nl1ght · [Geological Landforms](https://github.com/m00nl1ght-dev/GeologicalLandforms) · 원본 revision/라이선스는 상위ATTRIBUTION을 유지한다. GL 파생 레시피·지형 그림은CC BY-NC-SA4.0, 게임 자산의 권리는 별도다.
