# Landscape blending 독립 검토

2026-09-24. 기준 `d84ea4bfd319ec36708d66aaf0148cae8601eb71` 대비 작업 중 변경을 검토했다. 제품 코드를 수정하지 않았으며 외부 API, Claude, 원본 게임을 실행하지 않았다. 검토 범위는 `details` 저장·기본값·설명, 표면 보호, 생성 순서와 식생 Harmony 범위다. 네이티브 before/after 검증은 다른 작업자가 진행 중이며 이 기록의 독립 통과 근거에 포함하지 않는다.

## 확인된 결함과 수정 상태

- **P2, s4에서 해소 — native 아스팔트 도로의 자갈 어깨를 자동 흙/모래로 덮을 수 있었다.** 최초 검토 시 `LandscapeBlendGeneration.cs:35`는 `LocalRoadCells`, `Road` 태그와 foundation을 보호하지만 native 도로의 일반 `Gravel`에는 이 표식이 없다. 설치된 Core `RoadDefs.xml`의 `AncientAsphaltRoad`/`AncientAsphaltHighway`는 실제 `Gravel`을 어깨에 배치한다. 당시 `LandscapeBlendField.OrdinaryGround`는 이를 허용하고, 물가 샘플이 Soil이면 `LandscapeBlendGeneration.cs:50–51`에서 덮어썼다.
  - 재현 조건: 자연 상세가 켜진 landform의 floor와 native 자갈 어깨가 겹치고, 다른 보호 조건이 없으며 가까운 물이 있는 토양 바이옴. 순수 DLL의 `new LandscapeBlendField("23").Sample(0,10,5,20,true)`는 `Ground.Soil`을 반환했다. 이는 조건별 코드 경로의 재현이며 실제 타일에서의 변경 칸 수/화면 재현은 아직 없다.
  - 기존 native probe는 `Probe.cs:83`에서 월드 도로 타일을 제외하고 `road` fixture도 `localRoads`만 추가하므로, 그 검증 통과만으로 이 누락을 반박할 수 없다.
  - **수정 재확인:** s4의 `OrdinaryGround`는 Soil/Sand만 허용한다. 따라서 기존 Gravel은 `locked=true`가 되어 terrain 변경과 vegetation 가중치 대입 전에 모두 건너뛰고 가중치 1을 유지한다. 도로 출처를 추정할 필요 없이 자연 자갈도 함께 보존하는 보수적 수정이다. KO/EN 프롬프트도 기존 자갈 보존을 설명한다.
  - 실제 제품 파일의 SHA256을 직접 확인했다: `dev/Assemblies/MapGenAI.dll` = `6d854073d57fc6a20e8735aeae7a49cb13d481cdadc5061f5ab34d841b92285e`. 원래 반례의 field 샘플은 여전히 Soil이지만 `GravelEligible=False`이므로 해당 변경 경로에 들어갈 수 없다. 아래 s4 검사는 순수 코드 실행이며 native 도로 맵 실행 증명과 구별한다.

처음 발견된 지역 도로 보호 누락은 이미 `LocalRoadCells`/`HasTag("Road")`/`FoundationAt` 조건이 추가되어 별도 미해결 결함으로 중복 등록하지 않았다. 그 외 확정 제품 결함은 현재 찾지 못했다.

## 독립 실행 및 코드 대조

- `mapgenai-blend-tests-s1/TextToMap.Tests.dll`의 `LandscapeBlendTests.RunAll`을 PowerShell reflection으로 실행: **5 PASS / 0 FAIL**. 100 unfiltered variants에서 field failures 0, 변경 후보 1433..2044/6400, 서로 다른 결과 100개. DLL SHA256 `807fe0fd1908530242001f3d4b61e0a3beac0606d65de01c51e5e7b997b2e170`.
- 수정 후 `mapgenai-blend-tests-s4/TextToMap.Tests.dll`로 같은 5개 검사를 독립 재실행: **5 PASS / 0 FAIL**, 같은 100개 field 결과. 추가 직접 호출은 `OrdinaryGround("Gravel")=False`, Soil/Sand=True를 확인했다. DLL SHA256 `2aaf44b438ba39497fbdb153a73624ca06ff7b31959c7435854b58cb9338a4f6`.
- 실행 방식: `[Reflection.Assembly]::LoadFrom($assemblyPath).GetType('LandscapeBlendTests').GetMethod('RunAll',[Reflection.BindingFlags]'Static,Public').Invoke($null,@())`; `CoreRegressionTests`의 static `passed`/`failed` 필드도 읽어 종료 결과를 확인했다. 이 실행은 전체 261개 회귀 재실행이 아니다.
- `details`는 Clone/Scribe/일반 JSON 저장·설명에 연결되며, 기본값은 `shape_ops add`의 새 landform에만 부여한다. 기존 null/none은 새 생성 단계를 삽입하지 않는다. 현재 수정만으로 기존 높이·floor mask를 바꾸는 경로는 없다.
- 명시 material과 다른 authored shape mask를 제외하고, `region_fill`은 칠할 70% 선택 결과보다 넓은 **원본 참조 영역 전체**를 잠근다. 물·특수 지형은 현재 surface의 허용 목록에서 제외된다. 이는 코드 대조이며 실제 terrain layer/저장 파일 동일성의 네이티브 증명은 아니다.
- 로컬 decompile의 `GenStep_Plants`/`WildPlantSpawner`와 대조했다. `GetDesiredPlantsCountAt`과 `GetDesiredPlantsCountIn`은 서로 호출하지 않으므로 두 Prefix가 같은 요청에 중첩 적용되는 구조는 아니다. `activeMap`은 ThreadStatic이며 Finalizer가 이전 값을 복원한다. 가중치가 없거나 생성 범위 밖이면 1이고, `wildPlantsCareAboutLocalFertility=false` 바이옴도 1이다.

## 결과 해석의 경계

- UI의 바닥·식생 조화는 활성 설정 설명이다. 보호 영역만 남거나 물이 없는 부분에서는 바닥 변화가 없을 수 있으며, Map Preview에는 실제 풀/나무 배치가 나타나지 않는다.
- 식생은 native 종 선택·생육 조건과 override 곡선을 교체하지 않는 방식이다. 전체 원하는 식물 수, 주변 포화도, 생성 RNG 진행이 달라질 수 있으므로 **효과 영역 밖 식물 좌표까지 동일**하거나 **특수 랜드마크의 식물 수·군락을 동일하게 보존**한다는 의미로 확장하면 안 된다. 임의 모드의 override 곡선 + 비사막 바이옴 조합은 이 검토에서 실행하지 않았다.
- 100개 순수 field 검사는 실제 `LandscapeBlendGeneration.Apply`/Harmony/Native Scribe를 실행하지 않는다. 실제 레이어 보존, 초기 풀/나무 변화, null/none의 기존 DLL 대비 전체 동일성, 성능과 시각적 만족도는 네이티브 결과와 별도 판단이 필요하다.
