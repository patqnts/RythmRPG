using TMPro;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Sizes the Dialogue System's alert panel (quest updates, ShowAlert) to its text and centres it at the top of the
    /// screen, in whole game pixels. Sits on the alert panel next to its UIPanel.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(30500)]
    public sealed class PixelAlertBox : MonoBehaviour
    {
        public RectTransform body;
        public RectTransform bodyShadow;
        public TextMeshProUGUI label;
        [Min(16)] public int maxTextWidth = 240;
        [Min(0)] public int paddingX = 6;
        [Min(0)] public int paddingTop = 3;
        [Min(0)] public int paddingBottom = 4;
        [Tooltip("Distance from the top of the screen.")]
        [Min(0)] public int topMargin = 8;
        public Vector2Int shadowOffset = new(0, -1);

        private PixelDialogueSpace space;
        private PixelDialogueTheme theme;
        private int appliedThemeVersion = int.MinValue;
        private string laidOutText;
        private Vector2Int bodySize;

        private void OnEnable() => laidOutText = null;

        private void LateUpdate()
        {
            if (space == null) space = PixelDialogueSpace.For(this);
            if (space == null || label == null || body == null) return;
            if (theme == null) theme = PixelDialogueTheme.For(this);
            if (theme != null && theme.Palette != null)
            {
                int version = theme.Version + theme.Palette.Revision;
                if (version != appliedThemeVersion)
                {
                    theme.Apply(transform, theme.Palette);
                    appliedThemeVersion = version;
                }
            }

            if (laidOutText != label.text)
            {
                laidOutText = label.text;
                PixelDialogueStyle.FixSlicedPixelsPerUnit(body);
                PixelDialogueStyle.FixSlicedPixelsPerUnit(bodyShadow);
                PixelDialogueStyle.EnsurePointFiltering(label.font);
                Vector2 size = label.GetPreferredValues(maxTextWidth, 0f);
                int width = Mathf.Min(PixelDialogueStyle.Ceil(size.x), maxTextWidth);
                int height = Mathf.Max(PixelDialogueStyle.LineBoxHeight(label), PixelDialogueStyle.Ceil(size.y));
                PixelDialogueStyle.SetTopLeft(label.rectTransform, paddingX, paddingTop, Mathf.Min(width + 2, maxTextWidth), height);
                bodySize = new Vector2Int(Mathf.Max(20, width + 2 * paddingX), paddingTop + height + paddingBottom);
            }

            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            Vector2 origin = new(Mathf.Round((space.Size.x - bodySize.x) * 0.5f), Mathf.Round(space.Size.y - topMargin - bodySize.y));
            rect.anchoredPosition = space.SnapToScreenPixels(origin);

            Place(body, Vector2.zero);
            Place(bodyShadow, shadowOffset);
        }

        private void Place(RectTransform target, Vector2 position)
        {
            if (target == null) return;
            target.anchorMin = target.anchorMax = target.pivot = Vector2.zero;
            target.anchoredPosition = position;
            target.sizeDelta = bodySize;
        }
    }
}
