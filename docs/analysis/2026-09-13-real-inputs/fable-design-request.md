# MapGenAI 실제 맵 이미지 경로 — 한정 설계 자문

읽기 전용 Claude Fable 자문. 한국어로 간결하게, 파일 수정/다른 모델 위임/자격증명 접근 금지. 아래 소스와 이미지 외 탐색을 확대하지 말 것.

사용자는 사람이 봐도 산·물·평지 배치를 읽을 수 있는 합리적인 맵 참고 이미지를 가져온다고 가정한다. 주 입력: RimWorld Map Preview, 웹의 탑다운 지형 지도, GPT/Gemini가 그린 RimWorld 맵 이미지. 팔레트 손그림/필수 범례로 제한하지 않으며 임의 풍경사진의 숨은 공간 재구성은 범위 밖. API 모델은 Gemini3.8과 하위2.5 선택을 고려. 이전 simple fixture IoU를 이 실제 사용 품질로 확대하지 않는다.

현재 generic vision 응답 = 최대32 label polygons, 각64vertices, C#가128² rasterize. 이미지 soil은 elevation을 보존하고 mountain/water만 바꿈. 실제 이미지 전처리와 모델 응답을 benchmark 중이며 결과는 아직 없다.

읽을 소스(저장소 F:/Projects/Rimworld/active/mapgen_ai):
- dev/Source/ImageInput/ImageInterpretation.cs
- dev/Source/ImageInput/ImageTextureCodec.cs
- dev/Source/ImageInput/ImageMapData.cs
- dev/Source/UI/Dialog_ImageMap.cs
- dev/Source/Patches/GenStepPatches.cs 의 초기 image layer 적용 부분만

실제 기준 이미지(직접 볼 수 있으면 Read로 확인):
- docs/assets/before.png (원본 Map Preview)
- docs/assets/ref_examples/landform_07.jpg (산 협곡+강, 작은 설명 inset 포함)
- docs/analysis/2026-09-13-real-inputs/ai-map.png (이번 실제 이미지 생성 결과, 탑다운 지형)

질문:
1. 이미지가 이미 명확한 Map Preview일 때 모든 경계를 모델이 좌표로 다시 쓰는 방식의 대안은? 코드가 색상/영역을 추출하고 모델은 의미를 붙이는 방식과 현재 polygon을 비교하되, 복잡한 새 ML 의존성·아무 사진 지원으로 범위를 넓히지 말 것.
2. 제한된 색 Map Preview와 질감 있는 AI 맵에 같은 보완을 적용할 수 있는지, 가장 작은 구현+실측 비교를 제안.
3. 현재 soil 고도 보존은 기존 특징 보존에는 유리하나 이미지의 평지가 생성 타일의 원래 산으로 채워질 수 있다. 이미지 전체 지형 기반과 사용자의 추가 SDF를 함께 지키는 명시적 상태/UX 규칙은?
4. 실제 원본→해석 격자→생성 Map Preview 세 단계에서 과장 없는 합격/실패 근거를 무엇으로 남길지.

알 수 없는 실제 모델 성능은 단정하지 말고, 제안과 코드에서 확인한 결함을 구분. 1000단어 이하.
