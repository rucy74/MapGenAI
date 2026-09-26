# DEV 담수 물가와 취향 문답 개선

기준은 `48d4ed2`, 복귀 태그는 `dev-before-shoreline-guide-2026-09-26`이다. 사용자의 DEV A01 물가/사막 고려 요청과 앞뒤가 모순되는 취향 문답 피드백에 대응했다. 일반판/dist/main, 사용자 설정, 이미지 입력 비활성 상태를 유지한다.

## 변경

- `LandscapeBlendGeneration`은 `details:natural` 영역에 실제로 겹치는 일반 담수를 출발점으로 주변 육지 최대6칸에 효과를 허용한다. 연결된 강 전체를 따라 확장하지 않는다. 실제 물의 경계에서 거리를 재어 폭과 재료가 달라지는 끊어진 물가를 만든다.
- 진흙은 native biome의 patch thresholds에 Mud가 있고, 일반 Soil·비옥도 기반 식생·연평균 기온0도 초과·강수600 이상 조건이 맞는 물가1~2칸 일부에만 배치한다. Sand에는 진흙/Soil을 추가하지 않는다. Ice·특수 토양·기존 Gravel·도로·기반·구조물·비율 채움 원본 영역 전체는 보호한다. 온천·용암·염수·모드 특수물은 외부 담수 효과의 출발점에서 제외한다.
- 새 `shape_ops:add` 중 양수 roughness를 가진 담수 composite만 `details:natural` 기본값을 얻는다. 숫자 roughness/최상위 fill/연산 fill을 지원한다. 기존 저장 null/none, 전체 배열 복원, 일반 부분 수정과 정확한 도형은 자동 활성화하지 않는다. 이미 natural이 켜진 저장의 물가 표현은 이번 개선으로 바뀔 수 있다.
- 취향 문답은 준비된 로컬 질문이다. 이전 산·물의 양과 현재 확인된 물에 따라 선택지/표현을 조정하고, 특별한 지형 방향이 하나뿐이면 추가 질문을 생략한다. 작은 연못 뒤에 큰 만을 예로 들지 않는다. 뒤로 답을 바꾸면 이후 답을 다시 정리하며, 마지막 확인 뒤에만 기존 추천 API를 호출한다.

## 새 검증

명령은 저장소 루트에서 실행했다.

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --no-restore -v minimal
dotnet build tools/shoreline-probe/ShorelineProbe.csproj --no-restore -v minimal
# 각 native 폴더 launch.json에 실제 run 인자/제품 DLL SHA256/실행 PID가 기록된다.
python -X utf8 tools/shoreline-probe/evaluate.py
```

- 코드 회귀 **287 PASS / 0 FAIL**. 제품 빌드0오류/기존CS7035경고1, 프로브 빌드0오류/0경고.
- 최종 **7개 조건, 실제 프리뷰7개+완성맵7개, native 검사111개 PASS**. 무작위로 좋은 타일을 고르지 않고 고정 seed에서 조건에 맞는 첫 타일을 사용했다.
- 독립 산출물 대조 **58 PASS**, 원자료108파일 SHA256을 [artifact-audit.json](artifact-audit.json)에 기록했다. 변경 칸의 전량 RLE 대조, 높이/동굴/물 보존, 허용된 중앙 연못 주변 범위, 뒤 단계의 추가 지형 변화, OFF와 bypass 동일성을 검사했다.
- 새 유료 모델/API/Fable 호출 **0회**. 질문 분기/뒤로가기/취소는 코드 테스트 대상이며 이번 GUI의 실제 클릭·한국어 줄바꿈 화면 캡처는 하지 않았다.

| 최종 fixture | native 검사 | 프리뷰·완성맵 각각의 효과 |
|---|---:|---|
| [온대림](native-shore-temperate-natural-05/result.json) | 19 | 372칸: 진흙70/모래179/자갈123 |
| [사막](native-shore-desert-natural-01/result.json) | 15 | 145칸: 모래13/자갈132, 새 진흙·토양0 |
| [추운 툰드라](native-shore-cold-natural-01/result.json) | 15 | 301칸: 모래199/자갈102, 새 진흙·토양0 |
| [70% 채움·도로·기반](native-shore-protected-natural-01/result.json) | 21 | 효과309칸. 도로476칸/기반1칸/채움 원본4050칸 보호 |
| [온천](native-shore-hotspring-natural-01/result.json) | 15 | 담수 외부 효과0칸, 온천 그대로 |
| [명시적 OFF](native-shore-temperate-none-02/result.json) | 11 | 효과 단계 자체 미예약, bypass 결과와 동일 |
| [단계 bypass](native-shore-temperate-bypass-02/result.json) | 15 | 효과0칸, ON과 입력 지형·높이·동굴 동일 |

`protected`는 실제70% 채움과 실제 도로를 생성한다. 그 외 기반1칸·기존자갈·기름진토양은 검사기가 측정 직전에 주입한 보호 대조군이며, naturally generated 결과로 부르지 않는다. 직접 원자료와 주입 위치는 각 fixture에 보존했다. Map Preview는 식생을 표시하지 않는다.

## 실패와 보완 기록

- 첫 회귀283PASS/2FAIL은 과거 실제 KO/EN 추천 add 두 건의 details 기본값 변경이었다. 원래 기록은 수정하지 않았다. KO coast option2 `valley_lake`, EN existing option1 `northeast_lake`의 기대값 한 필드만 명시적으로 보정하고 나머지 전체 상태 동등 검사는 유지했다.
- `native-shore-temperate-natural-01`은 완성맵 생성이 끝난 뒤 폐기된 높이 그리드를 읽은 프로브 오류. 최종 스냅샷은 terrain만 읽고 높이 없음(null)을 명시하도록 고쳤다. 실제 생성 중 높이 검사는 그대로다.
- natural-02/03에서는 표면120칸 변화/보호 검사는 통과했지만 진흙0칸이었다. 조사 결과 생태 조건은 모두 참. 도형의 안쪽 마스크에서 거리를 재어 물의 feather 바깥 경계를 놓친 것이 원인이었다. 권한 범위는 opt-in mask로 제한하고 재료 거리는 실제 담수 경계로 분리했다. natural-04/05에서는 같은 입력에 진흙70칸이 생겼다. 최종 프로브는 이 사례에서 진흙이 실제 생기는지 단언한다.
- `native-shore-temperate-none-01`은 OFF일 때도 blend 함수가 두 번 호출되어야 한다고 검사한 하네스 오류. 제품은 효과 단계를 아예 예약하지 않는 것이 정상이다. none-02는 이 미예약과 최종 terrain의 bypass 동일성을 확인한다.
- 모든 실패/진단 폴더를 보존했다. 최종 판정은 위 표의7개 폴더만 사용한다.

## 범위와 직접 확인 문장

이번 작업은 물가 표면과 문답 일관성을 개선한다. 물 윤곽 자체의 미관, 기존 강/호수의 실제 윤곽을 모델이 참조하는 기능, 전 바이옴·모드 조합, 최종 식물의 완전 동일성은 검증하지 않았다. 요청한 큰 물줄기가 기존 강을 덮는 상위 계획 문제까지 해결됐다고 하지 않는다.

최신 **MapGen AI [DEV]**에서 아래 정도만 확인하면 된다. 기존64개 목록을 전부 재실행할 필요는 없다.

1. 온대림: `작은 자연스러운 연못 하나 만들어 줘. 주변 물가도 자연스럽게 해 줘.`
2. 같은 맵: `자동 바닥·식생 조화 효과만 꺼 줘. 연못 모양은 유지해.`
3. 사막: `작은 자연스러운 연못과 모래 물가를 만들어 줘. 나머지 사막은 유지해.`
4. 취향 문답: 탁 트인 공간 → 지금 있는 물만 → 독특한 풍경. 새 산/큰 물을 강요하는 후속 질문이 나오는지 확인.
5. 취향 문답: 작은 연못 → 특별한 물가. 후속 설명이 작은 연못 규모를 유지하는지 확인.
