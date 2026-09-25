using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// Title-screen logo finish for a TextMeshProUGUI label: ink letters with a grungy accent burst,
    /// hot core, halo swirl, and an animatable <see cref="TitleLogoBase.Disintegrate"/> (ember-edged dissolve
    /// whose pieces peel off as drifting flakes).
    ///
    /// Works with any TMP SDF font (assign it on the TMP component or in Font Asset here) and any colors.
    /// The component owns a runtime material instance (not saved), so it never edits your font's material.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    [AddComponentMenu("Rythm RPG/UI/Title Logo Text")]
    public sealed class TitleLogoText : TitleLogoBase
    {
        public const string ShaderName = "RythmRPG/UI/Title Logo SDF (URP)";

        [Tooltip("Optional. Any TMP SDF font asset. Overrides the font set on the TMP component.")]
        [SerializeField] TMP_FontAsset fontAsset;

        [Header("Burst Focus")]
        [Tooltip("Character index the burst centers on (0-based, spaces count). -1 = use Manual Focus.")]
        [SerializeField] int focusCharacter = 2;
        [Tooltip("Focus in text-block UV (0,0 bottom-left .. 1,1 top-right), used when Focus Character is -1.")]
        [SerializeField] Vector2 manualFocus = new Vector2(0.5f, 0.5f);
        [Tooltip("Extra offset from the focus, in text heights.")]
        [SerializeField] Vector2 focusOffset = Vector2.zero;

        TMP_Text text;
        TMP_FontAsset builtFont;
        bool mappingValid;
        readonly List<TMP_SubMeshUI> subMeshes = new List<TMP_SubMeshUI>();

        public TMP_Text Text => text ? text : (text = GetComponent<TMP_Text>());

        protected override string DefaultShaderName => ShaderName;
        protected override Texture FlakeTexture => text && text.font ? text.font.atlasTexture : null;
        protected override float PixelDensity => Mathf.Max(1f, look.pixelsPerTextHeight);

        /// <summary>Swap to any TMP SDF font. The material is rebuilt against the new atlas.</summary>
        public void SetFont(TMP_FontAsset font)
        {
            fontAsset = font;
            if (font && Text) Text.font = font;
            builtFont = null;
            RefreshLayout();
        }

        public void SetFocusCharacter(int index)
        {
            focusCharacter = index;
            MarkLayoutDirty();
        }

        // ------------------------------------------------------------------ lifecycle hooks

        protected override void OnLogoEnable()
        {
            text = GetComponent<TMP_Text>();
            text.OnPreRenderText += HandlePreRenderText;
            text.SetVerticesDirty();
        }

        protected override void OnLogoDisable()
        {
            if (text) text.OnPreRenderText -= HandlePreRenderText;
        }

        protected override void RestoreTarget()
        {
            if (text && text.font && text.fontSharedMaterial == mainMaterial)
                text.fontSharedMaterial = text.font.material;
        }

        protected override void OnPaddingChanged()
        {
            if (text) text.UpdateMeshPadding();
        }

        // ------------------------------------------------------------------ material

        protected override bool EnsureTarget()
        {
            if (!text) text = GetComponent<TMP_Text>();
            if (!text) return false;
            if (fontAsset && text.font != fontAsset) text.font = fontAsset;
            TMP_FontAsset font = text.font;
            if (!font || !ResolveShader()) return false;

            if (!mainMaterial)
            {
                mainMaterial = CreateOrReuseMaterial(text.fontSharedMaterial, "Title Logo");
                builtFont = null;
            }
            if (mainMaterial.shader != shader) mainMaterial.shader = shader;

            if (builtFont != font)
            {
                SyncAtlas(mainMaterial, font);
                if (flakeMaterial) SyncAtlas(flakeMaterial, font);
                builtFont = font;
                RefreshLayout();
            }

            if (text.fontSharedMaterial != mainMaterial) text.fontSharedMaterial = mainMaterial;
            // The whole effect lives in paragraph space so one burst spans every letter.
            if (text.horizontalMapping != TextureMappingOptions.Paragraph) text.horizontalMapping = TextureMappingOptions.Paragraph;
            if (text.verticalMapping != TextureMappingOptions.Paragraph) text.verticalMapping = TextureMappingOptions.Paragraph;
            return true;
        }

        protected override void OnFlakeMaterialCreated(Material m)
        {
            if (text && text.font) SyncAtlas(m, text.font);
        }

        protected override void ApplyExtra(Material m, bool isFlake, bool updateRatios)
        {
            // Scale ratios depend only on dilate / outline / glow widths; skip the per-frame allocation.
            if (updateRatios) ShaderUtilities.UpdateShaderRatios(m);
        }

        protected override void ApplyToExtraMaterials(float dissolve, bool updateRatios)
        {
            // Glyphs from fallback fonts / extra atlases render through sub-meshes with their own material copy.
            for (int i = 0; i < subMeshes.Count; i++)
            {
                TMP_SubMeshUI sub = subMeshes[i];
                if (!sub) continue;
                Material m = sub.sharedMaterial;
                if (m && m != mainMaterial && m.shader == shader) ApplyTo(m, false, dissolve, updateRatios);
            }
        }

        static void SyncAtlas(Material m, TMP_FontAsset font)
        {
            Material src = font.material;
            m.SetTexture(TitleLogoLook.Ids.MainTex, font.atlasTexture);
            m.SetFloat(TitleLogoLook.Ids.GradientScale, ReadOr(src, TitleLogoLook.Ids.GradientScale, font.atlasPadding + 1));
            m.SetFloat(TitleLogoLook.Ids.TextureWidth, font.atlasWidth);
            m.SetFloat(TitleLogoLook.Ids.TextureHeight, font.atlasHeight);
            m.SetFloat(TitleLogoLook.Ids.WeightNormal, ReadOr(src, TitleLogoLook.Ids.WeightNormal, font.normalStyle));
            m.SetFloat(TitleLogoLook.Ids.WeightBold, ReadOr(src, TitleLogoLook.Ids.WeightBold, font.boldStyle));
        }

        static float ReadOr(Material m, int id, float fallback) => m && m.HasProperty(id) ? m.GetFloat(id) : fallback;

        // ------------------------------------------------------------------ text layout

        void HandlePreRenderText(TMP_TextInfo info)
        {
            // Called inside TMP's mesh generation: gather data only; Graphic updates happen in LateUpdate.
            RecomputeMapping(info);
            MarkSourcesDirty();
            if (mainMaterial && text && text.font)
            {
                SyncAtlas(mainMaterial, text.font); // dynamic atlases can grow
                if (flakeMaterial) SyncAtlas(flakeMaterial, text.font);
                mainMaterial.SetFloat(TitleLogoLook.Ids.EffectAspect, aspect);
                mainMaterial.SetVector(TitleLogoLook.Ids.FocusPoint, focusUV);
            }
        }

        protected override void RecomputeLayout()
        {
            if (text) RecomputeMapping(text.textInfo);
        }

        void RecomputeMapping(TMP_TextInfo info)
        {
            mappingValid = false;
            if (info == null || info.characterCount == 0 || info.meshInfo == null) return;

            TMP_CharacterInfo[] chars = info.characterInfo;
            int count = Mathf.Min(info.characterCount, chars.Length);
            for (int i = 0; i < count; i++)
            {
                if (!chars[i].isVisible) continue;
                if (!TryGetQuad(info, chars[i], out Vector3 pBL, out Vector3 pTR, out _, out _, out Vector2 eBL, out Vector2 eTR, out _))
                    continue;
                float du = eTR.x - eBL.x, dv = eTR.y - eBL.y;
                if (Mathf.Abs(du) < 1e-6f || Mathf.Abs(dv) < 1e-6f) continue;
                float widthPerU = (pTR.x - pBL.x) / du;
                float heightPerV = (pTR.y - pBL.y) / dv;
                if (widthPerU <= 1e-6f || heightPerV <= 1e-6f) continue;
                aspect = widthPerU / heightPerV;
                heightLocal = heightPerV;
                mappingValid = true;
                break;
            }

            focusUV = manualFocus;
            if (focusCharacter >= 0 && count > 0)
            {
                int target = Mathf.Min(focusCharacter, count - 1);
                int found = -1;
                for (int i = target; i >= 0 && found < 0; i--) if (chars[i].isVisible) found = i;
                for (int i = target + 1; i < count && found < 0; i++) if (chars[i].isVisible) found = i;
                if (found >= 0 && TryGetQuad(info, chars[found], out _, out _, out _, out _, out Vector2 eBL, out Vector2 eTR, out _))
                    focusUV = (eBL + eTR) * 0.5f;
            }
            focusUV += new Vector2(focusOffset.x / Mathf.Max(aspect, 1e-4f), focusOffset.y);
        }

        static bool TryGetQuad(TMP_TextInfo info, in TMP_CharacterInfo c,
            out Vector3 pBL, out Vector3 pTR, out Vector4 aBL, out Vector4 aTR, out Vector2 eBL, out Vector2 eTR, out Color32 color)
        {
            pBL = pTR = default; aBL = aTR = default; eBL = eTR = default; color = default;
            if (c.materialReferenceIndex < 0 || c.materialReferenceIndex >= info.meshInfo.Length) return false;
            TMP_MeshInfo mesh = info.meshInfo[c.materialReferenceIndex];
            int v = c.vertexIndex;
            if (mesh.vertices == null || mesh.uvs0 == null || mesh.uvs2 == null || mesh.colors32 == null) return false;
            if (v < 0 || v + 3 >= mesh.vertices.Length || v + 3 >= mesh.uvs2.Length) return false;
            pBL = mesh.vertices[v];
            pTR = mesh.vertices[v + 2];
            aBL = mesh.uvs0[v];
            aTR = mesh.uvs0[v + 2];
            eBL = mesh.uvs2[v];
            eTR = mesh.uvs2[v + 2];
            color = mesh.colors32[v];
            return true;
        }

        // ------------------------------------------------------------------ flakes

        protected override void BuildFlakeSources()
        {
            subMeshes.Clear();
            GetComponentsInChildren(true, subMeshes);
            if (!text || !disintegration.flakes) return;

            TMP_TextInfo info = text.textInfo;
            if (info == null || info.characterCount == 0 || info.meshInfo == null || info.meshInfo.Length == 0) return;
            if (!mappingValid) RecomputeMapping(info);
            if (!mappingValid) return;

            // SDF alpha 0.5 is the glyph edge; 0.42 keeps thin strokes.
            TitleFlakeSystem.Context ctx = TitleFlakeSystem.MakeContext(look, disintegration, aspect, PixelDensity, FlakeTexture, 0.42f);
            TMP_CharacterInfo[] chars = info.characterInfo;
            int count = Mathf.Min(info.characterCount, chars.Length);
            for (int i = 0; i < count && flakeSystem.Count < disintegration.maxFlakes; i++)
            {
                TMP_CharacterInfo c = chars[i];
                if (!c.isVisible || c.materialReferenceIndex != 0) continue; // fallback-font glyphs: no flakes
                if (!TryGetQuad(info, c, out Vector3 pBL, out Vector3 pTR, out Vector4 aBL, out Vector4 aTR,
                        out Vector2 eBL, out Vector2 eTR, out Color32 color)) continue;
                flakeSystem.AddQuad(ctx, pBL, pTR, aBL, aTR, eBL, eTR, color);
            }
        }
    }
}
