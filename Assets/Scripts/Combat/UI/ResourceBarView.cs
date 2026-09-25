using System;
using PrimeTween;
using TMPro;
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
        [SerializeField] private TMP_Text valueLabel;
        [SerializeField] private TMP_Text titleLabel;

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

        // PrimeTween-driven values. fillValue/trailValue/countValue are pushed to the visuals from each
        // tween's onValueChange, so nothing needs to be re-evaluated every frame.
        private float fillValue;
        private float trailValue;
        private float countValue;
        private Tween fillTween;
        private Tween trailTween;
        private Tween countTween;
        private Tween lowPulseTween;
        // Kept as handles: the pulse lives inside a Sequence, and PrimeTween refuses Tween.StopAll(body) on
        // tweens nested in a Sequence, so the body's animations are always stopped through these instead.
        private Tween shakeTween;
        private Sequence pulseSequence;
        private bool lowPulseActive;
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

            if (!animate || !initialized || !isActiveAndEnabled)
            {
                StopValueTweens();
                fillValue = trailValue = after;
                countValue = Current;
                bool wasInitialized = initialized;
                initialized = true;
                PushValuesToVisuals();
                UpdateLowPulse();
                if (wasInitialized && previous != Current) ValueChanged?.Invoke(previous, Current, Maximum);
                return;
            }

            float shownFill = fillValue;
            float shownTrail = Mathf.Max(shownFill, trailValue);

            countTween.Stop();
            float fromCount = countValue;
            countTween = Tween.Custom(this, fromCount, Current, motion.countSeconds, (view, v) => view.SetCount(v), Ease.OutCubic);

            if (after < before)
            {
                fillTween.Stop();
                fillTween = Tween.Custom(this, shownFill, after, motion.lossSeconds, (view, v) => view.SetFill(v), Ease.OutCubic);
                // The lost chunk waits, then drains. Keep the highest trail if losses stack up.
                trailTween.Stop();
                trailTween = Tween.Custom(this, new TweenSettings<float>(shownTrail, after, new TweenSettings(motion.trailSeconds, Ease.OutCubic, startDelay: motion.trailDelay)),
                    (view, v) => view.SetTrail(v));
                if (trailGraphic != null) trailGraphic.color = loseTrailColor;
                PlayLossEffects(before, after);
            }
            else if (after > before)
            {
                trailTween.Stop();
                trailValue = after;
                SetTrail(after);
                fillTween.Stop();
                fillTween = Tween.Custom(this, shownFill, after, motion.gainSeconds, (view, v) => view.SetFill(v), Ease.OutCubic);
                if (trailGraphic != null) trailGraphic.color = gainTrailColor;
                PlayGainPulse();
            }

            if (previous != Current) ValueChanged?.Invoke(previous, Current, Maximum);
            UpdateLowPulse();
        }

        private void StopValueTweens()
        {
            fillTween.Stop();
            trailTween.Stop();
            countTween.Stop();
        }

        private void SetFill(float value)
        {
            fillValue = value;
            SetAmount(fill, fillValue);
            if (trailValue < fillValue) SetTrail(fillValue);
        }

        private void SetTrail(float value)
        {
            trailValue = Mathf.Max(fillValue, value);
            SetAmount(trail, trailValue);
        }

        private void SetCount(float value)
        {
            countValue = value;
            int number = Mathf.RoundToInt(value);
            if (valueLabel == null || number == lastShownNumber) return;
            lastShownNumber = number;
            valueLabel.text = string.Format(valueFormat, number, Maximum);
        }

        private void PushValuesToVisuals()
        {
            SetAmount(fill, fillValue);
            SetAmount(trail, trailValue);
            lastShownNumber = int.MinValue;
            SetCount(countValue);
            if (fillGraphic != null) fillGraphic.color = fillColor;
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
            PushValuesToVisuals();
            UpdateLowPulse();
        }

        private void OnDisable()
        {
            Tween.StopAll(this);
            lowPulseTween.Stop();
            lowPulseActive = false;
            StopBodyTweens();
            if (body != null && bodyRestCaptured)
            {
                body.anchoredPosition = bodyRestPosition;
                body.localScale = Vector3.one;
            }
            if (flash != null) flash.enabled = false;
        }

        private void CaptureBodyRest()
        {
            if (body == null || bodyRestCaptured) return;
            bodyRestPosition = body.anchoredPosition;
            bodyRestCaptured = true;
        }

        // A loss: flash + shake the body, then let the fill/trail tweens (already started in Set) play out.
        private void PlayLossEffects(float before, float after)
        {
            CaptureBodyRest();
            float shakeStrength = Mathf.Clamp01((before - after) / 0.2f) * 0.7f + 0.3f;

            if (flash != null && motion.flashSeconds > 0f)
            {
                Tween.StopAll(flash);
                Color color = flash.color;
                color.a = 0.85f;
                flash.color = color;
                flash.enabled = true;
                Tween.Alpha(flash, 0f, motion.flashSeconds, Ease.Linear).OnComplete(flash, target => target.enabled = false);
            }

            if (body != null && motion.shakeSeconds > 0f && motion.shakePixels > 0f)
            {
                StopBodyTweens();
                body.anchoredPosition = bodyRestPosition;
                body.localScale = Vector3.one;
                // PrimeTween's shake settles back to the rest position on its own.
                shakeTween = Tween.ShakeLocalPosition(body, new Vector3(motion.shakePixels, motion.shakePixels, 0f) * shakeStrength,
                    motion.shakeSeconds, frequency: 22f);
            }
        }

        // A gain: a quick single-hump scale pulse on the body.
        private void PlayGainPulse()
        {
            CaptureBodyRest();
            if (body == null || motion.pulseSeconds <= 0f || motion.gainPulse <= 0f) return;
            StopBodyTweens();
            if (bodyRestCaptured) body.anchoredPosition = bodyRestPosition;
            body.localScale = Vector3.one;
            float half = motion.pulseSeconds * 0.5f;
            pulseSequence = Sequence.Create()
                .Group(Tween.Scale(body, 1f + motion.gainPulse, half, Ease.OutSine))
                .Chain(Tween.Scale(body, 1f, half, Ease.InSine));
        }

        private void StopBodyTweens()
        {
            shakeTween.Stop();
            pulseSequence.Stop();
        }

        // Low-value warning: a continuous fill-color pulse between fillColor and lowFillColor while the bar
        // stays at or below Low Threshold. Runs as an infinite PrimeTween yoyo so it needs no per-frame polling.
        private void UpdateLowPulse()
        {
            bool shouldPulse = fillGraphic != null && lowThreshold > 0f && Current > 0
                && Normalized <= lowThreshold && motion.lowPulseSpeed > 0f;
            if (shouldPulse == lowPulseActive) return;
            lowPulseActive = shouldPulse;
            lowPulseTween.Stop();
            if (!shouldPulse)
            {
                if (fillGraphic != null) fillGraphic.color = fillColor;
                return;
            }
            float halfPeriod = 1f / Mathf.Max(0.01f, motion.lowPulseSpeed * 2f);
            lowPulseTween = Tween.Custom(this, new TweenSettings<float>(0f, 1f, new TweenSettings(halfPeriod, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo)),
                (view, v) =>
                {
                    if (view.fillGraphic != null) view.fillGraphic.color = Color.Lerp(view.fillColor, view.lowFillColor, v);
                });
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
            TMP_Text value, TMP_Text title)
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

            TMP_FontAsset font = hud != null ? hud.FontAsset : CombatText.DefaultFont;
            int fontSize = hud != null ? hud.FontSize : 20;
            Color textColor = hud != null ? hud.TextColor : Color.white;
            Color outline = hud != null ? hud.TextOutline : new Color(0f, 0f, 0f, 0.85f);

            TextMeshProUGUI value = CombatText.CreateUGUI("Value", bodyRoot, font, Mathf.Max(8, fontSize - 4), textColor,
                TextAlignmentOptions.Right, outline);
            Stretch(value.rectTransform, 0f);
            value.rectTransform.offsetMin = new Vector2(8f, 0f);
            value.rectTransform.offsetMax = new Vector2(-8f, 0f);
            value.enabled = style.showNumbers;

            TextMeshProUGUI title = CombatText.CreateUGUI("Title", root, font, fontSize, textColor,
                TextAlignmentOptions.BottomLeft, outline);
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

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
