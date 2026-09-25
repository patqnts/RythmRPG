using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    public enum KeyMarkerShape { Square, Diamond, Circle }

    /// <summary>
    /// Outline shape on the Perfect Hit Line that marks where a lane's notes land, with the lane's key inside. It lights
    /// up while the key is held and flashes in the judgement colour on a hit. A Stationary note charging on the lane
    /// shows as a copy of the outline shrinking onto the marker; a Stationary Hold note as a fill growing from the
    /// centre (see <see cref="LaneAnticipation"/>). Both use the marker's shape (square, diamond, circle or custom art). Built and positioned by
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
        private Image chargeFill;
        private Image holdFill;
        private float holdBlend;
        private Color holdColor = Color.white;
        private float holdSeconds;
        private RectTransform chargeOutlineRoot;
        private readonly List<Image> chargeOutline = new();
        private float layoutSide;
        private TMP_Text label;
        private CombatLanePresentationTheme theme;
        private float pressBlend;
        private float flashTime = -1f;
        private Color flashColor = Color.white;

        public int LaneId { get; private set; }

        // Last layout (hit line canvas units), for the morph into the ability frame.
        private float layoutSize;
        private float layoutOutline;

        /// <summary>Centre of the marker in the world.</summary>
        public Vector3 WorldCenter => transform.position;
        /// <summary>Outer width of the drawn shape in world units (a diamond's square side, not its diagonal).</summary>
        public float WorldShapeSize => layoutSize * Mathf.Abs(transform.lossyScale.x) * (Shape == KeyMarkerShape.Diamond ? 0.70710678f : 1f);
        public float WorldOutline => layoutOutline * Mathf.Abs(transform.lossyScale.x);
        public KeyMarkerShape Shape => theme != null && theme.KeyMarkerSprite == null ? theme.KeyMarkerShape : KeyMarkerShape.Square;
        public Color IdleColor => theme != null ? theme.KeyMarkerColor : new Color(1f, 1f, 1f, 0.85f);
        public string LabelText => label != null ? label.text : string.Empty;

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
            // Stationary Hold charge: grows from the centre, under the outline.
            chargeFill = NewImage("Charge Fill", shape);
            Stretch(chargeFill.rectTransform, 0f);
            chargeFill.enabled = false;
            // Hold notes: fills the marker in the note's colour while held.
            holdFill = NewImage("Hold Fill", shape);
            Stretch(holdFill.rectTransform, 0f);
            holdFill.enabled = false;

            Sprite custom = theme != null ? theme.KeyMarkerSprite : null;
            KeyMarkerShape kind = theme != null ? theme.KeyMarkerShape : KeyMarkerShape.Square;
            BuildOutline(shape, "Outline", outline);
            if (custom != null)
            {
                fill.sprite = custom; // the pressed glow takes the art's silhouette
                chargeFill.sprite = custom;
                chargeFill.preserveAspect = true;
                holdFill.sprite = custom;
                holdFill.preserveAspect = true;
            }
            else if (kind == KeyMarkerShape.Circle)
            {
                fill.sprite = CircleSprites.Disc;
                chargeFill.sprite = CircleSprites.Disc;
                holdFill.sprite = CircleSprites.Disc;
            }
            else if (kind == KeyMarkerShape.Diamond)
            {
                shape.localRotation = Quaternion.Euler(0f, 0f, 45f);
            }

            // Stationary charge: the same outline, drawn bigger and shrinking onto the marker (not scaled by presses).
            chargeOutlineRoot = NewRect("Charge Outline", root);
            chargeOutlineRoot.localRotation = shape.localRotation;
            BuildOutline(chargeOutlineRoot, "Charge", chargeOutline);
            chargeOutlineRoot.gameObject.SetActive(false);

            label = CombatText.CreateUGUI("Key", root, CombatText.ResolveFont(theme != null ? theme.ButtonFontAsset : null,
                    theme != null ? theme.ButtonFont : null), 10f, Color.white, TextAlignmentOptions.Center, Color.clear);
            label.fontStyle = FontStyles.Bold;
            Stretch(label.rectTransform, 0f);
            label.gameObject.SetActive(theme == null || theme.ShowKeyMarkerLabels);
        }

        /// <summary>Places the marker (canvas units of the hit line) and sizes the shape and outline.</summary>
        public void Layout(float localX, float size, float outlineThickness)
        {
            layoutSize = size;
            layoutOutline = outlineThickness;
            root.anchoredPosition3D = new Vector3(localX, 0f, 0f);
            root.localRotation = Quaternion.identity;
            bool diamond = theme != null && theme.KeyMarkerSprite == null && theme.KeyMarkerShape == KeyMarkerShape.Diamond;
            float side = diamond ? size / Mathf.Sqrt(2f) : size; // a rotated square spans side * sqrt(2)
            root.sizeDelta = new Vector2(size, size);
            shape.anchorMin = shape.anchorMax = shape.pivot = new Vector2(0.5f, 0.5f);
            shape.sizeDelta = new Vector2(side, side);
            shape.anchoredPosition = Vector2.zero;
            layoutSide = side;
            chargeOutlineRoot.anchorMin = chargeOutlineRoot.anchorMax = chargeOutlineRoot.pivot = new Vector2(0.5f, 0.5f);
            chargeOutlineRoot.anchoredPosition = Vector2.zero;

            if (outline.Count == 4)
            {
                float t = Mathf.Clamp(outlineThickness, 0.0001f, side * 0.5f);
                LayoutBars(outline, t);
                LayoutBars(chargeOutline, t);
                Stretch(fill.rectTransform, t);
                Stretch(chargeFill.rectTransform, t);
                Stretch(holdFill.rectTransform, t);
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
            TickHold(deltaTime);
            Color line = Color.Lerp(Color.Lerp(idle, held, pressBlend), flashColor, flash);
            float outlineTint = (theme != null ? theme.HoldOutlineTint : 0.6f) * holdBlend * (1f - flash);
            line = Color.Lerp(line, new Color(holdColor.r, holdColor.g, holdColor.b, line.a), outlineTint);
            Color inside = Color.Lerp(heldFill, flashColor, flash);
            // While a hold note is held its own colour fills the marker instead of the pressed fill.
            inside.a *= Mathf.Max(pressBlend * (1f - holdBlend), flash);

            foreach (Image image in outline) image.color = line;
            fill.color = inside;
            if (label != null)
            {
                Color text = theme != null ? theme.KeyMarkerLabelColor : Color.white;
                label.color = Color.Lerp(text, held, pressBlend * 0.6f);
            }

            float pressedScale = theme != null ? theme.KeyMarkerPressedScale : 1.15f;
            shape.localScale = Vector3.one * (Mathf.Lerp(1f, pressedScale, pressBlend) + flash * 0.12f);
            TickCharge();
        }

        // Stationary notes charging on this lane (the one closest to its hit time of each kind).
        private void TickCharge()
        {
            bool show = theme == null || theme.ShowStationaryCharge;
            float fadeIn = theme != null ? theme.ChargeFadeIn : 0.15f;

            float approach = 0f;
            bool approaching = show && LaneAnticipation.TryGet(LaneId, LaneAnticipationKind.ApproachOutline, out approach);
            if (chargeOutlineRoot.gameObject.activeSelf != approaching) chargeOutlineRoot.gameObject.SetActive(approaching);
            if (approaching)
            {
                float startScale = theme != null ? theme.ChargeOutlineStartScale : 2.2f;
                float size = layoutSide * Mathf.Lerp(startScale, 1f, approach);
                chargeOutlineRoot.sizeDelta = new Vector2(size, size);
                Color color = theme != null ? theme.ChargeOutlineColor : new Color(1f, 1f, 1f, 0.9f);
                color.a *= FadeIn(approach, fadeIn);
                foreach (Image image in chargeOutline) image.color = color;
            }

            float charge = 0f;
            bool charging = show && LaneAnticipation.TryGet(LaneId, LaneAnticipationKind.ChargeFill, out charge);
            if (chargeFill.enabled != charging) chargeFill.enabled = charging;
            if (charging)
            {
                chargeFill.rectTransform.localScale = Vector3.one * charge;
                Color color = theme != null ? theme.ChargeFillColor : new Color(0.55f, 0.85f, 1f, 0.65f);
                color.a *= FadeIn(charge, fadeIn);
                chargeFill.color = color;
            }
        }

        // A hold note being held on this lane: the marker fills with its colour, breathing a little.
        private void TickHold(float deltaTime)
        {
            bool show = theme == null || theme.ShowHoldFill;
            Color color = Color.white;
            float seconds = 0f;
            bool holding = show && LaneHold.TryGet(LaneId, out color, out seconds);
            if (holding)
            {
                holdColor = color;
                holdSeconds = seconds;
            }
            holdBlend = Mathf.MoveTowards(holdBlend, holding ? 1f : 0f, deltaTime / (holding ? 0.06f : 0.18f));
            bool visible = holdBlend > 0.001f;
            if (holdFill.enabled != visible) holdFill.enabled = visible;
            if (!visible) return;

            float pulse = holding ? Mathf.Sin(holdSeconds * Mathf.PI * 2f * 2.5f) * (theme != null ? theme.HoldFillPulse : 0.06f) : 0f;
            float pop = Mathf.Lerp(0.55f, 1f, 1f - (1f - holdBlend) * (1f - holdBlend));
            holdFill.rectTransform.localScale = Vector3.one * (pop + pulse);
            float alpha = (theme != null ? theme.HoldFillAlpha : 0.8f) * holdBlend;
            holdFill.color = new Color(holdColor.r, holdColor.g, holdColor.b, alpha);
        }

        private static float FadeIn(float progress, float fadeIn) => fadeIn <= 0f ? 1f : Mathf.Clamp01(progress / fadeIn);

        // The marker outline for the theme: custom art, a ring (Circle), or four bars (Square / Diamond).
        private void BuildOutline(RectTransform parent, string prefix, List<Image> into)
        {
            Sprite custom = theme != null ? theme.KeyMarkerSprite : null;
            KeyMarkerShape kind = theme != null ? theme.KeyMarkerShape : KeyMarkerShape.Square;
            if (custom != null)
            {
                Image art = NewImage(prefix == "Outline" ? "Outline" : prefix + " Outline", parent);
                art.sprite = custom;
                art.preserveAspect = true;
                Stretch(art.rectTransform, 0f);
                into.Add(art);
            }
            else if (kind == KeyMarkerShape.Circle)
            {
                Image ring = NewImage(prefix == "Outline" ? "Outline" : prefix + " Outline", parent);
                ring.sprite = CircleSprites.Ring;
                Stretch(ring.rectTransform, 0f);
                into.Add(ring);
            }
            else
            {
                // Four bars: crisp at any size, and the thickness is exact render-texture pixels.
                foreach (string side in new[] { "Top", "Bottom", "Left", "Right" })
                    into.Add(NewImage(prefix == "Outline" ? side : prefix + " " + side, parent));
            }
        }

        private static void LayoutBars(List<Image> bars, float thickness)
        {
            if (bars.Count != 4) return;
            Bar(bars[0], new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, thickness));
            Bar(bars[1], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, thickness));
            Bar(bars[2], new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(thickness, 0f));
            Bar(bars[3], new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(thickness, 0f));
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
