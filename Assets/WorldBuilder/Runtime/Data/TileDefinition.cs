using System;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One paintable tile: a sprite (which may be a sub-sprite of a larger atlas texture, or a
    /// standalone texture imported as a single sprite -- both work identically here), plus how it
    /// should behave when painted: whether it blocks movement, and whether its visual footprint should
    /// be projection-compensated for the game's tilted orthographic camera (see
    /// <see cref="WorldBuilderSettings.DefaultCompensationFactor"/>).
    ///
    /// Tiles are referenced by a stable <see cref="TileId"/> rather than by asset index or path, so
    /// palettes can be reordered and levels can be moved between scenes without breaking references
    /// (world data in <see cref="TileLayerData"/> stores this id, not an array index).
    /// </summary>
    [CreateAssetMenu(menuName = "World Builder/Tile Definition", fileName = "NewTileDefinition")]
    public class TileDefinition : ScriptableObject
    {
        [SerializeField] private string tileId;

        public string displayName = "Tile";
        public string category = "Uncategorized";
        public Sprite sprite;

        [Header("Gameplay")]
        [Tooltip("Whether this tile contributes to the chunk's generated ground collision.")]
        public bool collisionEnabled = true;

        [Header("Projection Compensation")]
        [Tooltip("Whether this tile's visual mesh should be depth-axis compensated for the tilted camera. " +
                 "Disable for art authored specifically for the angled camera (no correction needed).")]
        public bool useProjectionCompensation = true;

        [Tooltip("0 = use the world's default compensation factor (derived from camera tilt angle). " +
                 "Set a positive value to override it for this tile only.")]
        public float compensationOverride;

        public string[] tags = Array.Empty<string>();

        public string TileId
        {
            get
            {
                if (string.IsNullOrEmpty(tileId)) tileId = Guid.NewGuid().ToString("N");
                return tileId;
            }
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(tileId)) tileId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(displayName)) displayName = name;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(tileId)) tileId = Guid.NewGuid().ToString("N");
        }

        public float ResolveCompensationFactor(float worldDefaultFactor)
        {
            if (!useProjectionCompensation) return 1f;
            return compensationOverride > 0.0001f ? compensationOverride : worldDefaultFactor;
        }
    }
}
