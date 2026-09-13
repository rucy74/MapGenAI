# 텍스트 연속 편집·공간 배치·고대 위협 확장 검증

2026-09-14. 복구 태그 `dev-text-baseline-2026-09-14`가 가리키는 f9f6aaa 이후, 사용자가 승인한 복합 요청 검증 → 공간 관계 → 구조물 종류 확장을 완료했다. **이미지OFF·Fable/Claude 호출0회**, 기존 월드 연결 보존 정책을 유지했다. 모든 텍스트 요청이나 모든 모드 구조물을 지원한다는 뜻은 아니다.

## 변경

- **연속 복합 요청:** 물을 실제 용암으로 바꾸고 폐허 개수를 조절한 뒤, 섬/해자 이동·윤곽 수정·재료 변경·환경 밀도·참조 도형과 구조물 삭제를 이어 요청하는 검증을 추가했다. 다른 항목의 보존과 Undo까지 확인했다.
- **공간 배치:** 실제 강·물·산·영역 안쪽 경계에서의 거리와 동서남북 방향, 건물 사이 최소 간격을 검사한다. 단순 폐허는 손상된 벽 패턴까지90도 단위 회전한다. 기준이 없거나 전체 면적을 놓을 공간이 없으면 실패를 알린다.
- **고대 위협:** 같은 위치 검사 뒤 게임 기본 사원 생성기를 실행한다. 실제 지붕·내부·전리품·경고와 난이도 규칙을 사용한다. 미리보기는 주황색 예약 테두리, 내부는 실제 맵에서 생성한다.
- **안내/저장:** 현재 상태와 AI 요청 형식에 종류·거리·방향·회전·간격을 포함한다. Scribe/프리셋/Undo에서 유지하며 자연 생성 밀도와 위치 지정을 구별한다. 설명서·계획·개발 지도·DEV 패키지 안내를 갱신했다.

## 최종 검증

제품 DLL **4d02a121cd5b58cef52f9454c73650d622815dba8646b47a400d571db8a2e8df**에서 아래 결과를 확인했다. 서로 다른 단계의 오래된 DLL 검사나 초기 실행을 이 합계에 더하지 않았다.

| 검사 | 실제 결과 | 근거 |
|---|---|---|
| 순수 회귀 | 100 PASS / 0 FAIL | [로그](final-tests.log) |
| 고대 위협 | 5완성맵+배경Preview, 56검사 | [결과](ancient-native-verified/result.json) |
| 공간 관계 | 9완성맵+배경Preview, 88검사 | [결과](spatial-final/result.json) |
| 연속 복합 요청 | 6완성맵, 56검사 | [결과](compound-final/result.json) |
| Odyssey/VLE 지리 조건 | 6완성맵, 60검사 | [결과](geography-final-vle/result.json) |
| 재료/영역/기존 폐허/이미지 중단 | 11완성맵+배경Preview, 117검사 | [결과](regions-final/result.json) |

최종 DLL의 **37완성맵과3개 배경 Map Preview,377검사**가 통과했다. 이 수에는 의도적으로 공간 부족/대상 없음으로 위치 지정 구조물을 생성하지 않는 맵도 포함한다. 검사에는 실제 TerrainDef/벽/지붕/컨테이너, 독립적인 거리/면적/회전 계산, 상태·월드·BaseGen 공유 객체 보존, 실제 Dialog/Undo, 실제 Scribe와 이미지 UI·직접 적용 차단/저장 보존이 포함된다. 전체 마우스 조작이나 장시간 플레이 테스트를 대신하는 것은 아니다.

이번 새 실제 모델 호출은 Gemini3.8 Flash의 한국어 **18요청**이다: [복합8](compound-r1/compound-results.json), [공간5](spatial-provider-r1/spatial-results.json), [고대 위협5](ancient-provider-r1/ancient-results.json). 변경13·제한 안내5, 의도/다른 상태 보존 모두 통과했다. 복합은8연속 요청, 나머지는 독립 요청이다. 기존10개 영역 요청을 포함해 **28개 저장 응답**을 최종 DLL의 실제 Dialog/Undo에 재생했다. 최종 프롬프트에서28개를 새로 호출했다는 주장은 아니다.

제품 빌드 `dotnet build dev/Source/MapGenAI.csproj --no-restore -v minimal`은 오류0·기존CS7035경고1. runtime/provider probe 빌드 오류0. 회귀 명령은 `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`다. 최종검증 후 제품 DLL을 재빌드하지 않고 패키징한다.

실제 게임은 RimWorld1.6.4871 rev591, Harmony, Map Preview 및 설치DLC를 사용했다. 지리 검사는 추가로 VEF/Vanilla Landmarks Expanded를 사용하며, 실제 로드된226정의를4문맥에서 평가한 것은226개 맵을 생성한 것과 다르다. 기존 모드 검색 메타데이터/중복 packageId 경고, Mono fallback, VLE한국어번역3오류가 로그에 남아 있다. 의도적 잘못된 이미지 복구·부적합 요청·공간 부족 안내와 성공 검사를 구별했다. 모든 외부 모드 경고를 해결했다고 주장하지 않는다.

## 저장·복구·남은 범위

첫 단계 b4e21c1, 공간 구현49e4d54, 끝줄 정리09e25d1을 dev에 각각 저장했다. 고대 위협과 최종 근거는 다음 독립 커밋에 저장한다. 기준 태그는 이동하지 않는다. 원본main/v1.6/dist/일반 MapGenAI 설치와 사용자 설정은 별도로 보존하며, 새 ZIP은 `outputs/mapgenai-text-expansion`에 제공한다. 패키지 manifest와 설치 receipt가 최종 커밋/동일DLL/기존DEV8파일을 연결한다.

고대 위협은 가로·세로15~20, 계획별1~2, 전체최대4개, 회전0만 지원한다. 내부 특정 적/전리품, 임의 모드·퀘스트 구조물, 정확한 등간격 배치·곡선 방향 자동 회전은 후속이다. 공간 부족은 spawn 전에 전체 실패하지만 native 생성기 내부의 예상하지 못한 중간 실패에 대한 전체 물체 rollback은 미지원이다. 실제 맵의 다른 건물로 최종 위치가 미리보기와 달라질 수 있다.

모든 seed/모드 조합/오래된 사용자 세이브/RimWorld1.5/전체 공급자·언어·하위 모델은 미검증이다. 이미지 재활성화, Fable 재호출, main병합·정식Release·Steam게시를 이번 작업에 포함하지 않는다.

## 상세 및 실패 이력

- [연속 복합 요청](compound-report.md): 첫 harness의 필드 기반 JSON serializer에 익명 객체를 넘긴 결과누락, Dictionary로 수정하고 원본 보존.
- [공간 관계](spatial-report.md): 강 타일에도 mutator0을 요구한 테스트 선택 오류 및 지역변수 이름 충돌 수정. 원래 실패 파일 보존.
- [고대 위협](ancient-report.md): 초기41검사 → 최종독립검사46 → 모델 실제재생 포함56. 초기결과와 최종결과 분리.
- 공간 단계에서 EOF빈줄 검사 실패를 확인하기 전에 commit/push가 이어진 도구 오케스트레이션 실수를09e25d1로 수정했다. 이후 검사는 exit code를 확인한 뒤 다음 저장을 진행한다.
- [사용자 설명서](../../description-ko.md), [후속 계획](../../text-first-plan-ko.md).

실게임 재현은 runtime probe 빌드 후 `tools/runtime-probe/launch.ps1`의 `-Ancient -AncientResponses <ancient-provider-r1>`, `-Spatial -SpatialResponses <spatial-provider-r1>`, `-CompoundResponses <compound-r1>`, `-FeaturePolicy -Landmarks`, `-TextRegions -TextResponses <2026-09-13-text-regions/provider-r1>`를 각 `-Language Korean -Output <새폴더>`와 실행한다. 고유 임시 모드/새 프로필만 생성·자동정리하며 사용자 세이브를 로드하지 않는다.
