using UnityEngine;
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
        public Sprite ButtonSprite;
        public Image.Type ButtonImageType = Image.Type.Sliced;
        public Color ButtonColor = new(0.06f, 0.07f, 0.09f, 0.94f);
        public Vector2 ButtonSize = new(72f, 58f);
        [Min(0f)] public float ButtonSpacing = 24f;
        public float BottomOffset = 36f;
        public Font ButtonFont;
        [Min(1)] public int ButtonFontSize = 28;
        public FontStyle ButtonFontStyle = FontStyle.Bold;
        public Color ButtonTextColor = Color.white;

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
