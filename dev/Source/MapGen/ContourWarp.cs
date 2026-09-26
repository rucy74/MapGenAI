using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MapGenAI.MapGen
{
    // Shared smooth coordinates preserve relationships between CSG operands and holes.
    // Uses no global RNG; Mono and offline tests execute the same noise implementation.
    public sealed class ContourWarp
    {
        readonly Vector2 anchor;
        readonly float scale, amplitude;
        readonly uint seed;

        public static float Amount(string value)
        {
            switch (value)
            {
                case null: case "none": return 0;
                case "low": return .35f;
                case "medium": return .65f;
                case "high": return 1;
                default: return float.Parse(value, CultureInfo.InvariantCulture);
            }
        }

        public ContourWarp(List<ShapePrimitive> parts, string id, float amount)
        {
            anchor = Center(parts[0]);
            float span = .002f;
            foreach (var part in parts) span = Mathf.Max(span, Span(part));
            scale = span;
            amplitude = scale * .09f * amount / 3;
            uint hash = 2166136261u;
            unchecked { foreach (char c in id ?? "terrain") hash = (hash ^ c) * 16777619u; }
            seed = hash;
        }

        public Vector2 Sample(Vector2 point)
        {
            // Three small deformations allow more visible small bays without folding the field.
            for (int step = 0; step < 3; step++)
            {
                var local = (point - anchor) / scale;
                point += new Vector2(Field(local.x, local.y, seed), Field(local.x, local.y, seed ^ 0x9e3779b9u)) * amplitude;
            }
            return point;
        }

        static Vector2 Center(ShapePrimitive part)
        {
            if ((part.prim != "poly" && part.prim != "tri" && part.prim != "path") || part.verts == null) return part.GetCenter();
            var sum = new Vector2();
            foreach (var p in part.verts) sum += new Vector2(p[0], p[1]);
            return sum / part.verts.Length;
        }

        static float Span(ShapePrimitive part)
        {
            switch (part.prim)
            {
                case "path": return part.w;
                case "circle": case "star": return 2 * part.r;
                case "ellipse": case "rect": return Mathf.Max(part.w, part.h);
                case "heart": return 2 * (part.size > 0 ? part.size : .3f);
                default:
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var p in part.verts)
                    { minX = Mathf.Min(minX, p[0]); minY = Mathf.Min(minY, p[1]); maxX = Mathf.Max(maxX, p[0]); maxY = Mathf.Max(maxY, p[1]); }
                    return Mathf.Max(maxX - minX, maxY - minY);
            }
        }

        // Each step's displacement Jacobian norm <= 6*(.09/3)*(4+.3*10)/1.3 < 1.
        // Cubic value noise derivative <=3 per axis. Rasterization can still lose sub-cell details.
        static float Field(float x, float y, uint hash) =>
            (Noise(x * 4 + 17.3f, y * 4 + 9.1f, hash) + .3f * Noise(x * 10 - 3.7f, y * 10 + 2.8f, hash ^ 0x85ebca6bu)) / 1.3f;

        internal static float Noise(float x, float y, uint hash)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float tx = x - ix, ty = y - iy;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            return Mathf.Lerp(Mathf.Lerp(Value(ix, iy, hash), Value(ix + 1, iy, hash), tx),
                Mathf.Lerp(Value(ix, iy + 1, hash), Value(ix + 1, iy + 1, hash), tx), ty);
        }

        static float Value(int x, int y, uint hash)
        {
            unchecked
            {
                uint h = hash ^ ((uint)x * 0x8da6b343u) ^ ((uint)y * 0xd8163841u);
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (h & 0x00ffffffu) / 8388607.5f - 1;
            }
        }
    }
}
