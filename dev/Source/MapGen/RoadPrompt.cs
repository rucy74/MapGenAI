namespace MapGenAI.MapGen
{
    public static class RoadPrompt
    {
        public static string Rules(bool korean) => korean ? @"
정착 맵의 도로:
- 세계지도에 도로를 추가하지 않고 현재 지역맵 안에만 도로를 놓을 수 있습니다. 세계지도에 도로가 없는 타일도 가능합니다. 기존 세계 도로의 삭제/변경은 지원하지 않습니다. legacy roads:true/false는 지역 도로 생성/삭제 기능이 아니므로 출력하지 마세요.
- 포장된 길/흙길/아스팔트 도로 요청은 params.road_ops를 사용합니다. 산을 깎는 passage나 Soil 채움으로 도로를 대신하지 마세요. 도로는 지형 높이를 바꾸지 않고 게임의 도로 종류별 기본 폭·재료를 사용합니다. 별도 폭/재료/교량/터널 필드는 없습니다.
- 이번 기능은 도로의 포장면을 놓습니다. 고대 도로의 가로등·방호물 등 장식 건물은 생성하지 않습니다.
- kind는 DirtPath(흙길), DirtRoad(흙도로), StoneRoad(돌길), AncientAsphaltRoad(고대 아스팔트 도로), AncientAsphaltHighway(고대 아스팔트 고속도로)만 지원합니다. 유저에게는 읽기 쉬운 게임 언어 이름으로 설명하고 내부 ID를 보여주지 마세요.
- road의 필드는 id, kind, route, points, seed뿐입니다. id는 고유한 영숫자/밑줄/하이픈, kind 기본 DirtRoad, route 기본 avoid입니다. points는 2~16개의 [x,z] 경유점이며 각 좌표0..1, 서쪽x=0/동쪽x=1/남쪽z=0/북쪽z=1입니다. seed는 선택 정수입니다. 최대8개의 도로, 한 응답 최대16개 연산입니다.
- route:avoid는 경유점 순서대로 물·산바위를 피해 길을 찾습니다. 자연스럽게 굽은 길은 중간 경유점도 적절히 배치하고 avoid를 사용하세요. route:direct는 경유점 사이를 직선으로 연결합니다. 두 방식 모두 강/바다/용암/산/건물을 관통하거나 다리·터널을 자동으로 만들지 않습니다. 경로가 기존 건물에 겹치거나 길 전체가 놓일 공간이 없으면 실패를 알립니다. 설정 수락만으로 도로 생성 성공을 단정하지 마세요.
- 강 건너편까지의 도로나 산 관통 요청에서 교량/터널이 필요하면 ask로 현재 한계를 설명하고 같은 강변 경로/기존 마른 통로 등 가능한 대안을 제안하세요. 주변 지형을 멋대로 지워서 도로를 만들지 마세요. 유적까지 연결할 때는 건물 내부가 아닌 바깥의 접근 지점을 종점으로 잡으세요. 구조물 위치는 생성 때 조정될 수 있으므로 출입구 자동 연결을 보장하지 마세요.
- 추가 예, 서쪽에서 동쪽으로 아스팔트 도로: {""road_ops"":[{""op"":""add"",""road"":{""id"":""main_road"",""kind"":""AncientAsphaltRoad"",""route"":""avoid"",""points"":[[0,0.5],[1,0.5]]}}]}. 명시적으로 직선을 원하면 route:direct를 씁니다.
- 수정 예: {""road_ops"":[{""op"":""update"",""id"":""main_road"",""changes"":{""kind"":""StoneRoad""}}]}. 필요한 필드만 변경해 기존 경로·종류·다른 도로와 지형을 유지합니다. points를 변경할 때는 경유점 배열 전체를 보냅니다. 삭제 예: {""road_ops"":[{""op"":""remove"",""id"":""main_road""}]}. 전체 지역 도로 삭제는 현재 local_roads의 ID마다 remove를 보내며 road_ops:[]는 아무 변경도 하지 않습니다.
" : @"
Roads inside the settlement map:
- Add roads only inside the current local map, without changing the world map. The world tile does NOT need an existing road. Editing/removing existing world roads is unsupported. Never output legacy roads:true/false: it does not add/remove local roads.
- Use params.road_ops for dirt paths, stone roads and asphalt roads. Do not substitute mountain-cutting passages or Soil fill. Roads do not change elevation. Each road type uses its native width and material; there are no custom width/material/bridge/tunnel fields.
- This feature places road surfaces. Ancient-road lamp posts, barriers and other decorative buildings are not generated.
- Supported kind values: DirtPath (dirt path), DirtRoad (dirt road), StoneRoad (stone road), AncientAsphaltRoad (ancient asphalt road), AncientAsphaltHighway (ancient asphalt highway). Show readable names in the user's language, never internal IDs.
- A road accepts only id, kind, route, points, seed. IDs are unique alphanumeric/underscore/hyphen strings. kind defaults to DirtRoad, route to avoid. points contains 2..16 [x,z] waypoints with coordinates0..1: west x0, east x1, south z0, north z1. Optional seed is an integer. Maximum8 local roads and16 operations per response.
- route:avoid routes through waypoints in order around water and mountain rock. For a naturally winding road, also choose appropriate intermediate waypoints. route:direct joins waypoints with straight segments. Neither mode crosses rivers/ocean/lava/solid mountains/buildings or creates bridges/tunnels. A route overlapping existing buildings or insufficient space for the whole road produces an explicit failure. Accepted settings do not prove successful road generation.
- When crossing a river or mountain requires a bridge/tunnel, use ask to explain the current limit and suggest a same-bank route or an existing dry passage. Never erase surrounding terrain merely to force a road. For a road to ruins, end at an outside approach point rather than inside the building. Structure positions may move during generation; do not promise automatic entrance connection.
- Add west-east asphalt road: {""road_ops"":[{""op"":""add"",""road"":{""id"":""main_road"",""kind"":""AncientAsphaltRoad"",""route"":""avoid"",""points"":[[0,0.5],[1,0.5]]}}]}. Use route:direct only when straight segments are wanted.
- Update only requested fields: {""road_ops"":[{""op"":""update"",""id"":""main_road"",""changes"":{""kind"":""StoneRoad""}}]}. Preserve the other road fields and unrelated roads/terrain. Replacing points requires the entire waypoint array. Remove: {""road_ops"":[{""op"":""remove"",""id"":""main_road""}]}. To remove all local roads, remove each existing local_roads ID. road_ops:[] changes nothing.
";
    }
}
