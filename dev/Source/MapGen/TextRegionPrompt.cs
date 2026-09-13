namespace MapGenAI.MapGen
{
    public static class TextRegionPrompt
    {
        public static string Rules(bool korean) => (korean ? @"
텍스트 영역·재료·위치 지정:
- 이미지 입력/해석/팔레트 기능은 일시 중단입니다. 이미지 요청에는 action:ask로 텍스트 요청을 안내하세요. 저장된 이미지도 생성에 적용되지 않습니다.
- bump/ring/composite fill은 Active fill materials의 정확한 TerrainDef 이름을 지원합니다. 별칭 water/sand/soil/rich_soil/marsh/mud/ice/lava/cooled_lava/gravel/volcanic_rock도 해당 정의가 활성화돼 있으면 가능합니다. lava=LavaDeep(실제 깊은 용암)이며 물로 대체하지 않습니다. LavaShallow는 식는 임시 지형이라 현재 fill 미지원입니다. 미활성 재료는 action:ask로 설명합니다.
- composite의 최상위 fill을 update하면 렌더되는 영역들의 재료가 바뀝니다. 다중 재료 중 일부만 바꾸려면 compose의 해당 fill만 수정하세요. fill:null은 최상위 덮어쓰기를 해제하며 compose의 fill은 유지됩니다.
- 용암 속 섬: 바깥 원에서 안쪽 원을 sub한 도넛에 fill:lava, 별도 섬 영역에는 fill:soil와 e:0.05(평탄화)를 사용하세요. 내부를 물로 만들지 마세요. 재료가 다른 영역은 서로 다른 top-level id를 사용합니다.
- 위치는 params.structure_ops로 지정합니다. 현재 kind:ruin은 지붕·적·전리품 없는 손상된 화강암 벽/바닥 폐허입니다. 고대 위협·완전한 고대 단지·임의 모드 구조물 위치는 미지원이므로 action:ask로 범위를 설명하세요.
- 추가 예: {""structure_ops"":[{""op"":""add"",""structure"":{""id"":""ruins_1"",""kind"":""ruin"",""region"":""island"",""width"":11,""height"":9,""count"":1}}]}. region은 존재하는 top-level bump/ring/composite id입니다. 구멍과 자연스러운 경계를 포함한 실제 내부에 건물 전체 면적이 들어가야 합니다.
- 영역 없이 position:[x,z] 또는 bounds:[서쪽,남쪽,동쪽,북쪽]으로 지정 가능합니다. 좌표0..1, 북쪽z=1. position만 있으면 각 축±0.10 범위에서 찾습니다. region이면 해당 영역 전체에서 찾고 이동한 영역을 따라갑니다. bounds는 추가 제한입니다.
- width/height5..31칸, count1..8, 전체24개. 수정: {""op"":""update"",""id"":""ruins_1"",""changes"":{""count"":2}}. 삭제: {""op"":""remove"",""id"":""ruins_1""}. changes는 position/region/bounds/width/height/count/seed를 지원합니다. position/region/bounds는 null로 해제 가능하나 하나는 필요합니다.
- 참조 영역을 삭제할 때 같은 응답에서 유적도 제거하거나 region을 다시 지정하세요. 위치 지정은 자연 폐허/위협 밀도와 별개입니다. '이곳에만 폐허'라면 ruin_density:0을 함께 사용하되 다른 랜드마크 건물을 없앴다고 말하지 마세요.
- 물·용암·도로·막힌 바위·기존 건물·사용 중 공간에는 배치하지 않습니다. 공간 부족 시 위치 지정 유적은 전부 미생성되고 실패 이유가 표시됩니다. 설정 적용만으로 실제 배치 성공을 단정하지 마세요. 실패 시 영역 확대/평탄화/개수·크기 축소를 제안합니다.
- 구조물 공간 관계: relation:{""target"":""river"",""min_distance"":2,""max_distance"":12,""side"":""east""}는 실제 강의 동쪽에서 가장 가까운 건물 칸까지 거리2~12칸을 요구합니다. target은 river(실제 월드 강)/water(물)/mountain(실제 산 바위)/region_edge(지정 region의 안쪽 가장자리), side는 any/north/south/east/west입니다. 산기슭/강가 요청은 이 relation으로 표현하고 임의 좌표로 대신하지 마세요. 없는 지형은 자동 생성하지 않으며 조건 실패를 안내합니다.
- 섬 가장자리 요청은 region ID와 relation:{""target"":""region_edge"",""min_distance"":2,""max_distance"":8}을 함께 사용합니다. 전체 건물은 영역 안에 유지됩니다. relation만으로도 위치 기준을 지정할 수 있고 position/bounds/region이 함께 있으면 모두 만족해야 합니다. 거리0~64칸, max_distance>=min_distance. relation을 update하면 객체 전체를 교체하므로 기존 조건을 유지할 땐 포함하세요. relation:null은 관계를 해제합니다.
- rotation은0/90/180/270도, spacing은 구조물 사이 최소 빈칸 수1~60(기본1)입니다. 강을 따라 떨어뜨려 배치할 때 relation과 spacing을 함께 사용합니다. 최소 간격은 정확히 같은 간격이나 강 양쪽 동일 개수 배치가 아닙니다. 양쪽에 필요하면 east/west 등 계획을 별도로 만드세요. changes에도 relation/rotation/spacing을 사용할 수 있습니다.
" : @"
Text regions, materials and positioned structures:
- Image input, interpretation and palette generation are paused. For image requests use action:ask and suggest text. Stored image data is inactive.
- bump/ring/composite fill accepts exact names from Active fill materials. Aliases water/sand/soil/rich_soil/marsh/mud/ice/lava/cooled_lava/gravel/volcanic_rock require loaded definitions. lava means real LavaDeep, never water. Temporary cooling LavaShallow is unsupported. Explain unavailable materials with action:ask.
- Updating top-level composite fill overrides rendered regions. For one material in a multi-material composite update only its compose fill. Top-level fill:null removes that override, retaining compose fills.
- Island in lava: subtract an inner circle from the outer circle; fill the annulus with lava. Use a separate soil island region with e:0.05 to flatten it. Give distinct top-level IDs to separate material regions.
- params.structure_ops controls position, not density. Only kind:ruin is supported: damaged granite walls/floors, no roof, enemies or loot. Ancient dangers, complete ancient complexes and arbitrary mod structures need adapters; explain with action:ask.
- Add: {""structure_ops"":[{""op"":""add"",""structure"":{""id"":""ruins_1"",""kind"":""ruin"",""region"":""island"",""width"":11,""height"":9,""count"":1}}]}. region references an existing top-level composite/bump/ring ID. The full footprint must fit inside its actual mask, including holes and natural edges.
- Alternatively position:[x,z] or bounds:[west,south,east,north], normalized0..1, north=z1. A point searches within ±0.10 per axis. A region searches its entire interior and follows region moves. Optional bounds further restrict placement.
- Width/height5..31 cells, count1..8, total24. Update: {""op"":""update"",""id"":""ruins_1"",""changes"":{""count"":2}}. Remove: {""op"":""remove"",""id"":""ruins_1""}. Changes accept position/region/bounds/width/height/count/seed. position/region/bounds may be null, but at least one is required.
- Removing a referenced shape requires removing/rebinding its structures in the same response. Position does not affect random ruin/danger density. For ruins only here use ruin_density:0 too, without claiming it removes other landmark buildings.
- No placement on water/lava/roads/blocked rocks/buildings/used space. If any footprint cannot fit, no positioned structures spawn and generation reports failure. Settings applied does not mean placement succeeded. Suggest a larger/flatter region or fewer/smaller ruins.
- Spatial relation: relation:{""target"":""river"",""min_distance"":2,""max_distance"":12,""side"":""east""} requires the closest footprint cell to be 2..12 cells from the actual river, on its east side. Targets: river (world river), water, mountain (solid mountain rock), region_edge (inner boundary of the specified region). Sides: any/north/south/east/west. Use relations for riverbank/foothill requests, not guessed coordinates. Missing target terrain causes an explicit failure, never automatic terrain creation.
- For an island edge use region plus relation:{""target"":""region_edge"",""min_distance"":2,""max_distance"":8}. The entire footprint stays inside the region. Relation alone may locate a structure; any supplied position/bounds/region also applies. Distances are integer cells0..64, max>=min. Updating relation replaces the whole object; retain its other conditions when needed. relation:null clears it.
- rotation accepts0/90/180/270 degrees. spacing is minimum empty cells between footprints1..60 (default1). Combine relation and spacing for separated structures along a river. It does not guarantee exact equal intervals or equal counts on both banks; use separate east/west plans for both sides. Changes also accept relation/rotation/spacing.
") + "\nActive fill materials (loaded natural permanent terrain only):\n" + TerrainMaterials.Catalog();
    }
}
