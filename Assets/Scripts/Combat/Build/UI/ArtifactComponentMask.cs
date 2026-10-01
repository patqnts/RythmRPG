using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>One moving dissolve stencil for an item's background, outline, icons and text.</summary>
    [DisallowMultipleComponent]
    public sealed class ArtifactComponentMask : UnityEngine.UI.BaseMeshEffect
    {
        private Material ownedMaterial;
        private Material originalMaterial;
        private ArtifactInterfaceStyle style;
        private float amount, phase, dissolve;
        private float meshMargin = -1f;
        private int? responseSeed;
        [SerializeField] private ArtifactGeometry transitionBand;

        public static ArtifactComponentMask Ensure(RectTransform target)
        {
            RectTransform surface = target;
            // Text buttons need a rectangular stencil above the label rather than its glyph silhouette.
            if (target.GetComponent<UnityEngine.UI.Graphic>() is TMPro.TMP_Text)
            {
                if (target.parent != null && target.parent.TryGetComponent<ArtifactComponentMask>(out var parentMask)) return parentMask;
                var wrapper = new GameObject("Component Surface", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                wrapper.layer = target.gameObject.layer;
                surface = (RectTransform)wrapper.transform;
                surface.SetParent(target.parent, false);
                surface.SetSiblingIndex(target.GetSiblingIndex());
                surface.anchorMin = target.anchorMin; surface.anchorMax = target.anchorMax;
                surface.pivot = target.pivot; surface.sizeDelta = target.sizeDelta;
                surface.anchoredPosition = target.anchoredPosition;
                surface.localScale = target.localScale; surface.localRotation = target.localRotation;
                target.SetParent(surface, false);
                target.anchorMin = Vector2.zero; target.anchorMax = Vector2.one;
                target.offsetMin = target.offsetMax = Vector2.zero;
                target.localScale = Vector3.one; target.localRotation = Quaternion.identity;
                wrapper.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            }
            if (surface.TryGetComponent<ArtifactComponentMask>(out var existing)) return existing;
            bool drawBackground = surface.GetComponent<UnityEngine.UI.Image>() != null && surface == target;
            var image = surface.GetComponent<UnityEngine.UI.Image>() ?? surface.gameObject.AddComponent<UnityEngine.UI.Image>();
            if (!drawBackground) { image.color = Color.white; image.raycastTarget = false; }
            var mask = surface.GetComponent<UnityEngine.UI.Mask>() ?? surface.gameObject.AddComponent<UnityEngine.UI.Mask>();
            mask.showMaskGraphic = drawBackground;
            var effect = surface.gameObject.AddComponent<ArtifactComponentMask>();
            effect.style = ArtifactInterfaceStyle.Load();
            effect.transitionBand = ArtifactInterfaceView.Decorate("Disintegration Edge", surface, ArtifactGeometry.Shape.ComponentBands, Color.white);
            effect.transitionBand.EnableLivingOutline(effect.style);
            effect.EnsureMaterial();
            return effect;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
        }

        private void EnsureMaterial()
        {
            if (!isActiveAndEnabled || UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics() || graphic == null) return;
            if (style == null) style = ArtifactInterfaceStyle.Load();
            if (ownedMaterial == null)
            {
                var shader = style.livingOutlineShader != null ? style.livingOutlineShader : Shader.Find("RythmRPG/UI/Artifact Living Outline");
                if (shader == null) return;
                originalMaterial = graphic.material;
                ownedMaterial = new Material(shader) { name = "Artifact component dissolve", hideFlags = HideFlags.HideAndDontSave };
                graphic.material = ownedMaterial;
                graphic.SetVerticesDirty();
            }
            SyncMaterials();
        }

        public void SetResponse(float ripple, float flow, float progress, ArtifactInterfaceStyle settings, int? noiseSeed = null)
        {
            amount = ripple; phase = flow; dissolve = progress; style = settings;
            responseSeed = noiseSeed;
            transitionBand?.SetEdgeResponse(ripple, flow, progress, settings, noiseSeed);
            if (ownedMaterial != null) SyncMaterials();
        }

        private void LateUpdate()
        {
            EnsureMaterial();
            if (ownedMaterial != null && !UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics() && !Mathf.Approximately(meshMargin, SurfaceMargin()))
                graphic.SetVerticesDirty();
        }

        private float SurfaceMargin() => (style != null ? style.outlineRipplePixels * Mathf.Max(1f, style.pokeNoiseAmount) : 18f) * 2f + 18f;

        private void SyncMaterials()
        {
            Apply(ownedMaterial);
            var stencil = graphic.materialForRendering;
            if (stencil != null && stencil != ownedMaterial) Apply(stencil);
            if (graphic.canvasRenderer.popMaterialCount > 0)
            {
                var clear = graphic.canvasRenderer.GetPopMaterial(0);
                if (clear != null && clear != ownedMaterial) Apply(clear);
            }
        }

        private void Apply(Material target)
        {
            Rect r = graphic.GetPixelAdjustedRect();
            target.SetFloat("_ComponentSurface", 1f);
            target.SetVector("_RectSize", new Vector4(r.width, r.height, 0, 0));
            target.SetFloat("_PokeAmount", amount);
            target.SetFloat("_PokePhase", phase);
            target.SetFloat("_WobblePixels", style.outlineRipplePixels);
            target.SetFloat("_GlitchAmount", dissolve);
            target.SetFloat("_Disintegrate", dissolve * .78f);
            target.SetFloat("_EdgeDepth", style.pokeEdgeDepthPixels);
            target.SetFloat("_PixelSize", style.pokePixelSize);
            target.SetFloat("_DirectionBias", 0f);
            target.SetFloat("_Pixelate", 1f);
            target.SetFloat("_EffectAspect", r.width / Mathf.Max(1f, r.height));
            style.ApplyEdgeAppearance(target, responseSeed);
        }

        public override void ModifyMesh(UnityEngine.UI.VertexHelper vh)
        {
            if (!IsActive() || ownedMaterial == null || vh.currentVertCount != 4) return;
            Rect r = graphic.GetPixelAdjustedRect();
            float margin = meshMargin = SurfaceMargin();
            for (int index = 0; index < 4; index++)
            {
                UnityEngine.UIVertex vertex = default;
                vh.PopulateUIVertex(ref vertex, index);
                Vector2 sign = new(vertex.position.x < r.center.x ? -1f : 1f, vertex.position.y < r.center.y ? -1f : 1f);
                Vector2 position = (Vector2)vertex.position + sign * margin;
                vertex.position = position;
                vertex.uv0 = position - r.center;
                vh.SetUIVertex(vertex, index);
            }
        }

        protected override void OnDisable()
        {
            if (ownedMaterial != null)
            {
                if (graphic != null && graphic.material == ownedMaterial) graphic.material = originalMaterial;
                if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
                ownedMaterial = null;
            }
            base.OnDisable();
        }
    }
}
