using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Optional: where speech bubbles and the interact arrow point on this character. Bubbles above the character
    /// point at its head; bubbles below it (when it talks to someone higher up the screen) point at its feet.
    /// Without this component the top / bottom of the character's sprites and meshes are used (or its pivot
    /// and 1.5 units above it when it has no renderers).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelSpeechAnchor : MonoBehaviour
    {
        [Header("Head (bubble above, interact arrow)")]
        [Tooltip("Empty = this transform.")]
        public Transform anchor;
        [Tooltip("World offset added to the head point.")]
        public Vector3 worldOffset;
        [Tooltip("Extra offset in game pixels after projecting to the screen (x right, y up).")]
        public Vector2Int pixelOffset;
        [Tooltip("Ignore Anchor/World Offset and use the top of this character's renderers.")]
        public bool useRendererTop;

        [Header("Look")]
        [Tooltip("This character's bubbles use this palette instead of the Dialogue UI's default. Empty = default.")]
        public PixelDialoguePalette bubblePalette;

        [Header("Feet (bubble below)")]
        [Tooltip("Empty = the bottom of this character's renderers.")]
        public Transform footAnchor;
        public Vector3 footWorldOffset;
        public Vector2Int footPixelOffset;

        /// <summary>Where a character's bubble points, in world space, plus per-point pixel offsets.</summary>
        public struct Points
        {
            public Vector3 Head;
            public Vector3 Feet;
            public Vector2 HeadPixelOffset;
            public Vector2 FeetPixelOffset;
        }

        private static readonly List<Renderer> Buffer = new();

        public static Points Resolve(Transform character, float fallbackHeight)
        {
            var points = new Points();
            if (character == null) return points;

            bool hasBounds = TryGetRendererBounds(character, out Bounds bounds);
            points.Head = hasBounds
                ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
                : character.position + Vector3.up * fallbackHeight;
            points.Feet = hasBounds ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : character.position;

            PixelSpeechAnchor custom = character.GetComponentInChildren<PixelSpeechAnchor>();
            if (custom == null) return points;

            points.HeadPixelOffset = custom.pixelOffset;
            if (!custom.useRendererTop)
                points.Head = (custom.anchor != null ? custom.anchor : custom.transform).position + custom.worldOffset;

            points.FeetPixelOffset = custom.footPixelOffset;
            if (custom.footAnchor != null) points.Feet = custom.footAnchor.position;
            points.Feet += custom.footWorldOffset;
            return points;
        }

        /// <summary>World point the head of <paramref name="character"/> is at, plus a pixel offset.</summary>
        public static Vector3 Resolve(Transform character, float fallbackHeight, out Vector2 pixelOffset)
        {
            Points points = Resolve(character, fallbackHeight);
            pixelOffset = points.HeadPixelOffset;
            return points.Head;
        }

        private static bool TryGetRendererBounds(Transform character, out Bounds bounds)
        {
            bounds = default;
            Buffer.Clear();
            character.GetComponentsInChildren(false, Buffer);
            bool found = false;
            foreach (Renderer candidate in Buffer)
            {
                if (!candidate.enabled || candidate.forceRenderingOff) continue;
                Bounds rendererBounds;
                if (candidate is SpriteRenderer sprite)
                {
                    if (sprite.sprite == null || !TryGetOpaqueSpriteBounds(sprite, out rendererBounds)) continue;
                }
                else if (candidate is MeshRenderer || candidate is SkinnedMeshRenderer)
                {
                    rendererBounds = candidate.bounds;
                }
                else continue;

                if (!found) { bounds = rendererBounds; found = true; }
                else bounds.Encapsulate(rendererBounds);
            }
            Buffer.Clear();
            return found;
        }

        // ---------- Opaque sprite area ----------

        private static readonly Dictionary<Sprite, Rect> OpaqueRects = new();
        private static readonly List<Vector2> ShapeBuffer = new();
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>
        /// World bounds of the drawn (non-transparent) part of a sprite, as it is rendered: empty space in the sprite's
        /// frame is ignored, and camera-facing sprites that <see cref="RythmRPG.Core.ObliqueBillboard"/> stands upright
        /// for rendering are measured upright.
        /// </summary>
        private static bool TryGetOpaqueSpriteBounds(SpriteRenderer renderer, out Bounds bounds)
        {
            bounds = default;
            Rect local = OpaqueRect(renderer.sprite);
            if (local.width <= 0f && local.height <= 0f) return false;
            if (renderer.flipX) local = Rect.MinMaxRect(-local.xMax, local.yMin, -local.xMin, local.yMax);
            if (renderer.flipY) local = Rect.MinMaxRect(local.xMin, -local.yMax, local.xMax, -local.yMin);

            Corners[0] = new Vector3(local.xMin, local.yMin);
            Corners[1] = new Vector3(local.xMin, local.yMax);
            Corners[2] = new Vector3(local.xMax, local.yMax);
            Corners[3] = new Vector3(local.xMax, local.yMin);

            Matrix4x4 toWorld = renderer.transform.localToWorldMatrix;
            RythmRPG.Core.ObliqueBillboard billboard = renderer.GetComponentInParent<RythmRPG.Core.ObliqueBillboard>();
            Camera camera = Camera.main;
            bool upright = billboard != null && billboard.isActiveAndEnabled && camera != null &&
                           RythmRPG.Core.ObliqueProjection.TryGet(camera, out _);
            Quaternion correction = upright
                ? RythmRPG.Core.ObliqueProjection.BillboardRotation(camera) * Quaternion.Inverse(camera.transform.rotation)
                : Quaternion.identity;
            Vector3 pivot = upright ? billboard.transform.position : Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(Corners[i]);
                if (upright) world = pivot + correction * (world - pivot);
                if (i == 0) bounds = new Bounds(world, Vector3.zero);
                else bounds.Encapsulate(world);
            }
            return true;
        }

        /// <summary>
        /// The sprite's non-transparent area in its local units (pivot = origin): from its physics shape (generated
        /// from alpha on import, available without a readable texture), else its tight mesh, else its full rect.
        /// </summary>
        private static Rect OpaqueRect(Sprite sprite)
        {
            if (OpaqueRects.TryGetValue(sprite, out Rect cached)) return cached;

            bool any = false;
            float xMin = 0f, yMin = 0f, xMax = 0f, yMax = 0f;
            void Add(Vector2 point)
            {
                if (!any) { xMin = xMax = point.x; yMin = yMax = point.y; any = true; return; }
                xMin = Mathf.Min(xMin, point.x); xMax = Mathf.Max(xMax, point.x);
                yMin = Mathf.Min(yMin, point.y); yMax = Mathf.Max(yMax, point.y);
            }

            int shapes = sprite.GetPhysicsShapeCount();
            for (int i = 0; i < shapes; i++)
            {
                ShapeBuffer.Clear();
                sprite.GetPhysicsShape(i, ShapeBuffer);
                foreach (Vector2 point in ShapeBuffer) Add(point);
            }
            ShapeBuffer.Clear();
            if (!any)
            {
                foreach (Vector2 point in sprite.vertices) Add(point);
            }

            Rect rect = any ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : new Rect(sprite.bounds.min, sprite.bounds.size);
            OpaqueRects[sprite] = rect;
            return rect;
        }
    }
}
