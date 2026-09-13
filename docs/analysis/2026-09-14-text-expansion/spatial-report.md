# 실제 지형을 기준으로 하는 구조물 공간 관계

2026-09-14. dev-text-baseline-2026-09-14를 보존하고 복합 요청 검증 b4e21c1 다음 단계로 구현했다. Fable/Claude0회, 이미지OFF 유지.

relation은 실제 강/물/산 바위/region 안쪽 경계를 대상으로 min_distance/max_distance 및 동서남북 조건을 적용한다. 거리는 가장 가까운 건물 칸과 기준 지형 사이의 유클리드 거리다. 전체 면적은 기존 안전/영역/경계 검사도 만족해야 한다. 기준 지형 부재와 공간 부족은 위치 지정 batch를 생성하기 전에 실패한다.

spacing은 건물 사이 최소 빈칸 수, rotation은0/90/180/270도다. 실제 벽의 손상 패턴과 출입구까지 회전하며 폭/높이도 교환한다. 서로 다른 계획의 간격도 함께 검사한다. 같은 간격으로 배치하거나 곡선 방향을 자동으로 따라 회전하는 기능은 아니다.

- [97회귀](spatial-tests.log) PASS. 독립 전수 거리 계산과 결과 비교, 대상 없음, 강 동쪽/최소·최대 거리, 큰 건물의 중심 대신 가장 가까운 칸 검사, 최소 간격/공간 부족 원자성, 내부 구멍 경계, 저장/복사/부분 수정/잘못된 관계 거절.
- 제품 빌드 오류0, 기존CS7035경고1. 검증 DLL **cd2c023ea243210c02f7c378adac5f9092265de037eb428df68c7b72c1c285fb**.
- [네이티브9맵 + 배경 Map Preview88검사](spatial-native-verified/result.json) PASS. 강 양쪽, 산 서쪽, 섬 안쪽 가장자리, 호수 북쪽, 없는 강, 네 방향 회전. 실제 TerrainDef/암석에서 기준 좌표를 다시 수집하여 거리·방향을 독립 계산했다. 벽 생성과 회전 패턴 전체, 최소 간격, 실제 Scribe, 상태/월드 보존을 확인했다.
- [Gemini3.8 한국어5요청](spatial-provider-r1/spatial-results.json): 변경4·없는 강 안내1, 의도/보존 모두 PASS. 이 원문을 최종 네이티브 Dialog에 재생하고 Undo 및 완성맵을 검증했다.
- [실제 미리보기](spatial-native-verified/spatial-preview.png): 섬 가장자리 폐허3개 계획. 다른 자연 건물에 따라 실제 맵에서 최종 위치가 달라질 수 있으므로 생성 때 다시 검사한다.

실패 이력: spatial-native-r1은 테스트 타일 선택에서 “mutator 없음”을 강 타일에도 요구해 대상을 찾지 못했다. 강 자체가 지형 특징을 가지므로 강 타일 선택을 별도로 고쳤다. r2의9맵78검사를 통과했고 모델 재생을 추가한 verified에서88검사를 확인했다. 재생 harness의 지역변수 original 이름 충돌1건을 beforeReply로 수정했다. 이들은 제품 공간 계산 실패와 구분한다.

재현: dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore; probe빌드 후 tools/runtime-probe/launch.ps1 -Spatial -SpatialResponses <공간관계응답폴더> -Language Korean -Output <새폴더>. 모델 도구는 spatial 모드이며 native 캡처 폴더를 사용한다. 전체 모드 조합·오래된 사용자 세이브·모든 표현을 검증한 것은 아니다.
