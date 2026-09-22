using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One material per unique tile texture, shared across every chunk that uses it. This keeps
    /// generated ground meshes to one submesh (and one draw call) per distinct source texture instead
    /// of allocating a fresh material for every chunk, per the World Builder's material-reuse
    /// requirements.
    /// </summary>
    public static class TileMaterialCache
    {
        private static readonly Dictionary<Texture, Material> Cache = new Dictionary<Texture, Material>();
        private static Shader defaultShader;

        public static Material GetMaterial(Texture texture, Shader shaderOverride)
        {
            if (texture == null) return null;

            if (Cache.TryGetValue(texture, out Material existing) && existing != null)
            {
                return existing;
            }

            Shader shader = shaderOverride != null ? shaderOverride : ResolveDefaultShader();
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = $"WorldBuilder_{texture.name}",
                hideFlags = HideFlags.DontSave
            };

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            else if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);

            Cache[texture] = material;
            return material;
        }

        private static Shader ResolveDefaultShader()
        {
            if (defaultShader != null) return defaultShader;
            defaultShader = Shader.Find("Universal Render Pipeline/Lit");
            if (defaultShader == null) defaultShader = Shader.Find("Sprites/Default");
            if (defaultShader == null) defaultShader = Shader.Find("Standard");
            return defaultShader;
        }

        /// <summary>
        /// Destroys every cached material. Call from an editor menu or tests if source textures are
        /// being reimported/replaced and stale materials need to be discarded.
        /// </summary>
        public static void ClearCache()
        {
            foreach (Material material in Cache.Values)
            {
                if (material == null) continue;
                if (Application.isPlaying) Object.Destroy(material);
                else Object.DestroyImmediate(material);
            }

            Cache.Clear();
        }
    }
}
