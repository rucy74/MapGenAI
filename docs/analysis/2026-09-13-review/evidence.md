# 분석 근거와 재현 방법

2026-09-13 · 현재 검증과 과거 보고를 구분한 기록. 개선 결론은 [분석 보고서](analysis.md)에 있다.

## 기준과 변경 범위

- repo: `active/mapgen_ai`, GitHub [MapGenAI](https://github.com/rucy74/MapGenAI).
- commit: `1439bbdfac7601f6c668be49681f7d8f6073af09`, tree `a5f1edb3cbfb2e701e9e67165938b60934e60fea`.
- 보존: annotated tag `v1.6`, 개발: `dev`/`origin/dev`. main은 `6ece1a4` 유지.
- 과거 실험: 로컬 `experiment/image-to-heightmap` / `8c244555deb3097c606c6523beb8dcaabab25bde`. 원격에 보존됐다고 확인한 브랜치가 아니며 이번에 push하지 않았다.
- 분석 중 게임 소스·프로젝트·DLL 변경 없음. 문서와 재현 증거만 추가.
- 개발/배포/설치 DLL SHA256 모두 `371939200b78156211f0f8c658d88110d801f9cde633489e7af9fd33f48e9cd6` 유지. [manifest](evidence/manifest.json)

## 이번 실행 결과

`git archive HEAD dev`로 별도 작업 폴더에 기준 소스를 추출했다. 기준 소스의 프로젝트 설정은 RimWorld/Harmony 설치 DLL을 참조한다. 같은 환경 없이 일반 PC에서 그대로 빌드할 수 있다는 보장은 없다.

| 실행 | 위치 | 결과 |
|---|---|---|
| `dotnet build MapGenAI.csproj --nologo` | 추출본 `dev/Source` | exit 0, 오류 0, CS7035 경고 1. [로그](evidence/build-main.log) |
| `dotnet build TextToMap.Tests.csproj --nologo` | 추출본 `dev/Source/Tests` | exit 1, 오류 5, 경고 1. 필요한 타입 참조 누락. 테스트 실행 전 실패. [로그](evidence/build-tests.log) |
| `python -X utf8 run_probes.py` | `evidence`와 같은 파일 배치의 작업용 폴더 | probe 빌드/정상 실행 exit 0. 잘못 닫힌 배열은 2초 시간 초과 후 프로세스 종료. [결과 JSON](evidence/parser-probes.json) |

파서 재현은 .NET 10 콘솔이 **변형하지 않은 생산 `SimpleJson.cs`**를 링크한다. 사용자 게임에서 실행하지 않았으며, 결함 응답이 실제로 얼마나 자주 나오는지도 측정하지 않았다. 검사 원본 해시 일치는 manifest에 기록했다.

| 입력 | 엄격한 계약상 기대 | 이번 관찰 |
|---|---|---|
| 정상 generate + 도형 1개 | action과 도형 읽기 | 정상 대조군 통과 |
| `{"elevation_shapes":[]}` | 객체 배열의 명시적 비우기 표현 | `GetObjectArray` null, `GetArray` 길이 0 |
| `{"action":"generate","params":{"hill_amount":1.2}` | 루트 닫힘 누락 거부 | System.Text.Json 거부, 모드 파서는 generate/1.2 반환 |
| `{"message":"\uD55C\uAE00"}` | 한글 | `uD55CuAE00` |
| `{"action":"generate","params":{"elevation_shapes":[}` | 문법 오류로 종료 | 2초 내 반환 안 함, 외부 프로세스 종료 |

재현 파일: [runner](evidence/run_probes.py), [입력 코드](evidence/probes/Program.cs), [프로젝트](evidence/probes/ParserProbe.csproj), [원본 파서 사본](evidence/baseline/dev/Source/UI/SimpleJson.cs). `evidence` 폴더에서 위 Python 명령을 실행하면 자체 생성 bin/obj와 로그가 그 폴더 아래에 생긴다. Python과 .NET 10 SDK가 필요하다. 시간 초과 검사는 별도 자식 프로세스를 사용하므로 게임을 멈추게 하지 않는다.

## 정적 확인 근거

모든 아래 링크는 보존 commit을 가리킨다. 정적 경로의 존재와 특정 사용자의 실제 증상 원인을 구분한다.

| 쟁점 | 근거 | 판정 범위 |
|---|---|---|
| 배열 파싱 무진행 반복 | [SimpleJson:201](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/UI/SimpleJson.cs#L201), `ParsePrimitive:259` | `}`를 소비하지 못해 반복. 실제 파서 실행으로 시간 초과 확인 |
| 빈 patch/전체 복원 혼용 | [MapGenParams:326](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L326), [ParseParams:966](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/UI/Dialog_TextToMap.cs#L966) | 키 0개면 fullApply. 실제 월드 상태 변경 재현 미실행 |
| composite 복원 누락 | [ElevationShape:18](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L18), [ToSnapshot:798](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L798), [PresetManager:105](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/PresetManager.cs#L105) | 내부 도형·연산 미직렬화, Clone은 리스트 공유. 실제 재로드 화면 미검증 |
| 특징 제거→재추가 충돌 | [MapGenParams:424](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L424), 같은 파일 `ApplyMutatorsToWorldTile:563` | 이전 제거 목록 잔류 후 마지막 제거 순회. 인게임 미실행 |
| 강 좌표 연동 덮어쓰기 | [ParseParams:1009](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/UI/Dialog_TextToMap.cs#L1009), [Apply:404](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L404) | 한쪽만 명시해도 X/Z 둘을 쓰는 코드 |
| Undo 중복·응답과 실제 결과 분리 | [SendMessage:693](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/UI/Dialog_TextToMap.cs#L693), 같은 파일 `HandleResponse:782` | 전송 시 push, 성공한 상태 diff 미검증, 알 수 없는 action 명시 처리 없음 |
| 타일 인자와 실제 선택 불일치 | [ApplyMutatorsToWorldTile:563](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/MapGenParams.cs#L563) | 전달된 tile 대신 WorldSelector 재조회. 창 안에서 선택 변화가 가능한 경로는 미확인 |
| 공간 표현과 한계 | [SdfComposite:430](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/MapGen/SdfComposite.cs#L430), [RuinDangerDensityPatch](https://github.com/rucy74/MapGenAI/blob/1439bbdfac7601f6c668be49681f7d8f6073af09/dev/Source/Patches/RuinDangerDensityPatch.cs) | 프리미티브 좌표는 있음. 밀도 패치만으로 구조물 위치 지정은 안 됨 |

`null`을 정상 값으로 취급하는 파서 경로와 공급자 응답 추출 방식도 검토했으나 이 보고서의 별도 실행 검사에는 포함하지 않았다. current build는 설치된 RimWorld 1.6 DLL 기준이다. metadata의 1.5 지원 표기까지 검증한 결과가 아니다.

## Steam 전체 댓글 분류

수집: 2026-09-13 14:13:32 KST, [댓글 전용 페이지](https://steamcommunity.com/sharedfiles/filedetails/comments/3685385453). 14/14개 확인. 아래 날짜는 HTML title의 PDT 표시 기준, 원래 timestamp와 KST 변환은 manifest에 저장했다. 전체 원문은 보고서에 전재하지 않고 짧게 요약했다.

| 날짜(2026) | 작성자 | 분류·요지 | 원문 |
|---|---|---|---|
| 8/26 | 我是雷电将军的狗 | 완료 안내와 실제 변화 불일치 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_590687864233179841) |
| 4/13 | Nil | 호수·섬·유적의 관계 지정 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797841122761293366) |
| 3/31 | smartboy1122 | 작성자의 당시 모델 사용 경험 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797839944586499775) |
| 3/31 | Nil | 모델 선택 문의 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797839944586488464) |
| 3/29 | smartboy1122 | 작성자: Custom 입력 분리 수정 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797839675763123631) |
| 3/27 | Rururtya | Custom 설정 입력 혼란 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797839675763029869) |
| 3/21 | Nil | 감사 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797839165142136004) |
| 3/20 | smartboy1122 | 작성자: OpenRouter 추가 안내 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838866430321135) |
| 3/19 | Nil | OpenRouter 요청 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838866430230645) |
| 3/18 | CzarGopnik | 노력 인정·AI 회의론 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838866430128206) |
| 3/18 | Chicken | 호응 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838866430113591) |
| 3/17 | smartboy1122 | 작성자: 비결정성에 따른 검증 어려움 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838802067414851) |
| 3/16 | Kokorocodon | 관심·호응 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838547624338896) |
| 3/15 | 刀刀闪 | 호응 | [댓글](https://steamcommunity.com/sharedfiles/filedetails/?id=3685385453#comment_797838547624272916) |

저자 답변 4개 / 다른 사용자 10개. 구체적 사용자 요청 5개 중 OpenRouter와 Custom 입력 분리는 현재 소스에 구현돼 있다. 페이지의 숨겨진 공통 ‘제거됨/비호환’ 템플릿을 현재 게시물 상태로 해석하지 않았다. Steam 업로드본 DLL을 내려받아 현재 commit과 동일시하지도 않았다.

## 과거 기록의 범위

`git show experiment/image-to-heightmap:<path>`로 아래 원본을 읽었다. `history`의 파일은 해당 commit의 사본이며 그 안의 상대 링크는 옛 저장소 문맥을 가리킬 수 있다. 현재 분석의 새 결정으로 취급하지 않는다.

- [2026-06-03 검토](evidence/history/2026-06-03-macro-review-4axis.md): 과거 18회 API 비교와 이미지 테스트 139 PASS, 통합 테스트 36오류 기록. 이번 재실행 아님.
- [2026-04-12 승인 사양](evidence/history/2026-04-12-1900-spec-image_to_map_v2.md): 재현 우선/불가능하면 해석, 영역 수정, top-down 후보 비교.
- [2026-04-12 로그](evidence/history/2026-04-12-session.md): 승인과 후속 보류의 대화 맥락.
- 같은 commit의 `dev/Source/ImageInput/LLMSegmenter/GridParser.cs`: `chars[i % chars.Length]` 보정 경로.
- 같은 commit의 `dev/Source/ImageInput/Palette/GridBuilder.cs`: 레이블을 고도·비옥도 격자로 바꾸는 재사용 후보. 이번 실행 검증은 안 함.

## Fable 실제 자문과 반증 처리

다른 세션의 기록을 따라 `active/blueprint_ai/tools/design-lab/consult.py`를 수정 없이 실행했다. 이 호출기는 읽기 도구만 허용하고 요청 모델을 `claude-fable-5-1`로 고정한다. 기본 bridge의 모델을 Fable로 추정하지 않았다. 자문 입력에 필요한 코드와 기록만 제공했으며 API 키 설정 파일은 전달하지 않았다.

- 1차: [요청](fable-code-request.md), [답변](fable-code-review.md), [CLI 결과](fable-code-review.json).
- 2차: [요청](fable-synthesis-request.md), [답변](fable-synthesis-review.md), [CLI 결과](fable-synthesis-review.json).
- CLI의 `modelUsage`로 요청 모델의 실제 사용을 확인하며, 보조 Haiku 사용도 메타데이터에 남아 있다. 따라서 모든 내부 처리가 단일 모델로만 수행됐다고 표현하지 않는다.

| Fable 1차 의견 | 자체 근거에 따른 처리 |
|---|---|
| Undo/composite 저장/강 좌표 문제 | 현재 소스와 대조해 채택. 게임 실행 검증 범위는 제한 |
| 파서는 null/Unicode만 고치면 충분 | 무진행 반복과 잘린 JSON 수용 재현으로 반박. 엄격한 계약과 파서 선택 검토 |
| 좌표 표현이 없음 | 기존 SDF의 center/verts/회전/primitive ID로 정정 |
| 이미지는 전부 새 엔진이 필요 | 실험 브랜치 GridBuilder 등 재사용 자산으로 정정 |
| 자동 산 교체 | 기존 특징 추가 보존 요구와 충돌. add/move/remove 구별로 수정 |
| 강한 모델의 효과는 제한적일 것 | 구조상 병목 설명으로만 사용. 실제 모델 비교 결과로 주장하지 않음 |

2차 응답에서도 다음 세부 판단은 보정했다. 원문은 수정하지 않고 검토 결과를 여기에 남긴다.

- `removeMutators`를 매번 비우면 원본 타일 복원 후 삭제했던 특징이 되살아날 수 있다. 삭제 의도를 지속하는 최종 상태/제외 기록을 설계하고 재추가 시 충돌만 해소해야 한다.
- 자동 hills 누적은 코드 자체에도 있으므로 모든 경우에 LLM의 전체 목록 재출력을 필요로 한다는 설명은 과도하다. 반면 명시적 `elevation_shapes`는 현재 전체 교체이므로 그 경로의 보존 문제는 남는다.
- `[}` 재현 입력의 직접 경로는 `ParseArray:201`이다. `ParseObjectArray`와 `ParseNestedArray`의 유사 위험은 별도 정적 관찰이지 이번 입력의 실제 호출 경로로 보고하지 않는다.
- K-means는 색상 그룹을 만들 뿐 서로 다른 의미의 같은 색 영역을 항상 구별하지 못한다. 정답 레이블 입력의 결정적 변환을 기준선으로 삼고 이미지별 인식 품질을 따로 본다. 과거 승인 사양은 native segmentation이며 polygon은 이번에 확인한 최신 문서의 출력 형식이다.
- LLM 응답은 seed만으로 재현을 보장하지 않는다. 고정된 맵 생성 seed와 공급자가 지원하는 샘플링 설정을 구분하고 반복 결과를 측정한다. 상태 diff만 제공하는 검증 턴으로 최종 지형 정확도가 증명되지는 않는다.

## 외부 기술 자료 확인

- [Gemini 구조화 출력](https://ai.google.dev/gemini-api/docs/structured-output): JSON schema를 사용하는 현재 API 예제를 확인. 공급자 어댑터 검토의 근거이며 맵 의미 정확도를 증명하는 자료가 아님.
- [Gemini 3 가이드](https://ai.google.dev/gemini-api/docs/gemini-3): 명시된 3 Pro/3 Flash 픽셀 마스크 제약과 모델별 설정 차이를 확인.
- [이미지 이해·분할](https://ai.google.dev/gemini-api/docs/image-understanding#segmentation): 현재 3.8 Flash 예제의 polygon 형식 확인. 기존 실험의 mask 출력 계약과 동일하지 않으므로 실호출 probe 필요.

새 모델의 호출·가격·승률·실게임 품질은 이번에 검증하지 않았다. 모델 추천은 테스트 후 작성할 개선 항목이다.
