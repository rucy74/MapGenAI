using System;
using System.Collections.Generic;
using UnityEngine;
namespace MapGenAI.MapGen
{
    // This suite tests state and legacy pure geometry, not the engine's noise library.
    // Native profiles MUST run in the real RimWorld probe; never silently fake them here.
    public sealed class NativeWaterField
    {
        public readonly float Shelf,Shore;
        public NativeWaterField(Func<Vector2,float> sdf,List<ShapePrimitive> parts,float cols,float rows,float roughness,string identity)
        {throw new NotSupportedException("Native water generation requires the RimWorld runtime probe.");}
        public float Sample(Vector2 p)=>throw new NotSupportedException();
    }
}
