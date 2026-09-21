# MG29 — 지역 도로 자동 다리

## 동작

흙길·흙도로·돌길·고대 아스팔트 도로·고속도로가 기본 다리 건설이 가능한 물을 건널 때 `Bridge` foundation을 자동으로 설치한다. 강물 위를 흙으로 칠하지 않는다. `road_ops` 계약과 저장 형식은 그대로다. LLM에는 한영 모두 자동 다리 지원을 알린다. 다리를 따로 요청할 필요가 없다.

`avoid`는 기존 육지 경로를 먼저 그대로 사용하고, 실패한 경우에만 다리 경로를 찾는다. `direct`는 지정한 직선 구간에서 필요한 물을 건넌다. 시작과 끝은 육지 또는 기존 다리에 있어야 한다. 실제 도로 종류의 기본 Bridge 폭을 읽으며 다리 표면은 나무다. 기존 다리·바닥·지형 높이·세계지도 강/도로 링크를 보존한다.

깊은 강물 `WaterMovingChestDeep`도 Bridgeable이므로 지원한다. 얕은 호수/바닷물과 Bridgeable 습지를 지원하되, 깊은 호수·깊은 바다·용암·빈 우주·산·건물은 덮지 않는다. 건물 자동 철거·터널·도로 부속 건물은 범위 밖이다. 건물에 걸리는 경로는 실패할 수 있다.

전체 도로 묶음을 사전 검사한 뒤 적용한다. 적용 중 예외가 나면 이번에 바꾼 terrain/foundation/under/color를 복원한다. 이것은 임의의 타 모드가 부수효과로 바꾼 모든 Thing까지 복구하는 트랜잭션이라는 뜻은 아니다.

## 기본 게임과 미리보기

설치된 RimWorld 1.6 `GenStep_Roads`, `RoadDefGenStep_Place`, `TerrainGrid`와 Core RoadDefs를 확인했다. 기본 생성기도 우선 마른 경로를 찾으며 도로별 Bridge profile을 가지고 있다. Bridge는 foundation이므로 완성 맵에서는 `SetFoundation`을 사용한다.

Map Preview의 최소 Map에는 `SetFoundation`의 변경 알림이 요구하는 component 일부가 없다. MapGen AI의 preview 경로에서만 foundation grid를 기록하고 원래 top water를 그대로 둔다. Map Preview의 소스나 배포 DLL을 수정하지 않았다. 기존 Map Preview는 돌/아스팔트 `SetTerrain`의 under-layer 처리를 생략하므로 전체 layer hash가 서로 다를 수 있다. 다리 자체의 모든 층, 도로 경로/표면과 실제 화면 픽셀은 별도로 비교했다.

## 검증

- 기준: dev `78d489bbfbf22827786c9414320acea5d450badc`, GitHub rollback tag `dev-before-road-bridges-2026-09-22`.
- 최종 DLL SHA-256: `17c35e94fe6c3a5277137a28ccfe3c6b12990dbc7679ca08ec4517e5736d8549`.
- 제품 빌드: 오류0, 기존 CS7035 경고1. Offline **183 PASS / 0 FAIL**.
- 최종 선택된 검증: 새 DLL의 완성 맵23개 + Map Preview23개, 기준 DLL의 맵19개 + Preview19개. **369 검사항목 PASS**. 초기 측정 오류가 난 기존 다리 실행은 별도 원본으로 보존했다.
- 육지 도로5종 + 연못 우회: 기준 DLL과 경로·완성 terrain layer hash·전체 그림이 같음. Full/preview 각각 비교하여12개 결과 일치.
- 다리 도로5종·여러 물 구간·대각선·실제 월드 강·습지: 실제 Walkable과 도로 footprint 안4방향 연결 확인. 해당 물의 원래 재료가 다리 밑에 남음. 세계지도 강/도로 active/potential 링크와 높이 변화0.
- 실제 월드 강에서 `WaterMovingShallow`와 `WaterMovingChestDeep` 위의 다리를 모두 확인. 기본 오솔길 다리42칸. 다섯 도로의 통제된 수로 fixture는 각각153/153/153/153/357칸.
- 기존 다리93칸과 돌바닥을 주입한 별도 fixture: 기존 층 변경0, 연결된 다리60칸 추가. 일반 자연 맵을 가장하지 않고 `existingFixture`에 주입 사실을 기록했다.
- 깊은 호수·용암·물 위 종점·도로 묶음의 뒤쪽 산 장애물은 전체 적용 거절, 모든 terrain 층과 높이 그대로.
- 실제 다리5칸 쓰기 직후 Harmony postfix로 강제 예외: full/preview 모두 주입 확인 후 원래 모든 terrain 층 hash 복구. 실패 후 새 다리0.
- 정상 fixture15개에서 완성 맵과 Preview의 **62,500픽셀 전부 일치**. 한영 fresh provider2건도 각각 전픽셀 일치.
- 실제 `gemini-3.8-flash` 첫 요청 KO/EN 각1회: 모두 generate, 편집 없이 원문 응답을 게임에 재생하여 다리 포함 도로 생성 성공. 총 입력27,751 / 출력165토큰. Fable 호출0.

실제 명령은 repo 루트에서 실행했다:

```powershell
dotnet build dev/Source/MapGenAI.csproj --no-restore
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-22-road-bridges/fixture-cases.json -LandformReplies docs/analysis/2026-09-22-road-bridges/fixture-replies -Language Korean -Render -Output <새 폴더>
# 동일 manifest + 이전 DLL -SourceDll로 비교. fault-cases에 -RoadBridgeFaultAfter 5 또는 -RoadBridgeExisting 별도 실행.
dotnet run --project tools/provider-probe/ProviderProbe.csproj --no-build -- <repo> <output> landforms <captured-input>
python -X utf8 tools/evaluate_road_bridges.py
```

원문 응답/요청/production prompt·최종 상태·Scribe·Player.log·launch hash·자동 cleanup 기록을 각 실행 폴더에 남겼다. [자동 평가](evaluation.json), [실제 그림](gallery.html), [직접 테스트](manual-tests-ko.md).

## 발견과 한계

코드 검토에서 foundationGrid가 public인데 NonPublic만 조회하던 초기 오류를 수정한 뒤 첫 native 실행을 했다. 별도 probe 초기 측정은 기존 다리를 강둑으로 잘못 세어 양안 연결4검사를 실패시켰다(`evaluation-r1.json`). 제품에서는 Walkable·연결·기존 층 보존이 통과한 상태였다. 검사에서 원래 물과 기존 다리를 한 구간으로 묶고, 실제 마른 양안까지 확인하도록 고친 후 새 게임에서 재실행했다. 제품 DLL은 바뀌지 않았다.

검증은 설치된 RimWorld 1.6·Harmony·Map Preview·DLC의 격리된 새 월드다. 기준/새 DLL 모두 probe 초기화의 기존 null-reference 진단과 일부 개발 로그창 GUI 진단이 남아 있으므로 Player.log 무오류를 주장하지 않는다. 네 단일사례 probe는 결과 파일을 모두 기록한 뒤 Application.Quit로 종료되지 않아 PID·명령행·격리 프로필·소유 marker를 확인하고 종료했다. completed-probe-stop.json에 기록했고 사용자 게임은 종료하지 않았다. 원래 월드 도로는 Map Preview에서 자체 생략되므로 기존 도로와의 교차 모습 차이는 이전 한계 그대로다. 모든 모드 조합이나 기존 MG23 native Mono 충돌 해결은 주장하지 않는다. 이미지 입력은 계속 OFF다.
