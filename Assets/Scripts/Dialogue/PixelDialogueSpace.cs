using RythmRPG.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// A RectTransform where 1 unit = 1 game pixel, drawn at a whole-number screen scale, with its origin on the
    /// screen's bottom-left corner. The dialogue bubbles live inside it, so their sprites and the monogram font land
    /// exactly on the screen's pixel grid (the Eastward look: UI pixels are the same size as world pixels).
    /// <para>
    /// The scale is matched to the world's pixels: the RawImage that shows the pixel camera's render texture is
    /// measured on screen (480x270 at 1080p = 4 screen pixels per game pixel). It also converts world positions
    /// (a speaker's head) into this space through the pixel camera, the render texture and the RawImage, so it works
    /// with <see cref="ObliqueProjection"/>, <see cref="PixelPerfectRig"/> and letterboxing.
    /// </para>
    /// <para>Works on a Screen Space Overlay or Screen Space Camera canvas, nested or not.</para>
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    [DefaultExecutionOrder(30400)] // after Cinemachine / camera shake (30000), before the bubble panels (30500)
    public sealed class PixelDialogueSpace : MonoBehaviour
    {
        [Tooltip("Camera that renders the world. Empty = Camera.main (the Pixel Main Camera).")]
        [SerializeField] private Camera worldCamera;
        [Tooltip("RawImage that shows the world camera's render texture. Found automatically when empty.")]
        [SerializeField] private RawImage pixelOutput;
        [Tooltip("Screen pixels per UI pixel. 0 = match the world's pixel size (rounded to a whole number).")]
        [SerializeField, Min(0)] private int pixelScaleOverride;
        [Tooltip("Used when the world camera draws straight to the screen: the game's height in pixels.")]
        [SerializeField, Min(1)] private int referenceHeight = 270;

        private static readonly Vector3[] Corners = new Vector3[4];
        private RectTransform rectTransform;
        private Canvas canvas;

        /// <summary>Screen pixels per UI pixel (whole number, at least 1).</summary>
        public int Scale { get; private set; } = 1;

        /// <summary>Size of the screen in UI pixels.</summary>
        public Vector2 Size { get; private set; } = new(480f, 270f);

        public Camera WorldCamera
        {
            get
            {
                if (worldCamera != null) return worldCamera;
                return Camera.main;
            }
            set => worldCamera = value;
        }

        public static PixelDialogueSpace For(Component child) =>
            child != null ? child.GetComponentInParent<PixelDialogueSpace>() : null;

        private void Awake() => rectTransform = (RectTransform)transform;

        private void OnEnable() => Apply();

        private void LateUpdate() => Apply();

        /// <summary>Recomputes the scale and lines this space up with the screen. Cheap; runs every LateUpdate.</summary>
        public void Apply()
        {
            if (rectTransform == null) rectTransform = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            Canvas root = canvas.rootCanvas;
            if (root.renderMode == RenderMode.WorldSpace) return;

            Scale = ResolveScale(root);
            Rect pixelRect = root.pixelRect;
            Size = new Vector2(pixelRect.width / Scale, pixelRect.height / Scale);

            var rootRect = (RectTransform)root.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = Vector2.zero;
            rectTransform.sizeDelta = Size;
            rectTransform.SetPositionAndRotation(rootRect.TransformPoint(rootRect.rect.min), rootRect.rotation);

            float rootScaleFactor = Mathf.Max(0.0001f, root.scaleFactor);
            Vector3 wanted = rootRect.lossyScale * (Scale / rootScaleFactor);
            Vector3 parent = rectTransform.parent != null ? rectTransform.parent.lossyScale : Vector3.one;
            rectTransform.localScale = new Vector3(SafeDivide(wanted.x, parent.x), SafeDivide(wanted.y, parent.y), 1f);
        }

        /// <summary>Screen position (pixels) to a position in this space (UI pixels).</summary>
        public Vector2 ScreenToLocal(Vector2 screen)
        {
            Rect pixelRect = canvas != null ? canvas.rootCanvas.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
            return (screen - pixelRect.min) / Scale;
        }

        /// <summary>Snaps a position in this space to the screen's pixel grid.</summary>
        public Vector2 SnapToScreenPixels(Vector2 local) =>
            new(Mathf.Round(local.x * Scale) / Scale, Mathf.Round(local.y * Scale) / Scale);

        /// <summary>Where a world point is drawn on screen, in this space. False when it is behind the camera.</summary>
        public bool TryWorldToLocal(Vector3 world, out Vector2 local)
        {
            local = default;
            if (!TryWorldToScreen(world, out Vector2 screen)) return false;
            local = ScreenToLocal(screen);
            return true;
        }

        public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
        {
            screen = default;
            Camera cam = WorldCamera;
            if (cam == null) return false;

            ObliqueProjection.Prepare(cam, false);
            Vector3 viewport = ObliqueProjection.WorldToViewportPoint(cam, world);
            if (viewport.z < 0f) return false;

            if (cam.targetTexture != null && TryGetOutputScreenRect(cam, out Rect rect, out Rect uv))
            {
                screen = new Vector2(
                    rect.xMin + (viewport.x - uv.x) / uv.width * rect.width,
                    rect.yMin + (viewport.y - uv.y) / uv.height * rect.height);
                return true;
            }

            Rect camRect = cam.pixelRect;
            screen = new Vector2(camRect.x + viewport.x * camRect.width, camRect.y + viewport.y * camRect.height);
            return true;
        }

        private int ResolveScale(Canvas root)
        {
            if (pixelScaleOverride > 0) return pixelScaleOverride;

            float screenPerGamePixel;
            Camera cam = WorldCamera;
            RenderTexture target = cam != null ? cam.targetTexture : null;
            if (target != null && TryGetOutputScreenRect(cam, out Rect rect, out Rect uv) && Mathf.Abs(uv.height) > 0.0001f)
                screenPerGamePixel = rect.height * Mathf.Abs(uv.height) / target.height;
            else
                screenPerGamePixel = root.pixelRect.height / referenceHeight;

            // Slightly favour the larger scale (5.8 -> 6), otherwise round down so the UI never outgrows the world.
            return Mathf.Max(1, Mathf.FloorToInt(screenPerGamePixel + 0.25f));
        }

        private bool TryGetOutputScreenRect(Camera cam, out Rect rect, out Rect uv)
        {
            rect = default;
            uv = new Rect(0f, 0f, 1f, 1f);
            RawImage image = ResolveOutput(cam);
            if (image == null || image.canvas == null) return false;
            Canvas outputRoot = image.canvas.rootCanvas;
            Camera uiCamera = outputRoot.renderMode == RenderMode.ScreenSpaceOverlay ? null : outputRoot.worldCamera;

            image.rectTransform.GetWorldCorners(Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[2]);
            if (max.x - min.x < 1f || max.y - min.y < 1f) return false;

            uv = image.uvRect;
            if (Mathf.Abs(uv.width) < 0.0001f || Mathf.Abs(uv.height) < 0.0001f) return false;
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        private RawImage ResolveOutput(Camera cam)
        {
            RenderTexture target = cam != null ? cam.targetTexture : null;
            if (target == null) return null;
            if (pixelOutput != null && pixelOutput.texture == target && pixelOutput.isActiveAndEnabled) return pixelOutput;
            pixelOutput = null;
            foreach (RawImage candidate in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
            {
                if (candidate.texture != target) continue;
                pixelOutput = candidate;
                break;
            }
            return pixelOutput;
        }

        private static float SafeDivide(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;
    }
}
