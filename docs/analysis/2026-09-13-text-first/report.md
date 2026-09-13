# 텍스트 우선 전환과 기존 특징 제거 수정

2026-09-13 / Codex / `dev`, 기준 `6b386b7c31640fa2eb87c634be4da695d71b9491`.

## 결과

**강 전체 제거, 삼각주만 제거하여 일반 강 유지, 종류별 제거·복원 처리를 구현했다.** 회귀 75개, 실제 RimWorld 생성 5개 맵을 포함한 60검사, 실제 Gemini 3.8 Flash 한국어 6요청을 최종 버전에서 확인했다. 용암 영역 채움과 유적 위치 지정은 이번 구현에 포함되지 않는다. 후속 방향은 [텍스트 우선 계획](../../text-first-plan-ko.md), 사용자 예시는 [설명서](../../description-ko.md)에 정리했다.

최종 검증 DLL SHA-256: `ac66a054efc773f3662657d7e1123c1ef1f8663879e9f0b12679f7b5b8db7317`. 코드 검사·최종 검증은 Codex가 수행했다. Fable은 설계 요청 1회가 이미 완료된 뒤 사용자가 크레딧 부족으로 중단을 지시했고, **그 뒤 추가 호출이나 최종 Fable 검토는 하지 않았다.** [기존 설계 요청](fable-design-request.md), [당시 답변](fable-design-review.md), [실행 메타데이터](fable-design-review.json)를 보존한다.

## 원인과 변경

- 기존 `river.present:false`는 상태에 저장되지만, 기본 false와 명시적 삭제를 구분하지 못하고 실제 강 생성기를 제외하지 않았다. 새 명시적 요청은 `removeFeatureCategories:["River"]`로 저장한다. 구형 snapshot의 false는 미지정으로 유지한다.
- `remove_mutators:["RiverDelta"]`와 `remove_categories:["River"]`를 분리했다. 전자는 원래 월드 강 연결을 이용해 일반 River를 남기고, 후자는 River 계열 TileMutator 모두를 제외한다. `restore_categories`는 종류 억제를 해제하며 개별 제거 기록까지 지우지는 않는다.
- 타일 baseline과 외부 변경 보존 체계를 유지한다. 월드 강 연결선을 수정하지 않는다. Undo·Reset·프리셋·Scribe에 새 제거 상태가 포함된다.
- AI에 실제 타일 특징(원래 특징 포함), 원래 특징 목록, 실행 중인 정의의 카테고리를 전달한다. 이전 `active_mutators`는 사용자 추가분만 표시해 원래 특징의 부재로 오해할 수 있었다.
- 새 제거 명령의 잘못된 정의·카테고리와 상충하는 제거/복원은 전체 변경을 거부한다. 비활성 모드의 옛 제거 기록 자체는 다른 편집을 막지 않는다. 억제된 종류에 새 특징을 넣으려면 명시적으로 종류 억제도 해제해야 한다.
- 현재 미지원인 용암 fill / 구조물 위치 요청을 물 채움·밀도 변경으로 대신하고 완료했다고 답하지 않도록 지침을 추가했다. 게임 엔진의 불가능이 아니라 현재 모드의 미지원으로 설명한다.

핵심 소스: `MapStateEditor`, `WorldTileEditor`, `TileMapState`, `MapStateCodec`, `MapStateValidation`, `MapGenParams`, `MapParameterParser`, `FeatureEditPrompt`, `Dialog_TextToMap`.

## 실제 엔진 확인

설치된 RimWorld 1.6 `Assembly-CSharp.dll`의 Tile, SurfaceTile, MapGenerator, GenStep_Terrain, TileMutatorWorker_River/ RiverDelta를 로컬에서 조사했다. Tile의 mutator 목록에 없는 River worker는 해당 생성 단계에서 실행되지 않으며, 확인한 경로에서는 `SurfaceTile.Rivers`가 남았다는 이유로 River mutator를 재추가하지 않았다. 이 관찰을 실제 전체 생성으로 검증했다. 게임 디컴파일 소스는 저장소에 넣지 않았다.

강 없는 타일에 `river.present:true`를 보낸다고 새 월드 강 연결을 만들지는 않는다. 사용자 도형으로 만든 물 수로는 별도 도형 편집 대상이다.

## 검증 명령과 결과

저장소 루트에서 실행:

```powershell
dotnet build dev/Source/MapGenAI.csproj --no-restore
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore
& tools/runtime-probe/launch.ps1 -FeatureRemoval -Language Korean -Output docs/analysis/2026-09-13-text-first/native-r2
$env:MAPGENAI_PROBE_MODEL='gemini-3.8-flash'
dotnet run --project tools/provider-probe/ProviderProbe.csproj -- . docs/analysis/2026-09-13-text-first/provider-r2 features docs/analysis/2026-09-13-text-first/native-r2
```

빌드 오류 0, 기존 버전 표기 CS7035 경고 1. [최종 빌드](final-build.log), [최종 75회귀 전체 출력](final-tests.log). provider 도구는 Git에서 제외된 로컬 설정을 읽으며 실호출 비용이 발생한다.

### 실제 게임: 새 격리 월드, 한국어, 250×250 맵 5개

모두 자연 생성된 RiverDelta 타일을 사용했고, Delta 강제 추가는 없었다. 서로 다른 타일이므로 아래 수치를 같은 맵의 전후 면적 차이로 비교하면 안 된다.

| 경우 | 최종 강 타일 수 | 흐름 노드 | 바다 타일 | 중앙 호수 확인 |
|---|---:|---:|---:|---:|
| 원래 삼각주 | 1440 | 13 | 8764 | 441/441 |
| 삼각주만 제거 → 일반 강 | 1852 | 1 | 9980 | 441/441 |
| 강 전체 제거 | 0 | 0 | 3970 | 441/441 |
| 강 제거 후 원래대로 복원 | 1847 | 26 | 3736 | 441/441 |
| 강과 해안 종류 제거 | 0 | 0 | 0 | 441/441 |

실제 Dialog 응답 적용과 Undo, Reset으로 원래 특징 복원, 월드 강 연결·이웃 타일 유지, 생성 컨텍스트 종료도 각 경우 확인했다. 실제 Scribe 저장·복원과 새 필드가 없는 구형 fixture도 통과했다. [60검사](native-r2/result.json), [지형 관측값](native-r2/feature-observations.json), [실행 DLL·프로필](native-r2/launch.json), [전체 게임 로그](native-r2/Player.log).

이미지는 `native-r2/*-generated-preview.png`에 저장했다. 실제 전체 맵 생성 안에서 Map Preview의 색상 생성기를 호출한 결과이며, 사용자 마우스 조작이나 동일 타일 전후 이미지 대조는 아니다. 임시 모드는 [자동 정리](native-r2/cleanup.json)되었고 결과·격리 프로필은 보존했다.

### 실제 모델: 독립 한국어 요청 6개

[최종 결과](provider-r2/feature-results.json): 삼각주만 삭제 / 강 전체 삭제 / 복원 / 해안 삭제 4개 모두 의도한 상태 변경과 기존 도형 보존 PASS. 용암 채움 / 특정 섬에 유적 배치 2개는 `ask`로 미지원 설명 PASS이며 **해당 기능 생성 성공을 뜻하지 않는다**. 실제 게임에서 캡처한 각 상태의 production prompt를 썼다. 응답 해석·상태 변경은 production 소스, 지형 실행은 위 네이티브 프로브로 분리 검증했다. 라이브 응답을 게임 UI에서 재전송한 종단간 마우스 테스트는 아니다.

처음 실행 `provider/`에서도 6응답 조건은 통과했지만, 유적 응답이 엔진 제한으로 오해될 수 있어 최종 프롬프트를 보완했다. 그래서 새 DLL·프롬프트로 `native-r2`와 `provider-r2`를 다시 실행했다. 최초 `native-r1`에는 해당 경우가 아닌 조건을 통과로 세는 8개 항목이 있었으므로 최종 검사 수는 해당 조건만 평가하고 삼각주의 복수 노드를 추가 확인한 r2의 60개를 사용한다. 최초 자료는 이력으로 보존한다.

## 남은 범위

- 용암 재료 목록·영역 채움, 구조물 공간 배치는 [후속 설계](../../text-first-plan-ko.md) 단계다. 모든 텍스트 요청 완료를 주장하지 않는다.
- 카테고리 필터는 TileMutator를 제어한다. 바이옴·기본 생성 단계·다른 모드가 만드는 같은 효과까지 자동 제거되는 것은 아니다. River/Coast 이외 특징의 실제 생성 검증을 확대해야 한다.
- 외부 모드 조합, 실제 오래된 사용자 세이브, 모든 공급자·언어·하위 모델은 이번 검증 대상이 아니다. 기존 저장 호환은 실제 Scribe를 사용한 fixture다.
- 이미지 추가 개선은 사용자 지시로 보류했다. 기존 기능 회귀만 확인했다. Fable도 재허용 전까지 호출하지 않는다.
- 설치·Git 저장 시점은 패키지의 `build.json`, 출력의 `installation-receipt.json`, mapgenai 트랙 상태/로그에서 확인한다. 보존 태그·main·원본 설치 모드를 개발 DLL로 대체하지 않는다.
