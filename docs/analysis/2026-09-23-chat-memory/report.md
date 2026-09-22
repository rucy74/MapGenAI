# MG31 — 대화 연속성·입력 예산·도로 후속 수정

2026-09-23. 사용자 승인 범위는 DEV 구현/검증/설치. 일반판, dist, main, Steam 공개 배포는 보존한다. Fable 및 이미지 입력은 OFF다.

## 문제와 변경

실제 사용자 실패는 십자가 흙길을 요청한 뒤 “아니 완전한 십자가 모양. 십자가가 만나는 점이 맵의 중심”에 산을 새로 만든 응답이었다. 성공한 ApplyEdits에서 `_llmContext`를 삭제하므로, 현재 맵 설정은 보내도 앞의 대화와 대상 의도가 빠지는 경로가 있었다.

- 성공 후 원문 이력을 유지하며 최종 응답과 실제 적용 결과를 `APPLIED`로 기록한다. 실패는 `NOT APPLIED`, Undo/프리셋은 `STATE REPLACED`. 후보 선택은 명시적 선택과 모든 적용 revision을 기록한다. 최신 맵 상태가 정본이며 미선택 추천은 맵이 아니다.
- `ConversationMemory`는 원문을 바꾸지 않는 전송용 checkpoint를 만든다. 전체 입력80%에서 오래된 부분을 요약하고55%목표, 최근3유저턴 원문 유지. 원문은 창 메모리에 남으며 Reset/창닫기로 종료한다. 프리셋/세이브에 대화를 새로 영속 저장하지 않는다.
- 요약 실패/취소는 이전 원문·checkpoint 보존. 확인된 예산 초과에서는 편집 호출을 멈추고 안내한다. 고정 지침이 대부분을 차지하면 작은 옛 prefix를 매번 유료 요약하지 않는다. 압축할 수 없는 현재 상태/최신 요청을 잘라 목표 비율을 맞추지 않는다.
- Gemini는 models.get의 모델별 입력 한도, 안전 여유, near-threshold countTokens를 사용한다. input-only 한도에 출력 토큰을 중복 차감하지 않으며 모델 출력 한도로 maxOutputTokens도 제한한다. context-window 방식 공급자용 계산은 출력 여유를 먼저 제외한다. OpenAI 호환/로컬은 용량 메타데이터를 임의로 가정하지 않는다.
- 미확인 한도는32,768 추정 기준. 이 경우 추정만으로 기존 짧은 요청을 막지 않는다. 고급 `conversationInputBudget` 0=자동, 1,024이상 수동 지정. 알려진 모델 한도를 수동 값으로 늘리지는 못한다. 토큰 추정은 정확한 tokenizer가 아니다.
- `EditIntentGuard`는 명시적 도로-only 요청/확실한 짧은 후속에서 다른 종류 편집을 거부하고 기존 StructuredChat의1회수정 기회를 사용한다. 한영 복합 요청/새 주제/모호한 과거 대상은 과잉 차단하지 않는다. 모든 revision이도로뿐인 적용 후보도 대상이 된다. 재시도 입력에도 예산을 검사한다.
- 정확한 중앙 십자/직선 요청은 `route:direct`+기존ID와 중앙 좌표 사용을 안내한다. 도로·다리·지형 생성기 자체는 변경하지 않았다.

API 근거: [Gemini 모델 한도](https://ai.google.dev/api/models), [토큰 계산](https://ai.google.dev/api/tokens), [OpenAI models 조회](https://developers.openai.com/api/reference/resources/models). 한도 정책·80%/55%는 이 모드의 선택이며 공급자 표준이 아니다.

## 검증

- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **221 PASS / 0 FAIL**. 기존183개와 공급자10/대화16/의도12 포함. 기존의 도로·다리·기하·영역·특징·저장·추천 검사를 함께 실행했다. [전체 출력](regression-final.log).
- 제품/일반 probe/provider probe 빌드 오류0. 제품은 기존 CS7035 버전문자열 경고1. [제품](build-final.log), [probe](probe-build-final.log), [provider](provider-build.log).
- 격리 RimWorld의 실제 SendText→StartChat→StructuredChat→PollResponse→ApplyEdits 파이프라인: 최초KO23/EN23, 최종KO25 검사. 성공 이력/잘못된산응답원자적거부/1회복구/기존ID·seed·비도로상태유지/Undo/실제요약/Reset중늦은요약폐기/수정후후보선택을 확인한다. fake transport가 factory에서 모든 유료 호출을 차단한다. [최종KO](native-final-r2-ko/result.json), [EN](native-en/result.json).
- 실제 Gemini3.8-flash 한영 각각4연속응답: 흙길십자→중앙정확화→북동쪽호수→이전도로만돌길. 8개모두첫응답으로의도·현재상태검사통과. 선택모델의실제사용가능입력예산1,027,604토큰조회. [KO](provider-ko/result.json), [EN](provider-en/result.json). 캡처한실제게임prompt와매턴현재상태사용; 임의짧은가짜지침만으로지형응답을검증하지않았다.
- 별도실제요약시험은35메시지에인위적으로작은입력예산을지정했다. KO5,565→894, EN4,866→755토큰; 초반설계이름청솔/Bluepine과호수·돌길조건회상성공. 보수적조각입력추정때문에각4회요약+1회회상호출. 전체새모델생성호출은지형8+요약8+회상2=18회이며 countTokens/metadata HTTP는별도다. 전체청구토큰합계는수집하지않았으므로마지막호출의usage를전체비용으로표시하지않는다.
- 같은기록응답으로새DLL완성맵9+Preview9, 기존일반판DLL완성맵9+Preview9비교. **18결과의terrain layer hash와모든그림픽셀동일**. 이 중자연산/물끝점등에막힌8사례는양쪽모두실패하며원래층을보존했다. 실패를십자가생성성공으로계산하지않는다.
- 선언한마른평지fixture2개로도로기하분리검증: 완성맵2+Preview2에서두직선이정중앙125칸에서직교,경로전체통행·연결,세계지도연결보존. 왼쪽실제월드강에서42물칸에나무다리를설치하고원래물보존. [검사102개](evaluation.json), [실제결과](controlled-native-r2/suite-result.json).
- 완성맵비교는DLL f371459d…(대화만변경), controlled맵은6f850228…(후보revision영수증추가), 최종DLL9dbedd51…는applied_plan의도로대상인식을추가하고221회귀/실제dialog최종재실행했다. 세버전간생성기소스변경없음. 실행별DLL/probe해시는각launch.json에있다.

## 재현 명령

저장소 루트에서 실행하며 runtime 테스트는 별도 프로필/별도 월드/소유 marker가 있는 임시 모드를 사용한다. `launch.ps1`은PID를반환하며결과가완료될때까지기다려야한다.

```powershell
dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore
dotnet build dev/Source/MapGenAI.csproj --no-restore --nologo
dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore --nologo
powershell -NoProfile -ExecutionPolicy Bypass -File tools/runtime-probe/launch.ps1 -ChatMemory -Output <fresh-output> -Language Korean
dotnet run --project tools/provider-probe/ProviderProbe.csproj --no-build -- . <fresh-provider-output> conversation <native-capture> ko
python tools/evaluate_chat_memory.py docs/analysis/2026-09-23-chat-memory --evaluate
```

Provider 명령만 실제 설정된 Gemini API를사용하며ignored `docs/dev_config.json` 키는산출물에포함하지않는다. 입력fixture는같은스크립트의 `--prepare`/`--controlled`로구성한다. 실제생성은 `launch.ps1 -LandformSuite <cases.json> -LandformReplies <response-folder> -Render`다. 기존DLL에는 `dotnet build ... -p:DefineConstants=BASELINE_PROBE -o tools/runtime-probe/bin/baseline`로새인터페이스를제외한probe를빌드하고 `-InstalledRelease -ProbeAssembly <baseline-probe.dll>`로실행한다.

## 실패 원본과 검증 한계

첫baseline실행은probe의새IContextBudgetClient가구DLL에없어ReflectionTypeLoadException으로시작하지못했다. PID·프로필·명령행을확인해그소유프로세스만종료하고호환probe로재실행했다. [원본](maps-baseline/Player.log), [소유확인](maps-baseline/stopped.json). 첫controlled실행은dryfixture에강방향을남겨세계강조건검증에거부됐다. [원본](controlled-native/suite-result.json). 제품정책을약화하지않고fixture를정정했다.

사용자표현전체·다국어전체·모든모드조합의정확한해석은보장하지않는다. 뜻을잘못파악해도도로종류안에머무는오류, 요약의세부누락, 보호된산/깊은물로인한배치실패는가능하다. current-state가너무크면요약만으로해결할수없다. 유저전체모드구성/기존MG23 native Mono충돌해결/미관품질향상은이번검증결론이아니다. 실행로그에는기존probe초기화/설치모드메타데이터진단도있으므로로그무오류를주장하지않는다.

시작기준e1f9265와 `dev-before-chat-memory-2026-09-23` 태그를보존한다. [직접테스트문장](manual-tests-ko.md) · [모드사용설명](../../description-ko.md).
