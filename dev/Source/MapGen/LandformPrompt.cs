namespace MapGenAI.MapGen
{
    // Natural layouts and older exact recipes share the normal single-request edit pipeline.
    public static class LandformPrompt
    {
        public static string Rules(bool korean) => NaturalRules(korean)+LegacyRules(korean);
        static string NaturalRules(bool korean) => korean ? @"
자연스러운 육상 지형 (정확한 도형/기존 지형 편집보다 우선하지 않음):
- 새 자연 분지·넓은 굽은 골짜기·산기슭 평야는 type:landform으로 만들 수 있습니다. 산줄기와 연결된 내부 평지를 코드가 함께 생성합니다. 단순 원/별/하트, 닫힌 도넛/칼데라, 지정 폭의 좁은 협곡은 기존 도형/통로를 사용하세요. 기존 composite 지형을 요청 없이 새 방식으로 교체하지 마세요.
- landform:open_basin(열린 비대칭 분지), winding_valley(넓고 굽은 골짜기), foothills(한쪽 산줄기의 가지와 이어지는 평야). direction은 분지 출구 방향, 계곡 축 방향, 산기슭의 산이 있는 방향입니다. 허용: left/right/top/bottom/대각 방향 또는 각도. position:[x,z] 기본 중앙. size는 전체 규모: small=.55, medium=.75, large=.95 또는 .35..1, 기본 .9.
- gap은 사용 가능한 평지의 폭을 조절합니다(.1.. .32). 기본 분지/산기슭 .25, 계곡 .14. 커질수록 내부가 넓어집니다. 분지의 opening은 출구 폭이며 .08.. .3, 기본 .14. gap을 넓힐 때 출구 폭은 그대로 둡니다. variant는 정수0..999999(기본0)이며 새 후보의 다른 산줄기 배치를 원하면 바꾸고 후속 편집에서는 유지합니다. strength/fill/edge_roughness/compose는 여기에 사용하지 않습니다. opening:null은 기본 출구 폭으로 복원합니다. 다른 종류로 바꾸면 이전 분지의 출구 폭은 자동 해제됩니다.
- 예: {""shape_ops"":[{""op"":""add"",""shape"":{""id"":""basin"",""type"":""landform"",""landform"":""open_basin"",""direction"":""bottom"",""size"":0.9,""variant"":23}}]}.
- '안쪽 평지만 넓혀'는 같은 ID의 gap만 증가, '출구를 남쪽으로'는 direction, '출구만 넓혀'는 opening, 전체 이동은 shape_ops move, 전체 크기는 size만 변경합니다. 연결된 평지와 산줄기가 함께 따라갑니다. 분지의 direction은 산벽을 유지하고 출구만 옮깁니다. 골짜기/산기슭의 direction은 전체 방향입니다.
- 이 ID의 region_part:inside는 산이 아니라 생성한 평지입니다. '안쪽70%비옥토'는 region_fill(region:그ID,region_part:inside,coverage:0.7,fill:rich_soil), 폐허도 region:그ID/inside를 참조합니다. 열린 지형이므로 enclosed는 금지. 이 지형 삭제 때 그 영역을 참조하는 채움/구조물도 처리합니다. 물·도로·건물 등 보호 대상은 기존 규칙을 따르며 모든 바닥을 흙으로 칠하지 않습니다.
- 추천에서 타일/취향에 어울리는 경우만 사용하고 세 종류를 고정 세트로 추천하지 마세요. 기존 산·강·해안·특징과 타일 조건을 반영하세요. 강 없는 타일에 실제 강/해안을 새로 만들거나 기존 물/특징을 지워 맞추지 마세요. 추가 LLM 호출은 필요 없습니다.
" : @"
Natural land layouts (never override explicit exact geometry or existing edits):
- New natural open basins, broad winding valleys and foothill plains may use type:landform. Code generates related ridges and connected usable ground. Keep existing exact shapes for circles/stars/hearts, closed donuts/calderas, and passages for narrow canyons with specified cell widths. Never replace existing composite geometry without a request.
- landform is open_basin, winding_valley or foothills. direction specifies the basin opening, valley axis, or mountain side of the foothills. Use left/right/top/bottom/diagonals or degrees. position:[x,z] defaults to center. size is overall scale: small=.55, medium=.75, large=.95 or .35..1, default .9.
- gap controls usable floor width (.1.. .32); defaults basin/foothills .25, valley .14. Larger widens the floor. open_basin alone accepts opening (.08.. .3, default .14), independently controlling exit width. variant is a persisted integer0..999999(default0); vary it for a new layout, preserve it on follow-ups. Do not supply strength/fill/edge_roughness/compose. opening:null restores the default exit width; switching away from open_basin automatically clears its previous opening override.
- Example params: {""shape_ops"":[{""op"":""add"",""shape"":{""id"":""basin"",""type"":""landform"",""landform"":""open_basin"",""direction"":""bottom"",""size"":0.9,""variant"":23}}]}.
- Widen only the interior by updating gap on the existing ID; opening direction via direction, exit width via opening; move the whole layout using shape_ops move, resize using size. Floor and ridges follow together. For a basin, direction moves only the exit while retaining the walls. For valleys/foothills it sets the whole layout orientation.
- region_part:inside on this ID means its planned FLOOR, not the mountain. A 70% rich-soil interior uses region_fill(region:thatID,region_part:inside,coverage:0.7,fill:rich_soil); positioned ruins reference thatID/inside too. Never use enclosed for these open layouts. Handle dependent fills/structures on deletion. Existing protected water/roads/buildings rules still apply. Do not paint the entire floor with soil.
- Recommend these only when appropriate to the tile/preferences, never as a fixed trio. Preserve the tile's river/coast/features unless asked otherwise. Do not invent a world river/coast or remove existing water/features to fit the layout. No extra model call is required.
";
        static string LegacyRules(bool korean) => korean ? @"
기존 도형 구성 규칙 (정확한 칼데라/분지/좁은 협곡 또는 기존 composite 편집):
- 이것은 기존 composite와 passage의 조합입니다. 새 타입/GL 특징 이름을 만들어 보내지 마세요. 기존 단순 산·호수·도형 요청을 이 구성으로 바꾸거나 요청하지 않은 출구/물/폐허를 추가하지 마세요. 명시한 크기·위치·방향·기존 지형 보존 조건을 우선합니다.
- 칼데라: 바깥 산에서 안쪽을 sub한 닫힌 고리 composite. 마른 내부/정착지 요청이면 별도의 soil/e:0.05 내부를 먼저 적용하고 그 위에 고리를 놓습니다. 고리와 내부 평지는 각각 편집 가능한 ID를 사용합니다. 예: 중앙, 바깥 반경0.35, 안쪽 반경0.20, 내부 바닥 반경0.25. 내부 바닥은 고리 밑에만 겹치고 바깥 윤곽을 넘지 않게 합니다. 자연스러움은 산 윤곽에 주고 덮인 내부 바닥에 별도의 불일치하는 굴곡을 주지 마세요. 물/용암 칼데라나 내부 자연물 보존 요청에는 마른 바닥을 강요하지 않습니다.
- 열린 분지: 같은 닫힌 산 고리와 요청한 내부 바닥, 그리고 지정 방향의 별도 passage(scope:mountains)를 조합합니다. 예: 남쪽 출구는 중심→[중심x,0], 동쪽은 중심→[1,중심z]. 폭을 지정하지 않으면8칸을 시작값으로 사용합니다. 고리 자체를 열지 않아야 내부 채움/구조물의 enclosed 참조가 유지됩니다. 자연 윤곽은 명시한 경우에만 추가합니다.
- 좁은 협곡: 실제 산 덩어리를 만든 다음 passage로 관통합니다. 예: 동서 협곡은 중앙 rect 산(w:0.82,h:0.60,e:0.9)과 [[0,0.5],[1,0.5]]의 마른 통로. 넓은 gap의 split은 넓은 골짜기이며 좁은 협곡의 폭을 대신하지 않습니다. 지정 폭은 실제 칸 단위이며 생략 시12칸. 굽은 협곡이면 통로 points를 순서대로 꺾고, 직선이면 일직선을 유지합니다. 산 밖 평지는 보존합니다. 기존 산을 뚫는 요청이면 새 산은 추가하지 않습니다.
- 후속 '안쪽 평지만 넓혀': 고리의 안쪽 경계와 그 아래 내부 바닥을 함께 키우고 바깥 경계·산 윤곽 강도·출구 폭/방향·다른 영역은 유지합니다. 현재 크기를 읽고 산벽이 남을 수 있는 범위로 조절합니다. 내부 바닥을 늘려도 나중에 덮는 고리의 안쪽 경계를 바꾸지 않으면 평지가 넓어지지 않습니다.
- 후속 '분지 전체 이동': 그 분지의 산·내부 바닥·출구를 같은 변위로 옮기고 내부 채움/구조물은 원래 산 ID 참조를 유지합니다. '출구만 다른 방향'은 기존 passage의 points만 변경, '출구를 닫아'는 그 passage만 제거합니다. 고리/바닥/비옥토/폐허를 다시 생성하지 마세요. 특정 분지 전체 삭제는 그 소유 바닥·출구·참조 채움/구조물도 함께 제거하며 다른 분지는 유지합니다.
- 바다 반도/군도, 두꺼운 지붕 동굴, 실제 세계 강의 분기·합류를 이 세 구성으로 대체하지 마세요. 지원 범위와 현재 타일의 월드 연결 조건을 따릅니다.
" : @"
Existing exact recipes (caldera, precise basin with an exit, narrow canyon, or editing existing composites):
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
