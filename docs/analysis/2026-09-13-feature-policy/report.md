# 지형 특징 추가·제거의 월드 지리 조건

2026-09-13 / MapGen AI dev / 기준 commit `848a390` / 최종 검증 DLL `14be3840bce485953256a87b8c9a727db64ddfec5ac1ab3353cc77ce2b408b05`.

## 사용자 결정과 구현

사용자: “내륙에서 해안같은 건 만들면 안된다고 생각하는데”, “삼각주같은 건 강이 있을 때만 가능한거고 등등. 그런 식으로. 오디세이의 지형 특징같은 걸 잘 참조해 봐. 그리고 landamark expanded 모드도”. 근거는 프로젝트 루트 `agents/main/log/2026-09-13-1357-mapgenai.md` §2026-09-13 — 월드 연결 보존 정책 구현 승인 [decision].

이 결정에 따라 이전 dev의 강·해안 전체 생성 제외 허용을 변경했다. 월드 연결은 보존하고 내부 특징은 해당 조건 안에서 편집한다. Fable/Claude를 호출하지 않았고 이미지 추가 개선은 보류했다. 용암 fill·유적 좌표 배치는 여전히 미구현이다.

- 내륙의 해안·월드 강 추가와 기존 강·해안의 전체 삭제는 거절한다. 기존 방향·위치는 편집할 수 있다.
- 삼각주는 실제 강과 해안 하구, 합류점은 세 연결 이상 및 두 육지 강 연결, 발원지는 강 연결 하나, 강 섬은 두 연결 이상을 요구한다. 호숫가는 월드 호수 인접, 기본 바다 생성기는 바다 인접을 요구한다.
- 호수·오아시스·분화구 등 내부 특징은 세계지도에서 해안이 아니어도 자체 조건에 맞으면 추가한다. 이름 목록을 하드코딩하는 방식으로 VLE 전체를 별도 구현하지 않았다.
- 개별 강·해안 변형을 제거하면 기본 River 또는 Coast/Lakeshore를 유지한다. 다른 내부 특징의 개별·종류 제거/복원은 유지한다.
- 잘못된 요청 일부를 빼고 적용하던 UI 필터를 제거했다. Backend가 전체 요청을 검증한 뒤 적용하며, 오류일 때 상태·실제 특징·Undo 기록을 유지한다. 프리셋에도 같은 환경 검사를 적용한다.
- 기존 저장의 River/Coast 전체 억제는 타일 열기 또는 생성 진입 때 기본 연결을 복구한다. 새로 불러오는 억제 프리셋은 거절한다. 월드 원본과 다른 편집은 보존한다.
- 다른 타일의 CoastAngleAt 조회에 선택 타일의 방향 설정이 섞이던 범위 문제를 수정했다. TileWorldSnapshot은 오염도도 저장해 특징 추가 실패/Reset 때 원본 수치를 복원한다. 임의 외부 worker의 모든 부작용까지 복구한다는 뜻은 아니다.

## 참조한 실제 정의와 적용 범위

설치된 대상은 [Vanilla Landmarks Expanded의 공식 Workshop 페이지](https://steamcommunity.com/sharedfiles/filedetails/?id=3656316229) 및 설치본 `3656316229`, package `VanillaExpanded.VExplorationE`다. Odyssey와 Vanilla Expanded Framework 의존성은 공식 설명·About.xml을 대조했다. 원본 Workshop 파일은 수정하지 않았다.

RimWorld 1.6.4871의 TileMutatorDef/TileMutatorWorker와 VLE DLL을 ILSpy로 읽었다. 기본 게임/DLC raw95 + VLE raw151 = 246 XML 항목(추상 부모 포함)은 [source-definition-index.json](source-definition-index.json)에 조건 필드만 기록했다. 상속·XML 패치 전 자료이며 실행 정본은 실제 DefDatabase다. 타사 디컴파일 소스는 scratch에만 두고 저장소에 배포하지 않았다.

최종 실제 로드는 VLE 없이 **87개**, VLE 포함 **226개** 특징이었다. 각 로드 집합의 모든 정의에 대해 실제 내륙·강·하구·사막 4개 문맥으로 정책 함수를 실행했다: 각각 348/904 결과. [VLE 실행 매트릭스](native-verified-vle/resolved-policy-matrix.json), [기본 매트릭스](native-verified-core/resolved-policy-matrix.json). 이는 후보 조건 검사의 전 항목 실행 증거이며, 226개 모두의 완성 맵을 생성한 검증은 아니다.

검사 필드: biomeWhitelist/Blacklist, animal/plantDensityRange, pollutionRange, averageTemperatureRange, min/maxHilliness, coastSidesRange, canSpawnOnRiver/Road, EverValid(필요 세력), 실제 Worker.IsValidTile. 알려진 River/Coast 생성기 계열의 추가 요구사항도 검사한다. VLE의 worker 검사는 직접 assembly 참조 없이 실행한다.

`chanceOnNonLandmarkTile`과 `canSpawnOnLandmark`는 자연 출현·랜드마크 추첨 조건으로 구별해 수동 내부 편집을 막지 않는다. Def.IsValidTile 전체를 그대로 호출하면 기존 자기 특징·교체 대상의 우선순위까지 환경 부적합으로 취급하므로, 환경 검사와 기존 충돌 계획을 분리했다. 기존 자연 특징을 그대로 유지하는 편집은 허용하되 제거 후 새로 추가하거나 복원하면 현행 조건을 검사한다.

월드 타일의 실제 mutator 목록은 바뀌지만 LandmarkDef에 따른 **세계지도 랜드마크 이름·아이콘을 새로 배치하지 않는다**. 바이옴/별도 모드 생성기가 같은 효과를 다시 만드는 경우까지 종류 제거 하나로 모두 제어하지 않는다.

## 실제 검증

- `dotnet build dev/Source/MapGenAI.csproj --no-restore -v minimal`: 오류0, 기존 CS7035 버전 경고1. [빌드 기록](final-build.log).
- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **79 PASS / 0 FAIL**. [회귀 기록](final-tests.log). 부적합 요청 전체 거절, 특징 교체·원본/이웃 보존, 기존 저장 전환, 프리셋 우회, 오염도 rollback/Reset 등을 포함한다.
- `tools/runtime-probe/launch.ps1 -FeaturePolicy -Language Korean`: VLE 없는 실제5맵, **54 검사 PASS**. [결과](native-verified-core/result.json).
- 같은 명령에 `-Landmarks -FeatureResponses .../provider-r1`: VLE 포함 실제6맵, **82 검사 PASS**. 실제 Dialog에 모델 응답11개를 재생해 추가/거절·Undo와 월드 특징을 확인했다. [결과](native-verified-vle/result.json).
- `-FeaturePolicy -DeltaDiagnostics -Landmarks`: 실제 삼각주12맵을 추가 생성했다. 해안 east/강90도 지정, 강/분기 유지, 얇은 얼음 아래 강 검증을 포함한다. [결과](delta-verified/result.json), [지형 원시값](delta-verified/delta-diagnostics.json).
- Gemini 3.8 Flash 실제 한국어11요청: 변경4건(삼각주 제거/복원, 내륙호수, 사막오아시스), 제한 안내7건 모두 의도·무관 상태 보존 검사 PASS. 공급자 결과는 [provider-r1](provider-r1/feature-results.json). 실제 게임 재생 결과와 별도로 기록했다. Fable은 사용하지 않았다.

최종 VLE 생성의 지형 집계(임시 얼음 아래 원래 지형 기준):

| 사례 | 바이옴 | 물 셀 | 강 셀 | 강 그래프 노드 | 바다 셀 |
|---|---|---:|---:|---:|---:|
| RiverDelta | Desert | 11973 | 1602 | 13 | 10371 |
| plain-river | BorealForest | 25107 | 8008 | 1 | 6050 |
| Lake | TemperateForest | 18332 | 0 | 0 | 0 |
| Oasis | Desert | 1274 | 0 | 0 | 0 |
| VEE_CraterLake | Desert | 7440 | 0 | 0 | 0 |
| coast-variant-removed | Tundra | 6552 | 0 | 0 | 4530 |

실제 생성 이미지와 상태는 같은 native 디렉터리에 있다. 최소 표본에서 내륙 Lake/Oasis/VEE_CraterLake의 물 생성, 삼각주→일반 강, Fjord→일반 바다 해안을 확인했다. 각 생성 후 Reset과 월드 강 연결·주변 특징 불변을 검사했다.

## 실패와 수정 이력

- 첫 native-r1은 검증 도구의 riverless tile.Rivers null을 Count로 읽어 실패했다. 도구만 수정했고 원본 결과를 보존했다.
- 첫 오프라인 새 오염도 검사는 float 정확 비교로 .35와 .35000002를 다르게 봤다. 수치 허용오차로 검사 의도를 바로잡았다. 제품 rollback 실패가 아니었다.
- native-release-*에서 표면 IsRiver=0을 생성 실패로 판정했다. 추가 [12타일 진단](delta-diagnostic/delta-diagnostics.json)에서 툰드라의 riverNodes32·ThinIce2339를 관찰했다. 얇은 얼음이 원래 강을 가린 것이며, 최초의 방향 변경 문제라는 추정은 철회한다. TerrainGrid.TerrainAtIgnoreTemp로 기반 지형을 검사하고, 최종12타일 진단에는 첫 타일을 -100도로 만드는 명시적인 격리 fixture를 넣어 얼음 아래 강 검사를 실제 실행했다. 기존 실패를 성공 집계에 섞지 않았다.
- 최종 제품 DLL은 `14be3840bce485953256a87b8c9a727db64ddfec5ac1ab3353cc77ce2b408b05`로 고정했다. 최종 회귀 뒤 도구만 추가 수정한 경우 각 launch.json의 probe DLL hash를 구별한다.

## 한계와 다음 작업

모든 외부 모드·생성기 조합, 모든 정의의 전체 맵 결과, 사용자 실제 오래된 세이브, RimWorld1.5, 모든 언어·하위 LLM 모델은 미검증이다. 실제 Dialog 메서드·게임 생성기를 사용했지만 마우스로 처음부터 끝까지 조작하는 검증은 아니다. 모델이 한 번 규칙을 지켰다고 모든 요청에서 지킨다고 보장하지 않는다.

다음 텍스트 작업은 공통 영역 마스크를 이용한 활성 TerrainDef 재료(용암 포함)와 지원 구조물의 좌표·영역 배치다. [계획](../../text-first-plan-ko.md), [사용자 설명서](../../description-ko.md)를 함께 갱신했다. 정식 main/보존태그/Steam 배포는 이 변경의 대상이 아니다.
