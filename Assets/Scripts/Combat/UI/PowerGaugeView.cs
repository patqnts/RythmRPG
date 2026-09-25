using System;
using System.Collections.Generic;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Running total of an ability pattern's judgements, weighted exactly like the final performance
    /// (<see cref="RhythmPerformanceCalculator"/>): the multiplier is total weight / expected notes.
    /// </summary>
    public sealed class PowerTally
    {
        private AbilityOutcomeProfile profile;

        public int Expected { get; private set; }
        public int Resolved { get; private set; }
        public int Misses { get; private set; }
        public int Perfects { get; private set; }
        public float Total { get; private set; }

        public void Begin(AbilityOutcomeProfile outcome, int expected)
        {
            profile = outcome;
            Expected = Mathf.Max(0, expected);
            Resolved = Misses = Perfects = 0;
            Total = 0f;
        }

        /// <summary>Patterns can grow while they play (Ping-Pong volleys); the count never shrinks.</summary>
        public void SetExpected(int expected) => Expected = Mathf.Max(Expected, Mathf.Max(expected, Resolved));

        /// <summary>Counts one judged note; returns its weight.</summary>
        public float Add(HitJudgement judgement)
        {
            float weight = RhythmPerformanceCalculator.Weight(judgement, profile);
            Resolved++;
            Total += weight;
            if (judgement == HitJudgement.Miss) Misses++;
            if (judgement == HitJudgement.Perfect) Perfects++;
            if (Resolved > Expected) Expected = Resolved;
            return weight;
        }

        /// <summary>The weight of a Perfect (or the best judgement): the multiplier of a flawless pattern.</summary>
        public float BestWeight => Mathf.Max(0.0001f, Mathf.Max(
            Mathf.Max(RhythmPerformanceCalculator.Weight(HitJudgement.Perfect, profile),
                RhythmPerformanceCalculator.Weight(HitJudgement.Good, profile)),
            Mathf.Max(RhythmPerformanceCalculator.Weight(HitJudgement.Bad, profile),
                RhythmPerformanceCalculator.Weight(HitJudgement.Miss, profile))));

        /// <summary>Power banked so far; equals the ability's multiplier once every note is judged.</summary>
        public float Banked => Expected <= 0 ? 0f : Total / Expected;

        /// <summary>The most the pattern can still end on (every note left played at the best judgement).</summary>
        public float Potential => Expected <= 0 ? 0f : (Total + Mathf.Max(0, Expected - Resolved) * BestWeight) / Expected;

        /// <summary>Accuracy of the notes judged so far (the best multiplier before any).</summary>
        public float Running => Resolved == 0 ? BestWeight : Total / Resolved;

        public bool Flawless => Expected > 0 && Resolved >= Expected && Perfects == Resolved;
    }

    /// <summary>
    /// Power gauge shown while the player plays an ability's rhythm pattern: a bar that charges with every judged
    /// note (by the ability's Outcome Profile weights), a faint "potential" bar showing the most still reachable, and a
    /// live multiplier. The bar flashes each judgement's colour, shakes on a Miss, and when the pattern ends it locks
    /// on the ability's real multiplier and stamps its tier (or PERFECT!). Built from <see cref="PowerGaugeStyle"/>;
    /// the parts can also be assigned by hand.
    ///
    /// Every animation is a single PrimeTween tween kept as a handle and stopped through that handle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PowerGaugeView : MonoBehaviour
    {
        /// <summary>Viewport point (0..1) of the first lane's left edge on the hit line; false when unknown.</summary>
        public delegate bool LaneEdgeProvider(out Vector2 viewport);

        [SerializeField] private PowerGaugeStyle style;
        [Tooltip("Empty = the style's font, else the HUD font.")]
        [SerializeField] private TMP_FontAsset font;
        [Header("Parts (built automatically when empty)")]
        [SerializeField] private RectTransform popupRoot;
        [SerializeField] private RectTransform shaker;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform potentialFill;
        [SerializeField] private Image potentialImage;
        [SerializeField] private RectTransform fill;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image flash;
        [SerializeField] private RectTransform ticksRoot;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text multiplierLabel;
        [SerializeField] private TMP_Text rankLabel;

        private readonly PowerTally tally = new();
        private readonly List<GameObject> ticks = new();
        private Func<int> expectedSource;
        private LaneEdgeProvider laneEdge;
        private bool active;
        private bool visible;
        private bool finished;
        private float shownFill;
        private float shownPotential;
        private float shownValue;
        private float flashAlphaNow;
        private float hideAt = -1f;

        private Tween alphaTween;
        private Tween slideTween;
        private Tween fillTween;
        private Tween potentialTween;
        private Tween valueTween;
        private Tween punchTween;
        private Tween flashTween;
        private Tween shakeTween;
        private Tween rankScaleTween;

        public PowerGaugeStyle Style
        {
            get
            {
                if (style == null) style = PowerGaugeStyle.LoadOrDefault();
                return style;
            }
            set => style = value;
        }

        /// <summary>Lets the gauge stand beside the first lane (<see cref="PowerGaugeStyle.besideFirstLane"/>).</summary>
        public void SetLaneAnchor(LaneEdgeProvider provider) => laneEdge = provider;

        public PowerTally Tally => tally;
        public bool IsActive => active;
        public bool IsFinished => finished;
        public float ShownMultiplier => shownValue;

        /// <summary>
        /// Shows the gauge for a new pattern. <paramref name="expectedNow"/> is polled so notes added mid-pattern
        /// (Ping-Pong volleys) keep the scale right.
        /// </summary>
        public void Begin(AbilityOutcomeProfile profile, int expectedNotes, Func<int> expectedNow = null)
        {
            EnsureVisuals();
            PowerGaugeStyle s = Style;
            StopTweens();
            tally.Begin(profile, expectedNotes);
            expectedSource = expectedNow;
            active = true;
            finished = false;
            hideAt = -1f;

            BuildTicks(s);
            SetFill(0f);
            SetPotential(s.showPotential ? Scale(tally.Potential) : 0f);
            SetValue(Readout(s));
            SetFlashAlpha(0f);
            if (titleLabel != null)
            {
                titleLabel.enabled = !string.IsNullOrEmpty(s.title);
                titleLabel.text = s.title;
            }
            if (rankLabel != null)
            {
                rankLabel.enabled = false;
                rankLabel.rectTransform.localScale = Vector3.one;
            }
            if (multiplierLabel != null) multiplierLabel.rectTransform.localScale = Vector3.one;
            if (shaker != null) shaker.localPosition = Vector3.zero;
            FollowLane();
            PlayAppear(s);
        }

        /// <summary>One note of the pattern was judged.</summary>
        public void Add(HitJudgement judgement)
        {
            if (!active || finished) return;
            PowerGaugeStyle s = Style;
            tally.Add(judgement);
            AnimateTo(s, Readout(s), s.fillSeconds);
            Flash(s.FlashFor(judgement), s.flashSeconds);
            Punch(s.hitPunch * (judgement == HitJudgement.Perfect ? 1.05f : 1f), s.hitSeconds);
            if (judgement == HitJudgement.Miss) Shake(s.missShake);
        }

        /// <summary>
        /// The pattern ended: locks on the ability's real multiplier (<see cref="RhythmPerformanceResult.AverageWeight"/>)
        /// and stamps its tier.
        /// </summary>
        public void Finish(RhythmPerformanceResult performance)
        {
            if (!active) return;
            PowerGaugeStyle s = Style;
            finished = true;
            if (performance.ExpectedNoteCount > 0) tally.SetExpected(performance.ExpectedNoteCount);
            float final = performance.ExpectedNoteCount > 0 ? performance.AverageWeight : tally.Banked;
            bool flawless = IsFlawless(performance);

            AnimateTo(s, final, s.finishSeconds * 0.5f, final);
            PowerGaugeTier tier = s.TierFor(final);
            string label = flawless && !string.IsNullOrEmpty(s.flawlessLabel) ? s.flawlessLabel : tier.label;
            Color color = flawless ? s.flawlessColor : tier.color;
            Flash(color, s.finishSeconds);
            Punch(s.finishPunch, s.finishSeconds);

            if (rankLabel != null && !string.IsNullOrEmpty(label))
            {
                if (titleLabel != null) titleLabel.enabled = false;
                rankLabel.enabled = true;
                rankLabel.text = label;
                rankLabel.color = color;
                RectTransform rank = rankLabel.rectTransform;
                rankScaleTween.Stop();
                rank.localScale = Vector3.one * s.finishPunch;
                if (!Mathf.Approximately(s.finishPunch, 1f))
                    rankScaleTween = Tween.Scale(rank, 1f, s.finishSeconds, Ease.OutBack);
            }
            if (flawless) Shake(s.missShake);
        }

        /// <summary>Hides the gauge after <see cref="PowerGaugeStyle.holdAfterImpact"/> seconds.</summary>
        public void HideAfterImpact() => hideAt = Time.unscaledTime + Style.holdAfterImpact;

        public void Hide(bool animate)
        {
            active = false;
            finished = false;
            hideAt = -1f;
            expectedSource = null;
            if (!visible)
            {
                if (!animate) SnapHidden();
                return;
            }
            visible = false;
            if (!animate || group == null || popupRoot == null)
            {
                SnapHidden();
                return;
            }
            PowerGaugeStyle s = Style;
            alphaTween.Stop();
            slideTween.Stop();
            if (group.alpha > 0.001f) alphaTween = Tween.Alpha(group, 0f, s.hideSeconds, Ease.InQuad);
            Vector2 away = s.SlideOffset;
            if ((popupRoot.anchoredPosition - away).sqrMagnitude > 0.01f)
                slideTween = Tween.UIAnchoredPosition(popupRoot, away, s.hideSeconds, Ease.InQuad);
        }

        public void ResetImmediate()
        {
            active = false;
            finished = false;
            visible = false;
            hideAt = -1f;
            expectedSource = null;
            SnapHidden();
        }

        private void Update()
        {
            if (active && !finished && expectedSource != null)
            {
                int expected = expectedSource();
                if (expected > tally.Expected)
                {
                    tally.SetExpected(expected);
                    PowerGaugeStyle s = Style;
                    AnimateTo(s, Readout(s), s.fillSeconds);
                }
            }
            if (hideAt >= 0f && Time.unscaledTime >= hideAt)
            {
                hideAt = -1f;
                Hide(true);
            }
        }

        private void LateUpdate()
        {
            if (visible) FollowLane();
        }

        // Right edge a gap left of the first lane, bottom on the hit line. Falls back to Anchor / Position.
        private void FollowLane()
        {
            PowerGaugeStyle s = Style;
            if (!s.LabelsOnLeft || laneEdge == null || !(transform is RectTransform rect)
                || !(rect.parent is RectTransform parent)) return;
            if (!laneEdge(out Vector2 viewport)) return;
            Vector2 size = parent.rect.size;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(Mathf.Round(viewport.x * size.x - s.laneGap),
                Mathf.Round(viewport.y * size.y + s.laneHeightOffset));
        }

        private bool IsFlawless(RhythmPerformanceResult performance)
        {
            if (performance.ExpectedNoteCount <= 0 || performance.Judgements == null) return tally.Flawless;
            if (performance.Judgements.Count < performance.ExpectedNoteCount) return false;
            foreach (RhythmJudgementResult result in performance.Judgements)
                if (result.Judgement != HitJudgement.Perfect) return false;
            return true;
        }

        // ---------- values ----------

        private float Readout(PowerGaugeStyle s) => s.readout == PowerGaugeReadout.RunningAccuracy ? tally.Running : tally.Banked;

        // Multiplier -> bar fraction; a flawless pattern fills the bar.
        private float Scale(float multiplier) => Mathf.Clamp01(multiplier / tally.BestWeight);

        private void AnimateTo(PowerGaugeStyle s, float value, float seconds, float? lockFill = null)
        {
            seconds = Mathf.Max(0.01f, seconds);
            float fillTarget = Scale(lockFill ?? tally.Banked);
            float potentialTarget = s.showPotential ? Scale(lockFill ?? tally.Potential) : 0f;

            fillTween.Stop();
            if (!Mathf.Approximately(shownFill, fillTarget))
                fillTween = Tween.Custom(this, shownFill, fillTarget, seconds, (view, v) => view.SetFill(v), Ease.OutCubic);

            potentialTween.Stop();
            if (!Mathf.Approximately(shownPotential, potentialTarget))
            {
                // A drop waits so the lost chunk reads; a rise (more notes added) is immediate.
                float delay = potentialTarget < shownPotential && lockFill == null ? s.potentialDelay : 0f;
                potentialTween = Tween.Custom(this, new TweenSettings<float>(shownPotential, potentialTarget,
                    new TweenSettings(Mathf.Max(0.01f, s.potentialSeconds), Ease.OutCubic, startDelay: delay)),
                    (view, v) => view.SetPotential(v));
            }

            valueTween.Stop();
            if (!Mathf.Approximately(shownValue, value))
                valueTween = Tween.Custom(this, shownValue, value, seconds, (view, v) => view.SetValue(v), Ease.OutCubic);
        }

        private void SetFill(float fraction)
        {
            shownFill = Mathf.Clamp01(fraction);
            bool vertical = Style.IsVertical;
            if (fill != null) fill.anchorMax = vertical ? new Vector2(1f, shownFill) : new Vector2(shownFill, 1f);
            if (vertical) PlaceSideLabels();
        }

        // Vertical: the multiplier rides the top of the fill beside the bar (left when next to the lanes), the stamp
        // just above it.
        private void PlaceSideLabels()
        {
            float side = Style.LabelsOnLeft ? 0f : 1f;
            if (multiplierLabel != null)
            {
                RectTransform number = multiplierLabel.rectTransform;
                number.anchorMin = number.anchorMax = new Vector2(side, shownFill);
            }
            if (rankLabel != null)
            {
                RectTransform rank = rankLabel.rectTransform;
                rank.anchorMin = rank.anchorMax = new Vector2(side, shownFill);
            }
        }

        private void SetPotential(float fraction)
        {
            shownPotential = Mathf.Clamp01(fraction);
            if (potentialFill != null)
                potentialFill.anchorMax = Style.IsVertical ? new Vector2(1f, shownPotential) : new Vector2(shownPotential, 1f);
            if (potentialImage != null) potentialImage.enabled = Style.showPotential;
        }

        private void SetValue(float multiplier)
        {
            PowerGaugeStyle s = Style;
            shownValue = multiplier;
            Color color = s.TierFor(multiplier).color;
            if (multiplierLabel != null)
            {
                multiplierLabel.text = FormatMultiplier(s, multiplier);
                multiplierLabel.color = color;
            }
            if (fillImage != null) fillImage.color = color;
        }

        private static string FormatMultiplier(PowerGaugeStyle s, float value)
        {
            try
            {
                return string.Format(string.IsNullOrEmpty(s.multiplierFormat) ? "x{0:0.00}" : s.multiplierFormat, value);
            }
            catch (FormatException)
            {
                return $"x{value:0.00}";
            }
        }

        // ---------- feedback ----------

        private void Flash(Color color, float seconds)
        {
            if (flash == null) return;
            flashTween.Stop();
            flash.color = new Color(color.r, color.g, color.b, 1f);
            SetFlashAlpha(Style.flashAlpha);
            if (flashAlphaNow > 0.001f)
                flashTween = Tween.Custom(this, flashAlphaNow, 0f, Mathf.Max(0.01f, seconds), (view, a) => view.SetFlashAlpha(a), Ease.OutQuad);
        }

        private void SetFlashAlpha(float alpha)
        {
            flashAlphaNow = alpha;
            if (flash == null) return;
            Color c = flash.color;
            c.a = alpha;
            flash.color = c;
        }

        private void Punch(float scale, float seconds)
        {
            if (multiplierLabel == null) return;
            punchTween.Stop();
            RectTransform number = multiplierLabel.rectTransform;
            number.localScale = Vector3.one * scale;
            if (!Mathf.Approximately(scale, 1f))
                punchTween = Tween.Scale(number, 1f, Mathf.Max(0.01f, seconds), Ease.OutQuad);
        }

        private void Shake(float pixels)
        {
            if (shaker == null || pixels <= 0f) return;
            shakeTween.Stop();
            shaker.localPosition = Vector3.zero;
            shakeTween = Tween.ShakeLocalPosition(shaker, new Vector3(pixels, pixels * 0.6f, 0f), 0.22f, frequency: 30f);
        }

        private void PlayAppear(PowerGaugeStyle s)
        {
            if (popupRoot == null || group == null) return;
            alphaTween.Stop();
            slideTween.Stop();
            bool wasVisible = visible && group.alpha > 0.5f;
            visible = true;
            if (wasVisible)
            {
                group.alpha = 1f;
                popupRoot.anchoredPosition = Vector2.zero;
                return;
            }
            group.alpha = 0f;
            popupRoot.anchoredPosition = s.SlideOffset;
            alphaTween = Tween.Alpha(group, 1f, s.appearSeconds * 0.7f, Ease.OutQuad);
            if (!Mathf.Approximately(s.slideInDistance, 0f))
                slideTween = Tween.UIAnchoredPosition(popupRoot, Vector2.zero, s.appearSeconds, Ease.OutBack);
        }

        private void StopTweens()
        {
            alphaTween.Stop();
            slideTween.Stop();
            fillTween.Stop();
            potentialTween.Stop();
            valueTween.Stop();
            punchTween.Stop();
            flashTween.Stop();
            shakeTween.Stop();
            rankScaleTween.Stop();
        }

        private void SnapHidden()
        {
            EnsureVisuals();
            StopTweens();
            if (group != null) group.alpha = 0f;
            if (popupRoot != null) popupRoot.anchoredPosition = Vector2.zero;
            if (shaker != null) shaker.localPosition = Vector3.zero;
            if (multiplierLabel != null) multiplierLabel.rectTransform.localScale = Vector3.one;
            if (rankLabel != null)
            {
                rankLabel.rectTransform.localScale = Vector3.one;
                rankLabel.enabled = false;
            }
            SetFlashAlpha(0f);
        }

        // ---------- building ----------

        private void BuildTicks(PowerGaugeStyle s)
        {
            foreach (GameObject tick in ticks)
            {
                if (tick == null) continue;
                if (Application.isPlaying) Destroy(tick);
                else DestroyImmediate(tick);
            }
            ticks.Clear();
            if (ticksRoot == null || !s.showTierTicks || s.tiers == null) return;
            foreach (PowerGaugeTier tier in s.tiers)
            {
                if (tier == null) continue;
                float x = tier.minMultiplier / tally.BestWeight;
                if (x <= 0.001f || x >= 0.999f) continue;
                Image mark = NewImage("Tick", ticksRoot, s.tick, null);
                RectTransform rect = mark.rectTransform;
                rect.anchorMin = s.IsVertical ? new Vector2(0f, x) : new Vector2(x, 0f);
                rect.anchorMax = s.IsVertical ? new Vector2(1f, x) : new Vector2(x, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = s.IsVertical ? new Vector2(0f, 2f) : new Vector2(2f, 0f);
                rect.anchoredPosition = Vector2.zero;
                ticks.Add(mark.gameObject);
            }
        }

        private void EnsureVisuals()
        {
            if (popupRoot != null && shaker != null && group != null && fill != null && fillImage != null
                && multiplierLabel != null) return;
            BuildVisuals(Style);
        }

        private void BuildVisuals(PowerGaugeStyle s)
        {
            if (!(transform is RectTransform root)) return;
            root.anchorMin = root.anchorMax = root.pivot = s.anchor;
            root.anchoredPosition = s.position;
            root.sizeDelta = s.OrientedBarSize;
            bool vertical = s.IsVertical;
            bool left = s.LabelsOnLeft;

            popupRoot = FindOrCreateRect(root, "Popup");
            Stretch(popupRoot, 0f);
            group = popupRoot.GetComponent<CanvasGroup>();
            if (group == null) group = popupRoot.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            shaker = FindOrCreateRect(popupRoot, "Shaker");
            Stretch(shaker, 0f);

            Image frame = NewImage("Frame", shaker, s.frame, s.frameSprite);
            Stretch(frame.rectTransform, 0f);
            Image background = NewImage("Background", shaker, s.background, s.backgroundSprite);
            Stretch(background.rectTransform, s.frameThickness);

            potentialImage = NewImage("Potential", background.rectTransform, s.potential, s.fillSprite);
            potentialFill = potentialImage.rectTransform;
            AnchorFillStart(potentialFill, vertical);
            fillImage = NewImage("Fill", background.rectTransform, s.TierFor(0f).color, s.fillSprite);
            fill = fillImage.rectTransform;
            AnchorFillStart(fill, vertical);
            ticksRoot = FindOrCreateRect(background.rectTransform, "Ticks");
            Stretch(ticksRoot, 0f);
            flash = NewImage("Flash", background.rectTransform, new Color(1f, 1f, 1f, 0f), null);
            Stretch(flash.rectTransform, 0f);

            TMP_FontAsset fontAsset = font != null ? font : s.font != null ? s.font : CombatHudStyle.LoadOrDefault().FontAsset;

            titleLabel = CombatText.CreateUGUI("Title", shaker, fontAsset, s.titleFontSize, s.titleColor,
                left ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft, s.textOutline, s.outlineWidth);
            titleLabel.text = s.title;
            titleLabel.characterSpacing = 8f;
            RectTransform titleRect = titleLabel.rectTransform;
            titleRect.pivot = new Vector2(0f, 0f);
            if (vertical)
            {
                // Above the thin bar, from its outer edge (right edge when the labels are on the left).
                titleRect.pivot = new Vector2(left ? 1f : 0f, 0f);
                titleRect.anchorMin = titleRect.anchorMax = new Vector2(left ? 1f : 0f, 1f);
                titleRect.anchoredPosition = new Vector2(0f, 6f);
                titleRect.sizeDelta = new Vector2(Mathf.Max(160f, s.titleFontSize * 6f), s.titleFontSize + 10f);
            }
            else
            {
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.anchoredPosition = new Vector2(2f, 4f);
                titleRect.sizeDelta = new Vector2(0f, s.titleFontSize + 10f);
            }

            rankLabel = CombatText.CreateUGUI("Rank", shaker, fontAsset, s.rankFontSize, Color.white,
                vertical ? left ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft : TextAlignmentOptions.Bottom,
                s.textOutline, s.outlineWidth);
            rankLabel.fontStyle = FontStyles.Bold;
            rankLabel.characterSpacing = 6f;
            rankLabel.enabled = false;
            RectTransform rankRect = rankLabel.rectTransform;
            if (vertical)
            {
                // Beside the bar, just above the multiplier (both follow the top of the fill).
                rankRect.anchorMin = rankRect.anchorMax = new Vector2(left ? 0f : 1f, 0f);
                rankRect.pivot = new Vector2(left ? 1f : 0f, 0f);
                rankRect.anchoredPosition = new Vector2(left ? -14f : 14f, s.multiplierFontSize * 0.5f + 8f);
                rankRect.sizeDelta = new Vector2(Mathf.Max(260f, s.rankFontSize * 8f), s.rankFontSize + 12f);
            }
            else
            {
                rankRect.anchorMin = new Vector2(0f, 1f);
                rankRect.anchorMax = new Vector2(1f, 1f);
                rankRect.pivot = new Vector2(0.5f, 0f);
                rankRect.anchoredPosition = new Vector2(0f, 2f);
                rankRect.sizeDelta = new Vector2(0f, s.rankFontSize + 12f);
            }

            multiplierLabel = CombatText.CreateUGUI("Multiplier", shaker, fontAsset, s.multiplierFontSize, Color.white,
                left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left, s.textOutline, s.outlineWidth);
            multiplierLabel.fontStyle = FontStyles.Bold;
            RectTransform numberRect = multiplierLabel.rectTransform;
            numberRect.anchorMin = numberRect.anchorMax = new Vector2(left ? 0f : 1f, 0.5f);
            numberRect.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            numberRect.anchoredPosition = new Vector2(left ? -14f : 14f, 0f);
            numberRect.sizeDelta = new Vector2(Mathf.Max(160f, s.multiplierFontSize * 4.2f), s.multiplierFontSize + 16f);
            multiplierLabel.text = FormatMultiplier(s, 0f);
            if (vertical) PlaceSideLabels();
        }

        // Fills grow from the left (horizontal) or the bottom (vertical); their far anchor is the value.
        private static void AnchorFillStart(RectTransform rect, bool vertical)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = vertical ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            rect.pivot = vertical ? new Vector2(0.5f, 0f) : new Vector2(0f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform FindOrCreateRect(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            if (existing is RectTransform existingRect) return existingRect;
            var go = new GameObject(objectName, typeof(RectTransform));
            if (parent != null) go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string objectName, Transform parent, Color color, Sprite sprite)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            if (parent != null) go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            }
            return image;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Builds a power gauge under the given parent (mirrors ComboStreakView.CreateTemplate).</summary>
        public static PowerGaugeView CreateTemplate(Transform parent, PowerGaugeStyle activeStyle, CombatHudStyle hudStyle = null)
        {
            var go = new GameObject("Power Gauge", typeof(RectTransform));
            if (parent != null) go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            PowerGaugeView view = go.AddComponent<PowerGaugeView>();
            view.style = activeStyle;
            PowerGaugeStyle s = view.Style;
            view.font = s.font != null ? s.font : hudStyle != null ? hudStyle.FontAsset : null;
            view.BuildVisuals(s);
            return view;
        }

        private void OnDisable() => StopTweens();
    }
}
