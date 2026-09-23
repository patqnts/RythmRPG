using PrimeTween;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The combo streak on the centre-right of the screen: a big TextMeshPro number + a "COMBO" label. It slides in
    /// once the combo reaches <see cref="ComboStreakStyle.minComboToShow"/>, then every hit snaps the number up in
    /// scale, kicks its tilt, flashes it and settles it to a heat color that climbs with the streak (bigger punch and
    /// a shake on milestones). When the combo breaks it flashes red and drops away.
    ///
    /// Every animation is a single PrimeTween tween kept as a handle and stopped through that handle. No Sequences
    /// and no Tween.StopAll on shared transforms: PrimeTween refuses to stop tweens nested in a Sequence directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComboStreakView : MonoBehaviour
    {
        [SerializeField] private ComboStreakStyle style;
        [Tooltip("Empty = the style's font, else the HUD font.")]
        [SerializeField] private TMP_FontAsset font;
        [Header("Parts (built automatically when empty)")]
        [SerializeField] private RectTransform popupRoot;
        [SerializeField] private RectTransform shaker;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TMP_Text numberLabel;
        [SerializeField] private TMP_Text comboLabel;

        private bool visible;
        private int shownCombo;
        private float tiltSign = 1f;

        private Tween alphaTween;
        private Tween slideTween;
        private Tween shakeTween;
        private Tween numberScaleTween;
        private Tween numberTiltTween;
        private Tween numberColorTween;
        private Tween labelScaleTween;

        public ComboStreakStyle Style
        {
            get
            {
                if (style == null) style = ComboStreakStyle.LoadOrDefault();
                return style;
            }
            set => style = value;
        }

        public bool IsVisible => visible;
        public int ShownCombo => shownCombo;

        /// <summary>Push the current combo. 0 (or below the style's threshold) hides the streak.</summary>
        public void Show(int combo)
        {
            EnsureVisuals();
            ComboStreakStyle s = Style;
            if (combo < Mathf.Max(1, s.minComboToShow))
            {
                Hide(true);
                return;
            }

            shownCombo = combo;
            numberLabel.text = combo.ToString();
            if (!visible)
            {
                visible = true;
                PlayAppear(s);
            }
            PlayHit(s, s.ColorFor(combo), s.IsMilestone(combo));
        }

        /// <summary>Hides the streak (the combo broke, or the battle ended).</summary>
        public void Hide(bool animate)
        {
            if (!visible)
            {
                if (!animate) SnapHidden();
                return;
            }
            visible = false;
            shownCombo = 0;
            if (!animate || popupRoot == null || group == null)
            {
                SnapHidden();
                return;
            }

            ComboStreakStyle s = Style;
            StopTweens();
            if (numberLabel != null) numberLabel.color = s.breakColor;
            if (group.alpha > 0.001f)
                alphaTween = Tween.Alpha(group, 0f, s.hideSeconds, Ease.InQuad);
            var dropTo = new Vector2(0f, -s.breakDrop);
            if ((popupRoot.anchoredPosition - dropTo).sqrMagnitude > 0.01f)
                slideTween = Tween.UIAnchoredPosition(popupRoot, dropTo, s.hideSeconds, Ease.InQuad);
            if (numberLabel != null && !Mathf.Approximately(numberLabel.rectTransform.localScale.x, 0.8f))
                numberScaleTween = Tween.Scale(numberLabel.rectTransform, 0.8f, s.hideSeconds, Ease.InQuad);
        }

        /// <summary>Hides instantly with no animation (battle end / rebind).</summary>
        public void ResetImmediate()
        {
            visible = false;
            shownCombo = 0;
            SnapHidden();
        }

        private void PlayAppear(ComboStreakStyle s)
        {
            if (popupRoot == null || group == null) return;
            alphaTween.Stop();
            slideTween.Stop();
            group.alpha = 0f;
            popupRoot.anchoredPosition = new Vector2(s.slideInDistance, 0f);
            alphaTween = Tween.Alpha(group, 1f, s.appearSeconds * 0.6f, Ease.OutQuad);
            if (!Mathf.Approximately(s.slideInDistance, 0f))
                slideTween = Tween.UIAnchoredPosition(popupRoot, Vector2.zero, s.appearSeconds, Ease.OutBack);
        }

        private void PlayHit(ComboStreakStyle s, Color heat, bool milestone)
        {
            if (numberLabel == null) return;
            RectTransform number = numberLabel.rectTransform;
            numberScaleTween.Stop();
            numberTiltTween.Stop();
            numberColorTween.Stop();

            // Snap: jump up instantly, then settle fast.
            float peak = milestone ? s.milestoneScale : s.hitScale;
            float settle = s.hitSeconds * (milestone ? 1.7f : 1f);
            // PrimeTween warns when a tween starts at its end value, so each one only runs when it has
            // somewhere to go (e.g. below the warm threshold the flash color and the heat color are both white).
            number.localScale = Vector3.one * peak;
            if (!Mathf.Approximately(peak, 1f))
                numberScaleTween = Tween.Scale(number, 1f, settle, Ease.OutQuad);

            tiltSign = -tiltSign;
            float tilt = s.hitTilt * tiltSign * (milestone ? 1.6f : 1f);
            Quaternion kick = Quaternion.Euler(0f, 0f, tilt);
            number.localRotation = kick;
            if (!Mathf.Approximately(tilt, 0f))
                numberTiltTween = Tween.LocalRotation(number, kick, Quaternion.identity, settle * 1.4f, Ease.OutBack);

            numberLabel.color = s.flashColor;
            if (!SameColor(s.flashColor, heat))
                numberColorTween = Tween.Color(numberLabel, heat, s.flashSeconds, Ease.OutQuad);

            if (comboLabel != null)
            {
                labelScaleTween.Stop();
                RectTransform label = comboLabel.rectTransform;
                label.localScale = Vector3.one * (milestone ? 1.25f : 1.1f);
                labelScaleTween = Tween.Scale(label, 1f, settle, Ease.OutQuad);
            }

            if (milestone && shaker != null && s.milestoneShake > 0f)
            {
                shakeTween.Stop();
                shaker.localPosition = Vector3.zero;
                shakeTween = Tween.ShakeLocalPosition(shaker, new Vector3(s.milestoneShake, s.milestoneShake * 0.6f, 0f),
                    0.25f, frequency: 30f);
            }
        }

        private static bool SameColor(Color a, Color b) =>
            Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) &&
            Mathf.Approximately(a.b, b.b) && Mathf.Approximately(a.a, b.a);

        private void StopTweens()
        {
            alphaTween.Stop();
            slideTween.Stop();
            shakeTween.Stop();
            numberScaleTween.Stop();
            numberTiltTween.Stop();
            numberColorTween.Stop();
            labelScaleTween.Stop();
        }

        private void SnapHidden()
        {
            EnsureVisuals();
            StopTweens();
            if (group != null) group.alpha = 0f;
            if (popupRoot != null) popupRoot.anchoredPosition = Vector2.zero;
            if (shaker != null) shaker.localPosition = Vector3.zero;
            if (numberLabel != null)
            {
                numberLabel.rectTransform.localScale = Vector3.one;
                numberLabel.rectTransform.localRotation = Quaternion.identity;
            }
            if (comboLabel != null) comboLabel.rectTransform.localScale = Vector3.one;
        }

        private void EnsureVisuals()
        {
            if (popupRoot != null && shaker != null && group != null && numberLabel != null && comboLabel != null) return;
            BuildVisuals(Style);
        }

        private void BuildVisuals(ComboStreakStyle s)
        {
            if (!(transform is RectTransform root)) return;
            root.anchorMin = root.anchorMax = root.pivot = s.anchor;
            root.anchoredPosition = s.position;
            root.sizeDelta = s.size;

            popupRoot = FindOrCreateRect(root, "Popup");
            Stretch(popupRoot);
            group = popupRoot.GetComponent<CanvasGroup>();
            if (group == null) group = popupRoot.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            shaker = FindOrCreateRect(popupRoot, "Shaker");
            Stretch(shaker);

            TMP_FontAsset fontAsset = font != null ? font : s.font != null ? s.font : CombatHudStyle.LoadOrDefault().FontAsset;

            if (numberLabel == null)
                numberLabel = CombatText.CreateUGUI("Number", shaker, fontAsset, s.numberFontSize, s.numberColor,
                    TextAlignmentOptions.Right, s.textOutline, s.outlineWidth);
            RectTransform numberRect = numberLabel.rectTransform;
            numberRect.anchorMin = new Vector2(0f, 0.26f);
            numberRect.anchorMax = Vector2.one;
            numberRect.offsetMin = numberRect.offsetMax = Vector2.zero;
            // Scale / tilt around a point near the digits (right-aligned), so the punch reads from the number itself.
            numberRect.pivot = new Vector2(0.8f, 0.45f);
            numberLabel.fontStyle = FontStyles.Bold;

            if (comboLabel == null)
                comboLabel = CombatText.CreateUGUI("Label", shaker, fontAsset, s.labelFontSize, s.labelColor,
                    TextAlignmentOptions.TopRight, s.textOutline, s.outlineWidth);
            comboLabel.text = s.label;
            comboLabel.characterSpacing = 12f;
            RectTransform labelRect = comboLabel.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = new Vector2(1f, 0.3f);
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            labelRect.pivot = new Vector2(0.85f, 0.5f);
        }

        private static RectTransform FindOrCreateRect(Transform parent, string objectName)
        {
            Transform existing = parent.Find(objectName);
            if (existing is RectTransform existingRect) return existingRect;
            var go = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Builds a combo streak under the given parent (mirrors ResourceBarView.CreateTemplate).</summary>
        public static ComboStreakView CreateTemplate(Transform parent, ComboStreakStyle activeStyle, CombatHudStyle hudStyle = null)
        {
            var go = new GameObject("Combo Streak", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ComboStreakView view = go.AddComponent<ComboStreakView>();
            view.style = activeStyle;
            ComboStreakStyle s = view.Style;
            view.font = s.font != null ? s.font : hudStyle != null ? hudStyle.FontAsset : null;
            view.BuildVisuals(s);
            return view;
        }

        private void OnDisable() => StopTweens();
    }
}
