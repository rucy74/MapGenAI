using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MapGenAI.MapGen
{
    // Bounds also cap SDF expression depth/work before any terrain or world state is changed.
    public static class ShapeValidation
    {
        static readonly HashSet<string> Types = new HashSet<string> { "ridge", "slope", "split", "radial", "bump", "noise", "ring", "composite", "region_fill", "passage", "landform" };
        static readonly HashSet<string> Fills = new HashSet<string> { "water", "sand", "soil", "rich_soil", "marsh", "mud", "ice" };
        const string Positions = "center,top_left,top,top_right,left,right,bottom_left,bottom,bottom_right";

        public static void Validate(ElevationShape shape)
        {
            if (shape == null || shape.type == null || !Types.Contains(shape.type)) throw new FormatException("Unknown terrain type: " + shape?.type);
            if (shape.id != null) Id(shape.id);
            if(shape.anchor!=null || shape.placement!=null)
            {
                if(shape.type!="composite")throw new FormatException("anchor/placement require composite geometry");
                Id(shape.anchor);
                if(shape.placement!="inside" && shape.placement!="edge" && shape.placement!="beside")throw new FormatException("placement must be inside, edge or beside");
            }
            if(shape.details!=null && shape.details!="none" && shape.details!="natural")throw new FormatException("details must be none or natural");
            if(shape.type=="landform")
            {
                if(shape.landform!="open_basin" && shape.landform!="winding_valley" && shape.landform!="foothills")throw new FormatException("landform must be open_basin, winding_valley or foothills");
                if(shape.layout!=null && shape.layout!="classic" && shape.layout!="organic")throw new FormatException("landform layout must be classic or organic");
                if(shape.details!=null && shape.details!="none" && shape.details!="natural")throw new FormatException("landform details must be none or natural");
                if(shape.variant!=null && (!int.TryParse(shape.variant,NumberStyles.None,CultureInfo.InvariantCulture,out var variant) || variant<0 || variant>999999))throw new FormatException("variant must be an integer from 0 to 999999");
                Semantic(shape.direction,"left,right,top,bottom,top_left,top_right,bottom_left,bottom_right",0,360,"direction");
                Semantic(shape.size,"small,medium,large",.35f,1,"landform size");
                if(shape.gap!=null)Range(Number(shape.gap),.1f,.32f,"landform gap");
                if(shape.opening!=null)
                {
                    if(shape.landform!="open_basin")throw new FormatException("opening requires open_basin");
                    Range(Number(shape.opening),.08f,.3f,"basin opening");
                }
                if(shape.position!=null && !Positions.Split(',').Contains(shape.position))
                    ShapeEdits.ValidatePair(shape.position.Trim('[',']',' ').Split(',').Select(Number).ToArray());
                if(shape.strength!=null || shape.fill!=null || shape.fade!=null || shape.noise_amount!=null || shape.edge_roughness!=null || shape.region!=null || shape.region_part!=null || shape.coverage!=null || shape.points!=null || shape.width!=0 || shape.scope!=null || shape.compositeShapes!=null || shape.compositeOps!=null)
                    throw new FormatException("landform uses landform/layout/details/variant/position/size/direction/gap/opening; use region_fill for floor materials");
                return;
            }
            if(shape.landform!=null || (shape.variant!=null && shape.type!="composite") || shape.opening!=null || shape.layout!=null || (shape.details!=null && shape.type!="composite"))throw new FormatException("landform/layout/opening require landform geometry; details/variant also support composite");
            if(shape.variant!=null && (!int.TryParse(shape.variant,NumberStyles.None,CultureInfo.InvariantCulture,out var compositeVariant) || compositeVariant<0 || compositeVariant>999999))throw new FormatException("variant must be an integer from 0 to 999999");
            Fill(shape.fill);
            if(shape.type=="passage")
            {
                if(shape.points==null || shape.points.Length<2 || shape.points.Length>32)throw new FormatException("passage requires 2..32 points");
                Range(shape.width,1,64,"passage width in cells");
                foreach(var point in shape.points)ShapeEdits.ValidatePair(point);
                for(int i=1;i<shape.points.Length;i++)if(shape.points[i].SequenceEqual(shape.points[i-1]))throw new FormatException("passage has duplicate adjacent points");
                if(string.IsNullOrEmpty(shape.fill))throw new FormatException("passage requires a dry ground fill");
                if(shape.scope!=null && shape.scope!="full" && shape.scope!="mountains")throw new FormatException("passage scope must be full or mountains");
                Semantic(shape.edge_roughness,"none,low,medium,high",0,1,"edge_roughness");
                if(shape.region!=null || shape.region_part!=null || shape.coverage!=null || shape.direction!=null || shape.strength!=null || shape.position!=null || shape.size!=null || shape.gap!=null || shape.fade!=null || shape.noise_amount!=null || shape.compositeShapes!=null || shape.compositeOps!=null)
                    throw new FormatException("passage uses points, width, dry fill, scope and optional edge_roughness");
                return;
            }
            if(shape.points!=null || shape.width!=0 || shape.scope!=null)throw new FormatException("points/width/scope require passage");
            if(shape.type=="region_fill")
            {
                Id(shape.region);
                if(shape.region_part!="inside" && shape.region_part!="enclosed")throw new FormatException("region_part must be inside or enclosed");
                if(string.IsNullOrEmpty(shape.fill) || shape.coverage==null)throw new FormatException("region_fill requires fill and coverage");
                Range(Number(shape.coverage),0,1,"coverage");
                if(shape.direction!=null && !new[]{"left","right","top","bottom"}.Contains(shape.direction))throw new FormatException("region_fill direction must be left/right/top/bottom");
                if(shape.strength!=null || shape.position!=null || shape.size!=null || shape.gap!=null || shape.fade!=null || shape.noise_amount!=null || shape.edge_roughness!=null || shape.compositeShapes!=null || shape.compositeOps!=null)
                    throw new FormatException("region_fill follows its source region; do not supply independent geometry or height");
                return;
            }
            if(shape.region!=null || shape.region_part!=null || shape.coverage!=null)throw new FormatException("region/region_part/coverage require region_fill");
            if (!string.IsNullOrEmpty(shape.fill) && shape.type != "bump" && shape.type != "ring" && shape.type != "composite")
                throw new FormatException("This terrain type cannot apply fill: " + shape.type);
            Semantic(shape.direction, "left,right,top,bottom,top_left,top_right,bottom_left,bottom_right", 0, 360, "direction");
            Semantic(shape.strength, "weak,medium,strong,negative_weak,negative_medium,negative_strong", -2, 2, "strength");
            Semantic(shape.size, "small,medium,large", .001f, 1, "size");
            Semantic(shape.gap, "tiny,small,medium,large", .001f, .5f, "gap");
            Semantic(shape.fade, "small,medium,large", .001f, 1, "fade");
            Semantic(shape.noise_amount, "none,low,medium,high", 0, 1.5f, "noise_amount");
            Semantic(shape.edge_roughness, "none,low,medium,high", 0, 1, "edge_roughness");
            if (shape.edge_roughness != null && shape.type != "composite")
                throw new FormatException("edge_roughness requires composite geometry");
            if (shape.position != null && !Positions.Split(',').Contains(shape.position))
            {
                var pair = shape.position.Trim('[',']',' ').Split(',');
                if (pair.Length != 2) throw new FormatException("Invalid terrain position");
                ShapeEdits.ValidatePair(pair.Select(Number).ToArray());
            }
            if (shape.type != "composite") return;
            if (shape.compositeShapes == null || shape.compositeShapes.Count == 0 || shape.compositeShapes.Count > 32)
                throw new FormatException("Composite requires 1 to 32 primitives");
            if (shape.compositeOps == null || shape.compositeOps.Count == 0 || shape.compositeOps.Count > 32)
                throw new FormatException("Composite requires 1 to 32 operations");
            var cost = new Dictionary<string, int>();
            foreach (var part in shape.compositeShapes)
            {
                if (part == null) throw new FormatException("Null primitive");
                Id(part.id);
                if (cost.ContainsKey(part.id)) throw new FormatException("Duplicate primitive ID: " + part.id);
                cost[part.id] = 1;
                if (part.center != null) ShapeEdits.ValidatePair(part.center);
                Range(part.rot, -360, 360, "rotation");
                switch (part.prim)
                {
                    case "circle": Range(part.r, .001f, 1, "radius"); break;
                    case "star":
                        Range(part.r, .001f, 1, "radius"); Range(part.r2, 0, part.r, "inner radius");
                        if (part.n != 0 && (part.n < 3 || part.n > 32)) throw new FormatException("Star points must be 3 to 32");
                        break;
                    case "rect": case "ellipse":
                        Range(part.w, .001f, 2, "width"); Range(part.h, .001f, 2, "height"); break;
                    case "path":
                        Range(part.w,.008f,.7f,"path width");
                        if(part.verts==null || part.verts.Length<2 || part.verts.Length>12)throw new FormatException("path requires 2..12 ordered control points");
                        foreach(var point in part.verts)ShapeEdits.ValidatePair(point);
                        for(int i=1;i<part.verts.Length;i++)if(part.verts[i].SequenceEqual(part.verts[i-1]))throw new FormatException("path has duplicate adjacent points");
                        cost[part.id]=(part.verts.Length-1)*8;
                        break;
                    case "heart": Range(part.size, 0, 1, "heart size"); break;
                    case "tri": case "poly": ValidatePolygon(part.verts, part.prim == "tri"); break;
                    default: throw new FormatException("Unknown primitive: " + part.prim);
                }
            }
            bool renders = !string.IsNullOrEmpty(shape.fill);
            foreach (var op in shape.compositeOps)
            {
                if (op == null) throw new FormatException("Null composite operation");
                int work;
                switch (op.op)
                {
                    case "add": work = Cost(cost, op.s); break;
                    case "union": case "inter": work = Cost(cost, op.a) + Cost(cost, op.b); break;
                    case "sub": work = Cost(cost, op.a) + Cost(cost, op.from); break;
                    default: throw new FormatException("Unknown composite operation: " + op.op);
                }
                // Repeated unions of the same prior result otherwise grow exponentially per cell.
                if (work > 128) throw new FormatException("Composite expression is too complex");
                if (!string.IsNullOrEmpty(op.outId))
                {
                    Id(op.outId);
                    if (cost.ContainsKey(op.outId)) throw new FormatException("Composite output ID already exists: " + op.outId);
                    cost[op.outId] = work;
                }
                Range(op.k, 0, .5f, "blend radius"); Range(op.f, .001f, .5f, "falloff"); Range(op.e, -2, 2, "elevation");
                Fill(op.fill);
                renders |= op.e != 0 || !string.IsNullOrEmpty(op.fill);
            }
            if (!renders) throw new FormatException("Composite has no terrain-producing operation");
        }

        static int Cost(Dictionary<string,int> cost, string id)
        {
            if (id == null || !cost.TryGetValue(id, out var result)) throw new FormatException("Missing composite operand: " + id);
            return result;
        }
        static void ValidatePolygon(float[][] vertices, bool triangle)
        {
            if (vertices == null || vertices.Length < 3 || vertices.Length > 64 || (triangle && vertices.Length != 3)) throw new FormatException("Invalid polygon vertex count");
            double area = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i]; var b = vertices[(i+1) % vertices.Length];
                ShapeEdits.ValidatePair(a); ShapeEdits.ValidatePair(b);
                if (a[0] == b[0] && a[1] == b[1]) throw new FormatException("Polygon has a zero-length edge");
                area += a[0] * b[1] - b[0] * a[1];
            }
            if (Math.Abs(area) < .000001) throw new FormatException("Polygon has zero area");
            for(int i=0;i<vertices.Length;i++) for(int j=i+1;j<vertices.Length;j++)
            {
                if(j==i+1 || (i==0 && j==vertices.Length-1)) continue;
                if(Intersects(vertices[i],vertices[(i+1)%vertices.Length],vertices[j],vertices[(j+1)%vertices.Length]))
                    throw new FormatException("Polygon has intersecting edges");
            }
        }
        static bool Intersects(float[] a,float[] b,float[] c,float[] d)
        {
            if(Math.Max(a[0],b[0]) < Math.Min(c[0],d[0]) || Math.Max(c[0],d[0]) < Math.Min(a[0],b[0]) ||
               Math.Max(a[1],b[1]) < Math.Min(c[1],d[1]) || Math.Max(c[1],d[1]) < Math.Min(a[1],b[1])) return false;
            return Cross(a,b,c)*Cross(a,b,d)<=0 && Cross(c,d,a)*Cross(c,d,b)<=0;
        }
        static double Cross(float[] a,float[] b,float[] c) => ((double)b[0]-a[0])*(c[1]-a[1])-((double)b[1]-a[1])*(c[0]-a[0]);
        static void Id(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
                throw new FormatException("Invalid terrain ID");
        }
        static void Fill(string fill)
        {
            if (!string.IsNullOrEmpty(fill) && !Fills.Contains(fill) && (fill.Length > 128 || fill.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'))))
                throw new FormatException("Invalid terrain material name: " + fill);
        }
        static float Number(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || float.IsNaN(number) || float.IsInfinity(number))
                throw new FormatException("Invalid terrain number: " + value);
            return number;
        }
        static void Semantic(string value, string names, float min, float max, string label)
        {
            if (value == null || names.Split(',').Contains(value)) return;
            Range(Number(value), min, max, label);
        }
        public static void Range(float value, float min, float max, string label)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new FormatException(label + " outside supported range " + min.ToString(CultureInfo.InvariantCulture) + ".." + max.ToString(CultureInfo.InvariantCulture));
        }
    }
}
