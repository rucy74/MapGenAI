# MapGenAI 후속 검토: 실제 생성이 이미지 평지를 다시 산으로 덮는 원인

읽기 전용 자문. repo F:/Projects/Rimworld/active/mapgen_ai dev85886c5 이후 미커밋. 사용자 입력 범위는 읽기 쉬운 실제 Map Preview/탑다운/AI 맵이며 일반 풍경 복원은 제외.

당신의 이전 fable-design-review 조언 중 원본색상그룹+마스크AI분류, 명시적 image replaceElevation 옵션을 구현했다. 새로운 이미지 true, 기존 Scribe/defaultfalse. 코드 ImageMapData.Apply 는 M=.85, W/S=.2, 나머지 nonN 고도 Math.Min(.55). GenStepPatches.Postfix에서 이미지 먼저, SDF 나중. 새 평가 상태는 SDF 없음/hasRiverfalse/hasCavesfalse. 실제 Gemini3.8 final-38 3개는 유효하나 2.5 협곡은 아직 산을 토양으로 오해. 과장 금지.

문제: docs/analysis/2026-09-13-real-inputs/generated-preview_river와 generated-gorge는 실제 Elevation/caves/terrain 분류도와 이미지 geometry 거의 일치, 몇 개 광물 덩어리는 별개. 그런데 generated-ai_map은 테스트 소유 타일 hilliness=Mountainous로 만든 후 생성했고, 이미지평지10683칸 중3246칸이산으로남음. capture의 Elevation 기반 추가M3407이므로 단순 광물 덩어리만의 문제 아님. JSON에는 replaceElevation=true, 설치된 dev DLL도확인. 현재 단계별trace를추가중.

아래만 읽고 가능한 원인/범위안전한 수정 방향을 좁혀줘:
- dev/Source/ImageInput/ImageMapData.cs
- dev/Source/Patches/GenStepPatches.cs 앞100줄, GenerationContextPatch.cs, MapPreviewIntegration.cs
- dev/Source/MapGen/MapGenParams.cs/GenerationContext.cs 관련부
- tools/runtime-probe/RealImageProbe.cs
- 위 generated-*/result.json + 최종 final-38/ai_map-state.json (cells 전체출력불필요)

추정과 확인을 구별하고, 외부 mutator를 무조건 무력화하거나 이미지후SDF를덮는 해결은 피한다. 분류모델 문제와 실제 적용 문제를구별한다. 3~5개 국소지적이면 충분, 수정하지 말 것.
