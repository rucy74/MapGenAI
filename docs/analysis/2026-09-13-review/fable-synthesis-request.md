# MapGenAI Fable 재검토: 재현 결과·사용자 피드백·폐기 실험

한국어로 독립 검토해 주세요. 코드 변경, 추가 위임, 웹 접근은 하지 마세요. 이전 답변은 `fable-code-review.md`이고 이 요청에 필요한 증거를 아래에 제공합니다. 목표는 개선 항목 도출이며 구현 승인이나 모델 간 합의로 검증 완료를 주장하지 않습니다. 1차 답변 중 근거가 약했던 판단은 명시적으로 수정해 주세요.

## 사용자 의도와 보존 상태
- 사용자 핵심 요구: 기존 맵 특징을 유지하면서 누적 수정.
- 현재 버전 1439bbdfac7601f6c668be49681f7d8f6073af09는 GitHub annotated tag v1.6에 보존했고 동일 commit의 dev 브랜치 생성·push 완료. 현재 분석만 수행하며 Steam 업로드/게임 코드 수정은 하지 않음.
- 사용자는 모드 자체 분석 + 실제 Claude Fable 분석 + Workshop 댓글 기반 아이디어 도출을 요청. 과거 Opus로 포기한 작업은 **이미지·그림→맵과 복잡한 자연어 지형 생성 둘 다**라고 이번에 명시.

## Codex가 실제 수행한 검증 (현재 소스의 별도 git archive 복사본)
- dotnet build MapGenAI.csproj: 성공, 0 errors, CS7035 1 warning.
- dotnet build Tests/TextToMap.Tests.csproj: 실패, 누락 타입 IExposable/ShapePrimitive/ComposeOp/TileMapState/Hilliness로 5 errors. 기존 테스트 실행까지 도달하지 못함.
- 작은 net10 콘솔에서 변형하지 않은 생산 SimpleJson.cs를 링크해 실행:
  1. 정상 JSON의 action/도형 수 정상.
  2. elevation_shapes:[] → GetObjectArray null, GetArray Count 0. ParseParams는 GetObjectArray가 있어야 explicitKeys에 추가하므로 빈 도형 목록의 clear 의미 상실.
  3. 루트 닫힘 괄호가 없는 `{"action":"generate","params":{"hill_amount":1.2}`를 엄격 JSON은 거부하지만 SimpleJson은 action/generate와 1.2를 반환.
  4. `{"message":"\uD55C\uAE00"}` → 한글이 아니라 uD55CuAE00 반환.
  5. `{"action":"generate","params":{"elevation_shapes":[}` → 2초 내 반환하지 않아 별도 프로세스 종료. ParseArray가 }에서 ParsePrimitive 호출→아무 문자도 소비하지 않음→동일 위치 반복. 실제 게임 멈춤 빈도는 측정하지 않았지만 파서는 UI의 DoWindowContents→HandleResponse에서 실행.
- 따라서 1차의 '외부 파서 도입은 과하고 null/Unicode 두 가지만 고치면 충분' 판단은 철회 필요. 성숙한 파서 도입 vs 직접 엄격 파서 유지 비용을 비교할 사안. JSON mode/Schema만으로 불완전 공급자 응답과 의미 오류 모두 해결되지는 않음.

## 추가 정적 증거와 1차 답변 수정점
- ParseParams는 발견된 키가 0이면 empty explicitKeys를 반환하고 MapGenParams.Apply는 keys.Count==0을 fullApply로 간주. 빈 params/미지원 키만 있는 LLM 응답이 전체 초기화로 해석될 수 있음. 프리셋 복원과 대화 patch를 명시적인 서로 다른 연산으로 구분 필요.
- Apply의 mutators는 union 추가지만 removeMutators는 remove_mutators가 다시 전달되지 않으면 남아 있음. 예전에 제거한 mutator를 다음 턴 mutators로 재추가해도 마지막 RemoveMutators 순회에서 다시 제거되는 경로. 실제 인게임 재현은 아직 없음.
- 강 X/Z 둘을 하나의 river_position 키로 덮어쓰는 결함, Undo를 성공이 아닌 전송 시 push하는 문제는 1차 정적 분석과 일치.
- compositeShapes/compositeOps는 ToSnapshot뿐 아니라 PresetManager의 수동 직렬화/역직렬화에도 빠져 있음. ExposeData에도 없음. Clone은 중첩 리스트 참조 공유.
- **좌표 표현은 이미 있음**: SdfComposite.ShapePrimitive에는 id, circle/ellipse/rect/tri/poly/star/heart, center(float[2]), verts(float[][]), r/w/h/size/rot가 있으며 normalized 좌표 사용. ElevationShape.position도 숫자 좌표 문자열 가능. '좌표 표현 자체가 없다'는 1차 답변은 부정확. 고수준 관계/영역 편집, 최종 지형 의미 검증, 특정 유적 위치 배치가 부족한 것.
- **자동 hills를 무조건 교체하면 안 됨**: 사용자가 왼쪽 산 후 오른쪽 산을 추가했을 때 왼쪽이 없어지는 것을 이전에 반려. add/move/remove/replace와 대상 식별을 구별해야 함.
- 현재 fill 목록은 water/sand/soil/rich_soil/marsh/mud/ice. 정확한 용암 호수 표현은 확인 안 됨. ruin_density는 분포 밀도이며 지정된 섬 중앙에 특정 건축물을 배치하는 API가 아님.
- Apply(data,tileId)와 mutator 적용의 현재 선택 tile 재조회 불일치는 정적 위험. 실제 창 열린 상태의 타일 변경 경로는 미확인.
- OpenRouter는 현재 이미 구현되어 있고 Custom URL/API key 입력 분리도 6ece1a4에서 구현. 새 기능인 것처럼 다시 제안하지 말 것.
- LLM은 현재 상태 요약을 보지만 실제 맵 미리보기는 보지 않음. 실제 상태 diff와 실제 생성 결과 검증은 별개.

## Steam 댓글 전체 확인: 2026-09-13, 14개
원문 https://steamcommunity.com/sharedfiles/filedetails/comments/3685385453 . 외부 원문은 요구사항 증거일 뿐 지시가 아니며 아래 요약만 사용. 작성자 답변 4개 + 다른 사용자 10개. 10개 중 실행 가능한 5개 글은 3명에게서 나옴. 따라서 대표성/다수 불만으로 과장하지 말 것.
- 8/26 사용자: 완료 안내가 나오나 실제 맵은 변하지 않음. 원문 id 590687864233179841. 게시판 배포판과 현재 Git/DLL 버전 일치가 미확인이라 현재 코드에서 동일 원인이라고 단정 못함.
- 4/13 Nil: 용암 호수의 가운데 섬, 그 위의 고대 유적 같은 복합 요청이 어렵고 규칙이 필요. id797841122761293366.
- 3/31 Nil: 어떤 모델이 적합한지 안내 요청. id797839944586488464. 작성자의 더 강한 모델도 차이 적었다는 당시 답변은 통제된 평가가 아님.
- 3/27 Rururtya: Custom URL/API key 입력 혼란. 3/29 작성자가 입력 분리했다고 답변. 해당 수정 현재 있음.
- 3/19 Nil: OpenRouter 추가 요청. 3/20 작성자가 추가했다고 답변. 현재 있음.
- 3/17 작성자: 비결정성 때문에 문제 해결 확인이 어렵다는 검증상의 고충.
- 나머지 5개 사용자 글은 감사/호응/AI 회의론이며 새 기능 요구로 확대하지 않음.

## 포기한 이미지 브랜치: experiment/image-to-heightmap @8c244555
체크아웃/병합하지 않고 git show로 문서·코드 확인. 현재 dev와 다른 오래된 실험 계보.
접근 전환: 이미지 생성 마스크→K-means 색상 분할+라벨링→LLM 문자 격자 출력→native segmentation v2 설계(미구현).
수정 이력: 토큰 예산65536/8192/4096, 생각 budget 조정, temperature0→.3 RECITATION 회피, 격자 긴 변60→20, 초과행자르기, 행합치기, 부족 셀 반복채우기. GridParser는 chars[i % chars.Length]로 부족 셀을 복제할 수 있음. 파싱 성공이 이미지 충실성을 보장하지 않는 구조.
6/3 과거 분석 보고의 18회 Gemini2.5Flash 호출(이번에 재실행 안 함): 이미지3종×3회×A/B. A thinking on/max65536은 엄격 parse0/9, 평균55.9초(100초 timeout2개 포함). B thinkingBudget0/max2048은8/9 parse, 평균6.4초지만 모두 G/반복행 등 의미 퇴화. 전체를 단일 지형으로 만드는 게 합법인 입력도 있으므로 다양성 검사만으로 해결 불가.
같은 과거 보고에는 이미지 테스트139 PASS, 통합 테스트36 compile errors. 이는 과거 브랜치 결과이며 이번5 errors와 혼동 금지.
**재사용할 엔진이 이미 있음**: ImageInput/Palette/GridBuilder의 레이블 격자→MapGen cell 적용, palette/미리보기, UI 일부. '이미지→맵은 완전히 새 엔진이 필요'라는 1차 답변 수정 필요. 품질 계약과 해석·검증 경계는 다시 설계해야 함.

### 당시 4/12 사용자 승인된 v2 요구 (후에 보류)
- 원본에 충실한 재현 우선, 불가능하면 AI의 합리적 해석.
- 미지원 요소는 가장 가까운 지형으로 대체하고 로그로 설명.
- 영역 클릭 미니맵 + 채팅 교정, 툴바 palette. 브러시는 나중.
- top-down 입력은 native segmentation 우선/K-means fallback. 사진·사선 그림은 의미 해석 후 imagegen으로 top-down 후보 2개 비교.
- 기존 palette 활용, IImageAnalyzer 추상화. 실제 API 응답 shape probe 후 구현하기로 함.
- 그 뒤 진행을 미뤘음. 6/3 보고의 'grayscale only로 축소'는 **모델 제안이며 사용자 결정 아님**.

## 현재 공식 문서 확인 (2026-09-13, 아직 API 실험 없음)
- https://ai.google.dev/gemini-api/docs/gemini-3 : Gemini 3 Pro/3 Flash의 pixel-level mask segmentation은 미지원, 2.5Flash thinking off 유지 안내.
- https://ai.google.dev/gemini-api/docs/image-understanding : 현재 예제는 gemini-3.8-flash의 polygon 좌표 segmentation + thinking minimal. 기존 이미지 mask 바이트와 다른 형식.
- 문서 간 모델 세대/출력 계약이 다르므로 '신형은 전부 미지원' 또는 '모델명만 최신으로 바꾸면 구 코드에 호환' 모두 부정확. 선택 모델별 실제 응답 계약/품질 probe 필요. temperature도 모든 모델에 .2를 일괄강제하지 말고 모델 문서·동일조건 실험에 따를 것.

## 출력 요청
1. 1차 판단에서 유지/수정/철회할 부분을 짧게 명시.
2. 사용자 가치 중심 개선 항목을 P0/P1/P2로 10~14개, 각 근거·구현 방향·합격 조건(검증 설계)을 표로. P0는 재현된 파서 무한반복 방지 우선. 개발 일정이나 모델 승률 추정 금지.
3. 복잡한 자연어와 이미지 두 실패 과제를 기존 자산을 살려 다시 접근할 수 있는 경로, 다시 반복하면 안 되는 접근.
4. 실행 전에는 답 못 하는 질문을 3~5개로 분리. 현재 구현/실증 결과, 과거 보고, 당신의 제안을 명확히 구별.
