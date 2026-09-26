using System;
using System.Collections.Generic;
using System.Globalization;
using MapGenAI.MapGen;

namespace MapGenAI.TestFixtures
{
    // Authored examples for native rendering, not provider-output or visual-quality evidence.
    // Every large area uses the same path primitive. Separate IDs preserve editing roles.
    public static class LandscapeCompositionFixtures
    {
        public static TileMapState Create(string kind)
        {
            var state = new TileMapState();
            switch (kind)
            {
                case "lakeside":
                    // This deliberately mountain-free example removes native hills explicitly.
                    // An attached floor cannot silently flatten protected hills to make room.
                    state.hillAmount=.1f;
                    // A modest lake is the reference; the broad native-ground floor sits beside it.
                    state.elevationShapes.Add(Path("lakeside_water", 271, .15f, .05f, .008f,
                        Points(.18f,.30f, .22f,.42f, .16f,.56f, .21f,.69f), "WaterShallow"));
                    var lakesidePlain = Path("lakeside_plain", 272, .45f, .05f, .018f,
                        Points(.50f,.32f, .47f,.44f, .52f,.57f, .51f,.69f));
                    lakesidePlain.anchor = "lakeside_water";
                    lakesidePlain.placement = "beside";
                    lakesidePlain.direction = "right";
                    lakesidePlain.edge_roughness = "low";
                    state.elevationShapes.Add(lakesidePlain);
                    break;

                case "winding_valley":
                    state.elevationShapes.Add(Path("valley_west_ridge", 381, .17f, 1.08f, .045f,
                        Points(.19f,.10f, .15f,.33f, .28f,.53f, .21f,.76f, .27f,.92f)));
                    state.elevationShapes.Add(Path("valley_east_ridge", 382, .21f, 1.12f, .050f,
                        Points(.78f,.11f, .68f,.32f, .80f,.53f, .71f,.76f, .79f,.92f)));
                    // Flatten after the ridges so the broad winding axis remains open.
                    state.elevationShapes.Add(Path("valley_plain", 383, .38f, .05f, .024f,
                        Points(.48f,.07f, .44f,.30f, .52f,.51f, .46f,.73f, .52f,.95f)));
                    state.elevationShapes.Add(Pond("valley_pond", 384, "valley_plain", "right", .085f, .060f));
                    break;

                case "branching_ridges":
                    // A wide foreground connects several unequal ridges, with no required water.
                    state.elevationShapes.Add(Path("branching_plain", 491, .48f, .05f, .026f,
                        Points(.25f,.27f, .47f,.38f, .73f,.29f)));
                    state.elevationShapes.Add(Path("branching_main_ridge", 492, .17f, 1.10f, .047f,
                        Points(.10f,.70f, .26f,.84f, .46f,.78f, .64f,.90f, .89f,.80f)));
                    state.elevationShapes.Add(Path("branching_west_spur", 493, .12f, 1.00f, .036f,
                        Points(.28f,.82f, .31f,.68f, .21f,.53f)));
                    state.elevationShapes.Add(Path("branching_east_spur", 494, .14f, 1.06f, .041f,
                        Points(.68f,.88f, .76f,.73f, .67f,.58f, .77f,.49f)));
                    break;

                case "open_basin":
                    // An open, asymmetric C-shaped ridge; no concentric circle or ring operation.
                    state.elevationShapes.Add(Path("basin_ridge", 607, .17f, 1.12f, .045f,
                        Points(.22f,.23f, .16f,.47f, .28f,.78f, .53f,.87f,
                               .77f,.75f, .83f,.55f, .75f,.37f)));
                    state.elevationShapes.Add(Path("basin_plain", 608, .40f, .05f, .022f,
                        Points(.44f,.13f, .44f,.34f, .48f,.56f, .60f,.63f)));
                    state.elevationShapes.Add(Pond("basin_pond", 609, "basin_plain", "top_left", .090f, .065f));
                    break;

                default:
                    throw new ArgumentException("Unknown landscape composition fixture: " + kind, nameof(kind));
            }
            return state;
        }

        static ElevationShape Path(string id, int variant, float width, float elevation, float feather,
            float[][] points, string fill = null)
        {
            return new ElevationShape
            {
                id = id, type = "composite", details = "natural", edge_roughness = "medium",
                variant = variant.ToString(CultureInfo.InvariantCulture),
                compositeShapes = new List<ShapePrimitive>
                {
                    new ShapePrimitive { id = "axis", prim = "path", verts = points, w = width }
                },
                // A floor has an absolute low elevation but no fill: keep its biome terrain.
                compositeOps = new List<ComposeOp>
                {
                    new ComposeOp { op = "add", s = "axis", e = elevation, f = feather, fill = fill }
                }
            };
        }

        static ElevationShape Pond(string id, int variant, string anchor, string direction, float width, float height)
        {
            return new ElevationShape
            {
                id = id, type = "composite", details = "natural", edge_roughness = "medium",
                variant = variant.ToString(CultureInfo.InvariantCulture),
                anchor = anchor, placement = "edge", direction = direction,
                compositeShapes = new List<ShapePrimitive>
                {
                    new ShapePrimitive { id = "water", prim = "ellipse", center = new[] { .5f, .5f }, w = width, h = height }
                },
                compositeOps = new List<ComposeOp>
                {
                    new ComposeOp { op = "add", s = "water", e = .05f, f = .008f, fill = "WaterShallow" }
                }
            };
        }

        static float[][] Points(params float[] coordinates)
        {
            var points = new float[coordinates.Length / 2][];
            for (int i = 0; i < points.Length; i++) points[i] = new[] { coordinates[i * 2], coordinates[i * 2 + 1] };
            return points;
        }
    }
}
