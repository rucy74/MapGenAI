// UnityShim.cs — Unity 타입 스텁 (테스트 전용)
// MapGenParams.cs가 참조하는 UnityEngine 타입만 최소 구현.

using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public const float PI = (float)Math.PI;
        public static float Sign(float value) => value >= 0 ? 1f : -1f;
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Pow(float a, float b) => (float)Math.Pow(a,b);
        public static int RoundToInt(float value) => (int)Math.Round(value);

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        public static float Abs(float f) => Math.Abs(f);

        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;

        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;

        public static bool Approximately(float a, float b)
        {
            return Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);
        }

        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Exp(float power) => (float)Math.Exp(power);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sin(float f) => (float)Math.Sin(f);

        public static float PerlinNoise(float x, float y)
        {
            // 간이 구현 — 테스트에서 노이즈 값 자체를 검증하지 않으므로 0.5 고정
            return 0.5f;
        }

        /// <summary>t를 0~length 범위에서 반복 (Mathf.Repeat). 음수도 처리.</summary>
        public static float Repeat(float t, float length)
        {
            return t - (float)Math.Floor(t / length) * length;
        }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public float magnitude => (float)Math.Sqrt(x*x+y*y);
        public static float Dot(Vector2 a, Vector2 b) => a.x*b.x+a.y*b.y;
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x+b.x,a.y+b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a, float b) => new Vector2(a.x*b,a.y*b);
        public static Vector2 operator /(Vector2 a, float b) => new Vector2(a.x/b,a.y/b);

        public override string ToString() => $"({x:F2}, {y:F2})";

        public override bool Equals(object obj) =>
            obj is Vector2 v && Math.Abs(x - v.x) < 0.0001f && Math.Abs(y - v.y) < 0.0001f;

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

        public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
        public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);
    }
}
