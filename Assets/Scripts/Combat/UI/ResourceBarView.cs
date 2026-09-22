using System;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// A resource bar (health, mana...) that animates value changes:
    /// a loss drops the fill fast, flashes, shakes, and leaves a trail chunk that drains after a short delay;
    /// a gain shows the new chunk at once and fills up to it with a small pulse. The number counts to the new value.
    ///
    /// Works with any art: assign your own Fill / Trail / Flash / labels in a prefab, or let
    /// <see cref="CreateTemplate"/> build the default pixel template. A fill Image set to Filled (with a sprite) uses
    /// fillAmount; anything else is resized through its anchors, so 9-sliced frames keep their borders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceBarView : MonoBehaviour
    {
        [Header("Parts")]
        [Tooltip("Moved when the bar shakes and scaled when it pulses. Keep it a child so layout groups are not disturbed.")]
        [SerializeField] private RectTransform body;
        [SerializeField] private RectTransform fill;
        [Tooltip("Drawn behind the fill: shows the lost (or gained) chunk.")]
        [SerializeField] private RectTransform trail;
        [Tooltip("Optional overlay flashed on a loss.")]
        [SerializeField] private Graphic flash;
        [SerializeField] private Text valueLabel;
        [SerializeField] private Text titleLabel;

        [Header("Colors")]
        [SerializeField] private Color fillColor = new(0.36f, 0.86f, 0.42f);
        [SerializeField] private Color lowFillColor = new(1f, 0.28f, 0.28f);
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.25f;
        [SerializeField] private Color loseTrailColor = new(1f, 0.93f, 0.62f);
        [SerializeField] private Color gainTrailColor = new(0.75f, 1f, 0.8f);

        [Header("Text")]
        [Tooltip("{0} = current, {1} = maximum.")]
        [SerializeField] private string valueFormat = "{0}/{1}";

        [Header("Motion")]
        [SerializeField] private ResourceBarMotion motion = new();

        /// <summary>(previous, current, maximum). Hook extra effects here (sounds, particles, portraits...).</summary>
        public event Action<int, int, int> ValueChanged;

        public int Current { get; private set; }
        public int Maximum { get; private set; } = 1;
        public float Normalized => Maximum <= 0 ? 0f : Mathf.Clamp01((float)Current / Maximum);

        private Graphic fillGraphic;
        private Graphic trailGraphic;
        private bool initialized;
        private Ramp fillRamp;
        private Ramp trailRamp;
        private Ramp countRamp;
        private float shakeStart = -1f;
        private float shakeStrength;
        private float flashStart = -1f;
        private float pulseStart = -1f;
        private int lastShownNumber = int.MinValue;
        private Vector2 bodyRestPosition;
        private bool bodyRestCaptured;

        public string Title
        {
            get => titleLabel != null ? titleLabel.text : string.Empty;
            set
            {
                if (titleLabel == null) return;
                titleLabel.text = value ?? string.Empty;
                titleLabel.enabled = !string.IsNullOrEmpty(value);
            }
        }

        public ResourceBarMotion Motion
        {
            get => motion;
            set => motion = value ?? new ResourceBarMotion();
        }

        private void Awake() => CacheParts();

        /// <summary>Show a value. animate=false snaps (use it for the first value and for resets).</summary>
        public void Set(int current, int maximum, bool animate = true)
        {
            CacheParts();
            int previous = Current;
            float before = Normalized;
            Maximum = Mathf.Max(1, maximum);
            Current = Mathf.Clamp(current, 0, Maximum);
            float after = Normalized;
            float now = Time.unscaledTime;

            if (!animate || !initialized || !isActiveAndEnabled)
            {
                fillRamp = Ramp.Hold(after);
                trailRamp = Ramp.Hold(after);
                countRamp = Ramp.Hold(Current);
                bool wasInitialized = initialized;
                initialized = true;
                Apply(now);
                if (wasInitialized && previous != Current) ValueChanged?.Invoke(previous, Current, Maximum);
                return;
            }

            float shownFill = fillRamp.Evaluate(now);
            float shownTrail = Mathf.Max(shownFill, trailRamp.Evaluate(now));
            countRamp = new Ramp(countRamp.Evaluate(now), Current, now, motion.countSeconds);

            if (after < before)
            {
                fillRamp = new Ramp(shownFill, after, now, motion.lossSeconds);
                // The lost chunk waits, then drains. Keep the highest trail if losses stack up.
                trailRamp = new Ramp(shownTrail, after, now + motion.trailDelay, motion.trailSeconds);
                if (trailGraphic != null) trailGraphic.color = loseTrailColor;
                flashStart = now;
                shakeStart = now;
                shakeStrength = Mathf.Clamp01((before - after) / 0.2f) * 0.7f + 0.3f;
            }
            else if (after > before)
            {
                trailRamp = Ramp.Hold(after);
                fillRamp = new Ramp(shownFill, after, now, motion.gainSeconds);
                if (trailGraphic != null) trailGraphic.color = gainTrailColor;
                pulseStart = now;
            }

            if (previous != Current) ValueChanged?.Invoke(previous, Current, Maximum);
            Apply(now);
        }

        /// <summary>Copy colors and sprites from a style (used by the template and by code that restyles bars).</summary>
        public void ApplyStyle(ResourceBarStyle style)
        {
            if (style == null) return;
            CacheParts();
            fillColor = style.fill;
            lowFillColor = style.lowFill;
            lowThreshold = style.lowThreshold;
            loseTrailColor = style.loseTrail;
            gainTrailColor = style.gainTrail;
            if (fillGraphic is Image fillImage && style.fillSprite != null) SetSprite(fillImage, style.fillSprite);
            if (trailGraphic is Image trailImage && style.fillSprite != null) SetSprite(trailImage, style.fillSprite);
            if (valueLabel != null) valueLabel.enabled = style.showNumbers;
            Title = style.title;
            Apply(Time.unscaledTime);
        }

        private void Update()
        {
            if (initialized) Apply(Time.unscaledTime);
        }

        private void OnDisable()
        {
            if (body != null && bodyRestCaptured)
            {
                body.anchoredPosition = bodyRestPosition;
                body.localScale = Vector3.one;
            }
        }

        private void Apply(float now)
        {
            float fillValue = fillRamp.Evaluate(now);
            float trailValue = Mathf.Max(fillValue, trailRamp.Evaluate(now));
            SetAmount(fill, fillValue);
            SetAmount(trail, trailValue);

            if (fillGraphic != null)
            {
                Color color = fillColor;
                if (lowThreshold > 0f && Normalized <= lowThreshold && Current > 0 && motion.lowPulseSpeed > 0f)
                {
                    float wave = 0.5f + 0.5f * Mathf.Sin(now * motion.lowPulseSpeed * Mathf.PI * 2f);
                    color = Color.Lerp(fillColor, lowFillColor, wave);
                }
                fillGraphic.color = color;
            }

            if (flash != null)
            {
                float t = flashStart < 0f || motion.flashSeconds <= 0f ? 1f : (now - flashStart) / motion.flashSeconds;
                Color color = flash.color;
                color.a = t >= 1f ? 0f : 0.85f * (1f - t);
                flash.color = color;
                flash.enabled = color.a > 0.001f;
            }

            int number = Mathf.RoundToInt(countRamp.Evaluate(now));
            if (valueLabel != null && number != lastShownNumber)
            {
                lastShownNumber = number;
                valueLabel.text = string.Format(valueFormat, number, Maximum);
            }

            if (body == null) return;
            if (!bodyRestCaptured)
            {
                bodyRestPosition = body.anchoredPosition;
                bodyRestCaptured = true;
            }

            Vector2 offset = Vector2.zero;
            if (shakeStart >= 0f && motion.shakeSeconds > 0f)
            {
                float t = (now - shakeStart) / motion.shakeSeconds;
                if (t >= 1f) shakeStart = -1f;
                else
                {
                    float amplitude = motion.shakePixels * shakeStrength * (1f - t) * (1f - t);
                    offset = new Vector2(Mathf.Sin(now * 95f), Mathf.Cos(now * 71f)) * amplitude;
                }
            }
            // Whole pixels keep pixel art crisp.
            body.anchoredPosition = bodyRestPosition + new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));

            float scale = 1f;
            if (pulseStart >= 0f && motion.pulseSeconds > 0f)
            {
                float t = (now - pulseStart) / motion.pulseSeconds;
                if (t >= 1f) pulseStart = -1f;
                else scale += motion.gainPulse * Mathf.Sin(t * Mathf.PI);
            }
            body.localScale = new Vector3(scale, scale, 1f);
        }

        private static void SetAmount(RectTransform part, float value)
        {
            if (part == null) return;
            value = Mathf.Clamp01(value);
            if (part.TryGetComponent(out Image image) && image.type == Image.Type.Filled && image.sprite != null)
            {
                image.fillAmount = value;
                return;
            }
            Vector2 max = part.anchorMax;
            if (Mathf.Approximately(max.x, value) && Mathf.Approximately(part.anchorMin.x, 0f)) return;
            part.anchorMin = new Vector2(0f, part.anchorMin.y);
            part.anchorMax = new Vector2(value, max.y);
        }

        private static void SetSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            if (image.type != Image.Type.Filled)
                image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
        }

        private void CacheParts()
        {
            if (fill != null && fillGraphic == null) fillGraphic = fill.GetComponent<Graphic>();
            if (trail != null && trailGraphic == null) trailGraphic = trail.GetComponent<Graphic>();
        }

        /// <summary>Wire parts from code (the template builder uses this).</summary>
        public void AssignParts(RectTransform bodyRoot, RectTransform fillPart, RectTransform trailPart, Graphic flashOverlay,
            Text value, Text title)
        {
            body = bodyRoot;
            fill = fillPart;
            trail = trailPart;
            flash = flashOverlay;
            valueLabel = value;
            titleLabel = title;
            fillGraphic = trailGraphic = null;
            bodyRestCaptured = false;
            CacheParts();
        }

        /// <summary>
        /// Builds the default pixel-style bar: frame, dark background, trail, fill, flash overlay, number inside the bar
        /// and a title above it. Save the result as a prefab to restyle it by hand.
        /// </summary>
        public static ResourceBarView CreateTemplate(Transform parent, string name, ResourceBarStyle style, CombatHudStyle hud)
        {
            style ??= new ResourceBarStyle();
            RectTransform root = NewRect(name, parent);
            ResourceBarView view = root.gameObject.AddComponent<ResourceBarView>();

            RectTransform bodyRoot = NewRect("Body", root);
            Stretch(bodyRoot, 0f);

            Image frame = NewImage("Frame", bodyRoot, style.frame, style.frameSprite);
            Stretch(frame.rectTransform, 0f);
            Image background = NewImage("Background", bodyRoot, style.background, style.backgroundSprite);
            Stretch(background.rectTransform, style.frameThickness);

            Image trailImage = NewImage("Trail", background.rectTransform, style.loseTrail, style.fillSprite);
            Stretch(trailImage.rectTransform, 0f);
            Image fillImage = NewImage("Fill", background.rectTransform, style.fill, style.fillSprite);
            Stretch(fillImage.rectTransform, 0f);
            // Child of the fill so only the remaining bar flashes.
            Image flashImage = NewImage("Flash", fillImage.rectTransform, new Color(1f, 1f, 1f, 0f), null);
            Stretch(flashImage.rectTransform, 0f);

            Font font = hud != null ? hud.Font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            int fontSize = hud != null ? hud.FontSize : 20;
            Color textColor = hud != null ? hud.TextColor : Color.white;
            Color outline = hud != null ? hud.TextOutline : new Color(0f, 0f, 0f, 0.85f);

            Text value = NewText("Value", bodyRoot, font, Mathf.Max(8, fontSize - 4), textColor, outline, TextAnchor.MiddleRight);
            Stretch(value.rectTransform, 0f);
            value.rectTransform.offsetMin = new Vector2(8f, 0f);
            value.rectTransform.offsetMax = new Vector2(-8f, 0f);
            value.enabled = style.showNumbers;

            Text title = NewText("Title", root, font, fontSize, textColor, outline, TextAnchor.LowerLeft);
            RectTransform titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 0f);
            titleRect.anchoredPosition = new Vector2(2f, 2f);
            titleRect.sizeDelta = new Vector2(0f, fontSize + 8f);

            view.AssignParts(bodyRoot, fillImage.rectTransform, trailImage.rectTransform, flashImage, value, title);
            view.ApplyStyle(style);
            if (hud != null) view.Motion = hud.Motion.Clone();
            return view;
        }

        public static void ApplyLayout(RectTransform rect, HudBarLayout layout)
        {
            if (rect == null || layout == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = layout.anchor;
            rect.anchoredPosition = layout.position;
            rect.sizeDelta = layout.size;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
        {
            RectTransform rect = NewRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null) SetSprite(image, sprite);
            return image;
        }

        private static Text NewText(string name, Transform parent, Font font, int size, Color color, Color outline, TextAnchor alignment)
        {
            RectTransform rect = NewRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            if (outline.a > 0f)
            {
                Outline effect = rect.gameObject.AddComponent<Outline>();
                effect.effectColor = outline;
                effect.effectDistance = new Vector2(2f, -2f);
            }
            return text;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>A value that eases from one number to another, optionally after a delay.</summary>
        private readonly struct Ramp
        {
            private readonly float from;
            private readonly float to;
            private readonly float start;
            private readonly float duration;

            public Ramp(float from, float to, float start, float duration)
            {
                this.from = from;
                this.to = to;
                this.start = start;
                this.duration = duration;
            }

            public static Ramp Hold(float value) => new(value, value, 0f, 0f);

            public float Evaluate(float now)
            {
                if (duration <= 0f || now >= start + duration) return to;
                if (now <= start) return from;
                float t = (now - start) / duration;
                t = 1f - (1f - t) * (1f - t) * (1f - t); // ease out cubic
                return Mathf.LerpUnclamped(from, to, t);
            }
        }
    }
}
