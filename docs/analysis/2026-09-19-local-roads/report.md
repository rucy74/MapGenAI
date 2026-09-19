# 지역 맵 도로 — 2026-09-19

사용자 승인에 따라 세계지도 연결을 수정하지 않고 정착 맵에 도로를 추가·수정·삭제하도록 구현했다. 복귀 기준은 dev 6280cf4a94650de1ab64c72ea1ad4779e6c9b7eb, 태그는 dev-before-local-roads-2026-09-19이다. Fable/Claude 호출은 0회이며 이미지 입력은 계속 중단한다.

## 동작과 경계

오솔길, 흙도로, 돌길, 고대 아스팔트 도로, 고대 아스팔트 고속도로를 지원한다. 게임의 RoadDef에 있는 노면·폭·재료 확률을 사용하되 산과 물을 파괴할 수 있는 원래 도로 생성기의 Bulldoze와 무조건 연결 fallback은 호출하지 않는다. 돌길은 해당 지역 암석을 사용한다. native 도로의 가로등·방호벽 등 건물, 다리·터널은 이번 범위에 포함하지 않는다.

새 localRoads 상태와 road_ops 명령이 기존 누적 편집, Undo, 프리셋, Scribe, 추천 후보 검증·수정에 연결된다. 종류별 폭은 고정이고 경유점을 지정할 수 있다. avoid는 마른 공간을 우회하고 direct는 지정 구간을 직선으로 검사한다. 도로 없는 상태에는 생성 단계를 추가하지 않고 기존 roads bool도 새 도로로 자동 변환하지 않는다.

경로와 폭 전체를 검증한 다음 도로 묶음을 적용한다. 실패 시 앞 도로만 남기지 않는다. 원래 도로·건축 바닥은 유지하고 지형 높이를 변경하지 않는다. 도로 면적은 뒤의 비율 채움·위치 지정 유적에서 보호한다. 기존 건물을 자동으로 철거하거나 우회하는 기능은 없으며 충돌하면 설명한다. 이후 다른 생성기가 도로를 막으면 최종 검사에서 보고한다.

## 실제 확인

최종 제품 DLL SHA256: 50caa50cf25b1f6fc820b30f78271f4e5943ab15b8db194df04845e425ecba2a.

- 오프라인 173 PASS / 0 FAIL: 기존 150개와 도로 상태·편집 10개, 독립 경로 검사 13개.
- Gemini 3.8 실제 호출 6개: 한글 추가·우회·종류 변경·삭제, 영어 고속도로·돌길. 6개 모두 road_ops로 응답했고 최종 DLL에서 실제 맵과 Map Preview를 생성했다.
- 최종 도로 시나리오 20개 완성 맵 + 20개 배경 미리보기. 이 중 막힌 직선·물 장벽·묶음 실패·실패 시 유적 보존 4개는 **도로 생성 거부가 기대 결과**이다.
- 산 우회, 5종 노면, 기존 도로와의 교차, 삭제, Undo·저장 자료, 고리 산·남쪽 통로·내부 70% 비옥토·폐허 2개 조합을 검사했다.
- 월드 도로 없는 성공 사례 13개의 경로와 도로 면적 지형 해시가 실제 맵/미리보기에서 같았다. native 월드 도로 사례는 기존 바닥 664칸이 존재했고 수정 0칸, 월드 링크도 유지했다.
- 이전 추천 원문 6개를 새 DLL에 재생한 완성 맵 6장과 배경 미리보기 6장은 이전 버전과 바이트 단위로 같았다. 최종 판정은 [평가 영수증](evaluation.json)에 기록했다.

## 발견한 문제와 보완

초기 측정 도구가 null 월드 도로 목록을 열거하고, 생성 종료 후 해제된 MapGenerator.Elevation을 읽어 실패했다. null 처리와 종료 전 높이 복사로 고쳤고 실패한 fixtures-r1/r2 원본도 보존했다. 이 실행들은 합격 수치에 포함하지 않는다.

새 도로 예약 면적을 기존 비옥토 독립 측정기가 분모에 포함해 70% 검사가 실패했다. 측정에서도 예약 도로를 제외한 뒤 fixtures-final-r2를 재생해 실제 4033/5762칸을 확인했다. 이 수정은 시험 도구만 바꿨다. 평가기가 생성 결과 이미지 캡처 상태를 Scribe 저장 상태로 잘못 비교한 부분도 실제 Scribe XML 대조로 수정했다. 판정기의 세 가지 변경 금지 항목에는 수치를 1로 바꾼 양성 대조를 넣어 거부를 확인했다. 이는 판정기 검사이며 native 측정 센서 전체의 별도 오염 주입 검사는 아니다.

기존 RecordedLandform 회귀의 원문은 수정하지 않았다. 새 선택 필드 localRoads 기본값 때문에 생긴 직렬화 비교 실패 3개는 해당 필드만 정규화하여 해결했고, 나머지 기존 필드는 그대로 비교한다. tests-r1/r2 실패와 tests-r3/final 합격을 보존한다.

내부 Codex 검토에서 도로 실패가 공통 오류 목록을 통해 기존 유적 배치까지 막는 회귀를 발견했다. 일반 작성 오류의 차단 동작은 유지하면서 도로 실패를 분리했다. 최종 failed-road-keeps-ruins는 기존 폐허 2개, batch-failure는 추가 지형 0칸으로 확인했다. 재검토에서 차단급 결함은 발견하지 못했다.

## 남은 한계

Map Preview 기본 단계는 native Roads를 생략한다. 새 지역 도로는 미리보기에 포함하지만, 원래 월드 도로와 겹치는 바닥이나 기존 도로의 부속 건물은 실제 맵에서만 존재할 수 있다. 이 경우 일반/추천 미리보기에 안내를 표시한다. 실제 교차에서는 기존 바닥을 유지한다. native 월드 고대 도로·모든 모드 조합의 교차까지 검증했다고 주장하지 않는다.

검증은 고정 월드 seed와 격리 프로필 범위다. 원래 MG23의 native 충돌은 원인 미확정·미해결 상태를 유지한다. 실제 사용자 전체 모드 구성에서의 검증은 추가 피드백이 필요하다.

## 재현

저장소에서 다음을 실행한다. 모델 호출 없이 저장 응답을 재생할 수 있다.

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --nologo
dotnet build tools/runtime-probe/RuntimeProbe.csproj --nologo
tools/runtime-probe/launch.ps1 -LandformSuite docs/analysis/2026-09-19-local-roads/fixture-cases-final.json -LandformReplies docs/analysis/2026-09-19-local-roads/fixture-replies -Language Korean -Output <새 절대 경로>
python -X utf8 tools/evaluate_local_roads.py
```

평가기는 기록된 최종 DLL 해시와 원본 결과를 확인한다. 새 빌드의 버전 문자열 때문에 해시가 바뀌면 기록을 덮어쓰지 말고 새 실행 폴더와 평가 기준을 만든다. [수동 테스트 문장](manual-tests-ko.md), [실제 그림](gallery.html).

1차 근거: 설치된 RimWorld 1.6 RoadDefs.xml / Assembly-CSharp.dll과 [Map Preview 기본 단계 원본](https://github.com/m00nl1ght-dev/MapPreview/blob/main/Sources/MapPreview/MapPreviewRequest.cs).
