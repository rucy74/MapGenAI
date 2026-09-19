namespace MapGenAI.MapGen
{
    public static class PassagePrompt
    {
        public static string Rules(bool korean)=>korean?@"
연결·최소 폭을 명시한 마른 통로:
- scope로 적용 범위를 구별합니다. 산에 출구/고개/통로를 뚫거나 산을 관통하는 협곡 요청은 scope:mountains를 사용합니다. 경로상의 실제 산 부분만 깎고 도넛 안쪽 빈 공간과 산 바깥 평지의 토양·물은 그대로 둡니다. 산 경계는 생성 코드가 계산하므로 경계를 추측해서 points를 짧게 자르지 마세요. 산이 없는 경로는 평지를 바꾸지 않고 안내합니다.
- 사용자가 전체 경로를 흙길로 칠하기, 산 밖 평지에도 길 만들기, 맵 끝까지 흙길 이어주기를 명시하면 scope:full입니다. '산을 뚫어 밖으로 나갈 수 있게'만으로 바깥 평지까지 칠하지 마세요. 기존 scope 생략 통로는 full로 보존합니다. '산 부분만 남겨/바깥 흙길 지워'는 기존 ID에 changes:{""scope"":""mountains""}, 다시 전체 길을 원하면 changes:{""scope"":""full""}. 폭·재료만 바꾸는 요청에서는 기존 scope를 유지합니다.
- 사용자가 두 지점을 걸어서 연결하거나 최소 몇 칸 폭의 통로/협곡/출구를 요청하면 type:passage를 사용합니다. points는 순서대로 통과할 [x,z] 좌표 2..32개(0..1), width는 실제 맵 칸 수 정수1..64, fill은 활성 마른 안전한 재료(예:Soil)입니다. 8칸을 8%로 바꾸지 마세요. 시작점/끝점까지 모두 포함하며 중앙에서 남쪽 가장자리면 [[0.5,0.5],[0.5,0]]입니다. 다른 산·호수 ID는 보존하세요.
- 예: {""shape_ops"":[{""op"":""add"",""shape"":{""id"":""south_exit"",""type"":""passage"",""points"":[[0.5,0.5],[0.5,0]],""width"":8,""scope"":""mountains"",""fill"":""Soil""}}]}. 꺾인 길은 points에 요청한 꺾임 지점을 순서대로 넣습니다. 정확한 폭의 핵심 통로는 고정되며 주변의 자연스러운 산 윤곽은 별도 기존 도형으로 유지합니다. e/strength/shapes/compose/edge_roughness는 passage에 넣지 마세요.
- 후속 폭 변경은 이 ID의 changes:{""width"":12}, 경로 변경은 changes:{""points"":[...]}; move는 점들의 평균 위치를 옮깁니다. 삭제/Undo/저장이 가능합니다. passage는 마지막에 기존 사용자 지형을 깎는 명시적 통로이므로 나중에 막으려면 해당 통로를 remove/update해야 합니다. 면적 채움·일반 도형·기존 통로 재료만 바꾸는 요청을 임의로 이 타입으로 전환하지 마세요.
- 월드 강/바다를 건너는 마른 육교나 해안 매립, 두꺼운 산 지붕을 유지하는 동굴은 이 기능으로 보장하지 않습니다. 해당 조건이면 ask로 한계를 설명하세요. 완성 후 건물 등으로 폭이 막힌 경우에도 검증 결과를 알립니다.
":@"
Dry passages with explicit connectivity or minimum cell width:
- Distinguish scope: use scope:mountains for cutting an exit/pass/passage/canyon through a mountain. Only mountain cells along the route are cut; preserve existing ground and water in the donut interior and outside the mountain. The generator finds the mountain boundary: do not guess it by shortening points. A route that misses all mountains preserves open ground and reports this.
- Use scope:full when explicitly asked to paint the entire route as a soil path, extend the path across open ground, or continue a soil path to the map edge. Merely asking for a way out through a mountain does not request painting the plains. Old passages without scope retain full behavior. 'Keep only the mountain cut/remove the outside path' updates the existing ID with changes:{""scope"":""mountains""}; restore the entire path with changes:{""scope"":""full""}. Width/material-only edits preserve the existing scope.
- When the user asks to walk between endpoints or specifies a minimum passage/canyon/exit width in cells, use type:passage: points is 2..32 ordered normalized [x,z] waypoints, width is an integer1..64 MAP CELLS, fill is an active dry safe material such as Soil. Never turn 8 cells into8%. Include both endpoints; center to south edge is [[0.5,0.5],[0.5,0]]. Preserve other terrain IDs.
- Example: {""shape_ops"":[{""op"":""add"",""shape"":{""id"":""south_exit"",""type"":""passage"",""points"":[[0.5,0.5],[0.5,0]],""width"":8,""scope"":""mountains"",""fill"":""Soil""}}]}. Bends are ordered points. The clear core is precise; keep natural surroundings as separate existing shapes. Do not supply e/strength/shapes/compose/edge_roughness on passage.
- Edit width with changes:{""width"":12}, route with changes:{""points"":[...]}; move translates the average of its points. Supports remove/Undo/save. Explicit passages cut existing authored terrain last; remove/update the passage to close it later. Do not convert ordinary shapes, area fills or material-only edits of old passages to this type.
- This cannot promise dry bridges across world rivers/oceans, reclaimed coasts or thick-roof caves. Explain those limits with ask. Final generation reports obstruction if buildings etc. prevent the requested clear footprint.
";
    }
}
