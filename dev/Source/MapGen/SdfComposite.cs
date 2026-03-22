using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MapGenAI.MapGen
{
    /// <summary>
    /// CSG+SDF 기반 자유 형태 지형 시스템.
    /// LLM이 기본 도형(circle/rect/tri/star/heart/ellipse/poly) + 불리언 연산(union/sub/inter)을
    /// 조합하여 어떤 모양이든 표현. C#이 SDF로 래스터라이즈.
    /// </summary>
    public static class SdfComposite
    {
        // ========== SDF 프리미티브 ==========

        /// <summary>원: length(p - center) - r</summary>
        public static float SdfCircle(Vector2 p, Vector2 center, float r)
        {
            return (p - center).magnitude - r;
        }

        /// <summary>타원: 스케일 보정 원</summary>
        public static float SdfEllipse(Vector2 p, Vector2 center, float halfW, float halfH)
        {
            Vector2 d = p - center;
            d.x /= halfW;
            d.y /= halfH;
            float len = d.magnitude;
            // 근사 SDF (정확한 타원 SDF는 비용이 큼)
            return (len - 1f) * Mathf.Min(halfW, halfH);
        }

        /// <summary>직사각형: Quilez box SDF</summary>
        public static float SdfRect(Vector2 p, Vector2 center, float halfW, float halfH)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - center.x) - halfW, Mathf.Abs(p.y - center.y) - halfH);
            float outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(d.x, d.y), 0f);
            return outside + inside;
        }

        /// <summary>삼각형: Quilez triangle SDF (iquilezles.org 원본)</summary>
        public static float SdfTriangle(Vector2 p, Vector2 v0, Vector2 v1, Vector2 v2)
        {
            Vector2 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
            Vector2 w0 = p - v0, w1 = p - v1, w2 = p - v2;

            // winding direction
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);

            // 각 변까지 거리 제곱
            float t0 = Mathf.Clamp01(Vector2.Dot(w0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq0 = w0 - e0 * t0;
            float t1 = Mathf.Clamp01(Vector2.Dot(w1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq1 = w1 - e1 * t1;
            float t2 = Mathf.Clamp01(Vector2.Dot(w2, e2) / Vector2.Dot(e2, e2));
            Vector2 pq2 = w2 - e2 * t2;

            float distSq = Mathf.Min(Vector2.Dot(pq0, pq0),
                Mathf.Min(Vector2.Dot(pq1, pq1), Vector2.Dot(pq2, pq2)));

            float signedMin = Mathf.Min(
                s * (w0.x * e0.y - w0.y * e0.x),
                Mathf.Min(
                    s * (w1.x * e1.y - w1.y * e1.x),
                    s * (w2.x * e2.y - w2.y * e2.x)));

            return -Mathf.Sqrt(distSq) * Mathf.Sign(signedMin);
        }

        /// <summary>N각 별: 접힌 세그먼트 SDF</summary>
        public static float SdfStar(Vector2 p, Vector2 center, float outerR, float innerR, int points)
        {
            if (points < 3) points = 5;
            Vector2 d = p - center;
            float angle = Mathf.Atan2(d.y, d.x);
            float segAngle = Mathf.PI / points;

            // 각도를 한 세그먼트로 접기
            angle = Mathf.Abs(((angle % (2f * segAngle)) + 2f * segAngle) % (2f * segAngle) - segAngle);

            // outer tip과 inner valley 좌표
            Vector2 outerTip = new Vector2(outerR * Mathf.Cos(0f), outerR * Mathf.Sin(0f));
            Vector2 innerValley = new Vector2(innerR * Mathf.Cos(segAngle), innerR * Mathf.Sin(segAngle));

            Vector2 q = new Vector2(d.magnitude * Mathf.Cos(angle), d.magnitude * Mathf.Sin(angle));

            // 선분 거리 (outerTip ~ innerValley)
            Vector2 seg = innerValley - outerTip;
            Vector2 w = q - outerTip;
            float t = Mathf.Clamp01(Vector2.Dot(w, seg) / Vector2.Dot(seg, seg));
            Vector2 closest = outerTip + seg * t;
            float dist = (q - closest).magnitude;

            // 내부/외부 판별
            float crossVal = seg.x * w.y - seg.y * w.x;
            return crossVal > 0f ? -dist : dist;
        }

        /// <summary>하트: Quilez heart SDF (iquilezles.org 원본)</summary>
        public static float SdfHeart(Vector2 p, Vector2 center, float size)
        {
            Vector2 d = (p - center) / size;
            d.x = Mathf.Abs(d.x);

            if (d.y + d.x > 1f)
                return (new Vector2(d.x - 0.25f, d.y - 0.75f).magnitude - Mathf.Sqrt(2f) / 4f) * size;

            float dot1 = Vector2.Dot(d - new Vector2(0f, 1f), d - new Vector2(0f, 1f));
            float maxXY = Mathf.Max(d.x + d.y, 0f);
            Vector2 proj = d - new Vector2(0.5f, 0.5f) * maxXY;
            float dot2 = Vector2.Dot(proj, proj);
            return Mathf.Sqrt(Mathf.Min(dot1, dot2)) * Mathf.Sign(d.x - d.y) * size;
        }

        /// <summary>일반 다각형: 각 변까지 최소 거리 + winding number</summary>
        public static float SdfPolygon(Vector2 p, Vector2[] verts)
        {
            int n = verts.Length;
            if (n < 3) return float.MaxValue;

            float minDist = float.MaxValue;
            float sign = 1f;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 e = verts[i] - verts[j];
                Vector2 w = p - verts[j];
                float t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                Vector2 closest = verts[j] + e * t - p;
                minDist = Mathf.Min(minDist, Vector2.Dot(closest, closest));

                // Winding number
                bool cond1 = p.y >= verts[j].y;
                bool cond2 = p.y < verts[i].y;
                bool cond3 = e.x * w.y > e.y * w.x;
                if ((cond1 && cond2 && cond3) || (!cond1 && !cond2 && !cond3))
                    sign = -sign;
            }

            return sign * Mathf.Sqrt(minDist);
        }

        // ========== 불리언 연산 ==========

        public static float OpUnion(float a, float b) => Mathf.Min(a, b);
        public static float OpSubtract(float a, float from) => Mathf.Max(-a, from);
        public static float OpIntersect(float a, float b) => Mathf.Max(a, b);

        public static float OpSmoothUnion(float a, float b, float k)
        {
            if (k <= 0f) return Mathf.Min(a, b);
            k *= 4f;
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f);
            return Mathf.Min(a, b) - h * h * 0.25f / k;
        }

        public static float OpSmoothSubtract(float a, float from, float k)
        {
            return -OpSmoothUnion(a, -from, k);
        }

        // ========== 유틸 ==========

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static float SegDistSq(Vector2 w, Vector2 e)
        {
            float t = Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            Vector2 d = w - e * t;
            return Vector2.Dot(d, d);
        }

        /// <summary>Hermite smoothstep</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // ========== 래스터라이저 ==========

        /// <summary>
        /// composite shape를 맵에 적용.
        /// shapes[] → SDF 함수 딕셔너리, compose[] → 불리언 조합 → 최종 래스터라이즈.
        /// </summary>
        public static void ApplyComposite(
            List<ShapePrimitive> shapes,
            List<ComposeOp> compose,
            Map map,
            MapGenFloatGrid elevGrid)
        {
            if (shapes == null || shapes.Count == 0 || compose == null || compose.Count == 0)
                return;

            float mapW = map.Size.x;
            float mapH = map.Size.z;

            // 1. 각 shape에 대한 SDF 함수 생성
            var sdfFuncs = new Dictionary<string, Func<Vector2, float>>();
            foreach (var s in shapes)
            {
                var func = BuildSdfFunc(s);
                if (func != null)
                    sdfFuncs[s.id] = func;
            }

            // 2. compose 체인 실행 — 각 add를 독립 래스터라이즈
            // add가 여러 개면 각각 별도로 맵에 적용 (하트+별 동시 등)
            var renderQueue = new List<System.Tuple<Func<Vector2, float>, float, float>>();

            foreach (var op in compose)
            {
                Func<Vector2, float> result = null;

                switch (op.op)
                {
                    case "add":
                        if (sdfFuncs.TryGetValue(op.s ?? "", out var addFunc))
                            result = addFunc;
                        break;

                    case "union":
                        if (GetSdf(op.a, sdfFuncs, out var ua) && GetSdf(op.b, sdfFuncs, out var ub))
                        {
                            var a = ua; var b = ub; var k = op.k;
                            result = k > 0f
                                ? (Func<Vector2, float>)(p => OpSmoothUnion(a(p), b(p), k))
                                : (p => OpUnion(a(p), b(p)));
                        }
                        break;

                    case "sub":
                        if (GetSdf(op.a, sdfFuncs, out var sa) && GetSdf(op.from, sdfFuncs, out var sf))
                        {
                            var a = sa; var f = sf; var k = op.k;
                            result = k > 0f
                                ? (Func<Vector2, float>)(p => OpSmoothSubtract(a(p), f(p), k))
                                : (p => OpSubtract(a(p), f(p)));
                        }
                        break;

                    case "inter":
                        if (GetSdf(op.a, sdfFuncs, out var ia) && GetSdf(op.b, sdfFuncs, out var ib))
                        {
                            var a = ia; var b = ib;
                            result = p => OpIntersect(a(p), b(p));
                        }
                        break;
                }

                if (result != null)
                {
                    if (!string.IsNullOrEmpty(op.outId))
                        sdfFuncs[op.outId] = result;

                    // e가 있는 add = 최종 래스터라이즈 대상
                    if (op.e != 0f)
                    {
                        float elev = op.e;
                        float fall = op.f > 0f ? op.f : 0.05f;
                        renderQueue.Add(System.Tuple.Create(result, elev, fall));
                    }
                }
            }

            if (renderQueue.Count == 0) return;

            // 3. 래스터라이즈 — 각 대상을 독립적으로 적용
            MapGenFloatGrid fertilityGrid = null;
            try { fertilityGrid = MapGenerator.Fertility; } catch { }

            foreach (var item in renderQueue)
            {
                var sdf = item.Item1;
                float elevation = item.Item2;
                float falloff = item.Item3;
                bool isWater = elevation < 0f;

                foreach (var cell in CellRect.WholeMap(map))
                {
                    Vector2 p = new Vector2(cell.x / mapW, cell.z / mapH);
                    float d = sdf(p);
                    float t = Smoothstep(falloff, 0f, d);

                    if (t < 0.01f) continue;

                    if (isWater && fertilityGrid != null)
                    {
                        if (t > 0.5f)
                        {
                            fertilityGrid[cell] = -2005f;
                            elevGrid[cell] = Mathf.Min(elevGrid[cell], 0.3f);
                        }
                        else if (t > 0.1f)
                        {
                            fertilityGrid[cell] = -1025f;
                            elevGrid[cell] = Mathf.Min(elevGrid[cell], 0.3f);
                        }
                        else if (t > 0.05f)
                        {
                            fertilityGrid[cell] = 1f;
                        }
                    }
                    else
                    {
                        elevGrid[cell] += t * elevation;
                    }
                }
            }
        }

        private static bool GetSdf(string id, Dictionary<string, Func<Vector2, float>> funcs, out Func<Vector2, float> func)
        {
            if (!string.IsNullOrEmpty(id) && funcs.TryGetValue(id, out func))
                return true;
            func = null;
            return false;
        }

        /// <summary>ShapePrimitive → SDF 함수 빌드</summary>
        private static Func<Vector2, float> BuildSdfFunc(ShapePrimitive s)
        {
            switch (s.prim)
            {
                case "circle":
                    return p => SdfCircle(p, s.GetCenter(), s.r);

                case "ellipse":
                    return p => SdfEllipse(p, s.GetCenter(), s.w * 0.5f, s.h * 0.5f);

                case "rect":
                    return p => SdfRect(p, s.GetCenter(), s.w * 0.5f, s.h * 0.5f);

                case "tri":
                    var tv = s.GetVerts();
                    if (tv == null || tv.Length < 3) return null;
                    return p => SdfTriangle(p, tv[0], tv[1], tv[2]);

                case "poly":
                    var pv = s.GetVerts();
                    if (pv == null || pv.Length < 3) return null;
                    return p => SdfPolygon(p, pv);

                case "star":
                    return p => SdfStar(p, s.GetCenter(), s.r, s.r2 > 0f ? s.r2 : s.r * 0.4f, s.n > 0 ? s.n : 5);

                case "heart":
                    return p => SdfHeart(p, s.GetCenter(), s.size > 0f ? s.size : 0.3f);

                default:
                    Log.Warning($"[MapGenAI] Unknown SDF primitive: {s.prim}");
                    return null;
            }
        }
    }

    // ========== 데이터 클래스 ==========

    public class ShapePrimitive
    {
        public string id;
        public string prim;     // circle, ellipse, rect, tri, poly, star, heart
        public float[] center;  // [x, z] normalized
        public float r;         // radius (circle, star)
        public float r2;        // inner radius (star)
        public int n;           // points (star)
        public float w;         // width (rect, ellipse)
        public float h;         // height (rect, ellipse)
        public float size;      // size (heart)
        public float[][] verts; // vertices (tri, poly)
        public float rot;       // rotation degrees

        public Vector2 GetCenter()
        {
            if (center != null && center.Length >= 2)
                return new Vector2(center[0], center[1]);
            return new Vector2(0.5f, 0.5f);
        }

        public Vector2[] GetVerts()
        {
            if (verts == null) return null;
            var result = new Vector2[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                if (verts[i] != null && verts[i].Length >= 2)
                    result[i] = new Vector2(verts[i][0], verts[i][1]);
            }
            return result;
        }
    }

    public class ComposeOp
    {
        public string op;      // add, union, sub, inter
        public string s;       // single shape/result ID (add)
        public string a;       // first operand (union/sub/inter)
        public string b;       // second operand (union)
        public string from;    // subtract target (sub)
        public string outId;   // result ID
        public float k;        // smooth blending radius
        public float e;        // elevation (final op)
        public float f = 0.05f;// falloff radius
    }
}
