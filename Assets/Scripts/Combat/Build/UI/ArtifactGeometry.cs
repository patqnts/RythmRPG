using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Resolution-independent, translucent artifact tracery. No texture or generated bitmap is required.</summary>
    [AddComponentMenu("Rythm RPG/UI/Artifact Geometry")]
    public sealed class ArtifactGeometry : UnityEngine.UI.MaskableGraphic
    {
        public enum Shape { Frame, Sigil, Halo, Glass, PokeGlitch, ComponentBands }
        [SerializeField] private Shape shape;
        [SerializeField] private Color alternate = new(0.79f, 0.65f, 1f, 1f);
        public Shape Form { get => shape; set { shape = value; SetVerticesDirty(); } }
        public Color Secondary { get => alternate; set { alternate = value; SetVerticesDirty(); } }
        private Material outlineMaterial;
        [SerializeField] private bool livingOutline;
        private ArtifactInterfaceStyle responseStyle;
        private float reaction, reactionPhase, glitchAmount;
        private float meshMargin = -1f;
        private int? responseSeed;
        public float ReactionAmount => reaction;
        public void EnableLivingOutline(ArtifactInterfaceStyle style)
        {
            responseStyle = style;
            livingOutline = true;
            if (!UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics()) InitializeOutline();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (livingOutline && !UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics()) InitializeOutline();
        }

        private void LateUpdate()
        {
            if (livingOutline && outlineMaterial == null && !UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics()) InitializeOutline();
            if (outlineMaterial != null && !UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                var style = responseStyle != null ? responseStyle : ArtifactInterfaceStyle.Load();
                if (!Mathf.Approximately(meshMargin, OutlineMargin(style))) SetVerticesDirty();
            }
        }

        private static float OutlineMargin(ArtifactInterfaceStyle style) => style.outlineRipplePixels * 2f * Mathf.Max(1f, style.pokeNoiseAmount) + 18f;

        private void InitializeOutline()
        {
            if (!isActiveAndEnabled || !EnsureOutlineMaterial()) return;
            ApplyOutline(outlineMaterial);
            SetVerticesDirty();
        }

        public override Material GetModifiedMaterial(Material baseMaterial)
        {
            var result = base.GetModifiedMaterial(baseMaterial);
            if (outlineMaterial != null && baseMaterial == outlineMaterial) ApplyOutline(result);
            return result;
        }

        protected override void UpdateMaterial()
        {
            if (outlineMaterial != null) ApplyOutline(outlineMaterial);
            base.UpdateMaterial();
        }
        public void SetEdgeResponse(float amount, float phase, float glitch, ArtifactInterfaceStyle style, int? noiseSeed = null)
        {
            reaction = amount;
            reactionPhase = phase;
            glitchAmount = glitch;
            responseStyle = style;
            responseSeed = noiseSeed;
            if (outlineMaterial == null) return;
            ApplyOutline(outlineMaterial);
            // uGUI owns a cached stencil material beneath the outer dissolve mask.
            var stencil = materialForRendering;
            if (stencil != null && stencil != outlineMaterial) ApplyOutline(stencil);
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;
            if (shape == Shape.Frame || shape == Shape.ComponentBands) DrawFrame(vh, rect);
            else if (shape == Shape.Sigil) DrawSigil(vh, rect);
            else if (shape == Shape.Halo) DrawHalo(vh, rect);
            else if (shape == Shape.Glass) DrawGlass(vh, rect);
        }

        private bool EnsureOutlineMaterial()
        {
            if (outlineMaterial != null) return true;
            var style = responseStyle != null ? responseStyle : ArtifactInterfaceStyle.Load();
            var shader = style.livingOutlineShader != null ? style.livingOutlineShader : Shader.Find("RythmRPG/UI/Artifact Living Outline");
            if (shader == null) return false;
            outlineMaterial = new Material(shader) { name = "Artifact living outline", hideFlags = HideFlags.HideAndDontSave };
            material = outlineMaterial;
            return true;
        }

        private void ApplyOutline(Material target)
        {
            var style = responseStyle != null ? responseStyle : ArtifactInterfaceStyle.Load();
            Rect r = GetPixelAdjustedRect();
            target.SetFloat("_ComponentSurface", shape == Shape.ComponentBands ? 2f : 0f);
            target.SetVector("_RectSize", new Vector4(r.width, r.height, 0f, 0f));
            target.SetVector("_RectCenter", new Vector4(r.center.x, r.center.y, 0f, 0f));
            target.SetFloat("_PokeAmount", reaction);
            target.SetFloat("_PokePhase", reactionPhase);
            target.SetFloat("_WobblePixels", style.outlineRipplePixels);
            target.SetFloat("_GlitchAmount", glitchAmount);
            target.SetFloat("_Disintegrate", glitchAmount * .78f);
            target.SetFloat("_EdgeDepth", style.pokeEdgeDepthPixels);
            target.SetFloat("_DirectionBias", 0f);
            target.SetFloat("_Pixelate", 1f);
            target.SetFloat("_EffectAspect", r.width / Mathf.Max(1f, r.height));
            target.SetFloat("_PixelSize", style.pokePixelSize);
            style.ApplyEdgeAppearance(target, responseSeed);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (outlineMaterial == null) return;
            material = null;
            if (Application.isPlaying) Destroy(outlineMaterial);
            else DestroyImmediate(outlineMaterial);
            outlineMaterial = null;
        }

        private Color Tint(float t, float opacity = 1f)
        {
            Color result = Color.Lerp(color, alternate, Mathf.PingPong(t, 1f));
            result.a = color.a * opacity;
            return result;
        }

        private void DrawFrame(UnityEngine.UI.VertexHelper vh, Rect r)
        {
            if (outlineMaterial != null)
            {
                var style = responseStyle != null ? responseStyle : ArtifactInterfaceStyle.Load();
                float margin = meshMargin = OutlineMargin(style);
                Vector2 min = r.min - Vector2.one * margin, max = r.max + Vector2.one * margin;
                Vector2[] corners = { min, new(min.x, max.y), max, new(max.x, min.y) };
                foreach (var p in corners) vh.AddVert(p, color, p - r.center);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
                return;
            }
            r = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            const float step = 4f;
            Vector2[] points = {
                new(r.xMin + 8, r.yMin), new(r.xMax - 8, r.yMin),
                new(r.xMax - 8, r.yMin + step), new(r.xMax - step, r.yMin + step),
                new(r.xMax - step, r.yMin + 8), new(r.xMax, r.yMin + 8),
                new(r.xMax, r.yMax - 8), new(r.xMax - step, r.yMax - 8),
                new(r.xMax - step, r.yMax - step), new(r.xMax - 8, r.yMax - step),
                new(r.xMax - 8, r.yMax), new(r.xMin + 8, r.yMax),
                new(r.xMin + 8, r.yMax - step), new(r.xMin + step, r.yMax - step),
                new(r.xMin + step, r.yMax - 8), new(r.xMin, r.yMax - 8),
                new(r.xMin, r.yMin + 8), new(r.xMin + step, r.yMin + 8),
                new(r.xMin + step, r.yMin + step), new(r.xMin + 8, r.yMin + step) };
            Color white = new(1f, 1f, 1f, color.a);
            for (int i = 0; i < points.Length; i++) Line(vh, points[i], points[(i + 1) % points.Length], 4f, white);
        }

        private void DrawGlass(UnityEngine.UI.VertexHelper vh, Rect r)
        {
            // Quantized light pools give the glass a gelatinous depth without a texture or blur pass.
            const float cell = 16f;
            for (float y = r.yMin + 8f; y < r.yMax - 8f; y += cell)
                for (float x = r.xMin + 8f; x < r.xMax - 8f; x += cell)
                {
                    float u = (x - r.xMin) / r.width, v = (y - r.yMin) / r.height;
                    float pool = Mathf.Exp(-((u - .18f) * (u - .18f) * 9f + (v - .16f) * (v - .16f) * 7f));
                    float other = Mathf.Exp(-((u - .84f) * (u - .84f) * 12f + (v - .82f) * (v - .82f) * 10f));
                    float tide = Mathf.Clamp01((.13f + Mathf.Sin(u * 10f) * .04f - v) * 10f);
                    Color light = Color.Lerp(new Color(.55f, 1f, .84f), new Color(.82f, .75f, 1f), u);
                    light.a = Mathf.Round((pool * .42f + other * .28f + tide * .2f) * 32f) / 32f * color.a;
                    if (light.a > .001f)
                        Block(vh, new Rect(x, y, Mathf.Min(cell, r.xMax - 8f - x), Mathf.Min(cell, r.yMax - 8f - y)), light);
                }
            Color shine = new(1f, 1f, 1f, color.a * 1.6f);
            Block(vh, new Rect(r.xMin + 20f, r.yMax - 12f, Mathf.Min(80f, r.width * .22f), 4f), shine);
            Block(vh, new Rect(r.xMin + 12f, r.yMax - 36f, 4f, 16f), shine);
        }

        private static void Block(UnityEngine.UI.VertexHelper vh, Rect r, Color tint)
        {
            int index = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), tint, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), tint, Vector2.up);
            vh.AddVert(new Vector2(r.xMax, r.yMax), tint, Vector2.one);
            vh.AddVert(new Vector2(r.xMax, r.yMin), tint, Vector2.right);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }

        private void DrawSigil(UnityEngine.UI.VertexHelper vh, Rect r)
        {
            float radius = Mathf.Min(r.width, r.height) * 0.44f;
            Vector2 center = r.center;
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < 96; i++)
                {
                    if (ring != 1 && i % 24 > 19) continue;
                    float a = i * Mathf.PI * 2f / 96f;
                    float b = (i + 1) * Mathf.PI * 2f / 96f;
                    float rad = radius * (1f - ring * 0.12f);
                    Line(vh, center + Unit(a) * rad, center + Unit(b) * rad, ring == 1 ? 1.5f : 0.8f, Tint(i / 48f, ring == 1 ? 0.9f : 0.55f));
                }
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                Line(vh, center + Unit(a) * radius * 0.9f, center + Unit(a) * radius * 0.97f, 2f, Tint(i / 6f));
            }
            Vector2[] diamond = { center + Vector2.up * radius * 0.52f, center + Vector2.right * radius * 0.33f,
                center + Vector2.down * radius * 0.52f, center + Vector2.left * radius * 0.33f };
            for (int i = 0; i < 4; i++)
            {
                Line(vh, diamond[i], diamond[(i + 1) % 4], 1.8f, Tint(i / 2f));
                Line(vh, diamond[i], center, 0.8f, Tint(i / 2f, 0.45f));
            }
        }

        private void DrawHalo(UnityEngine.UI.VertexHelper vh, Rect r)
        {
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            Vector2 center = r.center;
            for (int i = 0; i < 64; i++)
            {
                int index = vh.currentVertCount;
                Color c = Tint(i / 32f, 0.16f), edge = c; edge.a = 0f;
                vh.AddVert(center, c, new Vector2(0.5f, 0.5f));
                vh.AddVert(center + Unit(i * Mathf.PI / 32f) * radius, edge, Vector2.zero);
                vh.AddVert(center + Unit((i + 1) * Mathf.PI / 32f) * radius, edge, Vector2.one);
                vh.AddTriangle(index, index + 1, index + 2);
            }
        }

        private static Vector2 Unit(float angle) => new(Mathf.Cos(angle), Mathf.Sin(angle));
        private static void Line(UnityEngine.UI.VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 d = (b - a).normalized;
            Vector2 normal = new Vector2(-d.y, d.x) * width * 0.5f;
            int index = vh.currentVertCount;
            vh.AddVert(a - normal, tint, Vector2.zero);
            vh.AddVert(a + normal, tint, Vector2.up);
            vh.AddVert(b + normal, tint, Vector2.one);
            vh.AddVert(b - normal, tint, Vector2.right);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
