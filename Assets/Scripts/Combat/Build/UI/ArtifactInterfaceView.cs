using RythmRPG.UI.Title;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Coordinates the actual title-menu dissolve across one artifact projection.</summary>
    public sealed class ArtifactInterfaceView : MonoBehaviour
    {
        private TitleLogoImage panelEffect;
        private float visibility = 1f;
        [SerializeField] private RectTransform fitTarget;
        [SerializeField] private RectTransform dissolveSurface;
        public float Visibility => visibility;

        /// <summary>One stencil surface clips the entire projection, including text and nested scroll views.</summary>
        public void MaskSurface(RectTransform surface, bool drawGlass)
        {
            dissolveSurface = surface;
            var image = surface.GetComponent<UnityEngine.UI.Image>() ?? surface.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = drawGlass ? ArtifactInterfaceStyle.Load().glass : Color.white;
            image.raycastTarget = false;
            var mask = surface.GetComponent<UnityEngine.UI.Mask>() ?? surface.gameObject.AddComponent<UnityEngine.UI.Mask>();
            mask.showMaskGraphic = drawGlass;
            RefreshEffects();
        }

        public void FitProjection(RectTransform target)
        {
            fitTarget = target;
            FitToCanvas();
        }

        private void LateUpdate() => FitToCanvas();

        private void FitToCanvas()
        {
            if (fitTarget == null || !(transform is RectTransform canvasRect)) return;
            Vector2 size = fitTarget.rect.size;
            if (size.x < 1f || size.y < 1f || canvasRect.rect.width < 1f || canvasRect.rect.height < 1f) return;
            float scale = Mathf.Min(1f, (canvasRect.rect.width - 64f) / size.x, (canvasRect.rect.height - 64f) / size.y);
            scale = Mathf.Max(0.1f, scale);
            Vector3 targetScale = Vector3.one * scale;
            if (fitTarget.localScale != targetScale) fitTarget.localScale = targetScale;
        }

        // Only the outer panel owns an effect. Descendants use their normal materials beneath its stencil mask.
        public void RefreshEffects()
        {
            if (dissolveSurface == null) return;
            ArtifactInterfaceStyle style = ArtifactInterfaceStyle.Load();
            panelEffect = dissolveSurface.GetComponent<TitleLogoImage>() ?? dissolveSurface.gameObject.AddComponent<TitleLogoImage>();
            panelEffect.enabled = style.animateDisintegration;
            panelEffect.Configure(style.imageShader, style.ProjectionLook(), style.disintegration);
            panelEffect.Disintegrate = 1f - visibility;
        }

        public void SetVisibility(float value)
        {
            visibility = Mathf.Clamp01(value);
            if (panelEffect != null) panelEffect.Disintegrate = 1f - visibility;
        }

        public static ArtifactInterfaceView Ensure(GameObject root) =>
            root.GetComponent<ArtifactInterfaceView>() ?? root.AddComponent<ArtifactInterfaceView>();

        public static ArtifactGeometry Decorate(string name, RectTransform parent, ArtifactGeometry.Shape form, Color tint)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(ArtifactGeometry));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var geometry = go.GetComponent<ArtifactGeometry>();
            geometry.Form = form;
            geometry.color = form == ArtifactGeometry.Shape.Frame ? new Color(1f, 1f, 1f, .7f) : tint;
            geometry.Secondary = ArtifactInterfaceStyle.Load().lilac;
            geometry.raycastTarget = false;
            return geometry;
        }

        public static ArtifactGeometry Glass(RectTransform parent) => Decorate("Slime Glass", parent,
            ArtifactGeometry.Shape.Glass, new Color(1f, 1f, 1f, .07f));

        public static void StyleHeading(TMP_Text text)
        {
            var style = ArtifactInterfaceStyle.Load();
            if (style.headingFont != null) text.font = style.headingFont;
            text.characterSpacing = 0f;
        }
    }
}
