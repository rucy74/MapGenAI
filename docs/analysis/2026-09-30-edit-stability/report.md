# MapGen AI — 편집 안정성 검증

2026-09-30 22:32 → 2026-10-01 00:05 KST / 작성: Codex / DEV 전용.

사용자 “순서대로 해 줘”에 따라 공개판 버튼 확인, MG42, MG41 순서로 처리했다. 공개 v1.7.2와 일반판·Workshop·dist·사용자 설정은 변경하지 않았다. 새 API/Fable/Claude 호출은 0회이며 실제 이전 모델 응답을 재생했다.

## 결과와 산출물

- [전후 캡처 19장 HTML](review.html): 실제 RimWorld/Map Preview 캡처만 사용한다. 클릭 확대 기능과 모바일 레이아웃을 포함한 단일 파일이다. 브라우저의 `file://` 보안 거부로 자동 표시·브라우저 렌더 검사는 수행하지 못했다. 다른 경로로 우회하지 않았다.
- 코드 기준: `a514160993a326daf5720156b38317fc83bb8683`. 구현 `eee7c39c7cf96486cc694608d9ed6d11f173564a`, 기존 Validate 호출 유지 `c9d3a2838f5ec241484ebf9e129fbf775220aec3`.
- DEV DLL SHA256: `6343F3116063E4C6DEA2CDEFA16B9914ECF9BDE401B13012FDB90ECE04004EF6`.
- 공개판 DLL SHA256: `9A792FAD321743582EE767548082B0AE72D23643D4F8E26358A42BEE5B1052B5` 유지.
- [회귀 345 PASS / 0 FAIL](unit-tests-final.txt), [실제 수정 전후 비교 35 PASS / 0 FAIL](comparison.json), [설치 DLL 기본 검사 31 PASS](installed-basic-runtime-final/result.json).
- [설치 영수증](installation-receipt.json), [최신 실행 DLL 해시](final-runtime-r9/launch.json), [설치 DLL 실행 해시](installed-basic-runtime-final/launch.json), [프로브 정리](installed-basic-runtime-final/cleanup.json).

## 1. 공개판 진입 버튼

공개 v1.7.2 DLL, 시작 지점 화면, Map Preview 툴바 OFF, 1920×1080/UI scale 1.5에서 OS SendInput 클릭으로 실제 Dialog_TextToMap이 열렸다. [결과](os-entry-runtime/result.json)의 `dialogOpenedByOsInput=true`, `syntheticClickCount=0`, `paidCallsThisRun=0`. 사람의 물리 마우스 검사와는 구분한다. 일반 사용자 프로필이나 다른 세션 게임을 사용하지 않았다.

## 2. MG42: 특징 수정과 미리보기

`FeatureInitStream`은 순수 추가의 기존 난수 격리를 유지하면서 삭제된 native 특징의 Init 난수 소비를 참조 맵/worker로 예약한다. Caves PostTerrain의 두 난수 소비도 보존한다. 참조 맵은 등록·완성 맵 지형 쓰기를 하지 않는다. 외부 worker와 지형에 따라 난수 소비가 달라지는 일부 PostTerrain은 안전하게 기존 생성 경로와 경고로 돌아간다.

- 기존 Caves를 삭제하거나 Cavern으로 교체하면 기준 DLL은 강 중심을 `[101,131]`에서 `[174,101]`로 바꿨다. 수정 DLL은 중심과 강 경로 1,692칸 해시를 미리보기·실제 생성 모두 유지한다. 원래 동굴을 제거한 뒤 새 RiverIsland가 먼저 생성되는 순서도 검사했다.
- 기준 해안 미리보기 물 칸은 13,225 → 11,213으로 바뀌었고 수정판은 13,225를 유지한다. 미리보기·실제 맵에서 Coast noise 62,500개 값이 동일하다. 동굴 변경으로 암석 지형이 달라지면 native Coast painting의 바위 보존 조건 때문에 최종 완성 맵의 물 칸 마스크까지 동일하지는 않을 수 있다.
- 편집 타일 A의 강 설정이 타일 B의 일반 미리보기에 새는 문제를 타일별 생성 컨텍스트와 River hook의 타일 확인으로 막았다. 기준 B 중심 `[158,104]` → `[212,104]`; 수정 B는 `[158,104]`와 전체 terrain hash 유지.
- 추천 후보를 native `genOrder`로 정렬한다. 수정 전 후보 River/HotSprings/Caves와 적용 Caves/HotSprings/River가 달랐다. 수정 후 둘 다 Caves/HotSprings/River이며 선택 전후 전체 지형 해시·62,500픽셀이 일치한다(다른 픽셀 0).
- 미편집 강·해안과 온천 순수 추가 4사례 × 미리보기·완성 맵 = 기존 생성 8결과가 이전 DLL과 전체 terrain/river/ocean hash·온천칸·강 중심까지 동일하다.

## 3. MG41: 강 이동과 산 내부 도로 경유점

실패했던 실제 모델 응답을 [fixtures](fixtures/)에 보존하여 production MapGenParams.ApplyPatch 경로로 재생했다. 모델을 새로 호출하지 않았으므로 새로운 자연어 해석 품질을 입증하는 검사는 아니다.

- 강 숫자 위치는 병합 후 강 방향에 수직인 축으로 해석한다. 자동 방향은 native GenerateRiverGraph와 같은 타일 연결 방향을 난수 소비 없이 읽는다. 기존 두 인자 Merge/다섯 인자 Validate 호출은 유지한다. 명시 X/Z, named 북/남/동/서는 독립 좌표를 보존하고 숫자와 명시 좌표의 혼합은 모호하므로 거부한다.
- 원문 `river_position: 0.85` 재생: 기준 `[174,101]` → `[212,101]`(동서), 수정 `[174,101]` → `[174,212]`(북쪽). 기존 도넛·호수·통로·섬 상태를 유지했다.
- 우회도로만 막힌 내부 경유점을 가까운 연결된 평지로 옮긴다. 끝점·이미 가능한 경유점·direct 모드는 그대로이고 산·물·건물을 지우지 않는다. 검색 범위는 짧은 맵 변의 12% 또는 도로 여유폭을 위한 최소 반경이다. 닫힌 지형/막힌 끝점/범위 밖이면 기존 실패 안내를 유지한다.
- 원문 도로 재생: 기준은 산 내부 경유점 때문에 길이 없었다. 수정은 미리보기·완성 맵 모두 177칸 경로·459칸 바닥의 흙길 1개, 막힌 경로 0칸, 오류 0개다. 통행 가능한 AncientHydrant(PassThroughOnly)를 장애물로 세던 최종 결과 검사도 수정했다. 소화전을 삭제하지 않았다.

## 재현 명령

저장소 `F:/Projects/Rimworld/active/mapgen_ai`에서 실행한다. 전용 런타임에는 `MAPGENAI_HEADLESS_OWNED` 표식이 있어야 한다. 새 출력 폴더를 사용하며 다른 게임은 종료하지 않는다.

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj -c Release
dotnet build dev/Source/MapGenAI.csproj -c Release
dotnet build tools/runtime-probe/RuntimeProbe.csproj -c Release
python -X utf8 tools/evaluate_edit_stability.py docs/analysis/2026-09-30-edit-stability/baseline-runtime-r5 docs/analysis/2026-09-30-edit-stability/final-runtime-r9 --output docs/analysis/2026-09-30-edit-stability/comparison.json
python -X utf8 tools/render_edit_stability_report.py
```

실게임 비교 실행은 launch.ps1의 `-RiverStability edit-stability`와 `-SourceDll`로 기준/최종 DLL을 각각 지정한다. 본 회차 실행 해시와 출력은 각 launch.json에 있다. 설치 검증은 다음 실제 실행을 사용했다.

```powershell
& tools/runtime-probe/launch.ps1 -GameRoot 'C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-headless-runtime' -Output 'F:/Projects/Rimworld/active/mapgen_ai/docs/analysis/2026-09-30-edit-stability/installed-basic-runtime-final' -SourceDll 'G:/SteamLibrary/steamapps/common/RimWorld/Mods/MapGenAI-Dev/Assemblies/MapGenAI.dll' -NaturalShapes -Language English
```

31개 설치 검사에는 실제 Scribe 저장/복원, Dialog 적용/거부/Undo, paused image 입력 거부, 기존 Unity 이미지 데이터 호환, 자연 모양의 실제 생성이 포함된다. 이미지 입력 기능을 활성화한 검사가 아니다. 모든 기본 기능의 새 자연어 호출·사용자 조작을 전수 검사했다고 주장하지 않는다.

## 설치 및 남은 한계

`work/mapgenai-packages/2026-09-30-edit-stability/`의 build.json과 Install-MapGenAI-Dev.ps1으로 예상 이전 DLL·DEV packageId·해시를 확인하고, G: 일반 게임이 없는지 설치 직전에 다시 확인했다. DEV만 백업 후 교체했다. 최초 45646072… → A9F6… 설치 영수증은 [r8 영수증](installation-receipt-r8.json), 최종 A9F6… → 6343…는 [최종 영수증](installation-receipt.json)에 있다. 설치 소유 프로브는 [ArchivedProbes로 정리](installed-basic-runtime-final/cleanup.json)했다. 다른 트랙 게임, 사용자 Config, 모델 설정은 그대로다.

외부 모드 특징의 삭제/교체 및 지형 의존 PostTerrain 예약은 미지원 조합이 남는다. 임의 모드 조합·모든 자연어 입력·Map Preview 창 드래그 후 실제 버튼 조작·사람의 물리 마우스 검사는 미검증이다. 공개 게시 승인이나 일반판 업데이트를 한 것으로 해석하지 않는다.

## 중간 실패와 증거 구분

- `baseline-runtime`: edit-stability 인자를 launch에서 절대경로로 바꿔 시나리오가 인식되지 않았다. 경로 마지막 이름으로 비교하도록 고쳤다.
- `baseline-runtime-r2`: 평지에 Cavern을 넣는 부적합한 테스트였다. 임시 Mountainous+Caves native 기준으로 수정한 r3 이후를 사용한다.
- `final-runtime-r4`: probe namespace 오타로 빌드 실패 후 stale probe가 실행됐다. 실제 응답 재생 검증에서 제외한다. 이후 빌드 성공 여부를 확인하고 새 DLL로만 재실행했다.
- r5 강 이동은 auto 방향이 legacy X fallback으로 남아 실패했다. native 방향 조회를 추가한 r6 이후는 북쪽으로 이동한다.
- r6/r7 도로는 만들어졌지만 AncientHydrant 1칸 오진이 남았다. impassable edifice 판정을 적용한 r8/r9는 오류 0이다.
- `road-diagnostic`는 생성 완료 뒤 해제된 MapGenerator.Elevation을 읽어 실패했다. 생성 중 기록한 r2 진단이 정본이다.
- r8은 첫 전체 PASS, r9는 최종 public Validate overload 보존 DLL의 전체 PASS다. 최신 기본 검사 정본은 installed-basic-runtime-final이다. 그 외 실행은 개발 과정 자료로 보존하며 최종 성공 근거로 섞지 않는다.
- HTML 자동 열기는 브라우저 보안 정책에서 file://를 거부했다. 다른 브라우저/로컬 HTTP 등 우회로를 사용하지 않는다. 정적 자원·캡처 바이트·문자 인코딩만 확인했다.

RimWorld 1.6.4871 native 생성 코드와 Map Preview 생성 루프를 로컬에서 참조했다. 타사 decompile 코드는 work 패키지에만 두고 제품 저장소에는 포함하지 않았다. Player.log와 production prompt 원문은 줄바꿈·의도된 trailing space를 수정하지 않고 각 실행 폴더의 .zip에 보존한다. Git diff 검사를 위해 실제 프롬프트 내용을 바꾸지 않았다.
