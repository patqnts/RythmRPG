using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One material per unique prop texture, forced into URP Lit's opaque alpha-clip (cutout) surface
    /// mode. This is what makes prop/character occlusion "just work": an alpha-clipped surface writes to
    /// the depth buffer like any other opaque object, so Unity's normal 3D depth test sorts a prop
    /// against a character in front of or behind it automatically, from any angle, with no per-frame
    /// sorting-order code -- the approach chosen over a manual Y-sort system for this project's props.
    /// The tradeoff (documented for whoever authors prop art): edges must be hard alpha (0 or 1), not
    /// antialiased/soft, since a depth-tested cutout has no concept of partial transparency.
    /// </summary>
    public static class PropMaterialCache
    {
        private readonly struct CacheKey
        {
            private readonly Texture texture;
            private readonly float cutoff;

            public CacheKey(Texture texture, float cutoff)
            {
                this.texture = texture;
                this.cutoff = cutoff;
            }

            public override bool Equals(object obj) =>
                obj is CacheKey other && other.texture == texture && Mathf.Approximately(other.cutoff, cutoff);

            public override int GetHashCode() =>
                (texture != null ? texture.GetHashCode() : 0) * 397 ^ cutoff.GetHashCode();
        }

        private static readonly Dictionary<CacheKey, Material> Cache = new Dictionary<CacheKey, Material>();
        private static Shader defaultShader;

        public static Material GetMaterial(Texture texture, Shader shaderOverride, float alphaCutoff = 0.5f)
        {
            if (texture == null) return null;

            CacheKey key = new CacheKey(texture, alphaCutoff);
            if (Cache.TryGetValue(key, out Material existing) && existing != null)
            {
                return existing;
            }

            Shader shader = shaderOverride != null ? shaderOverride : ResolveDefaultShader();
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = $"WorldBuilderProp_{texture.name}",
                hideFlags = HideFlags.DontSave
            };

            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            else if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);

            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);

            ApplyAlphaCutout(material, alphaCutoff);

            Cache[key] = material;
            return material;
        }

        /// <summary>
        /// Forces a URP Lit-family material into opaque + alpha-clip: surface stays Opaque (so it
        /// participates in normal depth-buffer occlusion, unlike a Transparent surface), AlphaClip is
        /// turned on with the given cutoff, and the render queue is moved to AlphaTest so it batches with
        /// other cutout geometry instead of the default opaque queue. These are the standard scripted
        /// URP Lit property names (_Surface, _AlphaClip, _Cutoff) and have been stable across URP
        /// versions; a non-Lit shaderOverride that doesn't expose them is left alone (HasProperty guards
        /// every set) rather than failing.
        /// </summary>
        private static void ApplyAlphaCutout(Material material, float cutoff)
        {
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f); // 0 = Opaque
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", cutoff);

            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        private static Shader ResolveDefaultShader()
        {
            if (defaultShader != null) return defaultShader;
            defaultShader = Shader.Find("Universal Render Pipeline/Lit");
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
