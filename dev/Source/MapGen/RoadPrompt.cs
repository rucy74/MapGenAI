namespace MapGenAI.MapGen
{
    public static class RoadPrompt
    {
        // Shared technical rules; Dialog supplies the response language.
        public static string Rules(bool korean) => @"
Local roads and follow-ups:
- Resolve follow-ups from current state AND last confirmed applied change. 'Center it/more natural/make it a cross' edits that target, not a new mountain/lake. Failed commands and pending recommendations are not applied. Ask if ambiguous; honor explicit new topics/compound requests.
- Use params.road_ops for dirt/stone/asphalt roads, never Soil fills or mountain passages. Local roads need no world road; existing world roads cannot be edited/removed. Never output legacy roads:true/false. Only surfaces, no ancient lamps/barriers/decorations or elevation changes.
- road fields ONLY: id(unique alphanumeric/underscore/hyphen), kind(default DirtRoad), route(default avoid), points(2..16 normalized [x,z]), optional integer seed. West x0/east x1/south z0/north z1. Maximum8 roads/16 operations per response. Native width/material; no custom width/material/bridge/tunnel fields.
- kind:DirtPath(dirt path),DirtRoad(dirt road),StoneRoad(stone road),AncientAsphaltRoad(ancient asphalt road),AncientAsphaltHighway(ancient asphalt highway). Player text uses readable game-language names, not IDs.
- route:avoid prefers dry detours around water/rock, then bridgeable water if no dry route; blocked interior waypoints may shift to nearby connected clear ground (at most 12% of the shorter map side, or the road-clearance minimum). Usable waypoints and endpoints remain exact. route:direct connects exact waypoints by straight segments, bridging eligible water. Endpoints must be land/existing bridges.
- Exact centered cross: TWO direct roads, points:[[0,0.5],[1,0.5]] and [[0.5,0],[0.5,1]]. A correction updates those existing local_roads IDs, preserving kind/seed/other roads; NEVER duplicate or substitute mountain/passage shapes. avoid may miss the center. Bridges follow automatically, not separate shapes. Water endpoints/impossible routes require supported land endpoints/routes, not erased terrain.
- All kinds automatically use native wooden bridges/native bridge width on bridgeable water, including deep river water, shallow water and wet ground. Preserve original water/world river/road links. Deep lake/ocean water, lava, solid mountains and buildings remain obstacles; no tunnels. Whole-route failure leaves the map unchanged. Do not refuse river crossings as unsupported; offer routes/banks/existing dry passages only for actual obstacles. To reach ruins end outside, not inside; positions may shift during generation, so no guaranteed entrance connection. Accepted settings are not proof of generation success.
- Add: {""road_ops"":[{""op"":""add"",""road"":{""id"":""main_road"",""kind"":""AncientAsphaltRoad"",""route"":""avoid"",""points"":[[0,0.5],[1,0.5]]}}]}. Update: {""road_ops"":[{""op"":""update"",""id"":""main_road"",""changes"":{""kind"":""StoneRoad""}}]}. Only requested fields change; points replaces the entire waypoint array. Remove: {""road_ops"":[{""op"":""remove"",""id"":""main_road""}]}. Remove all by listing every current local_roads ID; road_ops:[] does nothing.
";
    }
}
