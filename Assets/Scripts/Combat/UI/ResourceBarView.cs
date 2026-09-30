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

        [Header("Barrier (shield)")]
        [Tooltip("White segment after the fill showing a shield. Created automatically next to the fill when empty.")]
        [SerializeField] private RectTransform barrier;
        [SerializeField] private Color barrierColor = new(1f, 1f, 1f, 0.95f);
        [Tooltip("Appended to the value text while a barrier is up. {0} = barrier amount. Empty = nothing.")]
        [SerializeField] private string barrierValueFormat = " <color=#FFFFFFCC>+{0}</color>";

        [Header("Pending (mana still to be paid)")]
        [Tooltip("Faint segment after the fill showing an amount that will be added soon (the enemy turn's accuracy mana). " +
                 "Created automatically next to the fill when empty. Clamped to the bar.")]
        [SerializeField] private RectTransform pending;
        [Tooltip("Pending segment color. Alpha 0 = the fill color at Pending Alpha.")]
        [SerializeField] private Color pendingColor = new(0f, 0f, 0f, 0f);
        [SerializeField, Range(0f, 1f)] private float pendingAlpha = 0.4f;
        [Tooltip("Appended to the value text while something is pending. {0} = pending amount. Empty = nothing.")]
        [SerializeField] private string pendingValueFormat = " <alpha=#AA>+{0}";

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
        /// <summary>Barrier (shield) amount shown after the fill.</summary>
        public int Barrier => barrierTarget;
        /// <summary>Pending amount shown after the fill (see <see cref="SetPending"/>).</summary>
        public int Pending => pendingTarget;

        /// <summary>
        /// Width of the bar per point when a barrier pushes health + barrier past the maximum: the bar then shows
        /// max(maximum, current + barrier) so both fit (League of Legends style). 1 when there is room.
        /// </summary>
        private float DisplayScale => Maximum <= 0 ? 1f : Maximum / Mathf.Max(Maximum, Current + barrierShown);

        private Graphic fillGraphic;
        private Graphic trailGraphic;
        private Graphic barrierGraphic;
        private bool initialized;
        private int barrierTarget;
        private float barrierShown;
        private Tween barrierTween;
        private Graphic pendingGraphic;
        private int pendingTarget;
        private float pendingShown;
        private Tween pendingTween;

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
            if (trailValue < fillValue) trailValue = fillValue;
            ApplyGeometry();
        }

        private void SetTrail(float value)
        {
            trailValue = Mathf.Max(fillValue, value);
            ApplyGeometry();
        }

        /// <summary>
        /// Shows a barrier (shield) segment after the fill. The segment eases to the new size; a barrier that would
        /// overflow the bar rescales health and barrier together.
        /// </summary>
        public void SetBarrier(int amount, bool animate = true)
        {
            EnsureBarrierPart();
            amount = Mathf.Max(0, amount);
            if (amount == barrierTarget && initialized) return;
            barrierTarget = amount;
            barrierTween.Stop();
            if (!animate || !isActiveAndEnabled || !initialized)
            {
                SetBarrierShown(amount);
                return;
            }
            float seconds = amount > barrierShown ? motion.gainSeconds * 0.6f : motion.lossSeconds * 1.5f;
            barrierTween = Tween.Custom(this, barrierShown, amount, Mathf.Max(0.01f, seconds), (view, v) => view.SetBarrierShown(v), Ease.OutCubic);
        }

        private void SetBarrierShown(float value)
        {
            barrierShown = Mathf.Max(0f, value);
            ApplyGeometry();
            lastShownNumber = int.MinValue;
            SetCount(countValue);
        }

        /// <summary>
        /// Shows a faint "coming soon" segment after the fill (clamped to the bar, never rescales it). Used by the mana
        /// bar for the enemy turn's accuracy mana; 0 hides it.
        /// </summary>
        public void SetPending(int amount, bool animate = true)
        {
            EnsurePendingPart();
            amount = Mathf.Max(0, amount);
            if (amount == pendingTarget && initialized) return;
            pendingTarget = amount;
            pendingTween.Stop();
            if (!animate || !isActiveAndEnabled || !initialized)
            {
                SetPendingShown(amount);
                return;
            }
            pendingTween = Tween.Custom(this, pendingShown, amount, Mathf.Max(0.01f, motion.gainSeconds * 0.5f),
                (view, v) => view.SetPendingShown(v), Ease.OutCubic);
        }

        private void SetPendingShown(float value)
        {
            pendingShown = Mathf.Max(0f, value);
            ApplyGeometry();
            lastShownNumber = int.MinValue;
            SetCount(countValue);
        }

        private void ApplyGeometry()
        {
            float scale = DisplayScale;
            SetAmount(fill, fillValue * scale);
            SetAmount(trail, trailValue * scale);
            ApplyPendingGeometry(scale);
            if (barrier == null) return;
            bool visible = barrierShown > 0.5f && Maximum > 0;
            if (barrierGraphic != null) barrierGraphic.enabled = visible;
            if (!visible) return;
            float start = Mathf.Clamp01(fillValue * scale);
            float end = Mathf.Clamp01((fillValue + barrierShown / Maximum) * scale);
            barrier.anchorMin = new Vector2(start, barrier.anchorMin.y);
            barrier.anchorMax = new Vector2(Mathf.Max(start, end), barrier.anchorMax.y);
        }

        private void ApplyPendingGeometry(float scale)
        {
            if (pending == null) return;
            bool visible = pendingShown > 0.5f && Maximum > 0 && fillValue < 0.999f;
            if (pendingGraphic != null) pendingGraphic.enabled = visible;
            if (!visible) return;
            float start = Mathf.Clamp01(fillValue * scale);
            float end = Mathf.Clamp01((fillValue + pendingShown / Maximum) * scale);
            pending.anchorMin = new Vector2(start, pending.anchorMin.y);
            pending.anchorMax = new Vector2(Mathf.Max(start, end), pending.anchorMax.y);
        }

        private Color ResolvedPendingColor =>
            pendingColor.a > 0f ? pendingColor : new Color(fillColor.r, fillColor.g, fillColor.b, pendingAlpha);

        /// <summary>The pending part is made on demand next to the fill (drawn under it, so the fill grows into it).</summary>
        private void EnsurePendingPart()
        {
            if (pending == null) pending = CreateSegmentNextToFill("Pending", 0);
            if (pending != null && pendingGraphic == null)
            {
                pendingGraphic = pending.GetComponent<Graphic>();
                if (pendingGraphic != null)
                {
                    pendingGraphic.color = ResolvedPendingColor;
                    pendingGraphic.enabled = pendingShown > 0.5f;
                }
            }
        }

        private RectTransform CreateSegmentNextToFill(string partName, int siblingOffset)
        {
            if (fill == null || fill.parent == null) return null;
            var go = new GameObject(partName, typeof(RectTransform));
            go.layer = fill.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(fill.parent, false);
            rect.SetSiblingIndex(Mathf.Max(0, fill.GetSiblingIndex() + siblingOffset));
            rect.anchorMin = new Vector2(0f, fill.anchorMin.y);
            rect.anchorMax = new Vector2(0f, fill.anchorMax.y);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(0f, fill.offsetMin.y);
            rect.offsetMax = new Vector2(0f, fill.offsetMax.y);
            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            if (fill.TryGetComponent(out Image fillImage) && fillImage.sprite != null && fillImage.type != Image.Type.Filled)
                SetSprite(image, fillImage.sprite);
            return rect;
        }

        /// <summary>Scene / prefab bars made before barriers existed get a barrier part next to their fill.</summary>
        private void EnsureBarrierPart()
        {
            if (barrier == null) barrier = CreateSegmentNextToFill("Barrier", 1);
            if (barrier != null && barrierGraphic == null)
            {
                barrierGraphic = barrier.GetComponent<Graphic>();
                if (barrierGraphic != null)
                {
                    barrierGraphic.color = barrierColor;
                    barrierGraphic.enabled = barrierShown > 0.5f;
                }
            }
        }

        private void SetCount(float value)
        {
            countValue = value;
            int number = Mathf.RoundToInt(value);
            if (valueLabel == null || number == lastShownNumber) return;
            lastShownNumber = number;
            string text = string.Format(valueFormat, number, Maximum);
            if (barrierTarget > 0 && !string.IsNullOrEmpty(barrierValueFormat)) text += string.Format(barrierValueFormat, barrierTarget);
            if (pendingTarget > 0 && !string.IsNullOrEmpty(pendingValueFormat)) text += string.Format(pendingValueFormat, pendingTarget);
            valueLabel.text = text;
        }

        private void PushValuesToVisuals()
        {
            ApplyGeometry();
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
            barrierColor = style.barrier;
            EnsureBarrierPart();
            if (barrierGraphic != null) barrierGraphic.color = barrierColor;
            Sprite barrierSprite = style.barrierSprite != null ? style.barrierSprite : style.fillSprite;
            if (barrierGraphic is Image barrierImage && barrierSprite != null) SetSprite(barrierImage, barrierSprite);
            if (pendingGraphic != null)
            {
                pendingGraphic.color = ResolvedPendingColor;
                if (pendingGraphic is Image pendingImage && style.fillSprite != null) SetSprite(pendingImage, style.fillSprite);
            }
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
            Image barrierImage = NewImage("Barrier", background.rectTransform, style.barrier,
                style.barrierSprite != null ? style.barrierSprite : style.fillSprite);
            Stretch(barrierImage.rectTransform, 0f);
            barrierImage.enabled = false;
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
            view.barrier = barrierImage.rectTransform;
            view.barrierGraphic = null;
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
