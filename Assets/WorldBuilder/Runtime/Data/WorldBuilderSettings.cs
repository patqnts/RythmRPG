using System;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    public enum TilePixelSize
    {
        Size16 = 16,
        Size32 = 32,
        Size64 = 64,
        Custom = 0
    }

    /// <summary>
    /// Per-world configuration: grid dimensions, chunking, the reference camera this world is being
    /// built for, and projection-compensation defaults. This is intentionally a plain serializable
    /// class (not a ScriptableObject) because these values are specific to one world/scene, not shared
    /// -- shared data (palettes, tile definitions, presets) lives in their own ScriptableObject assets
    /// instead, per the World Builder's data-architecture separation.
    /// </summary>
    [Serializable]
    public class WorldBuilderSettings
    {
        [Header("Grid")]
        public TilePixelSize tilePixelSize = TilePixelSize.Size32;
        public int customTilePixelSize = 32;
        [Tooltip("World-space size of one tile, in Unity units.")]
        public float tileWorldSize = 1f;
        [Tooltip("Tiles per chunk, per axis. Larger chunks mean fewer draw calls but coarser rebuild granularity.")]
        public int chunkSizeInTiles = 16;
        [Tooltip("World-space Y distance between adjacent elevation levels.")]
        public float elevationIncrement = 1f;

        [Header("Camera")]
        [Tooltip("The game's actual orthographic presentation camera. Used to read the tilt angle and to " +
                 "align the Scene view for an accurate preview.")]
        public Camera referenceCamera;
        [Tooltip("Degrees the presentation camera is tilted down from horizontal around the X axis. " +
                 "This project's default 2.5D camera uses 35 degrees.")]
        public float cameraTiltDegrees = 35f;

        [Header("Projection Compensation")]
        [Tooltip("Master switch. When off, no tile is ever depth-compensated regardless of its own setting.")]
        public bool projectionCompensationEnabled = true;

        [Header("Rendering")]
        [Tooltip("Generate a combined MeshCollider per chunk from tiles whose Tile Definition has " +
                 "Collision Enabled checked.")]
        public bool generateGroundCollision = true;
        [Tooltip("Optional shader override for generated ground materials. Defaults to Universal Render " +
                 "Pipeline/Lit when left empty.")]
        public Shader tileShaderOverride;

        public int EffectiveTilePixelSize =>
            tilePixelSize == TilePixelSize.Custom ? Mathf.Max(1, customTilePixelSize) : (int)tilePixelSize;

        /// <summary>
        /// 1 / sin(cameraTiltDegrees): the factor a top-down-authored square tile's depth (Z) axis needs
        /// to be scaled by, in the generated visual mesh only, so it reads as square again once
        /// foreshortened by the tilted orthographic camera. Collision and gameplay geometry never use
        /// this factor -- see TileMeshBuilder.
        /// </summary>
        public float DefaultCompensationFactor
        {
            get
            {
                float clampedAngle = Mathf.Clamp(cameraTiltDegrees, 5f, 90f);
                float sine = Mathf.Sin(clampedAngle * Mathf.Deg2Rad);
                return sine > 0.0001f ? 1f / sine : 1f;
            }
        }
    }
}
