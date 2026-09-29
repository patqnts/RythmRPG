using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Colours for the pixel dialogue bubbles. The bubble sprites are drawn with fixed "key" colours
    /// (<see cref="ArtKeys"/>); the Pixel Palette UI shader swaps each key for the matching colour here, so one
    /// set of sprites can be any palette. Text colours are applied to the TextMeshPro labels directly.
    /// <para>
    /// Use: the Dialogue UI prefab's <see cref="PixelDialogueTheme"/> has the default palette; a character can
    /// have its own via <see cref="PixelSpeechAnchor.bubblePalette"/>; <see cref="PixelDialogueTheme.SetPalette"/>
    /// (or the PixelBubblePalette sequencer command) swaps the default at runtime.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Dialogue/Bubble Palette", fileName = "Bubble Palette", order = 400)]
    public sealed class PixelDialoguePalette : ScriptableObject
    {
        [Header("Bubble")]
        public Color outline = Hex(0x2E2A3F);
        public Color fill = Hex(0xF7F1DE);
        [Tooltip("Darker band at the bottom of the bubble and inside the downward tail.")]
        public Color shade = Hex(0xE4D8B8);
        [Tooltip("Drop shadow under bubbles and the interact arrow (alpha = strength).")]
        public Color shadow = new(0f, 0f, 0f, 0.45f);

        [Header("Continue arrow & choice cursor")]
        public Color accent = Hex(0x5B6BA8);
        public Color accentDark = Hex(0x3E4A82);

        [Header("Interact arrow")]
        public Color highlight = Hex(0xF2C14E);
        public Color highlightDark = Hex(0xC98A2E);

        [Header("Text")]
        public Color nameText = Hex(0x8A8494);
        public Color bodyText = Hex(0x2C3868);
        [Tooltip("Selected response.")]
        public Color choiceText = Hex(0x2C3868);
        [Tooltip("Responses that aren't selected.")]
        public Color choiceDim = Hex(0x7C7890);
        [Tooltip("Responses that can't be picked.")]
        public Color choiceDisabled = Hex(0xB0AA9E);

        /// <summary>The colours the sprites in Assets/Art/UI/Dialogue are painted with, in <see cref="Target"/> order.</summary>
        public static readonly Color[] ArtKeys =
        {
            Hex(0x2E2A3F), // outline
            Hex(0xF7F1DE), // fill
            Hex(0xE4D8B8), // shade
            Hex(0x5B6BA8), // accent
            Hex(0x3E4A82), // accent dark
            Hex(0xF2C14E), // highlight
            Hex(0xC98A2E), // highlight dark
        };

        /// <summary>The colour that replaces <see cref="ArtKeys"/>[<paramref name="index"/>].</summary>
        public Color Target(int index)
        {
            switch (index)
            {
                case 0: return outline;
                case 1: return fill;
                case 2: return shade;
                case 3: return accent;
                case 4: return accentDark;
                case 5: return highlight;
                case 6: return highlightDark;
                default: return Color.magenta;
            }
        }

        /// <summary>Bumped whenever the asset is edited, so live bubbles pick up Inspector changes.</summary>
        public int Revision { get; private set; }

        private void OnValidate() => Revision++;

        private static Color Hex(uint rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
