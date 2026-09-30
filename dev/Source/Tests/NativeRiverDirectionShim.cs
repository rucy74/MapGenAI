namespace MapGenAI.MapGen
{
    // Pure tests have no spherical world graph. Real inference is covered by the recorded-response runtime replay.
    public static class NativeRiverDirection { public static float Angle(int tile) => -1f; }
}
