# Claude consultation

Requested: claude-fable-5-1

Observed modelUsage: ["claude-haiku-4-5-20251001", "claude-fable-5-1"]

검토 결과: 지정 범위에서 실질적인 P0/P1 결함은 찾지 못했습니다. 아래는 확인한 근거와 P2 수준의 메모입니다.

**확인한 정합성**
- 캡처/복원 좌표 매핑이 `ImageMapData.Apply`와 동일합니다. `cell.x*width/Size.x`는 정수 나눗셈이라 `width-1`을 넘지 않아 Math.Min 없이도 범위 밖 접근이 없습니다.
- 캡처는 이미지·SDF·클램프 적용 후 두 경로(도형 없음 조기 반환, 일반 경로) 모두에서 호출됩니다. `HasParams`와 `MapGenParams.ImageMap`이 같은 frozen 스코프 상태를 읽으므로 캡처 대상 이미지와 Apply된 이미지가 불일치할 여지가 없습니다.
- 복원은 `ImageMap==map` 검사로 중첩 스코프·다른 맵을 배제하고, N 셀은 건드리지 않습니다. `Prepare`가 대상 메서드 부재 시 패치를 건너뛰어 비 Odyssey 환경에서도 안전합니다.
- generation-trace는 stage 전 구간 추가 0/누락 0이고, result.json의 mountainExtra 98은 ground 셀 암석으로 사용자 설명과 일치합니다.
- cleanup.ps1은 경로 상위 디렉터리·이름 패턴·ReparsePoint·양방향 marker·PID 이름/커맨드라인 일치를 모두 검증한 뒤에만 삭제하며, 실행 중이면 15분 대기 후 종료하지 않고 포기합니다. cleanup.json은 정상 제거 영수증입니다.

**P2 메모 (수정 불필요, 문서/문구 수준)**
- `Dialog_ImageMap.cs:128` 안내문은 "옵션을 끄면 토양은 기존 높이를 유지"만 말합니다. 실제로 끄면 이미지의 산·물 칸도 mutator 복원 대상에서 빠지므로 Valley 같은 랜드폼이 이를 덮을 수 있습니다. 체크박스 라벨은 이를 함의하지만 하단 설명은 토양만 언급해 절반만 설명합니다.
- 캡처 시점이 이 모드의 `GenStep_ElevationFertility` Postfix 안이라, 같은 GenStep에 뒤이어 붙는 다른 모드의 Postfix가 만든 고도 변경은 nonN 셀에서 복원 시 지워집니다. 이미 "외부 모드 조합 한계"로 기록한 범주에 속합니다.
- `cleanup.ps1:11`은 PowerShell 자동 변수 `$profile`을 덮어씁니다. 스크립트 스코프라 동작에는 문제가 없지만 이름 충돌이 읽기를 방해합니다.
- cleanup 프로세스 자체가 종료되면(재부팅 등) 임시 모드가 남습니다. launch.ps1에 재실행 안내가 없다면 README 한 줄로 충분합니다.