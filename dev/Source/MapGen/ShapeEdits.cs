using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MapGenAI.UI;
using UnityEngine;

namespace MapGenAI.MapGen
{
    public sealed class ShapeEdit
    {
        public string op, id, relativeTo, relation;
        public SimpleJsonObject values;
        public float distance;
        public float[] position;
    }

    // Edits operate on an isolated candidate. Missing targets and invalid geometry reject the entire patch.
    public static class ShapeEdits
    {
        public const int MaxShapes = 32;
        static readonly string[] TextFields = { "type", "direction", "strength", "position", "size", "gap", "fill", "fade", "noise_amount", "edge_roughness", "region", "region_part", "coverage", "scope", "landform", "variant", "opening", "layout", "details", "anchor", "placement" };

        public static void AssignIds(List<ElevationShape> shapes)
        {
            var used = new HashSet<string>();
            foreach (var shape in shapes)
                if (!string.IsNullOrEmpty(shape.id) && !used.Add(shape.id)) throw new FormatException("Duplicate terrain ID: " + shape.id);
            int next = 1;
            foreach (var shape in shapes)
                if (string.IsNullOrEmpty(shape.id))
                {
                    while (used.Contains("terrain_" + next)) next++;
                    shape.id = "terrain_" + next++;
                    used.Add(shape.id);
                }
        }

        public static List<Dictionary<string, object>> Describe(IEnumerable<ElevationShape> shapes)
        {
            var copies = shapes.Select(s => s.Clone()).ToList();
            AssignIds(copies);
            return copies.Select(ToObject).ToList();
        }

        public static Dictionary<string, object> ToObject(ElevationShape shape)
        {
            var result = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(shape.id)) result["id"] = shape.id;
            foreach (var name in TextFields)
            {
                var value = (string)typeof(ElevationShape).GetField(name).GetValue(shape);
                if (value != null) result[name] = value;
            }
            if (shape.compositeShapes != null) result["shapes"] = shape.compositeShapes;
            if (shape.compositeOps != null) result["compose"] = shape.compositeOps;
            if (shape.points != null) result["points"] = shape.points;
            if (shape.width != 0) result["width"] = shape.width;
            return result;
        }

        public static ElevationShape ParseShape(SimpleJsonObject obj)
        {
            if (obj == null) throw new FormatException("Expected terrain shape");
            var shape = new ElevationShape { id = obj.GetString("id") };
            foreach (var key in obj.Keys)
                if (key != "id" && !TextFields.Contains(key) && key != "shapes" && key != "compose" && key != "points" && key != "width")
                    throw new FormatException("Unsupported terrain field: " + key);
            foreach (var name in TextFields)
            {
                if (!obj.ContainsKey(name)) continue;
                string value = obj.GetString(name);
                if (name == "position" && value == null && !obj.IsNull(name))
                {
                    var pair = obj.GetFloatArray(name);
                    ValidatePair(pair);
                    value = PairText(pair[0], pair[1]);
                }
                if (value == null && name != "fill" && name!="anchor" && name!="placement" && !(name=="opening" && shape.type=="landform")) throw new FormatException("Invalid terrain field: " + name);
                typeof(ElevationShape).GetField(name).SetValue(shape, value);
            }
            if (shape.type == "composite")
            {
                shape.compositeShapes = MapParameterParser.ParseCompositeShapes(obj);
                shape.compositeOps = MapParameterParser.ParseCompositeOps(obj);
            }
            else if (obj.ContainsKey("shapes") || obj.ContainsKey("compose")) throw new FormatException("Only composite terrain accepts shapes/compose");
            if(obj.ContainsKey("points") || obj.ContainsKey("width"))
            {
                if(shape.type!="passage")throw new FormatException("points/width require passage");
                shape.points=obj.GetNestedFloatArray("points");shape.width=obj.GetInt("width");
            }
            ShapeValidation.Validate(shape);
            return shape;
        }

        public static List<ShapeEdit> Parse(SimpleJsonObject obj)
        {
            var array = obj.GetObjectArray("shape_ops");
            if (array == null) throw new FormatException("shape_ops must be an array");
            if (array.Count > 32) throw new FormatException("Too many terrain edits (maximum 32)");
            var result = new List<ShapeEdit>();
            foreach (var item in array)
            {
                var edit = new ShapeEdit { op = item.GetString("op"), id = item.GetString("id") };
                switch (edit.op)
                {
                    case "add": edit.values = item.GetObject("shape"); ParseShape(edit.values); break;
                    case "update":
                        edit.values = item.GetObject("changes");
                        if (edit.values == null) throw new FormatException("update requires changes");
                        if (edit.values.ContainsKey("id") || edit.values.ContainsKey("type")) throw new FormatException("An update cannot change terrain id or type");
                        break;
                    case "remove": break;
                    case "move":
                        edit.position = item.GetFloatArray("position");
                        edit.relativeTo = item.GetString("relative_to");
                        edit.relation = item.GetString("relation");
                        edit.distance = item.GetFloat("distance", .2f);
                        if (edit.position != null) ValidatePair(edit.position);
                        if ((edit.position == null) == string.IsNullOrEmpty(edit.relativeTo)) throw new FormatException("move requires either position or relative_to");
                        break;
                    default: throw new FormatException("Unknown terrain operation: " + edit.op);
                }
                if (edit.op != "add" && string.IsNullOrEmpty(edit.id)) throw new FormatException("Terrain edit requires target id");
                var allowed = new HashSet<string> { "op", "id" };
                if (edit.op == "add") allowed.Add("shape");
                if (edit.op == "update") allowed.Add("changes");
                if (edit.op == "move") foreach (string key in new[] { "position", "relative_to", "relation", "distance" }) allowed.Add(key);
                foreach (string key in item.Keys) if (!allowed.Contains(key)) throw new FormatException("Unsupported operation field: " + key);
                result.Add(edit);
            }
            return result;
        }

        public static void Apply(List<ElevationShape> shapes, List<ShapeEdit> edits)
        {
            if (edits == null || edits.Count == 0) return;
            AssignIds(shapes);
            foreach (var edit in edits)
            {
                if (edit.op == "add")
                {
                    var added = ParseShape(edit.values);
                    // Default only NEW additions. Loading/patching a legacy snapshot must not
                    // change its mountains, source mask, dependent fill or ruin placement.
                    if(added.type=="landform" && added.layout==null)added.layout="organic";
                    if(added.type=="landform" && added.details==null)added.details="natural";
                    // Opt in only NEW rough-edged freshwater areas; old saves, exact geometry,
                    // updates and explicit details:none retain their previous behavior.
                    if(added.type=="composite" && added.details==null &&
                        ContourWarp.Amount(added.edge_roughness)>0 &&
                        added.compositeOps.Any(o=>LandscapeBlendField.OrdinaryFreshwater(added.fill ?? o.fill) || (added.fill ?? o.fill)=="water"))
                        added.details="natural";
                    if (shapes.Any(s => s.id == added.id && added.id != null)) throw new FormatException("Terrain ID already exists: " + added.id);
                    shapes.Add(added); AssignIds(shapes);
                }
                else
                {
                    var index = shapes.FindIndex(s => s.id == edit.id);
                    if (index < 0) throw new FormatException("Terrain target does not exist: " + edit.id);
                    var target = shapes[index];
                    switch (edit.op)
                    {
                        case "remove": shapes.RemoveAt(index); break;
                        case "update":
                            var fields = ToObject(target);
                            foreach (string key in edit.values.Keys) fields[key] = edit.values.Values[key];
                            if(target.type=="landform" && edit.values.ContainsKey("landform") && edit.values.GetString("landform")!="open_basin" && !edit.values.ContainsKey("opening"))fields.Remove("opening");
                            shapes[index] = ParseShape(SimpleJson.Parse(SimpleJson.Serialize(fields)));
                            break;
                        case "move": Move(target, edit, shapes); break;
                    }
                }
                if (shapes.Count > MaxShapes) throw new FormatException("Too many terrain shapes (maximum 32)");
            }
        }

        static Vector2 Center(ElevationShape shape)
        {
            if (shape.type == "bump" || shape.type == "ring" || shape.type == "landform") return ElevationShape.ParsePosition(shape.position);
            if (shape.type == "passage") return new Vector2(shape.points.Average(p=>p[0]),shape.points.Average(p=>p[1]));
            if (shape.type != "composite") throw new FormatException("Only bump, ring and composite terrain can be moved by position");
            var points = shape.compositeShapes.SelectMany(p => p.prim == "tri" || p.prim == "poly" || p.prim == "path" ? p.verts : new[] { p.center ?? new[] { .5f, .5f } }).ToList();
            return new Vector2(points.Average(p => p[0]), points.Average(p => p[1]));
        }

        static void Move(ElevationShape shape, ShapeEdit edit, List<ElevationShape> shapes)
        {
            if(shape.anchor!=null)throw new FormatException("This area follows its anchor; change direction/placement, or remove both anchor and placement before an absolute move");
            Vector2 old = Center(shape), next;
            if (edit.position != null) next = new Vector2(edit.position[0], edit.position[1]);
            else
            {
                var anchor = shapes.Find(s => s.id == edit.relativeTo);
                if (anchor == null || anchor == shape) throw new FormatException("Invalid relative terrain target");
                ShapeValidation.Range(edit.distance, .01f, 1f, "move distance");
                next = Center(anchor);
                switch (edit.relation)
                {
                    case "left_of": next.x -= edit.distance; break;
                    case "right_of": next.x += edit.distance; break;
                    case "above": next.y += edit.distance; break;
                    case "below": next.y -= edit.distance; break;
                    default: throw new FormatException("Unsupported terrain relation: " + edit.relation);
                }
            }
            ValidatePair(new[] { next.x, next.y });
            if(shape.type=="passage")
            {
                var delta=next-old;foreach(var point in shape.points){point[0]+=delta.x;point[1]+=delta.y;}
            }
            else if (shape.type == "composite")
            {
                var delta = next - old;
                foreach (var part in shape.compositeShapes)
                    if (part.prim == "tri" || part.prim == "poly" || part.prim == "path")
                        foreach (var vertex in part.verts) { vertex[0] += delta.x; vertex[1] += delta.y; }
                    else { var center = part.GetCenter() + delta; part.center = new[] { center.x, center.y }; }
            }
            else shape.position = PairText(next.x, next.y);
            shape.autoHills = false;
            ShapeValidation.Validate(shape);
        }

        internal static string PairText(float x, float z) => x.ToString("R", CultureInfo.InvariantCulture) + "," + z.ToString("R", CultureInfo.InvariantCulture);
        internal static void ValidatePair(float[] pair)
        {
            if (pair == null || pair.Length != 2) throw new FormatException("Position must contain exactly two coordinates");
            foreach (float value in pair) ShapeValidation.Range(value, 0, 1, "position");
        }
    }
}
