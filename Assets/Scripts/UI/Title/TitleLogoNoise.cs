using UnityEngine;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// CPU mirror of the noise and dissolve field in TitleLogoSDF.shader. Flakes use it to detach
    /// exactly where (and when) the text dissolves. Keep both files in sync.
    /// </summary>
    public static class TitleLogoNoise
    {
        /// <summary>Extra headroom in the cut so progress 1 always removes everything (shader: 1.02).</summary>
        public const float CutHeadroom = 1.02f;

        public static uint HashU(uint x, uint y, uint seed)
        {
            unchecked
            {
                uint h = (x * 0x8da6b343u) ^ (y * 0xd8163841u) ^ (seed * 0xcb1ab31fu);
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        public static float Hash01(int x, int y, uint seed)
        {
            unchecked
            {
                return (HashU((uint)x, (uint)y, seed) & 0xFFFFFFu) * (1f / 16777216f);
            }
        }

        public static float ValueNoise(Vector2 p, uint seed)
        {
            float ix = Mathf.Floor(p.x), iy = Mathf.Floor(p.y);
            float fx = p.x - ix, fy = p.y - iy;
            int cx = (int)ix, cy = (int)iy;
            float a = Hash01(cx, cy, seed);
            float b = Hash01(cx + 1, cy, seed);
            float d0 = Hash01(cx, cy + 1, seed);
            float d1 = Hash01(cx + 1, cy + 1, seed);
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);
            float top = a + (b - a) * ux;
            float bottom = d0 + (d1 - d0) * ux;
            return top + (bottom - top) * uy;
        }

        public static float Fbm(Vector2 p, uint seed)
        {
            float sum = 0f, amp = 0.5f;
            for (uint o = 0; o < 3; o++)
            {
                unchecked { sum += ValueNoise(p, seed + o * 101u) * amp; }
                p = p * 2.03f + new Vector2(17.13f, 17.13f);
                amp *= 0.5f;
            }
            return sum / 0.875f;
        }

        /// <summary>
        /// Shader TitlePixelize(): snaps effect-space position to a pixel grid anchored at the logo's
        /// bottom-left corner (q = (-aspect/2, -1/2)).
        /// </summary>
        public static Vector2 Pixelize(Vector2 q, bool pixelate, float density, float aspect)
        {
            if (!pixelate) return q;
            float cx = 0.5f * aspect, cy = 0.5f;
            return new Vector2(
                (Mathf.Floor((q.x + cx) * density) + 0.5f) / density - cx,
                (Mathf.Floor((q.y + cy) * density) + 0.5f) / density - cy);
        }

        /// <summary>Shader DissolveField(): 0..1, low values dissolve first.</summary>
        public static float DissolveField(Vector2 q, float angleDeg, float aspect, float directionBias, float noiseScale, uint seed)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float extent = 0.5f * (Mathf.Abs(dir.x) * aspect + Mathf.Abs(dir.y));
            float along = Mathf.Clamp01((Vector2.Dot(q, dir) + extent) / Mathf.Max(2f * extent, 1e-4f));
            float n = Fbm(q * noiseScale, seed);
            n = Mathf.Clamp01((n - 0.5f) * 1.8f + 0.5f);
            return n + (along - n) * directionBias;
        }

        /// <summary>
        /// Dissolve progress at which a point with field value <paramref name="field"/> is fully gone.
        /// Inverse of the shader's cut: gone when field &lt; progress * (1.02 + E + A) - A - E.
        /// </summary>
        public static float ReleaseProgress(float field, float emberWidth, float charWidth)
        {
            return (field + emberWidth + charWidth) / (CutHeadroom + emberWidth + charWidth);
        }
    }
}
