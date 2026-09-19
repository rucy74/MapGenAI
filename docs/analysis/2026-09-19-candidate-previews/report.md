# 추천 후보 Map Preview

구현·검증 완료. 사용자 승인: 텍스트만으로 맵을 떠올리기 어려우므로 추천을 받으면 자동으로 세 후보 이미지를 표시한다. 기존 이미지 입력 OFF와 Fable 호출 금지는 유지한다. 기준 dev3073eef, 복귀 태그 dev-before-recommendation-previews-2026-09-19. 설치 여부는 별도 설치 영수증으로 구별한다.

## 설계와 확인할 사항
- 기존 recommend 명령을 그대로 사용하며 추가 모델 호출 없이 Map Preview의 요청 큐로 후보를 순차 생성한다.
- 요청별 상태와 타일 특징 복사본을 생성 스레드에 한정한다. WorldComponent·세계지도 원본·편집 캐시에는 후보를 적용하지 않는다.
- 추천 받기→썸네일 비교→클릭 확대→기존 번호/버튼 적용. 적용/Undo/Reset/닫기/후속 요청은 후보와 텍스처를 폐기한다. 늦은 결과는 표시하지 않는다.
- 지형 이미지에는 실제 건물 내부·적·식물 등 전체 게임 생성과의 차이가 있다. 상세 창에 설명한다.
- 실제 게임에서 세 이미지가 서로 다름, 선택 후 일반 Preview와 모든 픽셀 동일, 실세계 상태/특징/Undo 불변, 주 스레드와 생성 스레드 타일 분리, 실패/늦은 결과/seed·크기 변경 폐기, 한영 1280×800 화면과 확대 화면을 확인했다.

## 최종 결과와 재현
- 제품 DLL SHA256: `d53110ee3d5e7e5a7a05efa3a5da1b192d83c0d15c707c326d802397a93cddcd`. 검증 후 제품 DLL 재빌드 없이 패키징한다.
- `dotnet build dev/Source/MapGenAI.csproj --no-restore --nologo`: [build-r2.log](build-r2.log), 오류0/기존 CS7035 경고1.
- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: [146 PASS / 0 FAIL](final-tests.log).
- `dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore --nologo`: [probe-build-final-r2.log](probe-build-final-r2.log), 오류0/경고0.
- `./tools/runtime-probe/launch.ps1 -Render -Language Korean -CandidatePreviews ./docs/analysis/2026-09-19-landscape-recommendations/provider-final-ko -Output ./docs/analysis/2026-09-19-candidate-previews/native-ko-final`; 영어는 Language English, provider-final-en, native-en-final.
- [한국어56검사](native-ko-final/result.json), [영어56검사](native-en-final/result.json): 새 지형3개/기존 분지 보완3개/온천·연못 특징 교체3개를 각각 렌더하고 선택·Undo. 총18후보가 선택 후 일반 Map Preview와62500픽셀씩 정확히 일치했다. 실패주입 이후 나머지 후보 생성, 취소·초기화·교체의 늦은 결과 폐기, seed·맵크기 변경 차단도 포함한다.
- `python tools/evaluate_candidate_previews.py`: [집계](evaluation.json). 이전 실제 응답 fixture를 재사용했으며 새 공급자 호출0/Fable0. 후보가 사용하는 지형 원본·WorldComponent·Undo 기록을 변경하지 않았음을 실행 중 대조했다.
- [한국어 비교 화면](native-ko-final/recommend-plain-ui.png), [영어 확대 화면](native-en-final/recommend-existing-zoom.png)을 직접 확인했다. 표시는 순차 완료되며 추가 모델 토큰은 없다. 측정값은 의도적인 worker100ms 대기를 포함하므로 사용자 응답 속도로 단정하지 않는다.
- 최종 두 게임은 worker 완료 후 정상 종료했고 임시 모드 폴더를 자동 정리했다. 로그의 `Intentional candidate preview failure` 각1회는 복구 검증용이다.

## 범위와 한계
- 고정 seed/온대림 내륙 평지·기본 DLC와 Map Preview 격리 프로필이다. 모든 사용자 모드 조합/바이옴/시드/오래된 세이브를 확인한 것은 아니다.
- 픽셀 대조 대상은 일반 Map Preview다. 전체 맵의 식물·건물 내부·적까지 동일하다는 의미가 아니며 확대 창에 이 차이를 표시한다.
- tile 특징 목록과 관련 캐시를 분리했지만 임의 외부 모드의 월드 쓰기나 내부 리스트 직접 접근까지 격리하지 않는다. 외부 모드가 공유 Preview 큐를 강제로 비우는 경우의 시간 제한 복구는 미구현이다.
- 생성 엔진·자연어 스키마·모델 설정·저장 형식은 유지한다. 후보 요청에만 별도 상태를 제공한다. 이미지 입력/해석/팔레트와 GL 원본 생성기 연동은 이번 범위가 아니다.

## API 근거
- [Map Preview 원본](https://github.com/m00nl1ght-dev/MapPreview): 로컬 지형 미리보기·타일별 시드와 지형 색상 제공.
- 설치 DLL ILSpy: MapPreviewRequest는 Seed/MapSize/Promise를 갖고, MapPreviewGenerator.GeneratePreview→GenerateContentsIntoPreview가 worker 초기화 전에 TileInfo를 읽는다. Promise 완료는 Lunar main-thread callback. 취소할 때 공유 큐 전체를 비우거나 worker를 중단하지 않는다.
- 본체 PlanetTile.Tile→PlanetLayer indexer, Map.TileInfo→WorldGrid PlanetTile indexer. 후보 요청인 해당 스레드에서만 타일 참조를 바꾼다. 일반 생성/다른 타일은 원래 값을 유지한다.

## 초기 검증
- 변경 전 오프라인 146 PASS / 0 FAIL. 첫 제품 build는 UI 네임스페이스와 Verse.UI 이름 충돌 2오류로 실패했고 명시적인 Verse.UI로 수정했다.
- 최초 native-ko-r1에서 Map Preview 타입을 캡처한 컴파일러 생성 closure 필드가 Lunar 로드 전에 TypeLoadException을 일으켰다. 단순 NoInlining만으로 해결되지 않아 캡처 필드를 object/Stopwatch로 변경했다. probe의 정적 캐시 lambda도 지역 번호를 캡처하도록 수정했다. 이 첫 실패 로그는 보존한다.
- native-ko-r2: 실제 새 추천/기존 분지/HotSprings→Pond 등9후보가 모두 성공, 선택 후 일반 Preview와62500픽셀씩 일치, 세계 원본/Undo 유지. 화면 캡처는 같은 프레임의 창 전환 때문에 잘못된 단계가 찍혔고 측정 anonymous object는 serializer가 빈 객체로 기록했다. 후속 probe는 전환 전4프레임을 기다리고 Dictionary로 측정값을 보관한다. 이 초기 화면/측정은 최종 UI·시간 근거로 사용하지 않는다.
- native-ko-r3/native-en-r1은55검사를 통과했지만 마지막 취소 작업이 끝나기 전에 probe가 Application.Quit를 호출해 ThreadAbort/종료 crash가 발생했다. `ok:true`만으로 성공 처리하지 않았으며 worker idle을 프레임별 확인한 후 종료하도록 harness를 수정했다. 최종 두 실행에서는 이 종료 예외가 없다. 최초 잘못된 csproj 경로의 MSB1009도 probe-build-final.log로 보존했다.
