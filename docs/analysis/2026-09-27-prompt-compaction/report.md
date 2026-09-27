# DEV 프롬프트 비용 압축 — 2026-09-27

## 결론

최종 표본16개에서 **입력 토큰15.80% 감소**(한국어17.06%, 영어13.80%). 출력까지 현재 요금으로 환산한 표본 비용은 **12.76% 감소**했다. 최초20~35% 목표에는 못 미쳤지만, history/현재 상태/기능 카탈로그를 잘라내지 않고 얻은 절감이라 채택한다. 추가 라우팅·모델 교체·요약 호출은 도입하지 않았다.

제품 기준: `dev` / `e8c33ef`. 복귀 태그: `dev-before-prompt-compaction-2026-09-27`. 이번 변경은 DEV 전용이며 `dist`, 일반판/Steam 구독본, 공개 게시물은 바꾸지 않는다.

## 변경

- `TextRegionPrompt`, `RoadPrompt`, `RecommendationPlan.Rules`의 반복 설명과 긴 예시를 압축했다. 앞의 두 기술 규칙은 기존 추천 규칙처럼 영어 공통 문장으로 보낸다. 사용자에게 답할 언어와 게임 내 이름 표시 지침은 유지한다.
- 영역 비율·닫힌 고리·건조 통로·용암·구조물 범위/위치·도로/다리·추천과 적용의 구분을 유지했다. 카탈로그의 모드 지형 이름도 생략하지 않는다.
- 대화 기록과 압축 조건, 현재 맵 상태, 추천 후보 상태, JSON 파서, 생성 코드, 모델과 추론 설정, 이미지 입력 중단 상태는 변경하지 않았다. 요청 분류로 기능 설명을 생략하는 방식은 이번에 적용하지 않았다.

## 실측

Gemini 3.8 Flash의 응답 `usageMetadata`를 사용했다. 각 쌍은 동일 타일/카탈로그/현재 상태/대화 이력을 사용하며 세 규칙 블록만 달라진다. KO/EN 원본 런타임 타일은 서로 다르므로 언어 간 절대량 비교가 아닌 **동일 언어 쌍의 전후 비교**다.

| 표본 | 입력: 기존 → 압축 | 입력 절감 | 출력 포함 비용 절감 |
|---|---:|---:|---:|
| 한국어9개 | 157,815 → 130,896 | 17.06% | 14.27% |
| 영어7개 | 99,566 → 85,825 | 13.80% | 10.38% |
| 합계16개 | 257,381 → 216,721 | 15.80% | 12.76% |

기존 출력3,071 → 압축 출력4,242토큰으로, 특히 추천이 더 길어져 입력 절감률과 총비용 절감률이 다르다. 실제 요청별 출력/재시도는 달라질 수 있다. 계산은 2026-09-27 확인한 [Gemini 공식 표준 요금](https://ai.google.dev/gemini-api/docs/pricing) 입력$0.75/M, 출력·thinking$3.75/M(2026-12-31까지)을 사용하며 세금·환율·캐시 할인은 제외한다. 같은16표본은 $0.204552 → $0.17844825. thinking은 이번 기록에서0이며 항상0이라는 뜻은 아니다.

이번 개발 검증 전체는46유료 호출, 입력669,015·출력9,507토큰, 같은 단가의 추산 **$0.5374**. Fable/Claude 호출은 없다. 최초 비교24회 + 정해둔 추천 반복12회 + 최종 편집10회이며 자동 retry/repair/fallback은 사용하지 않았다.

## 검증과 실패 기록

- `dotnet build dev/Source/MapGenAI.csproj --nologo`: 성공, 기존 CS7035 버전 형식 경고1개.
- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **323 PASS / 0 FAIL**. 파서/상태/순수 생성 계산 회귀이며 모델의 모든 요청을 증명하지 않는다.
- 최종 DLL의 별도 게임 프로필 검사 **34/34**: 실제 Dialog 적용/Undo, 저장/로드, 현재 상태 포함, 기본 호수 생성, 실패 시 컨텍스트 복원. 최종 프롬프트의 세 규칙 블록이 모델 실험 입력과 일치함을 별도로 확인했다. 자연어 표본16개 전체의 게임 지도 렌더를 재실행한 것은 아니다.
- 최종 모델 편집10/10: 도넛, 내부70%, 짧은 후속 “절반만”, 용암+폐허3개 복합, 정확한 십자가 수정 KO/EN, 온천 특징, 호수 위치 변경 EN, 내륙 해안 요청 설명, 미적용 후보의 자연스러운 통로 수정. 상태 유지·요청한 수정·응답 언어를 검사했다.
- 최종 추천 KO/EN 각3회 **6/6**: JSON/파서, 서로 다른3후보, 지형 변화 포함, 금지한 전역 보너스·UndergroundCave 없음. 미관 자체나 전체 모드의 특징 호환성은 채점하지 않았다. 같은 반복의 기존 지침은5/6; 한 후보가 도로+Fertile뿐이라 지형 포함 검사 실패였다.
- **초안 실패를 보존:** 첫24호출은 기존12/12, 압축11/12. 압축 KO 추천이 region_fill의 필수 region_part를 누락해 실패했다(잘못된 값을 쓴 것이 아님). 허용값 inside|enclosed가 채움/구조물에 적용된다는 문장을 명시적으로 복원했다. 이후 추천6회와 최종 편집10회는 모두 통과. 이 소표본으로 앞으로 회귀가 없다고 보장하지 않는다.
- 검사기 자체 확인: no-op 응답을 넣으면9개의 편집/설명 검사가 실제로 실패한다. 깨진 캡처를 주면 프롬프트 교체기가 실패한다. Recommendation/candidate 검사는 이 no-op9개에 포함되지 않는다.

## 재현·증거

- [summary.json](summary.json): 실제 usage와 비용, 표본별 전후 결과. `python docs/analysis/2026-09-27-prompt-compaction/summarize.py`로 재집계(유료 호출 없음).
- [offline-token-comparison.json](offline-token-comparison.json): o200k 추정치. Gemini 과금 수치와 구분한다. `python tools/provider-probe/prompt_cost.py docs/analysis/2026-09-27-prompt-compaction`.
- `provider-pairs/`: 초안24회 원본 요청/응답/검사(실패 포함). `provider-recommendations-final/`: 최종 추천 쌍12회. `provider-edits-final/`: 최종 편집10회.
- `final-prompts/`: 비교에 사용한 동일 컨텍스트의 before/after 템플릿. 런타임 캡처는 baseline-runtime(KO), baseline-runtime-en(EN), final-runtime(최종KO). 최종 런타임의 타일은 원본과 다르므로 전체 파일 동일성 대신 세 규칙 블록을 비교했다.
- 실제 모델 검사를 다시 실행하면 비용이 든다: provider-probe 모드 `prompt-cost`, `prompt-cost-recommendations`, `prompt-cost-final-edits`. API 키는 기존 git 제외 `docs/dev_config.json`만 읽는다.

## 설치 경계

최종 DEV DLL SHA256: `45646072C2E758AD909ECAF9679C5586D3977C900E41AEC97B24CD607E9ECE8C`.
배포판/기존 설치본 SHA256: `0B582DA94E1E2420E6AEAB4932A5C3BEC171E48A5E6BCBE56D3785374CFBBC69`.
DEV 전용 설치 패키지는 워크스페이스 `work/mapgenai-packages/2026-09-27-prompt-compaction/`에 준비한다. 게임 실행 중에는 설치 DLL 교체를 보류한다. 일반판 승격/창작마당 게시 여부는 이후 별도 결정한다.
