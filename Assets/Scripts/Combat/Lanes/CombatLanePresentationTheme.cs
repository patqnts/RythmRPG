using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Persistent visual settings for the runtime-generated rhythm lane presentation.
    /// Edit the asset in Resources/Combat/UI to restyle the controls without changing code.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatLanePresentationTheme", menuName = "Rythm RPG/Combat/Lane Presentation Theme")]
    public sealed class CombatLanePresentationTheme : ScriptableObject
    {
        [Header("Button UI")]
        [FormerlySerializedAs("ButtonSprite")]
        public Sprite ButtonUnpressedSprite;
        public Sprite ButtonPressedSprite;
        public Image.Type ButtonImageType = Image.Type.Sliced;
        public Color ButtonColor = new(0.06f, 0.07f, 0.09f, 0.94f);
        public Vector2 ButtonSize = new(72f, 58f);
        [Min(0f)] public float ButtonSpacing = 24f;
        [Min(0f)] public float BottomOffset = 8f;
        [Tooltip("TextMeshPro font for the key labels. Empty = generated from Button Font (legacy), else TMP's default font.")]
        public TMP_FontAsset ButtonFontAsset;
        [Tooltip("Legacy uGUI font, only used to generate a TMP font when Button Font Asset is empty.")]
        public Font ButtonFont;
        [Min(1)] public int ButtonFontSize = 28;
        public FontStyle ButtonFontStyle = FontStyle.Bold;
        public Color ButtonTextColor = Color.white;
        [Tooltip("Key label outline (alpha 0 = none).")]
        public Color ButtonTextOutline = new(0f, 0f, 0f, 0.75f);

        [Header("Perfect Hit Line")]
        [Tooltip("Optional sprite. Leave empty to use the LineRenderer fallback.")]
        public Sprite LineSprite;
        public Material LineMaterial;
        public Color LineColor = Color.white;
        [Min(0.25f)] public float LineThickness = 2f;
        [Min(0f)] public float LineGapAboveButtons = 8f;
        [Min(0f)] public float LineHorizontalPadding = 8f;
        public bool SnapToRenderTexturePixels = true;
    }
}
