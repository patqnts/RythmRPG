using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// The title logo finish for a UI <see cref="Image"/> or <see cref="RawImage"/> (a painted or pixel-art logo,
    /// a character splash...). Same accent burst, grunge, swirl, pixelate and <see cref="TitleLogoBase.Disintegrate"/>
    /// with flakes as <see cref="TitleLogoText"/>, but the shape comes from the sprite's alpha.
    ///
    /// Works with every Image type (Simple, Sliced, Tiled, Filled), atlased sprites and "Use Sprite Mesh".
    /// The component owns a runtime material instance (not saved) and gives the Image its old material back
    /// when disabled or removed.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    [AddComponentMenu("Rythm RPG/UI/Title Logo Image")]
    public sealed class TitleLogoImage : TitleLogoBase, IMeshModifier
    {
        public const string ShaderName = "RythmRPG/UI/Title Logo Sprite (URP)";

        [Header("Burst Focus")]
        [Tooltip("Burst center on the drawn image (0,0 bottom-left .. 1,1 top-right).")]
        [SerializeField] Vector2 focus = new Vector2(0.5f, 0.55f);

        [SerializeField] TitleSpriteSettings sprite = new TitleSpriteSettings();

        struct Quad
        {
            public Vector3 pBL, pTR;
            public Vector4 aBL, aTR;
            public Vector2 eBL, eTR;
            public Color32 color;
        }

        Graphic graphic;
        readonly List<Quad> quads = new List<Quad>();
        readonly List<UIVertex> verts = new List<UIVertex>();
        Rect drawnRect;
        bool hasMesh;
        float texelsPerLocal;

        public Graphic Graphic => graphic ? graphic : (graphic = GetComponent<Graphic>());

        /// <summary>Sprite-only settings (ink replace, keep shading, pixel outline...). Edits apply next frame.</summary>
        public TitleSpriteSettings Sprite => sprite;

        public void SetFocus(Vector2 imageUV)
        {
            focus = imageUV;
            MarkLayoutDirty();
        }

        protected override string DefaultShaderName => ShaderName;
        protected override Texture FlakeTexture => Graphic ? graphic.mainTexture : null;

        /// <summary>The sprite's own pixel density when Match Sprite Pixels is on, else Look > Pixels Per Text Height.</summary>
        protected override float PixelDensity =>
            look.pixelate && sprite.matchSpritePixels && texelsPerLocal > 0f
                ? Mathf.Max(1f, heightLocal * texelsPerLocal)
                : Mathf.Max(1f, look.pixelsPerTextHeight);

        /// <summary>Presets keep Pixelate on if it was on (sprites usually want their pixel grid).</summary>
        public override void ApplyLook(TitleLogoLook preset)
        {
            if (preset == null) return;
            bool wasPixelated = look.pixelate;
            base.ApplyLook(preset);
            look.pixelate = wasPixelated || preset.pixelate;
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            look = TitleLogoLook.SpriteDefault();
            disintegration.flakesPerTextHeight = 60f; // an image is usually much taller than a text line
        }
#endif

        // ------------------------------------------------------------------ target graphic

        protected override void OnLogoEnable()
        {
            graphic = GetComponent<Graphic>();
            if (graphic) graphic.SetVerticesDirty();
        }

        protected override void OnLogoDisable()
        {
            RestoreTarget();
            if (graphic) graphic.SetVerticesDirty();
        }

        protected override void RestoreTarget()
        {
            if (graphic && mainMaterial && graphic.material == mainMaterial) graphic.material = null;
        }

        protected override bool EnsureTarget()
        {
            if (!Graphic || !ResolveShader()) return false;

            if (!mainMaterial) mainMaterial = CreateOrReuseMaterial(graphic.material, "Title Logo");
            if (mainMaterial.shader != shader) mainMaterial.shader = shader;
            if (graphic.material != mainMaterial) graphic.material = mainMaterial;

            // The effect UV travels in TEXCOORD1.
            Canvas canvas = graphic.canvas;
            if (canvas)
            {
                const AdditionalCanvasShaderChannels needed = AdditionalCanvasShaderChannels.TexCoord1;
                if ((canvas.additionalShaderChannels & needed) != needed) canvas.additionalShaderChannels |= needed;
                Canvas root = canvas.rootCanvas;
                if (root && (root.additionalShaderChannels & needed) != needed) root.additionalShaderChannels |= needed;
            }
            return true;
        }

        protected override void ApplyExtra(Material m, bool isFlake, bool updateRatios) => sprite.ApplyTo(m);

        // ------------------------------------------------------------------ mesh

#pragma warning disable 0618, 0672
        public void ModifyMesh(Mesh mesh)
        {
            using (var vh = new VertexHelper(mesh))
            {
                ModifyMesh(vh);
                vh.FillMesh(mesh);
            }
        }
#pragma warning restore 0618, 0672

        /// <summary>Writes the effect UV (0..1 over the drawn image) into TEXCOORD1 and captures quads for flakes.</summary>
        public void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;

            quads.Clear();
            verts.Clear();
            int n = vh.currentVertCount;
            hasMesh = n > 0;
            if (!hasMesh)
            {
                MarkSourcesDirty();
                return;
            }

            UIVertex v = default;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                verts.Add(v);
                min = Vector2.Min(min, v.position);
                max = Vector2.Max(max, v.position);
            }

            var image = graphic as Image;
            bool filled = image && image.type == Image.Type.Filled;
            // Filled images keep a stable pattern while filling: map over the full rect, not the partial mesh.
            Rect r = filled ? graphic.GetPixelAdjustedRect() : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            if (r.width < 1e-4f || r.height < 1e-4f) return;

            for (int i = 0; i < n; i++)
            {
                v = verts[i];
                v.uv1 = new Vector4((v.position.x - r.xMin) / r.width, (v.position.y - r.yMin) / r.height, 0f, 0f);
                vh.SetUIVertex(v, i);
                verts[i] = v;
            }

            drawnRect = r;
            CaptureQuads(n, image, filled);

            // Push layout now so the first frame is already right; LateUpdate re-applies.
            RecomputeLayout();
            if (mainMaterial)
            {
                mainMaterial.SetFloat(TitleLogoLook.Ids.EffectAspect, aspect);
                mainMaterial.SetVector(TitleLogoLook.Ids.FocusPoint, focusUV);
                mainMaterial.SetFloat(TitleLogoLook.Ids.PixelDensity, PixelDensity);
            }
            MarkSourcesDirty();
        }

        void CaptureQuads(int n, Image image, bool filled)
        {
            // Image builds Simple / Sliced / Tiled meshes from axis-aligned quads (BL, TL, TR, BR).
            bool quadLayout = n % 4 == 0 && !filled && !(image && image.useSpriteMesh && image.type == Image.Type.Simple);
            for (int q = 0; quadLayout && q < n; q += 4)
            {
                Vector3 bl = verts[q].position, tl = verts[q + 1].position, tr = verts[q + 2].position, br = verts[q + 3].position;
                quadLayout = Mathf.Approximately(bl.x, tl.x) && Mathf.Approximately(tr.x, br.x) &&
                             Mathf.Approximately(bl.y, br.y) && Mathf.Approximately(tl.y, tr.y);
            }

            if (quadLayout)
            {
                for (int q = 0; q < n; q += 4)
                    AddCapturedQuad(verts[q], verts[q + 2]);
            }
            else
            {
                // Tight sprite mesh / radial fill: UVs are linear in position, so use the mesh's overall box.
                UIVertex lo = verts[0], hi = verts[0];
                for (int i = 1; i < n; i++)
                {
                    UIVertex v = verts[i];
                    lo.position = Vector3.Min(lo.position, v.position);
                    hi.position = Vector3.Max(hi.position, v.position);
                    lo.uv0 = Vector4.Min(lo.uv0, v.uv0);
                    hi.uv0 = Vector4.Max(hi.uv0, v.uv0);
                    lo.uv1 = Vector4.Min(lo.uv1, v.uv1);
                    hi.uv1 = Vector4.Max(hi.uv1, v.uv1);
                }
                AddCapturedQuad(lo, hi);
            }

            // Sprite texels per local unit, measured on the first (unstretched corner) quad.
            texelsPerLocal = 0f;
            Texture tex = graphic.mainTexture;
            if (tex && quads.Count > 0)
            {
                Quad first = quads[0];
                float h = first.pTR.y - first.pBL.y;
                if (h > 1e-4f) texelsPerLocal = Mathf.Abs(first.aTR.y - first.aBL.y) * tex.height / h;
            }
        }

        void AddCapturedQuad(in UIVertex bl, in UIVertex tr)
        {
            if (tr.position.x - bl.position.x < 1e-4f || tr.position.y - bl.position.y < 1e-4f) return;
            quads.Add(new Quad
            {
                pBL = bl.position,
                pTR = tr.position,
                aBL = bl.uv0,
                aTR = tr.uv0,
                eBL = bl.uv1,
                eTR = tr.uv1,
                color = bl.color,
            });
        }

        protected override void RecomputeLayout()
        {
            if (hasMesh && drawnRect.height > 1e-4f)
            {
                aspect = drawnRect.width / drawnRect.height;
                heightLocal = drawnRect.height;
            }
            focusUV = focus;
        }

        protected override void BuildFlakeSources()
        {
            if (!disintegration.flakes || quads.Count == 0) return;
            TitleFlakeSystem.Context ctx = TitleFlakeSystem.MakeContext(
                look, disintegration, aspect, PixelDensity, FlakeTexture, sprite.flakeAlphaThreshold);
            for (int i = 0; i < quads.Count && flakeSystem.Count < disintegration.maxFlakes; i++)
            {
                Quad q = quads[i];
                flakeSystem.AddQuad(ctx, q.pBL, q.pTR, q.aBL, q.aTR, q.eBL, q.eTR, q.color);
            }
        }
    }
}
