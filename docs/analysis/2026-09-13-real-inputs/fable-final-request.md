# MapGenAI 최종 국소 검토 (읽기 전용)

repo F:/Projects/Rimworld/active/mapgen_ai, dev85886c5 이후 변경. 이전 자문과 사용자 결정은 docs/analysis/2026-09-13-real-inputs/fable-design-review.md, fable-generation-review.md.

실측: generated-ai-valley/generation-trace.json에서 Valley가 elevation stage10 다음 stage20에 원치 않는 산6399칸을 추가. 원래 실패타일50258의 정확한 mutator는 미기록으로 확정하지 않는다. 새 generated-valley-fixed는 stage20 후 추가고도산0/누락0, 실제산3984/3984·물1717/1717. 남은 ground98칸 암석은 native 광물/산란의 별도 효과, 범용 fidelity 보장 안 함.

선택 구현: 이미지 replaceElevation(true 신규, false 기존저장) UI를 '이미지·추가 도형의 높이를 월드 지형보다 우선'으로 명확화. GenerationContext.CaptureImageElevation은 이미지와 SDF 적용 완료 후 nonN 셀의 최종 고도를 scope에 저장. GenStep_MutatorPostElevationFertility Postfix에서 그 셀만 복원. 원본 이미지의 재적용으로 SDF를 지우지 않고, 모든 mutator metadata 제거하지 않음. N·legacy·다른 mutator 효과 유지. 전용worker/metadata제거 대안 대신 좁은 생성scope복원 경로를 검증했다. Caves 등이 바꾸는 지형/외부모드 조합은 한계 기록.

추가: 사용자에게 임시 테스트모드가 과다 노출되어 MapGenAI probe11개 수동삭제, tools/runtime-probe/cleanup.ps1 + launch.ps1에 종료자동정리 추가. 자기marker/절대경로/프로세스일치검증, 다른모드/게임삭제종료금지. generated-valley-fixed/cleanup.json actual removed true.

현재62 offline PASS, runtime-final36 PASS(최신우선순위전), runtime-render 최신검사실행중. 기본 이미지방식은 ColorTerrainPlan+Unity원본/그룹마스크, 기존polygon선택 가능. 3.8 원본랜드마크 최종18/19,15/16,18/24, nonblind59개 국소검사이며 fullIoU아님. AI map작은물길/바위경계/글자inset은한계. 2.5협곡산을여전히토양으로오분류. 프롬프트가확률적이라몇샘플통과를모든맵지원으로말하지않음.

아래 국소 파일과 증거만 확인하고 실질적인 P0/P1 결함이 있으면 파일/재현조건으로 짧게 답해줘. 이미 알려진 범용정확도 부족은 한계와구별. 수정하지말것.
- dev/Source/MapGen/GenerationContext.cs
- dev/Source/Patches/ImageElevationPriorityPatch.cs; GenStepPatches.cs 앞105줄
- dev/Source/ImageInput/ColorTerrainPlan.cs
- dev/Source/UI/Dialog_ImageMap.cs
- tools/runtime-probe/cleanup.ps1, launch.ps1 끝20줄
- docs/analysis/2026-09-13-real-inputs/generated-valley-fixed/result.json/generation-trace.json/cleanup.json
