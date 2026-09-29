using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>Marks a bubble graphic with the part of the <see cref="PixelDialoguePalette"/> it takes its colour from.</summary>
    [DisallowMultipleComponent]
    public sealed class PixelPaletteGraphic : MonoBehaviour
    {
        public enum Role
        {
            /// <summary>Sprite painted in the art key colours; recoloured by the Pixel Palette shader.</summary>
            Art,
            /// <summary>Drop shadow; takes the palette's shadow colour.</summary>
            Shadow,
            NameText,
            BodyText,
        }

        public Role role;
    }
}
