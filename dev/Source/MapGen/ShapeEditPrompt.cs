namespace MapGenAI.MapGen
{
    public static class ShapeEditPrompt
    {
        public static string Rules(bool korean) => korean ? @"
지형 편집 계약:
- 일반 대화에서는 params.shape_ops로 요청한 지형만 편집합니다. ID는 현재 상태의 id를 사용합니다. 다른 지형은 코드가 보존하므로 다시 출력하지 마세요.
- 추가: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""lake_south"",""type"":""bump"",""position"":[0.5,0.2],""strength"":""negative_strong"",""size"":""small"",""fill"":""water""}}]}}
- 수정: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""update"",""id"":""lake_south"",""changes"":{""size"":""medium""}}]}}. changes에 보낸 필드만 바뀝니다. fill:null은 지형 채우기를 해제합니다. ID/type은 변경 불가.
- 삭제: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""remove"",""id"":""lake_south""}]}}
- 이동: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""position"":[0.7,0.2]}]}}. bump/ring/composite만 이동 가능. 산맥 방향은 update의 direction을 사용합니다.
- 상대 이동: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""relative_to"":""island"",""relation"":""below"",""distance"":0.2}]}}. 두 지형 중심 기준, left_of/right_of/above/below와 정규화 거리. 포함·통로 연결 보장이 아닙니다.
- 첫 맵 전체 설계는 elevation_shapes 목록도 허용합니다. 이미 지형이 있는데 전체 재설계를 명시적으로 요청했을 때만 replace_shapes:true와 전체 elevation_shapes를 함께 출력합니다. elevation_shapes:[]는 모든 사용자 지형 제거입니다. shape_ops와 elevation_shapes를 한 응답에 혼용하지 마세요.
- hills는 간단한 산 추가 단축키입니다. hills:none은 단축키로 만든 산만 제거합니다. ID가 여러 개라 대상이 모호하면 action:ask로 먼저 구분합니다.
- composite 내부 필드는 shapes와 compose입니다. 좌표는 x=0 왼쪽/1 오른쪽, z=0 아래/1 위. 존재하지 않는 대상·연산, 범위 밖 좌표, 빈 도형, 효과 없는 연산은 적용되지 않습니다.
- 원형·별·하트·도넛처럼 모양을 지정하면 composite를 사용합니다. 그냥 원형/별/하트는 정확한 도형이며 edge_roughness를 생략하거나 none으로 둡니다. ""자연스러운"", ""울퉁불퉁한"", ""불규칙한 윤곽""을 명시하면 composite 최상위에 edge_roughness:""medium""을 설정합니다. ""살짝""은 low, ""많이/거칠게""는 high. none/low/medium/high 또는 숫자0~1을 지원하며 생략=0입니다. 자연스러운 것은 윤곽 변형이며 도형 내부를 비우거나 다른 지형을 바꾸라는 뜻이 아닙니다.
- 기존 composite를 자연스럽게/다시 정확하게 바꿀 때는 해당 ID의 changes:{""edge_roughness"":""medium""} 또는 {""edge_roughness"":""none""}만 보냅니다. shapes/compose/크기/위치/높이/채움은 보존합니다. 이 필드는 composite 전용이며 ridge의 noise_amount와 다릅니다. 기존 bump/ring 채움은 원래부터 불규칙한 윤곽입니다. 기존 bump/ring을 정확한 도형으로 바꿔야 하면 해당 대상만 remove+add로 composite로 변환합니다.
- 예: 자연스러운 원형 호수 추가 {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""round_lake"",""type"":""composite"",""edge_roughness"":""medium"",""shapes"":[{""id"":""c"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.2}],""compose"":[{""op"":""add"",""s"":""c"",""fill"":""water"",""e"":0,""f"":0.015}]}}]}}. 자연스러운 별/하트도 같은 옵션을 사용하며, 도넛만 바깥 circle에서 안쪽 circle을 sub합니다.
- 서로 다른 지형을 수십 개 나열해 그림을 흉내내지 마세요. 지형 최대32, composite당 primitive/operation 최대32. 표현 불가능한 건 구체적으로 설명하고 대안을 질문하세요.
" : @"
Terrain edit contract:
- Use params.shape_ops for conversational edits. Target the id shown in current state. Unmentioned terrain is preserved by code; do not re-list it.
- Add: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""lake_south"",""type"":""bump"",""position"":[0.5,0.2],""strength"":""negative_strong"",""size"":""small"",""fill"":""water""}}]}}
- Update: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""update"",""id"":""lake_south"",""changes"":{""size"":""medium""}}]}}. Only supplied fields change. fill:null clears fill. id/type cannot change.
- Remove: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""remove"",""id"":""lake_south""}]}}
- Move: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""position"":[0.7,0.2]}]}}. Only bump/ring/composite can move. Update direction for a ridge.
- Relative move: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""move"",""id"":""lake_south"",""relative_to"":""island"",""relation"":""below"",""distance"":0.2}]}}. Center-relative left_of/right_of/above/below, normalized distance. Does not guarantee containment or connected passages.
- Initial layout may use elevation_shapes. On an existing layout, output replace_shapes:true plus the full elevation_shapes only when the user explicitly requests complete redesign. elevation_shapes:[] clears all custom terrain. Never combine elevation_shapes with shape_ops.
- hills is a simple mountain addition shortcut. hills:none removes only shortcut-created mountains. Ask if multiple IDs make the target ambiguous.
- Composite fields are shapes and compose. Coordinates x=0 left/1 right; z=0 bottom/1 top. Missing operands/targets, out-of-range positions and ineffective shapes are rejected.
- Use composite for explicitly shaped circles/stars/hearts/donuts. A plain circle/star/heart is precise: omit edge_roughness or use none. Explicit natural/organic/irregular contour modifiers set top-level composite edge_roughness:medium; slightly=low, very rough=high. Accepted values none/low/medium/high or numeric 0..1; omitted=0. Natural changes the outline, not the fill, interior holes or unrelated terrain.
- To make an existing composite natural or precise again, update ONLY its edge_roughness field to medium or none; preserve shapes/compose, size, position, height and fill. This field is composite-only and distinct from ridge noise_amount. Legacy filled bump/ring already have irregular edges. To convert a legacy bump/ring to an exact shape, remove+add only that target as a composite.
- Natural circle lake example: {""action"":""generate"",""params"":{""shape_ops"":[{""op"":""add"",""shape"":{""id"":""round_lake"",""type"":""composite"",""edge_roughness"":""medium"",""shapes"":[{""id"":""c"",""prim"":""circle"",""center"":[0.5,0.5],""r"":0.2}],""compose"":[{""op"":""add"",""s"":""c"",""fill"":""water"",""e"":0,""f"":0.015}]}}]}}. Stars/hearts use the same option; only a donut subtracts an inner circle from an outer one.
- At most 32 terrain shapes, 32 primitives/operations per composite. Explain concrete representation limits and ask about alternatives when needed.
";
    }
}
