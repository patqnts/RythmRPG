using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    public enum KeyMarkerShape { Square, Diamond, Circle }

    /// <summary>
    /// Outline shape on the Perfect Hit Line that marks where a lane's notes land, with the lane's key inside. It lights
    /// up while the key is held and flashes in the judgement colour on a hit. Built and positioned by
    /// <see cref="CombatLanePresentation3D"/> as a child of the hit line's world-space canvas, so it shares the line's
    /// camera, sorting and fade. Styled from the <see cref="CombatLanePresentationTheme"/> (Hit Line Key Markers).
    /// </summary>
    public sealed class LaneKeyMarker : MonoBehaviour
    {
        private const float FlashSeconds = 0.22f;

        private RectTransform root;
        private RectTransform shape;
        private readonly List<Image> outline = new();
        private Image fill;
        private TMP_Text label;
        private CombatLanePresentationTheme theme;
        private float pressBlend;
        private float flashTime = -1f;
        private Color flashColor = Color.white;

        public int LaneId { get; private set; }

        public static LaneKeyMarker Create(Transform parent, int laneId, CombatLanePresentationTheme theme)
        {
            var go = new GameObject($"Lane {laneId} Key Marker", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            LaneKeyMarker marker = go.AddComponent<LaneKeyMarker>();
            marker.LaneId = laneId;
            marker.theme = theme;
            marker.Build();
            return marker;
        }

        private void Build()
        {
            root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);

            shape = NewRect("Shape", root);
            fill = NewImage("Fill", shape);
            Stretch(fill.rectTransform, 0f);

            Sprite custom = theme != null ? theme.KeyMarkerSprite : null;
            KeyMarkerShape kind = theme != null ? theme.KeyMarkerShape : KeyMarkerShape.Square;
            if (custom != null)
            {
                Image art = NewImage("Outline", shape);
                art.sprite = custom;
                art.preserveAspect = true;
                Stretch(art.rectTransform, 0f);
                outline.Add(art);
                fill.sprite = custom; // the pressed glow takes the art's silhouette
            }
            else if (kind == KeyMarkerShape.Circle)
            {
                Image ring = NewImage("Outline", shape);
                ring.sprite = CircleSprites.Ring;
                Stretch(ring.rectTransform, 0f);
                outline.Add(ring);
                fill.sprite = CircleSprites.Disc;
            }
            else
            {
                // Four bars: crisp at any size, and the thickness is exact render-texture pixels.
                foreach (string side in new[] { "Top", "Bottom", "Left", "Right" }) outline.Add(NewImage(side, shape));
                if (kind == KeyMarkerShape.Diamond) shape.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            label = CombatText.CreateUGUI("Key", root, CombatText.ResolveFont(theme != null ? theme.ButtonFontAsset : null,
                    theme != null ? theme.ButtonFont : null), 10f, Color.white, TextAlignmentOptions.Center, Color.clear);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 0f);
            label.gameObject.SetActive(theme == null || theme.ShowKeyMarkerLabels);
        }

        /// <summary>Places the marker (canvas units of the hit line) and sizes the shape and outline.</summary>
        public void Layout(float localX, float size, float outlineThickness)
        {
            root.anchoredPosition3D = new Vector3(localX, 0f, 0f);
            root.localRotation = Quaternion.identity;
            bool diamond = theme != null && theme.KeyMarkerSprite == null && theme.KeyMarkerShape == KeyMarkerShape.Diamond;
            float side = diamond ? size / Mathf.Sqrt(2f) : size; // a rotated square spans side * sqrt(2)
            root.sizeDelta = new Vector2(size, size);
            shape.anchorMin = shape.anchorMax = shape.pivot = new Vector2(0.5f, 0.5f);
            shape.sizeDelta = new Vector2(side, side);
            shape.anchoredPosition = Vector2.zero;

            if (outline.Count == 4)
            {
                float t = Mathf.Clamp(outlineThickness, 0.0001f, side * 0.5f);
                Bar(outline[0], new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t));
                Bar(outline[1], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t));
                Bar(outline[2], new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f));
                Bar(outline[3], new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f));
                Stretch(fill.rectTransform, t);
            }

            label.fontSize = size * (theme != null ? theme.KeyMarkerLabelScale : 0.55f);
        }

        public void SetLabel(string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        /// <summary>Brief flash in the judgement colour (called when a note in this lane is judged).</summary>
        public void Flash(Color color)
        {
            flashColor = color;
            flashTime = 0f;
        }

        /// <summary>Per-frame visuals. <paramref name="pressed"/> = the lane's key is held.</summary>
        public void Tick(bool pressed, float deltaTime)
        {
            pressBlend = Mathf.MoveTowards(pressBlend, pressed ? 1f : 0f, deltaTime / 0.06f);
            float flash = 0f;
            if (flashTime >= 0f)
            {
                flashTime += deltaTime;
                flash = 1f - Mathf.Clamp01(flashTime / FlashSeconds);
                if (flashTime >= FlashSeconds) flashTime = -1f;
            }

            Color idle = theme != null ? theme.KeyMarkerColor : new Color(1f, 1f, 1f, 0.85f);
            Color held = theme != null ? theme.KeyMarkerPressedColor : new Color(1f, 0.85f, 0.35f, 1f);
            Color heldFill = theme != null ? theme.KeyMarkerPressedFill : new Color(1f, 0.85f, 0.35f, 0.35f);
            Color line = Color.Lerp(Color.Lerp(idle, held, pressBlend), flashColor, flash);
            Color inside = Color.Lerp(heldFill, flashColor, flash);
            inside.a *= Mathf.Max(pressBlend, flash);

            foreach (Image image in outline) image.color = line;
            fill.color = inside;
            if (label != null)
            {
                Color text = theme != null ? theme.KeyMarkerLabelColor : Color.white;
                label.color = Color.Lerp(text, held, pressBlend * 0.6f);
            }

            float pressedScale = theme != null ? theme.KeyMarkerPressedScale : 1.15f;
            shape.localScale = Vector3.one * (Mathf.Lerp(1f, pressedScale, pressBlend) + flash * 0.12f);
        }

        // ---------- Helpers ----------

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        private static Image NewImage(string name, Transform parent)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Bar(Image image, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Generated ring / disc sprites for the Circle shape (point-filtered to stay crisp in pixel art).</summary>
        private static class CircleSprites
        {
            private const int Size = 64;
            private static Sprite ring, disc;

            public static Sprite Ring => ring != null ? ring : ring = Make(true);
            public static Sprite Disc => disc != null ? disc : disc = Make(false);

            private static Sprite Make(bool hollow)
            {
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                {
                    name = hollow ? "Key Marker Ring" : "Key Marker Disc",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                var pixels = new Color32[Size * Size];
                float center = (Size - 1) * 0.5f;
                float outer = Size * 0.5f - 0.5f;
                float inner = outer - Size * 0.09f;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    float a = Mathf.Clamp01(outer - d + 0.5f);
                    if (hollow) a *= Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
