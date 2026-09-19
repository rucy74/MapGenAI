namespace MapGenAI.MapGen
{
    // Recipes use the existing editable shapes. No second model call or alternate generator.
    public static class LandformPrompt
    {
        public static string Rules(bool korean) => korean ? @"
대표 육상 지형의 구성 규칙 (칼데라/분지/협곡을 조성할 때):
- 이것은 기존 composite와 passage의 조합입니다. 새 타입/GL 특징 이름을 만들어 보내지 마세요. 기존 단순 산·호수·도형 요청을 이 구성으로 바꾸거나 요청하지 않은 출구/물/폐허를 추가하지 마세요. 명시한 크기·위치·방향·기존 지형 보존 조건을 우선합니다.
- 칼데라: 바깥 산에서 안쪽을 sub한 닫힌 고리 composite. 마른 내부/정착지 요청이면 별도의 soil/e:0.05 내부를 먼저 적용하고 그 위에 고리를 놓습니다. 고리와 내부 평지는 각각 편집 가능한 ID를 사용합니다. 예: 중앙, 바깥 반경0.35, 안쪽 반경0.20, 내부 바닥 반경0.25. 내부 바닥은 고리 밑에만 겹치고 바깥 윤곽을 넘지 않게 합니다. 자연스러움은 산 윤곽에 주고 덮인 내부 바닥에 별도의 불일치하는 굴곡을 주지 마세요. 물/용암 칼데라나 내부 자연물 보존 요청에는 마른 바닥을 강요하지 않습니다.
- 열린 분지: 같은 닫힌 산 고리와 요청한 내부 바닥, 그리고 지정 방향의 별도 passage(scope:mountains)를 조합합니다. 예: 남쪽 출구는 중심→[중심x,0], 동쪽은 중심→[1,중심z]. 폭을 지정하지 않으면8칸을 시작값으로 사용합니다. 고리 자체를 열지 않아야 내부 채움/구조물의 enclosed 참조가 유지됩니다. 자연 윤곽은 명시한 경우에만 추가합니다.
- 좁은 협곡: 실제 산 덩어리를 만든 다음 passage로 관통합니다. 예: 동서 협곡은 중앙 rect 산(w:0.82,h:0.60,e:0.9)과 [[0,0.5],[1,0.5]]의 마른 통로. 넓은 gap의 split은 넓은 골짜기이며 좁은 협곡의 폭을 대신하지 않습니다. 지정 폭은 실제 칸 단위이며 생략 시12칸. 굽은 협곡이면 통로 points를 순서대로 꺾고, 직선이면 일직선을 유지합니다. 산 밖 평지는 보존합니다. 기존 산을 뚫는 요청이면 새 산은 추가하지 않습니다.
- 후속 '안쪽 평지만 넓혀': 고리의 안쪽 경계와 그 아래 내부 바닥을 함께 키우고 바깥 경계·산 윤곽 강도·출구 폭/방향·다른 영역은 유지합니다. 현재 크기를 읽고 산벽이 남을 수 있는 범위로 조절합니다. 내부 바닥을 늘려도 나중에 덮는 고리의 안쪽 경계를 바꾸지 않으면 평지가 넓어지지 않습니다.
- 후속 '분지 전체 이동': 그 분지의 산·내부 바닥·출구를 같은 변위로 옮기고 내부 채움/구조물은 원래 산 ID 참조를 유지합니다. '출구만 다른 방향'은 기존 passage의 points만 변경, '출구를 닫아'는 그 passage만 제거합니다. 고리/바닥/비옥토/폐허를 다시 생성하지 마세요. 특정 분지 전체 삭제는 그 소유 바닥·출구·참조 채움/구조물도 함께 제거하며 다른 분지는 유지합니다.
- 바다 반도/군도, 두꺼운 지붕 동굴, 실제 세계 강의 분기·합류를 이 세 구성으로 대체하지 마세요. 지원 범위와 현재 타일의 월드 연결 조건을 따릅니다.
" : @"
Landform recipes (when creating a caldera, enclosed basin with an exit, or canyon):
- Compose existing composite and passage shapes; do not invent shape types or GL feature names. Do not turn simple mountain/lake/geometric requests into these recipes or add unrequested exits, water or ruins. Explicit size, position, direction and preservation constraints take precedence.
- Caldera: a CLOSED composite mountain ring formed by subtracting an inner shape. For an explicitly dry/buildable interior, apply a separate soil/e:0.05 floor FIRST and the ring above it. Give floor and ring separate editable IDs. Example center, outer radius0.35, inner radius0.20, floor radius0.25. Overlap the floor beneath the ring without crossing its outer contour. Put requested natural roughness on the mountain; do not give the hidden floor an independent mismatching warp. Do not force a dry floor on a water/lava caldera or a request to preserve interior nature.
- Open basin: use that CLOSED source ring, the requested floor, and a separate passage with scope:mountains in the requested direction. South exit: center→[centerX,0]; east: center→[1,centerZ]. Default width8 cells when unspecified. Do not cut the source ring itself: fills/structures need its enclosed reference. Add natural contours only when requested.
- Narrow canyon: create a real mountain mass, then cut a passage through it. Example east-west: central rect mountain(w:0.82,h:0.60,e:0.9) plus a dry passage [[0,0.5],[1,0.5]]. A split with a broad gap is a wide valley, not a substitute for a narrow canyon's width. Use requested width in cells, default12. Use ordered bends for a winding canyon and collinear points for a straight one. Preserve open ground outside the mountain. If asked to cut an EXISTING mountain, do not add a new mountain mass.
- Follow-up 'enlarge only the interior plain': enlarge the ring's INNER boundary and its floor together, preserving the OUTER boundary, roughness, exit width/direction and unrelated regions. Read current dimensions and retain a mountain wall. Increasing only the floor cannot enlarge the plain under an unchanged ring.
- 'Move the whole basin': move its mountain, floor and exit by the SAME offset; retain source IDs on interior fills/structures. 'Change only exit direction' updates the existing passage points; 'close the exit' removes only that passage. Do not recreate ring/floor/soil/ruins. Deleting one entire basin also removes its own floor, exit and dependent fills/structures, preserving other basins.
- These recipes do not implement ocean peninsulas/archipelagos, thick-roof caves, or branching/merging world rivers. Honor existing capability and world-connection constraints.
";
    }
}
