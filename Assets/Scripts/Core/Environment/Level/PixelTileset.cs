using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// A pixel-art tileset: one sprite sheet cut into a grid of equal tiles (32×32 by default). Used by
    /// <see cref="PixelLevel"/> (ground, walls, ramps, stairs) and <see cref="PixelTileMesh"/> (tiles on mesh faces).
    /// Tile index 0 is the top-left tile, counting left to right, then row by row downwards.
    /// Create one with Tools > Rythm RPG > Level > Create Tileset From Selected Texture (also fixes the texture's
    /// import settings for pixel art and makes a matching Pixel Mesh material).
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Pixel Tileset", fileName = "New Pixel Tileset")]
    public sealed class PixelTileset : ScriptableObject
    {
        [Tooltip("The sprite sheet. Import it with Filter Mode = Point, Compression = None, no mip maps.")]
        public Texture2D texture;
        [Tooltip("Size of one tile in pixels.")]
        [Min(1)] public int tileSize = 32;
        [Tooltip("Empty pixels between tiles in the sheet.")]
        [Min(0)] public int spacing;
        [Tooltip("Empty pixels around the whole sheet.")]
        [Min(0)] public int margin;
        [Tooltip("Material used to draw the tiles (RythmRPG/Pixel Mesh with this texture). Made for you by the " +
                 "Create Tileset menu; if empty, one is made at runtime.")]
        public Material material;

        private Material runtimeMaterial;

        public int Columns => texture == null ? 0 : Mathf.Max(0, (texture.width - 2 * margin + spacing) / (tileSize + spacing));
        public int Rows => texture == null ? 0 : Mathf.Max(0, (texture.height - 2 * margin + spacing) / (tileSize + spacing));
        public int Count => Columns * Rows;

        public bool IsValid(int index) => index >= 0 && index < Count;

        /// <summary>UV rectangle of a tile (inset by a hair so neighbouring tiles never bleed in).</summary>
        public Rect TileUV(int index)
        {
            if (texture == null || Columns == 0) return new Rect(0, 0, 1, 1);
            index = Mathf.Clamp(index, 0, Mathf.Max(0, Count - 1));
            int column = index % Columns;
            int row = index / Columns;
            float w = texture.width, h = texture.height;
            float px = margin + column * (tileSize + spacing);
            float pyFromTop = margin + row * (tileSize + spacing);
            float py = h - pyFromTop - tileSize;
            const float inset = 0.01f; // pixels
            return new Rect((px + inset) / w, (py + inset) / h, (tileSize - 2f * inset) / w, (tileSize - 2f * inset) / h);
        }

        /// <summary>Material for the tiles: the assigned one, or a runtime Pixel Mesh (or URP Lit) material.</summary>
        public Material GetMaterial()
        {
            if (material != null) return material;
            if (runtimeMaterial != null) return runtimeMaterial;
            Shader shader = Shader.Find("RythmRPG/Pixel Mesh");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            runtimeMaterial = new Material(shader) { name = name + " (runtime)", hideFlags = HideFlags.HideAndDontSave };
            ApplyTexture(runtimeMaterial);
            return runtimeMaterial;
        }

        /// <summary>Puts the sheet on a material (Pixel Mesh: no light dithering; URP Lit: base map).</summary>
        public void ApplyTexture(Material target)
        {
            if (target == null) return;
            if (target.HasProperty("_MainTex")) target.SetTexture("_MainTex", texture);
            if (target.HasProperty("_BaseMap")) target.SetTexture("_BaseMap", texture);
            if (target.HasProperty("_LightDither")) target.SetFloat("_LightDither", 0f);
            if (target.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", 0f);
        }

        private void OnDisable()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }
    }
}
