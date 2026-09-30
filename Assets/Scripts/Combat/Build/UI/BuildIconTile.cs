using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// One square icon with a modifiable frame: frame (sprite or plain colour), background, icon sprite or a fallback
    /// letter, a count in the bottom-right corner (turns / charges) and a tag in the top-right corner (level).
    /// Used by the passive column, the in-effect icon rows and the loadout panel. Built in code from an
    /// <see cref="IconTileLook"/>; a hand-made tile works too when its parts are assigned.
    /// </summary>
    public sealed class BuildIconTile : MonoBehaviour
    {
        [SerializeField] private RectTransform root;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image frame;
        [SerializeField] private Image background;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text glyph;
        [SerializeField] private TMP_Text count;
        [SerializeField] private TMP_Text corner;
        [SerializeField] private TMP_Text label;

        private Color frameColor = Color.white;
        private Color flashColor = Color.white;
        private float punchScale = 1f;
        private Tween flashTween;
        private Tween popTween;

        public RectTransform Rect => root != null ? root : root = (RectTransform)transform;
        public TMP_Text Label => label;
        public string Key { get; set; }

        public float Alpha
        {
            get => group != null ? group.alpha : 1f;
            set { if (group != null) group.alpha = value; }
        }

        // ---------- content ----------

        /// <summary>Icon sprite, or (no sprite) a letter on the tile in <paramref name="glyphColor"/>.</summary>
        public void SetContent(Sprite sprite, string fallbackGlyph, Color glyphColor)
        {
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }
            if (glyph != null)
            {
                glyph.text = sprite != null ? string.Empty : fallbackGlyph ?? string.Empty;
                glyph.color = glyphColor;
            }
        }

        /// <summary>Bottom-right number (turns left, charges...). Empty = hidden.</summary>
        public void SetCount(string text, Color color)
        {
            if (count == null) return;
            count.text = text ?? string.Empty;
            count.color = color;
        }

        /// <summary>Top-right tag (passive level...). Empty = hidden.</summary>
        public void SetCorner(string text, Color color)
        {
            if (corner == null) return;
            corner.text = text ?? string.Empty;
            corner.color = color;
        }

        public void SetLabel(string text, Color color)
        {
            if (label == null) return;
            label.text = text ?? string.Empty;
            label.color = color;
            label.enabled = !string.IsNullOrEmpty(text);
        }

        public void SetFrameColor(Color color)
        {
            frameColor = color;
            if (frame != null && !flashTween.isAlive) frame.color = color;
        }

        /// <summary>Applies a look (sizes, sprites, colours) to this tile's parts.</summary>
        public void ApplyLook(IconTileLook look)
        {
            if (look == null) return;
            Rect.sizeDelta = look.size;
            float thickness = Mathf.Max(0f, look.frameThickness);
            if (frame != null)
            {
                frame.sprite = look.frameSprite;
                frame.type = look.frameSprite != null && look.frameSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                frame.color = frameColor = look.frameColor;
                Stretch(frame.rectTransform, 0f);
                // A plain frame is a coloured square behind the background; only a sprite frame can sit on top.
                if (look.frameOnTop && look.frameSprite != null) frame.rectTransform.SetAsLastSibling();
                else frame.rectTransform.SetAsFirstSibling();
            }
            if (background != null)
            {
                background.sprite = look.backgroundSprite;
                background.type = look.backgroundSprite != null && look.backgroundSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                background.color = look.backgroundColor;
                Stretch(background.rectTransform, thickness);
            }
            if (icon != null)
            {
                icon.preserveAspect = look.preserveAspect;
                Stretch(icon.rectTransform, thickness + look.iconInset);
            }
            if (glyph != null)
            {
                glyph.fontSize = look.glyphSize;
                Stretch(glyph.rectTransform, thickness);
            }
            // Numbers stay readable above everything, including a frame drawn on top.
            if (count != null) count.rectTransform.SetAsLastSibling();
            if (corner != null) corner.rectTransform.SetAsLastSibling();
        }

        // ---------- motion ----------

        /// <summary>Frame flashes <paramref name="color"/> and the tile punches up, then both settle back.</summary>
        public void Flash(Color color, float seconds, float scale)
        {
            if (seconds <= 0f || !isActiveAndEnabled) return;
            flashColor = color;
            punchScale = Mathf.Max(1f, scale);
            flashTween.Stop();
            flashTween = Tween.Custom(this, 1f, 0f, seconds, (tile, v) => tile.ApplyFlash(v), Ease.OutQuad);
        }

        /// <summary>Quick scale-in when the tile appears or its value changes.</summary>
        public void Pop(float seconds)
        {
            if (seconds <= 0f || !isActiveAndEnabled || flashTween.isAlive) return;
            popTween.Stop();
            popTween = Tween.Custom(this, 1.25f, 1f, seconds, (tile, v) => tile.Rect.localScale = Vector3.one * v, Ease.OutBack);
        }

        private void ApplyFlash(float amount)
        {
            if (frame != null) frame.color = Color.Lerp(frameColor, flashColor, amount);
            Rect.localScale = Vector3.one * Mathf.Lerp(1f, punchScale, amount);
        }

        private void OnDisable()
        {
            flashTween.Stop();
            popTween.Stop();
            if (frame != null) frame.color = frameColor;
            if (root != null) root.localScale = Vector3.one;
        }

        // ---------- template ----------

        /// <summary>Builds a tile under <paramref name="parent"/>. <paramref name="labelSize"/> 0 = no name label.</summary>
        public static BuildIconTile Create(string name, Transform parent, IconTileLook look, TMP_FontAsset font, Color outline,
            int countSize, int cornerSize, int labelSize = 0)
        {
            look ??= new IconTileLook();
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = look.size;
            CanvasGroup canvasGroup = go.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Image frameImage = NewImage("Frame", rect);
            Image backgroundImage = NewImage("Background", rect);
            Image iconImage = NewImage("Icon", rect);
            iconImage.enabled = false;
            TMP_Text glyphText = NewText("Glyph", rect, font, look.glyphSize, outline, TextAlignmentOptions.Center);

            TMP_Text countText = NewText("Count", rect, font, Mathf.Max(4, countSize), outline, TextAlignmentOptions.BottomRight);
            Corner(countText.rectTransform, new Vector2(1f, 0f), new Vector2(3f, -3f));
            TMP_Text cornerText = NewText("Corner", rect, font, Mathf.Max(4, cornerSize), outline, TextAlignmentOptions.TopRight);
            Corner(cornerText.rectTransform, new Vector2(1f, 1f), new Vector2(3f, 3f));

            TMP_Text labelText = null;
            if (labelSize > 0)
            {
                labelText = NewText("Name", rect, font, labelSize, outline, TextAlignmentOptions.Left);
                RectTransform labelRect = labelText.rectTransform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(1f, 0.5f);
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(10f, 0f);
                labelRect.sizeDelta = new Vector2(320f, labelSize + 8f);
                labelText.enabled = false;
            }

            BuildIconTile tile = go.AddComponent<BuildIconTile>();
            tile.Key = name;
            tile.root = rect;
            tile.group = canvasGroup;
            tile.frame = frameImage;
            tile.background = backgroundImage;
            tile.icon = iconImage;
            tile.glyph = glyphText;
            tile.count = countText;
            tile.corner = cornerText;
            tile.label = labelText;
            tile.ApplyLook(look);
            return tile;
        }

        private static Image NewImage(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text NewText(string name, RectTransform parent, TMP_FontAsset font, int size, Color outline,
            TextAlignmentOptions alignment)
        {
            TMP_Text text = CombatText.CreateUGUI(name, parent, font, size, Color.white, alignment, outline,
                CombatText.OutlineWidthFromPixels(3f, size) + 0.1f);
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.text = string.Empty;
            return text;
        }

        private static void Corner(RectTransform rect, Vector2 corner, Vector2 overhang)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = overhang;
            rect.sizeDelta = new Vector2(80f, 40f);
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
