using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.UI.Title
{
    /// <summary>Something that can fill the flake mesh of a <see cref="TitleLogoFlakes"/> graphic.</summary>
    public interface ITitleFlakeOwner
    {
        void FillFlakeMesh(VertexHelper vh);
    }

    /// <summary>
    /// Cuts a logo's quads (TMP glyph quads or Image quads) into a grid of chunks, works out when each chunk
    /// dissolves (same field as the shader), and turns the live ones into drifting flake quads.
    /// Shared by <see cref="TitleLogoText"/> and <see cref="TitleLogoImage"/>.
    /// </summary>
    public sealed class TitleFlakeSystem
    {
        struct FlakeSource
        {
            public Vector2 p0, p1;   // local-space rect of the chunk (BL, TR)
            public Vector2 a0, a1;   // texture UV rect
            public Vector2 e0, e1;   // effect UV rect
            public float uvW;        // uv0.w carried through (TMP SDF scale; 0 for sprites)
            public float release;    // dissolve progress at which this chunk is gone from the logo
            public float r0, r1, r2; // per-flake randoms
            public Color32 color;
        }

        /// <summary>Everything the grid needs, gathered once per rebuild.</summary>
        public struct Context
        {
            public float aspect;
            public float cell;           // chunk size in effect units (1 = logo height)
            public bool pixelate;
            public float pixelDensity;
            public uint seed;
            public TitleDisintegrateSettings settings;
            public Texture2D inkTexture; // readable texture to skip empty chunks, or null
            public float inkThreshold;
        }

        readonly List<FlakeSource> sources = new List<FlakeSource>();

        public int Count => sources.Count;

        public void Clear() => sources.Clear();

        /// <summary>
        /// Chunk size. With Pixelate + Match Pixel Grid, chunks are a whole number of logo pixels, as close
        /// as possible to Flakes Per Text Height.
        /// </summary>
        public static float CellSize(TitleLogoLook look, TitleDisintegrateSettings s, float pixelDensity)
        {
            float wanted = Mathf.Max(1f, s.flakesPerTextHeight);
            if (look.pixelate && s.matchPixelGrid && pixelDensity > 0f)
            {
                int pixelsPerChunk = Mathf.Max(1, Mathf.RoundToInt(pixelDensity / wanted));
                return pixelsPerChunk / pixelDensity;
            }
            return 1f / wanted;
        }

        public static Context MakeContext(TitleLogoLook look, TitleDisintegrateSettings s, float aspect,
            float pixelDensity, Texture texture, float inkThreshold)
        {
            var tex2D = texture as Texture2D;
            return new Context
            {
                aspect = aspect,
                cell = CellSize(look, s, pixelDensity),
                pixelate = look.pixelate,
                pixelDensity = Mathf.Max(1f, pixelDensity),
                seed = (uint)Mathf.Max(0, s.seed),
                settings = s,
                inkTexture = tex2D && tex2D.isReadable ? tex2D : null,
                inkThreshold = inkThreshold,
            };
        }

        /// <summary>
        /// Adds the chunks of one axis-aligned quad. <paramref name="aBL"/>/<paramref name="aTR"/> are its
        /// texture UVs (xy used, w carried), <paramref name="eBL"/>/<paramref name="eTR"/> its effect UVs (0..1),
        /// <paramref name="color"/> its vertex color (tint and alpha are carried onto the flakes).
        /// </summary>
        public void AddQuad(in Context ctx, Vector3 pBL, Vector3 pTR, Vector4 aBL, Vector4 aTR,
            Vector2 eBL, Vector2 eTR, Color32 color)
        {
            TitleDisintegrateSettings s = ctx.settings;
            if (sources.Count >= s.maxFlakes) return;

            float halfW = 0.5f * ctx.aspect;
            Vector2 qBL = new Vector2((eBL.x - 0.5f) * ctx.aspect, eBL.y - 0.5f);
            Vector2 qTR = new Vector2((eTR.x - 0.5f) * ctx.aspect, eTR.y - 0.5f);
            float qw = qTR.x - qBL.x, qh = qTR.y - qBL.y;
            if (qw < 1e-6f || qh < 1e-6f) return;

            float cell = ctx.cell;
            // Grid anchored at the logo's bottom-left corner (same as the shader's pixel grid).
            int x0 = Mathf.FloorToInt((qBL.x + halfW) / cell), x1 = Mathf.CeilToInt((qTR.x + halfW) / cell);
            int y0 = Mathf.FloorToInt((qBL.y + 0.5f) / cell), y1 = Mathf.CeilToInt((qTR.y + 0.5f) / cell);

            for (int gy = y0; gy < y1; gy++)
            for (int gx = x0; gx < x1; gx++)
            {
                if (sources.Count >= s.maxFlakes) return;
                if (TitleLogoNoise.Hash01(gx, gy, ctx.seed + 901u) > s.flakeChance) continue;

                float cx0 = Mathf.Max(gx * cell - halfW, qBL.x), cx1 = Mathf.Min((gx + 1) * cell - halfW, qTR.x);
                float cy0 = Mathf.Max(gy * cell - 0.5f, qBL.y), cy1 = Mathf.Min((gy + 1) * cell - 0.5f, qTR.y);
                if (cx1 - cx0 < cell * 0.2f || cy1 - cy0 < cell * 0.2f) continue; // skip slivers

                float sx0 = (cx0 - qBL.x) / qw, sx1 = (cx1 - qBL.x) / qw;
                float sy0 = (cy0 - qBL.y) / qh, sy1 = (cy1 - qBL.y) / qh;
                var a0 = new Vector2(Mathf.Lerp(aBL.x, aTR.x, sx0), Mathf.Lerp(aBL.y, aTR.y, sy0));
                var a1 = new Vector2(Mathf.Lerp(aBL.x, aTR.x, sx1), Mathf.Lerp(aBL.y, aTR.y, sy1));
                if (ctx.inkTexture && !HasInk(ctx.inkTexture, a0, a1, ctx.inkThreshold)) continue;

                var qCenter = new Vector2((cx0 + cx1) * 0.5f, (cy0 + cy1) * 0.5f);
                float field = TitleLogoNoise.DissolveField(
                    TitleLogoNoise.Pixelize(qCenter, ctx.pixelate, ctx.pixelDensity, ctx.aspect),
                    s.directionAngle, ctx.aspect, s.directionBias, s.noiseScale, ctx.seed);

                sources.Add(new FlakeSource
                {
                    p0 = new Vector2(Mathf.Lerp(pBL.x, pTR.x, sx0), Mathf.Lerp(pBL.y, pTR.y, sy0)),
                    p1 = new Vector2(Mathf.Lerp(pBL.x, pTR.x, sx1), Mathf.Lerp(pBL.y, pTR.y, sy1)),
                    a0 = a0,
                    a1 = a1,
                    e0 = new Vector2(Mathf.Lerp(eBL.x, eTR.x, sx0), Mathf.Lerp(eBL.y, eTR.y, sy0)),
                    e1 = new Vector2(Mathf.Lerp(eBL.x, eTR.x, sx1), Mathf.Lerp(eBL.y, eTR.y, sy1)),
                    uvW = aBL.w,
                    release = TitleLogoNoise.ReleaseProgress(field, s.emberWidth, s.charWidth),
                    r0 = TitleLogoNoise.Hash01(gx, gy, ctx.seed + 913u),
                    r1 = TitleLogoNoise.Hash01(gx, gy, ctx.seed + 927u),
                    r2 = TitleLogoNoise.Hash01(gx, gy, ctx.seed + 941u),
                    color = color,
                });
            }
        }

        static bool HasInk(Texture2D texture, Vector2 a0, Vector2 a1, float threshold)
        {
            // Sample a few points so thin strokes / single pixels are not missed.
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                float u = Mathf.Lerp(a0.x, a1.x, 0.2f + 0.3f * x);
                float v = Mathf.Lerp(a0.y, a1.y, 0.2f + 0.3f * y);
                if (texture.GetPixelBilinear(u, v).a > threshold) return true;
            }
            return false;
        }

        /// <summary>
        /// Emits the flakes alive at <paramref name="progress"/> (0..1, same value as the component's Disintegrate).
        /// <paramref name="heightLocal"/> is the logo height in the graphic's local units.
        /// </summary>
        public void Fill(VertexHelper vh, float progress, float heightLocal, TitleLogoLook look,
            TitleDisintegrateSettings s, float pixelDensity)
        {
            if (sources.Count == 0 || progress <= 0f || !s.flakes) return;

            float life = Mathf.Max(0.01f, s.flakeLifetime);
            float t = progress * (1f + life);
            float h = heightLocal;
            bool pixelate = look.pixelate;
            float snap = pixelate ? h / Mathf.Max(1f, pixelDensity) : 0f;
            float spread = s.windSpreadDegrees * Mathf.Deg2Rad;

            UIVertex vert = UIVertex.simpleVert;
            vert.normal = new Vector3(0f, 0f, -1f);
            vert.tangent = new Vector4(-1f, 0f, 0f, 1f);

            for (int i = 0; i < sources.Count; i++)
            {
                FlakeSource src = sources[i];
                float age = (t - src.release) / life;
                if (age <= 0f || age >= 1f) continue;
                if (vh.currentVertCount > 64000) break;

                float travel = Mathf.Pow(age, 1.6f);
                Vector2 wind = Rotate(s.wind, (src.r0 - 0.5f) * 2f * spread) * Mathf.Lerp(0.6f, 1.4f, src.r1);
                Vector2 offset = wind * (h * travel);
                float phase = src.r2 * 6.2831853f;
                offset += new Vector2(Mathf.Sin(age * 9f + phase), Mathf.Cos(age * 7f + phase * 1.3f))
                          * (h * s.turbulence * 0.25f * age);
                if (snap > 0f)
                {
                    offset.x = Mathf.Round(offset.x / snap) * snap;
                    offset.y = Mathf.Round(offset.y / snap) * snap;
                }

                Vector2 center = (src.p0 + src.p1) * 0.5f + offset;
                Vector2 half = (src.p1 - src.p0) * (0.5f * (1f - s.shrink * age));
                float rotation = pixelate ? 0f : (src.r2 - 0.5f) * 2f * s.spin * age * Mathf.PI * 2f;
                float cos = Mathf.Cos(rotation), sin = Mathf.Sin(rotation);

                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, age));
                vert.color = new Color32(src.color.r, src.color.g, src.color.b, (byte)Mathf.RoundToInt(src.color.a * fade));

                int start = vh.currentVertCount;
                AddCorner(vh, ref vert, src, center, half, cos, sin, 0f, 0f, age);
                AddCorner(vh, ref vert, src, center, half, cos, sin, 0f, 1f, age);
                AddCorner(vh, ref vert, src, center, half, cos, sin, 1f, 1f, age);
                AddCorner(vh, ref vert, src, center, half, cos, sin, 1f, 0f, age);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start + 2, start + 3, start);
            }
        }

        static void AddCorner(VertexHelper vh, ref UIVertex vert, in FlakeSource src, Vector2 center, Vector2 half,
            float cos, float sin, float u, float v, float age)
        {
            float lx = (u * 2f - 1f) * half.x;
            float ly = (v * 2f - 1f) * half.y;
            vert.position = new Vector3(center.x + lx * cos - ly * sin, center.y + lx * sin + ly * cos, 0f);
            vert.uv0 = new Vector4(Mathf.Lerp(src.a0.x, src.a1.x, u), Mathf.Lerp(src.a0.y, src.a1.y, v), age, src.uvW);
            vert.uv1 = new Vector4(Mathf.Lerp(src.e0.x, src.e1.x, u), Mathf.Lerp(src.e0.y, src.e1.y, v), 0f, 0f);
            vh.AddVert(vert);
        }

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
