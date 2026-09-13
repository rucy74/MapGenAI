using System;
using System.Collections.Generic;
using MapGenAI.UI;

namespace MapGenAI.MapGen
{
    public static class MapStateCodec
    {
        public static string Serialize(TileMapState state) => SimpleJson.Serialize(
            new Dictionary<string, object> { { "schema_version", 2 }, { "state", state } });

        public static TileMapState Deserialize(string json)
        {
            var root = SimpleJson.Parse(json);
            TileMapState state;
            if (root.ContainsKey("schema_version"))
            {
                if (root.GetInt("schema_version") != 2) throw new FormatException("Unsupported preset version");
                var data = root.GetObject("state");
                if (data == null) throw new FormatException("Missing preset state");
                state = SimpleJson.ConvertTo<TileMapState>(data);
            }
            else
            {
                // Older presets wrote optional shape fields as null. Normalize only this
                // legacy file path; a null value in a new conversation remains an error.
                RemoveNulls(root);
                state = MapStateEditor.FromLegacySnapshot(MapParameterParser.Parse(root));
            }
            state.elevationShapes = state.elevationShapes ?? new List<ElevationShape>();
            state.mutators = state.mutators ?? new List<string>();
            state.removeMutators = state.removeMutators ?? new List<string>();
            state.rockTypes = state.rockTypes ?? new List<string>();
            if (state.elevationShapes.Exists(s => s == null)) throw new FormatException("Null terrain shape in preset");
            MapStateValidation.Validate(state);
            return state;
        }
        private static void RemoveNulls(SimpleJsonObject obj)
        {
            foreach (var key in new List<string>(obj.Keys))
            {
                if (obj.IsNull(key)) { obj.Values.Remove(key); continue; }
                var child = obj.GetObject(key);
                if (child != null) RemoveNulls(child);
                var children = obj.GetObjectArray(key);
                if (children != null) foreach (var item in children) RemoveNulls(item);
            }
        }
        public static List<string> ChangedFields(TileMapState before, TileMapState after)
        {
            var changes = new List<string>();
            foreach (var field in typeof(TileMapState).GetFields())
            {
                string a = SimpleJson.Serialize(field.GetValue(before));
                string b = SimpleJson.Serialize(field.GetValue(after));
                if (a != b) changes.Add(field.Name);
            }
            return changes;
        }
    }
}
