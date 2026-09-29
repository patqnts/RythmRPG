using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>Small helpers shared by the pixel dialogue panels (everything is measured in game pixels).</summary>
    public static class PixelDialogueStyle
    {
        /// <summary>Rounds up, ignoring float noise (12.0001 -> 12).</summary>
        public static int Ceil(float value) => Mathf.CeilToInt(value - 0.01f);

        /// <summary>Height of one line box (ascender to descender) of a TMP text, in its own units.</summary>
        public static int LineBoxHeight(TMP_Text text)
        {
            if (text == null || text.font == null) return 11;
            var face = text.font.faceInfo;
            float pointSize = face.pointSize > 0 ? face.pointSize : text.fontSize;
            float scale = text.fontSize / pointSize * (face.scale > 0f ? face.scale : 1f);
            return Mathf.Max(1, Ceil((face.ascentLine - face.descentLine) * scale));
        }

        /// <summary>Places a rect by its top-left corner, relative to its parent's top-left corner (y grows down).</summary>
        public static void SetTopLeft(RectTransform rect, int x, int y, int width, int height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Bitmap (non-SDF) font atlases must be point-sampled to stay crisp. TMP can create new atlas textures at
        /// runtime (dynamic fonts), so this is re-checked whenever a panel lays out.
        /// </summary>
        public static void EnsurePointFiltering(TMP_FontAsset font)
        {
            if (font == null || font.atlasTextures == null) return;
            if (font.atlasRenderMode.ToString().StartsWith("SDF")) return;
            foreach (Texture2D atlas in font.atlasTextures)
            {
                if (atlas != null && atlas.filterMode != FilterMode.Point) atlas.filterMode = FilterMode.Point;
            }
        }

        /// <summary>
        /// Makes a sliced Image draw 1 sprite texel per UI unit whatever the canvas' Reference Pixels Per Unit is,
        /// so the 9-slice borders stay exactly as drawn.
        /// </summary>
        public static void FixSlicedPixelsPerUnit(Component target)
        {
            if (target == null || !target.TryGetComponent(out Image image) || image.sprite == null) return;
            Canvas canvas = image.canvas;
            float reference = canvas != null ? canvas.referencePixelsPerUnit : 100f;
            image.pixelsPerUnitMultiplier = reference / image.sprite.pixelsPerUnit;
        }
    }
}
