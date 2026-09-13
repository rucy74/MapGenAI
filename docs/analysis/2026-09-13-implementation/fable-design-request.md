# MapGenAI dev 구현 설계 자문

사용자가 분석에 이은 추천 방향 구현을 승인했습니다: “그래 추천하는 방향으로 fable이랑 잘 논의하면서 해줘봐”. 한국어로 읽기 전용 설계 검토를 해 주세요. 추가 위임·파일 수정·API 호출은 하지 마세요. 필요한 생산 코드만 읽고 설정 파일/API 키는 읽지 마세요.

repo F:/Projects/Rimworld/active/mapgen_ai, branch dev, base1439bbdf. 이전 분석은 docs/analysis/2026-09-13-review/analysis.md 및 evidence.md. 사용자 핵심은 기존 특징 보존이며 이미지·그림→맵과 복잡한 자연어 둘 다 재개 대상입니다. 전면 재작성 없이 기반부터 단계적으로 구현합니다.

이번 먼저 결정할 설계:
1. SimpleJson 자체 파서 교체/강화. 정상 유니코드·빈 배열·중첩 verts 유지, 잘림/오류/중복키/과도중첩/비유한 숫자는 명시 실패. 기존 public getter 인터페이스 유지 가능한 wrapper. net472 게임에 Newtonsoft.Json 도입(테스트13.0.3사용) vs bounded strict parser 최소유지 비교. JSON.NET은 기본 문법이 관대하므로 '완전 strict JSON'이라고 과장하지 말 것. 실제게임Managed Newtonsoft 존재 여부는 조사 중. 보존DLL은 태그/dist에 고정, dev만 빌드.
2. 단일 TileMapState + pure reducer MapStateEditor.Merge(existing, patch), 대화 ApplyPatch와 RestoreSnapshot 명시 분리. empty explicitKeys가 fullApply가 되는 추론 제거. 누락/null/empty 배열 계약(누락 유지, null 오류, 빈배열명시clear). UI ParseParams를 독립 parser로 추출해 생산 경로 그대로 테스트.
3. mutators는 지속 add/remove 상태: remove누적, add시해당remove해제; 매턴remove비우면 원본복원때 부활하므로 금지. 동일요청add/remove충돌은거부. 카테고리겹침 무조건clear가 기존특징보존과충돌하므로 실제배타제약만검증하고경고. tileId끝까지 전달.
4. composite의 deep clone + preset/snapshot/Scribe 직렬화 공통화. 기존 save키 유지, 새 optional 필드로만확장. snapshot은정규state의정확복원(clamp나autohills재추가X). 프리셋 구포맷읽기유지.
5. 도형 stable id + shape_ops add/update/remove. hills는추가로유지하되 move/remove는명시ID사용. elevation_shapes전체교체는레거시호환으로명시가이드. state텍스트에 composite내용포함. 이구조는후속inside/center_of 제약편집용.
6. UI는변경전statecapture후성공시만Undo등록, no-op/error는이력없음. 전후diff실제출력; world적용/preview실패전파(파라미터변경과최종맵일치는별도). 창닫기·취소 후늦은응답적용방지(CancellationToken+request id).
7. Tests 현재누락타입5errors. pure core를링크해직접검증하고기존MdpApplyTests복구. testshim은Verse저장호출 compile용이며게임저장실증으로주장금지. 게임검증은격리프로필프로브재사용가능성조사예정(다른세션실판게임종료금지).

확인할 파일: dev/Source/UI/SimpleJson.cs, UI/Dialog_TextToMap.cs, MapGen/MapGenParams.cs, TileMapState.cs, PresetManager.cs, SdfComposite.cs, MapGenAIWorldComponent.cs(실제위치찾기), LLM/세클라이언트, Tests프로젝트/셈.

요청: 위설계의가장큰회귀위험5개, 지금제일작은안전한구현경계, 구save/undo/preset호환계약, 반증가능한회귀테스트목록을제시해 주세요. 구현의요지를모호하게동의하지말고위험한제안은거부하세요. 특히 category삭제와snapshot복원에관한판단에소스근거를대주세요. 1단계기반을완성한후이미지/관계기능을이어붙일예정이며전체미래기능설계를확정할필요는없습니다.
