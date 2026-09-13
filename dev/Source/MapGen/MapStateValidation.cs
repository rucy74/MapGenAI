using System;

namespace MapGenAI.MapGen
{
    public static class MapStateValidation
    {
        public static void Validate(TileMapState state)
        {
            if (state == null) throw new FormatException("Missing map state");
            state.imageMap?.Validate();
            ShapeValidation.Range(state.hillAmount,.1f,1.6f,"hill_amount");
            ShapeValidation.Range(state.vegetationDensity,0,2,"vegetation_density");
            ShapeValidation.Range(state.animalDensity,0,2,"animal_density");
            ShapeValidation.Range(state.fertilityOffset,-1,1,"fertility_offset");
            ShapeValidation.Range(state.riverDirectionAngle,-1,360,"river angle");
            ShapeValidation.Range(state.riverXPosition,0,1,"river x");
            ShapeValidation.Range(state.riverZPosition,0,1,"river z");
            ShapeValidation.Range(state.geyserCount,-1,20,"geysers");
            ShapeValidation.Range(state.hillSize,.005f,.1f,"hill_size");
            ShapeValidation.Range(state.hillSmoothness,.5f,6,"hill_smoothness");
            ShapeValidation.Range(state.rockCount,-1,15,"rock_count");
            ShapeValidation.Range(state.oreDensity,0,2.5f,"ore_density");
            ShapeValidation.Range(state.ruinDensity,0,2.5f,"ruin_density");
            ShapeValidation.Range(state.dangerDensity,0,2.5f,"danger_density");
            if (state.elevationShapes.Count > ShapeEdits.MaxShapes) throw new FormatException("Too many terrain shapes");
            if (state.removeFeatureCategories.Count > 64 || state.removeFeatureCategories.Exists(string.IsNullOrWhiteSpace))
                throw new FormatException("Invalid suppressed feature categories");
            foreach (var shape in state.elevationShapes) ShapeValidation.Validate(shape);
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var shape in state.elevationShapes)
                if (!string.IsNullOrEmpty(shape.id) && !ids.Add(shape.id)) throw new FormatException("Duplicate terrain ID");
        }
    }
}
