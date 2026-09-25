using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// Draws the drifting flakes of a disintegrating <see cref="TitleLogoText"/> or <see cref="TitleLogoImage"/>.
    /// Each flake is a small square chunk of the real glyph / sprite (same texture, same finish), so ink pieces
    /// fly off as ink and gold pieces as gold, then cool to ash. Created and driven by the logo component as a
    /// hidden child; not saved.
    /// </summary>
    [AddComponentMenu("")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TitleLogoFlakes : MaskableGraphic
    {
        ITitleFlakeOwner owner;
        Texture atlas;

        public override Texture mainTexture => atlas ? atlas : s_WhiteTexture;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        internal void Bind(ITitleFlakeOwner logo, Material flakeMaterial, Texture fontAtlas)
        {
            owner = logo;
            raycastTarget = false;
            if (m_Material != flakeMaterial) material = flakeMaterial;
            if (atlas != fontAtlas)
            {
                atlas = fontAtlas;
                SetMaterialDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (owner != null) owner.FillFlakeMesh(vh);
        }
    }
}
