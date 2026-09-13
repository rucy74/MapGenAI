# Fable: 기반 구현 반대 검토
사용자가 추천 방향으로 Fable과 논의하며 구현하라고 승인했습니다. 소스 수정/위임/외부 API 호출 없이 한국어로 검토하세요. 고정 스냅샷 cwd=C:/Users/choco/Documents/Codex/2026-09-13/new-chat-2/work/mapgenai-phase1-snapshot . 실제 repo는 후속 개발 중이므로 이 스냅샷만 읽으세요.

기준 구현의 핵심: SimpleJson을 48-depth/1MiB bounded strict grammar로 교체, null/빈배열/duplicate/Unicode/잘림 처리. Newtonsoft를 실제 게임에 추가하는 비용(게임Managed에없음, 기본문법관대/배포결합) 대비 기존작은파서 전체문법을 닫는 방식 선택. 동의가 아니라 구체적 결함을 찾아주세요.
TileMapState 중심 MapStateEditor Merge 분리, ApplyPatch/RestoreSnapshot API분리. mutator제거누적+재추가제외해제, 강X/Z독립, auto hills는명시hills에서만작동. composite deepClone, optional compositeJson Scribe키추가, 버전2 presetstate직렬화, legacy parser읽기.
UI는 RequestGate cancellation/reset/close 뒤늦은응답무효화, 성공한상태변경만Undo(nullable이전state포함),실제diff표시, 스냅샷복원통합, 저장성공여부체크. ProviderResponse는공급자envelope를실제JSON파싱/finishReason검증. cancellation token 클라이언트전달. 자동fallback은config클라이언트만mainthread캡처,worker가설정수정안함.

검증: 메인net472 build오류0/기존CS7035경고1. 실제productionparser/reducer/Apply/codec linked 테스트25 PASS, 그중생성입력3000개문법검사. 원래4개재현검사는수정전FAIL→수정후PASS. 모든테스트파일새로읽어 assertion이틀린동작을숨기는지도검토. 실제게임Scribe저장/로드/새provider호출/UI클릭미검증.

집중파일: UI/SimpleJson.cs, MapGen/MapStateEditor.cs, MapParameterParser.cs, MapStateCodec.cs, MapGenParams.cs, UI/Dialog_TextToMap.cs, LLM/RequestGate.cs, ProviderResponse.cs; test/CoreRegressionTests.cs 및 MdpApplyTests.cs (모두dev/Source하위).
중요미완료: stable shape id와shape_ops 및관계편집/이미지경로는다음단계. 현재같은categoryclear는바닐라Def/배타규칙조사후수정할예정. 기존hillsnone→자동shape제거도id/auto표식과같이보완예정. 현재프롬프트composite필드명(compositeShapes/Ops vs shapes/compose)다음단계에서통일필요. LastApplyWarning은state저장성공후world/preview실패를알리는임시경로,최종runtimegate에서다룰예정.

요청: (1)실제재현가능한우선P0/P1오류최대8개 (2)각최소수정안과실패테스트 (3)사용자기존특징보존/취소/호환관점의누락. 이미명시한후속미완료와지금생긴회귀를구별. 모델합의/빌드만으로완료선언금지. 심각한게없으면범위내없다고말하되근거없는칭찬은불필요.
