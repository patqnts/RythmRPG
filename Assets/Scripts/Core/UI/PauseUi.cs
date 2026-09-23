using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.Core
{
    /// <summary>Colors and sizes of the pause menu. Edit here to restyle it.</summary>
    public static class PauseTheme
    {
        public static readonly Color Dim = new(0.02f, 0.02f, 0.05f, 0.78f);
        public static readonly Color CountdownDim = new(0.02f, 0.02f, 0.05f, 0.35f);
        public static readonly Color Panel = new(0.078f, 0.09f, 0.15f, 0.97f);
        public static readonly Color PanelEdge = new(1f, 0.78f, 0.35f, 0.9f);
        public static readonly Color Accent = new(1f, 0.78f, 0.35f, 1f);
        public static readonly Color Text = new(0.95f, 0.95f, 1f, 1f);
        public static readonly Color MutedText = new(0.62f, 0.66f, 0.8f, 1f);
        public static readonly Color Button = new(0.13f, 0.15f, 0.24f, 1f);
        public static readonly Color ButtonSelected = new(0.25f, 0.29f, 0.46f, 1f);
        public static readonly Color ButtonPressed = new(1f, 0.7f, 0.25f, 1f);
        public static readonly Color ButtonDisabled = new(0.1f, 0.11f, 0.17f, 0.7f);
        public static readonly Color TabActive = new(0.42f, 0.33f, 0.16f, 1f);
        public static readonly Color RowStripe = new(1f, 1f, 1f, 0.03f);
    }

    /// <summary>Small uGUI + TextMeshPro builders for the runtime-built pause menu.</summary>
    internal static class PauseUi
    {
        private const int UiLayer = 5;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        public static RectTransform Centered(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        public static Image AddImage(RectTransform rect, Color color, bool raycast = false)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = Rect("Text", parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        public static LayoutElement Size(Component target, float width = -1f, float height = -1f, float flexibleWidth = -1f,
            float flexibleHeight = -1f)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            if (element == null) element = target.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) element.preferredWidth = element.minWidth = width;
            if (height >= 0f) element.preferredHeight = element.minHeight = height;
            if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0f) element.flexibleHeight = flexibleHeight;
            return element;
        }

        public static VerticalLayoutGroup Vertical(RectTransform rect, float spacing, RectOffset padding = null,
            TextAnchor alignment = TextAnchor.UpperCenter)
        {
            VerticalLayoutGroup group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = alignment;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing, RectOffset padding = null,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            HorizontalLayoutGroup group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childAlignment = alignment;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            return group;
        }

        public static ColorBlock Colors(Color normal)
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = PauseTheme.ButtonSelected;
            colors.selectedColor = PauseTheme.ButtonSelected;
            colors.pressedColor = PauseTheme.ButtonPressed;
            colors.disabledColor = PauseTheme.ButtonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        public static Button MakeButton(Transform parent, string text, Action onClick, float height = 64f, float fontSize = 30f,
            float width = -1f)
        {
            RectTransform rect = Rect(text + " Button", parent);
            Image background = AddImage(rect, Color.white, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = Colors(PauseTheme.Button);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            rect.gameObject.AddComponent<PointerSelects>();

            TextMeshProUGUI label = Label(rect, text, fontSize, PauseTheme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            // Horizontal padding only, and no vertical clipping: TMP's Ellipsis/Truncate modes hide a line that is even
            // slightly taller than its box, which blanked the labels of the shorter buttons.
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(10f, 0f);
            label.rectTransform.offsetMax = new Vector2(-10f, 0f);
            label.overflowMode = TextOverflowModes.Overflow;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Min(14f, fontSize);
            label.fontSizeMax = fontSize;
            Size(rect, width, height);
            return button;
        }

        public static TMP_Text ButtonLabel(Button button) => button.GetComponentInChildren<TMP_Text>(true);

        /// <summary>Explicit navigation (automatic navigation could wander into the game's own UI behind the menu).</summary>
        public static void Nav(Selectable selectable, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            if (selectable == null) return;
            selectable.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right
            };
        }

        public static void Select(Selectable selectable)
        {
            if (selectable == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    /// <summary>Hovering with the mouse selects, so mouse, keyboard and gamepad share one highlight.</summary>
    internal sealed class PointerSelects : MonoBehaviour, IPointerEnterHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            Selectable selectable = GetComponent<Selectable>();
            if (selectable != null && selectable.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }

    /// <summary>
    /// A settings row that cycles through options: label on the left, "&lt; value &gt;" on the right. Left / Right
    /// (keys, d-pad, stick) change the value; Submit steps forward; the arrow buttons work with the mouse.
    /// </summary>
    internal sealed class PauseSelector : Selectable, ISubmitHandler
    {
        private TMP_Text valueText;
        private string[] options = Array.Empty<string>();
        private Button leftArrow, rightArrow;

        public int Index { get; private set; }
        public event Action<int> Changed;

        public static PauseSelector Create(Transform parent, string label)
        {
            RectTransform row = PauseUi.Rect(label + " Selector", parent);
            Image background = PauseUi.AddImage(row, Color.white, true);
            PauseUi.Size(row, height: 64f);
            PauseUi.Horizontal(row, 12f, new RectOffset(24, 12, 6, 6));

            PauseSelector selector = row.gameObject.AddComponent<PauseSelector>();
            selector.targetGraphic = background;
            selector.colors = PauseUi.Colors(PauseTheme.Button);
            row.gameObject.AddComponent<PointerSelects>();

            TextMeshProUGUI nameLabel = PauseUi.Label(row, label, 30f, PauseTheme.Text, TextAlignmentOptions.MidlineLeft);
            PauseUi.Size(nameLabel, flexibleWidth: 1f);

            selector.leftArrow = PauseUi.MakeButton(row, "<", () => selector.Step(-1), 52f, 30f, 56f);
            selector.valueText = PauseUi.Label(row, string.Empty, 30f, PauseTheme.Accent, TextAlignmentOptions.Center,
                FontStyles.Bold);
            PauseUi.Size(selector.valueText, 320f);
            selector.rightArrow = PauseUi.MakeButton(row, ">", () => selector.Step(1), 52f, 30f, 56f);
            foreach (Button arrow in new[] { selector.leftArrow, selector.rightArrow })
            {
                arrow.navigation = new Navigation { mode = Navigation.Mode.None };
                Destroy(arrow.GetComponent<PointerSelects>());
            }
            return selector;
        }

        public void SetOptions(string[] values, int index)
        {
            options = values ?? Array.Empty<string>();
            Index = options.Length == 0 ? 0 : Mathf.Clamp(index, 0, options.Length - 1);
            Refresh();
        }

        public void SetInteractableState(bool value)
        {
            interactable = value;
            if (leftArrow != null) leftArrow.interactable = value;
            if (rightArrow != null) rightArrow.interactable = value;
            if (valueText != null) valueText.color = value ? PauseTheme.Accent : PauseTheme.MutedText;
        }

        public void Step(int direction)
        {
            if (!IsInteractable() || options.Length == 0) return;
            Index = (Index + direction + options.Length) % options.Length;
            Refresh();
            Changed?.Invoke(Index);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) Step(-1);
            else if (eventData.moveDir == MoveDirection.Right) Step(1);
            else base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData) => Step(1);

        private void Refresh()
        {
            if (valueText != null) valueText.text = options.Length > 0 ? options[Index] : string.Empty;
        }
    }
}
