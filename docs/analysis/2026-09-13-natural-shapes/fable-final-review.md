# Claude consultation

Requested: claude-fable-5-1

Observed modelUsage: ["claude-haiku-4-5-20251001", "claude-fable-5-1"]

검토를 마쳤습니다. 프로덕션 코드, 수학적 경계, 오프라인/제공자/네이티브 r2 증거를 독립적으로 확인한 결과입니다.

**결론: P0 없음, P1 없음. 요청 기능은 지원됩니다.** 원·별·하트·도넛은 기본값에서 정확한 도형이고, `edge_roughness`를 요청했을 때만 자연스러운 윤곽이 적용됩니다. 편집은 가역적이며 Scribe/Clone/Describe 경로 모두에 필드가 반영되어 있습니다.

**수학 검증 (독립 확인)**
- 3차 값 노이즈의 축별 편미분은 `(b-a)·s'(t)`이고 `|b-a|≤2`, `s'≤1.5`이므로 3 이하가 맞습니다. 이는 이 구현에 한정된 경계이며 일반 Perlin 경계가 아닙니다.
- Field 편미분 ≤ (4·3 + 0.3·10·3)/1.3 = 16.15. 스텝당 야코비안 성분 ≤ 0.03·amount·16.15 = 0.485·amount. 4개 성분의 Frobenius 노름 ≤ 0.969·amount로 코드 주석의 식과 일치합니다. Frobenius ≥ 스펙트럼 노름이므로 `I+J`는 가역이고 접힘이 없습니다. 3회 합성도 미분동형사상이므로 위상 보존 논리는 타당합니다.
- 진폭은 축별 최대 0.09·span이 맞지만 벡터 크기로는 최대 약 0.127·span입니다. 문서 표현이 "축별"임을 명시하면 좋습니다.

**코드 검증**
- `ContourWarp.cs:36` 시드는 UTF-16 문자 FNV-1a로 런타임 `GetHashCode`/`Rand`를 쓰지 않습니다.
- `SdfComposite.cs:317` 워프가 렌더 큐 전체에 공유 좌표로 적용되어 sub/union 피연산자 관계가 유지됩니다.
- `ShapeValidation.cs:28-30` 비composite에 `edge_roughness`가 있으면 거부, `ShapeEdits.cs:77`에서 명시적 null 거부, `none`은 Amount 0으로 정확 도형 복원. 테스트가 이를 실제로 단언합니다.
- `GenStepPatches.cs:49-53` ID 없는 자연 composite는 클론 후 `AssignIds`로 Describe와 동일 알고리즘으로 ID를 받습니다.
- `MapGenParams.cs:50,69` Scribe와 Clone에 필드 포함.

**증거 확인**
- offline-tests-r2.log: `70 PASS / 0 FAIL` 확인.
- native-r2/result.json: 33개 PASS, 자연 윤곽 전용 적용과 undo 복원 포함. 첫 `{}` 결과는 반영하지 않았습니다.
- Player.log에 실제 dialog 경로에서 composite add 후 `edge_roughness:medium` update가 적용된 기록이 있고 예외는 없습니다.
- 미리보기 PNG: 왼쪽 열은 정확한 도형, 오른쪽 열은 눈에 띄는 불규칙 윤곽이며 별 꼭짓점·하트 골·도넛 구멍이 모두 유지됩니다.
- provider/natural-results.json: 7턴 모두 intended/preserved true, roughness 값이 요청(살짝→low, 많이→high, 정확히→none)과 일치.

**P2 제한 사항**
- 시드와 앵커가 `id`와 `parts[0]`에 묶여 있습니다. `parts[0]`의 크기·순서 변경, remove+add로 ID 변경, 또는 ID 없는 레거시 도형 앞 항목 삭제 시 윤곽이 재추첨됩니다.
- Frobenius 경계 0.969는 1에 근접합니다. 최악 케이스 국소 압축이 이론상 크지만 네 편미분이 동시에 극값일 때만이며 실측에서는 문제가 없었습니다.
- 워프 축은 월드 정렬이라 `rot`이 있는 도형은 회전 후 윤곽이 강체 회전과 다릅니다. 이미 공개된 제한입니다.
- `GenStepPatches.cs:49`는 `id == null`만 검사합니다. 빈 문자열 ID는 현재 검증에서 차단되지만 `IsNullOrEmpty`가 더 견고합니다.
- 오프라인 .NET과 Mono의 부동소수 결과가 비트 단위로 동일하다는 것은 검증되지 않았습니다. 게임 내 결정성은 같은 DLL이므로 성립합니다.
- 워프 샘플이 렌더 항목마다 재계산됩니다. 현 맵 크기에서는 무시 가능한 비용입니다.
- KO 프롬프트 `Dialog_TextToMap.cs:258`에 "또는0~1" 띄어쓰기 오타가 있습니다.

**미검증/미포함**
- 프로덕션 DLL은 아직 교체되지 않았으므로 설치본 동작은 확인 대상이 아닙니다.
- 네이티브 검증은 자동화된 dialog 호출이며 수동 UI 조작은 아닙니다.
- 용암 지형과 섬 위 폐허 배치는 여전히 구현되지 않았고 이 변경에서 주장하지 않습니다.