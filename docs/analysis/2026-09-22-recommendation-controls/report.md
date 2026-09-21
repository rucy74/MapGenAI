# MG28 추천 취소·재추천와 채팅 공간

사용자가 보류했던 두 피드백(마음에 드는 후보가 없을 때 선택 강요, 그림 때문에 채팅이 좁음)을 재개했다. 기준은 dev `7fb184eb1c953616823bf37f23306681097f2e63`, 복귀 태그는 `dev-before-recommendation-controls-2026-09-22`다.

## 변경

- **다시 추천받기 / New suggestions**: 이전 후보를 닫고 실제 맵 상태를 기준으로 다른 추천을 요청한다. 후보 수정 모드로 전달하지 않으며 즉시 생성 응답도 허용하지 않는다. 대화의 선호·이전 후보는 새 요청의 참고로 남긴다.
- **선택 안 함 / Select none**: 후보와 진행 중인 후보 요청을 취소한다. 현재 맵, 월드 메타데이터, 되돌리기 스택, 입력 초안을 보존한다. 늦은 API 응답은 요청 세대로 무효화하고, 미리보기 결과는 해당 후보 소유권으로 폐기한다.
- **그림 접기 / Hide previews**: 로컬 그림만 숨겨 채팅 공간을 확보한다. 다시 펼칠 때 같은 그림을 쓰며 새 모델 요청·재렌더를 하지 않는다.
- 창 기본 크기 900×760, 화면 가장자리 40 여백에 맞춰 제한, 드래그 이동. 내부 높이를 배분할 때 채팅 240을 우선하고 아주 작은 창에서는 그림을 숨긴다. 적용·관리·입력·하단 버튼은 유지한다.
- 한영 시작 안내, 사용자 설명서, 패키지 설명을 갱신했다. `추천 취소`, `다시 추천해 줘` 등 명확한 단독 요청도 처리한다. 후보 번호를 지목한 수정이나 실제 맵 편집은 기존 경로를 사용한다.

재추천은 새 모델 요청이므로 API 사용량이 발생한다. 선택 취소, 그림 접기·펼치기, 저장된 후보 선택은 추가 모델 호출이 없다. 추천 최대 3개, 생성 엔진·재료·도로·온천 정책은 유지한다. 이미지 입력 OFF 및 Fable 중단을 유지했다.

## 검증

- `dotnet run --project dev/Source/Tests/TextToMap.Tests.csproj --no-restore`: **178 PASS / 0 FAIL**. 단독 취소·재추천과 번호 지정 수정 구분, 화면 크기별 공간 배분, 기존 지형/도로/온천 관련 회귀를 포함한다.
- `dotnet build dev/Source/MapGenAI.csproj --no-restore`: 오류 0, 기존 버전 문자열 경고 1. 최종 DLL SHA-256 `7f29ee77ae97e903882b96177b6c79af401dc501848ac54bb6cf369478e4117c`.
- `dotnet build tools/runtime-probe/RuntimeProbe.csproj --no-restore` 후 격리 게임 실행. `launch.ps1 -CandidatePreviews docs/analysis/2026-09-19-landscape-recommendations/provider-final-ko -Language Korean -Render -Output .../native-final-ko`, 영어는 `provider-final-en / English / native-final-en`. **한국어 109 + 영어 109 = 218 검사 통과**.
- 실제 편집 뒤 추천 준비·취소·재추천 시 맵/월드 메타데이터/Undo 전체 내용/초기 스냅샷/입력 초안 보존. 진행 중 응답과 이미 큐에 도착한 응답의 취소, 늦은 미리보기 폐기, 확대창 무효화, 후보 수정·선택·Undo 확인.
- 각 언어에서 원래 후보 9개 + 수정 후보 1개의 선택 전후 Map Preview **62,500 픽셀 전부 일치**(20비교). 접기·펼치기는 원본 텍스처와 모든 픽셀을 유지하며 추가 생성/모델 호출 0.
- 기본·접힘·620×520 창 캡처를 보존했다. 한영 기본/접힘 및 작은 한국어 창을 직접 확인했고, 버튼·입력창은 잘리지 않는다. 실제 썸네일 표시 픽셀 검사도 통과했다. [실제 화면](gallery.html).
- 초기 한국어·영어 실행은 CPU 픽셀/상태 검사 각109개가 통과했지만 작은 그림이 회색이었다. `native-ko/en`에 실패 화면을 보존했다. 기존 Texture2D의 빈 축소 레벨을 사용하던 문제를 후보 텍스처의 mipmap 할당 제거로 수정했다. 최종 캡처의 각 카드에서 여러 지형 색이 확인되며, 수정 전후 원본 지도 PNG 18장은 바이트가 동일하다. **초기 상태 검사 통과를 UI 통과로 간주하지 않았다.**
- `python -X utf8 tools/evaluate_recommendation_controls.py`: [평가 결과](evaluation.json). 새 외부 모델 요청 **Gemini 0 / Fable 0**, 새 완성 맵 검사 0. 검증용 모드 폴더는 모든 실행 종료 후 제거했다. 산출물 검사 기록: `artifact-audit.json`.

## 범위와 다음 확인

이번 검증은 저장된 응답과 통제된 가짜 지연 응답을 실제 RimWorld 1.6/Map Preview에 재생한다. 새 추천의 다양성·미관은 모델 응답에 달려 있으며 모든 문장이나 전체 사용자 모드 조합을 보장하지 않는다. 이번 변경에는 다리/터널, Geological Landforms 생성기 연동, 이미지 입력 재개를 포함하지 않았다. 기존 MG23 네이티브 Mono 충돌은 원인 미확정 상태를 유지한다.

[직접 테스트할 순서](manual-tests-ko.md) · [사용자 설명서](../../description-ko.md)
