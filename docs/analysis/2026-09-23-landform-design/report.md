# 짧은 요청을 위한 자연지형 기본 배치 개선 — 로컬 DEV

사용자 요청: “그래 해봐. 음 근데 사람이 아주 디테일하게 얘기하지 않아도 디자인 측면에서 이쁘개 되는 방향이 좋아”.
기준 `b75900d8a6e3b0a59cf4cd944d64a3d3b5629cec`, 작업 브랜치 `dev-natural-landforms`,
복귀 태그 `dev-before-landform-design-2026-09-23`를 작업 전에 생성했다.

## 변경

열린 분지·넓은 골짜기·산기슭의 새 배치에 `layout:organic`을 추가했다. 넓은 주 평지 주변에
크기가 다른 산줄기를 배치하고, 작은 골짜기에는 곡률과 폭 차이를 주었다. 골짜기 입구는 주 평지와
부드럽게 이어지며 분지 산벽은 내부 폭을 넓혀도 충분한 두께를 남긴다. 반복되는 톱니나 반듯한
홈으로 보였던 초기 시안은 수정했다. 기본 구조를 먼저 잡고 세부 굴곡을 더하므로 사용자가
세부 윤곽을 모두 설명할 필요가 줄어드는 방향이다. 모든 변형의 미관을 보장하는 기능은 아니다.

추천에는 타일에 어울리는 주된 지형과 넓은 정착 공간을 먼저 정하고, 같은 간격의 반복·중앙 자원
원판·불필요한 호수/자원 묶음을 피하도록 안내했다. 이미 편집한 맵은 기존 배치를 보완한다.
추천이 무조건 새 세 종류만 고르는 것은 아니며, 작게 바꾸는 편이 좋은 타일은 그렇게 제안하도록 했다.
실제 공급자가 이 지침을 따르는지는 이번 연결 실패로 검증하지 못했다.

`shape_ops add`로 새 landform을 만들면 organic이 기본이다. 새 전체 `elevation_shapes` 배열에는
모델이 organic을 명시하도록 안내했다. 저장/전체 스냅샷의 생략 값과 `classic`은 종전 수식을 유지한다.
기존 맵을 열거나 폭/위치를 수정하는 것만으로 새 배치로 전환하지 않는다. 명시적인 자연스러움 변경
요청에서만 기존 자연지형의 layout을 바꾸도록 했다. layout은 Clone/Scribe/프리셋/되돌리기에 보존한다.

정확한 원/별/하트/도넛, composite, 좁은 통로, 도로의 생성 수식은 변경하지 않았다.
같은 지형 ID의 내부 폭, 분지 출구, 70% 채움, 폐허 참조도 유지한다. 생성 수식은 전역 RNG를
사용하지 않고 variant로 결정하며, 지형 세부 생성에 추가 모델 호출은 없다. 프롬프트는 지침만큼 늘었다.
이미지 입력/Fable은 OFF. Geological Landforms의 생성기 통합 또는 전체 지형 구현은 아니다.

## 검증

최종 제품 DLL SHA256: `fd1327816da1a171f347bed08577cd7aac27d898954d70edb0b8104b93cfd284`.

- 제품 빌드 오류 0, 기존 경고 2(NuGet 취약성 조회 네트워크 제한, wildcard 버전).
- 전체 순수 회귀 **256 PASS / 0 FAIL** (`pure-d4.log`). 기본1200+경계300개를 재추첨 없이 검사했다.
  고의로 평지를 끊은 사례를 실패로 검출하는 대조군도 포함한다.
- 독립 test-engineer: 새 seed160개/종 × 3종 × 3조합 = **1440개**, 실제 혼합 높이 BFS에서 실패0.
  같은 **9,216,000좌표**에서 현 null/classic 둘 다 이전 frozen DLL과 influence/elevation 비트값 및
  floor가 정확히 같았다. 두 경로 합18,432,000비교. [독립 결과](independent-review.md).
- 최종 DLL native-n20/n21/n22: 산기슭/골짜기/분지 각5변형, **16검사씩 PASS**.
- 최종 n23: 내부 확대·70% 비옥토·폐허·강/해안·완성 맵 **23 PASS**.
  완성 맵의 적격 **16,545칸 중11,581칸(약70%)**이 실제 비옥토이며, 폐허1개/벽19개가 생성됐다.
  계획된 평지의 산 칸0. 미리보기와 실제 맵은 원래 건물 등 보호 대상 때문에 적격 수가 다를 수 있다.
- 강 **9,082셀**, 바다 **9,213셀**은 원래 타일과 완전히 동일했다. 일반 연못까지 모두 불변이라는 뜻은 아니다.
- 최종 n24: 기존 빠른/문답 추천·취소·편집·되돌리기·배치 방어 재생 **36 PASS**.
  nographics 실행에는 종전 비교 기준과 같은 텍스처 atlas 관련 NullReferenceException 문구1917건이
  있다. GUI/무오류 실행으로 보고하지 않는다. 그래픽을 켠 n20~n23/n25에는 예외 문구0건이었다.
- 최종 n25: 종전 classic 배치의 내부 편집·물·완성 맵 **23 PASS**.
- 이전 추천6맵 + 종전 배치/강/해안7맵 = **13장,812,500픽셀 차이0**.
  비교기에 1픽셀 변경을 넣어 정확히1 차이를 검출했다. 총 최종 네이티브 검사 **130 PASS**.
- 별도 코드 검토의 후속 편집 감사12개 PASS. 검토자는 Codex이며 Claude 자문으로 보고하지 않는다.

실제 Map Preview 텍스처와 완성 맵을 생성한 증거다. API 생성 이미지나 그림 목업이 아니다.
5변형은 같은 native 월드/맵 시드에서 variant만 다르게 만든 것이며 서로 다른 월드 시드5개는 아니다.
최종 그림: `native-n22/open_basin-23.png`, `native-n21/winding_valley-23.png`,
`native-n20/foothills-23.png`. 통계 및 DLL별 실행 구분은 [summary.json](summary.json)에 있다.

250² 기본 표본에서 새 지형 계산 약 **72~164ms**, 전체 미리보기 약 **0.25~0.48초**,
분지+토양+폐허 완성 맵 약 **5.34초**를 관측했다. 한 컴퓨터의 소수 표본이며, 이전 단순 수식보다
계산은 늘었다. API 응답 속도·FPS·메모리 최적화라고 주장하지 않는다.

## 실패·수정·미검증

- 최초 pure 빌드의 지역변수 이름 충돌을 수정했다. 초기 native 실행 스크립트의 PowerShell
  `$Profile`/`$profile` 충돌도 전용 이름으로 수정한 뒤 실행했다. 이전 실패 로그는 유지했다.
- provider harness 기본 출력 경로의 기존 test apphost가 잠겨 한 번 빌드에 실패했다.
  알 수 없는 이전 프로세스를 종료하지 않고 새 출력 폴더와 `UseAppHost=false`로 빌드했다.
- 실제 요청 `예쁘고 정착하기 좋은 맵 추천해 줘.` **1회 시도 → HttpRequestException, 응답0**.
  추가 유료 재시도나 우회를 하지 않았다. 모델 품질·실제 문장의 성공률은 미검증이며,
  네이티브 검증용 상태를 실제 모델 응답으로 취급하지 않는다. 키/요청URI는 오류 출력에서 제외했다.
- 독립 검토에서 테스트용 DesignBench가 폭/방향 외의 몰래 바뀐 설정을 놓칠 수 있음을 발견했다.
  허용 필드만 바꾼 예상 상태와 전체 직렬화를 비교하도록 수정했다. 정상2+부정대조10개 통과.
  실제 모델 응답이 없었으므로 잘못된 live PASS를 기록한 사례는 없다. [재검토](independent-review-d2.md).
- n13~n19는 중간 DLL의 시안이다. 최종 n20~n25와 혼합해 최종 바이너리 결과로 세지 않는다.
- 소스/도구/작성 문서는 `git diff --check`를 통과했다. 원본 Player.log와 캡처 프롬프트의
  후행 공백은 증거 보존을 위해 수정하지 않았으며 전체 raw diff에서는 경고가 남는다.
- 순수 연결 검사는 중앙 배치·중립 바닥 기준이다. 이동으로 잘린 지형, 원래 물/특징과 결합한
  최종 동선, 모든 바이옴/모드 조합, GUI 배치, 장기 플레이, 주관적 미관은 별도 확인해야 한다.

## 설치와 인계

원본 repo는 `F:/Projects/Rimworld/active/mapgen_ai`, `dev`,
`1e33b8dcbc174c6c97ec41431ea0379ae300080c`로 작업 후에도 clean이다.
현재 쓰기 범위 밖이라 원본 통합·게임 DEV 설치·GitHub push는 하지 못했다.
설치DEV `9dbedd51…578`, 일반/dist `17c35e94…549` 해시 불변을 확인했다.
완성 패키지·누적 patch·completion receipt는 작업 폴더의 `outputs/mapgenai-landform-design`에,
공유 트랙 갱신 대기는 기존 `outputs/mapgenai-recommendation-feedback/pending-track-update.md`에 둔다.
일반 배포/Steam/main은 유지한다. 다음 단계는 원본 상태를 재확인하고 DEV만 통합/설치한 뒤
[짧은 요청 테스트](manual-tests-ko.md)를 실제 공급자와 사용자 취향으로 확인하는 것이다.

## 재현 명령

```powershell
dotnet build dev/Source/MapGenAI.csproj --no-restore -m:1 -p:UseSharedCompilation=false --nologo -v:q
dotnet build tools/provider-probe/ProviderProbe.csproj --no-restore -m:1 -p:UseSharedCompilation=false -p:UseAppHost=false --nologo -v:q -o <fresh-output>
dotnet <fresh-output>/TextToMap.Tests.dll
dotnet <fresh-output>/ProviderProbe.dll . <audit-output> design-audit-selftest
# 확인된 소유 게임 복사본에서 사용하지 않은 Run 번호로만 실행:
./tools/natural-landform-probe/run.ps1 -Run n26 -Set followup -Graphics -Layout organic -Evidence 2026-09-23-landform-design
python tools/natural-landform-probe/summarize_design.py
```

summary 명령은 기록된 n13~n25를 검사한다. 독립 harness 실행법은 해당 보고서/README 참조.
제품 재빌드는 wildcard 버전 때문에 동일 소스라도 DLL 해시가 바뀔 수 있다.
