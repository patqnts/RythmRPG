using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// One reward card. The parts are plain uGUI objects so a hand-made card prefab works as long as they are assigned;
    /// <see cref="RewardSelectionScreen.CreateTemplate"/> builds the default card. <see cref="Root"/> is what moves and
    /// scales (the card's layout slot stays put).
    /// </summary>
    public sealed class RewardCardView : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private UnityEngine.UI.Image frame;
        [SerializeField] private UnityEngine.UI.Image background;
        [SerializeField] private UnityEngine.UI.Image kindStrip;
        [SerializeField] private TMP_Text kindLabel;
        [SerializeField] private UnityEngine.UI.Image icon;
        [SerializeField] private TMP_Text iconGlyph;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text summary;
        [SerializeField] private TMP_Text details;
        [SerializeField] private TMP_Text keyHint;
        [SerializeField] private TMP_Text stamp;

        public RectTransform Root => root != null ? root : (RectTransform)transform;
        public CanvasGroup Group => group;
        public UnityEngine.UI.Image Frame => frame;
        public TMP_Text Stamp => stamp;
        public RewardOptionData Option { get; private set; }
        public RewardPreview Preview { get; private set; }

        public void Assign(RectTransform cardRoot, CanvasGroup cardGroup, UnityEngine.UI.Image cardFrame, UnityEngine.UI.Image cardBackground,
            UnityEngine.UI.Image strip, TMP_Text kind, UnityEngine.UI.Image cardIcon, TMP_Text glyph, TMP_Text cardTitle, TMP_Text cardSummary,
            TMP_Text cardDetails, TMP_Text hint, TMP_Text cardStamp)
        {
            root = cardRoot;
            group = cardGroup;
            frame = cardFrame;
            background = cardBackground;
            kindStrip = strip;
            kindLabel = kind;
            icon = cardIcon;
            iconGlyph = glyph;
            title = cardTitle;
            summary = cardSummary;
            details = cardDetails;
            keyHint = hint;
            stamp = cardStamp;
        }

        public void Setup(RewardSelectionStyle style, RewardOptionData option, RewardPreview preview, Sprite iconSprite,
            string glyph, int number)
        {
            Option = option;
            Preview = preview;
            Color kindColor = style.KindColor(option.kind);
            if (background != null) background.color = style.CardColor;
            if (frame != null) frame.color = style.FrameColor;
            if (kindStrip != null) kindStrip.color = kindColor;
            if (kindLabel != null) kindLabel.text = style.KindLabel(option.kind);
            if (icon != null)
            {
                icon.sprite = iconSprite;
                icon.enabled = iconSprite != null;
                icon.preserveAspect = true;
            }
            if (iconGlyph != null)
            {
                iconGlyph.text = iconSprite == null ? glyph : string.Empty;
                iconGlyph.color = kindColor;
            }
            if (title != null) title.text = Plain(preview?.Title ?? option.contentId);
            if (summary != null) summary.text = Plain(preview?.Summary ?? string.Empty);
            if (details != null)
                details.text = preview != null ? Plain(string.Join("\n", preview.Details)) : string.Empty;
            if (keyHint != null) keyHint.text = number > 0 && number <= 9 ? $"[{number}]" : string.Empty;
            if (stamp != null)
            {
                stamp.text = style.ClaimedStamp;
                stamp.color = new Color(kindColor.r, kindColor.g, kindColor.b, 0f);
            }
        }

        /// <summary>
        /// Swaps symbols a pixel font usually lacks (bullets, arrows, middle dots) for plain ASCII, so preview text never
        /// shows missing-glyph boxes.
        /// </summary>
        public static string Plain(string text) => string.IsNullOrEmpty(text) ? string.Empty
            : text.Replace("• ", "- ").Replace("•", "-").Replace("→", ">").Replace("←", "<").Replace(" · ", " | ")
                .Replace("·", "|").Replace("◦", "-").Replace("…", "...");

        /// <summary>0 = resting, 1 = fully selected: frame takes the kind colour.</summary>
        public void SetHighlight(RewardSelectionStyle style, float amount)
        {
            if (frame == null || Option == null) return;
            frame.color = Color.Lerp(style.FrameColor, style.KindColor(Option.kind), Mathf.Clamp01(amount));
        }
    }
}
