using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// The "CrispWorldUI" layer: world-space UI (hit line, lane key markers, judgement text, ability slots...) that is
    /// kept out of the low-resolution pixel render texture and drawn at full screen resolution on top of it by a
    /// <see cref="CrispWorldUICamera"/>. It stays in the same world position, so it still lines up with the pixel world.
    /// <para>
    /// Call <see cref="Apply"/> on the root of anything that should be crisp. It only moves objects onto the layer
    /// while a <see cref="CrispWorldUICamera"/> is active, so without the setup (Tools > Rythm RPG > Rendering >
    /// Set Up Crisp World UI Camera) everything keeps rendering into the pixel texture as before.
    /// </para>
    /// For a canvas, the layer of the Canvas object decides which camera draws it; children created later should
    /// still copy their parent's layer to keep the hierarchy consistent.
    /// </summary>
    public static class CrispWorldUI
    {
        public const string LayerName = "CrispWorldUI";

        /// <summary>Stencil bit the crisp camera sets where a <see cref="CrispWorldUIOccluder"/> covers the screen.</summary>
        public const int OcclusionStencilBit = 128;

        /// <summary>
        /// Makes a UI material hide where characters and notes (<see cref="CrispWorldUIOccluder"/>) cover it. The shader
        /// needs the usual UI stencil properties (_Stencil, _StencilComp, _StencilReadMask, _StencilWriteMask,
        /// _StencilOp), like UI/Default, TextMeshPro and the Hit Line UI shader. Without the crisp camera the stencil
        /// bit is never set, so the material draws normally.
        /// </summary>
        public static void MakeOccludable(Material material)
        {
            if (material == null) return;
            material.SetFloat("_Stencil", OcclusionStencilBit);
            material.SetFloat("_StencilReadMask", OcclusionStencilBit);
            material.SetFloat("_StencilWriteMask", 0f);
            material.SetFloat("_StencilComp", (float)UnityEngine.Rendering.CompareFunction.NotEqual);
            material.SetFloat("_StencilOp", (float)UnityEngine.Rendering.StencilOp.Keep);
        }

        private const int Unresolved = int.MinValue;
        private static int layer = Unresolved;

        /// <summary>Index of the layer, or -1 when the project has no "CrispWorldUI" layer.</summary>
        public static int Layer
        {
            get
            {
                if (layer == Unresolved) layer = LayerMask.NameToLayer(LayerName);
                return layer;
            }
        }

        /// <summary>True when the layer exists and a crisp camera is drawing it.</summary>
        public static bool IsActive => Layer >= 0 && CrispWorldUICamera.Active != null;

        /// <summary>Puts <paramref name="root"/> and all its children on the crisp layer (no-op when not <see cref="IsActive"/>).</summary>
        public static void Apply(GameObject root)
        {
            if (root == null || !IsActive) return;
            SetLayerRecursively(root.transform, Layer);
        }

        /// <summary>Cheap per-frame version of <see cref="Apply"/>: only walks the hierarchy when the root is not on the layer yet.</summary>
        public static void ApplyIfNeeded(GameObject root)
        {
            if (root == null || !IsActive || root.layer == Layer) return;
            SetLayerRecursively(root.transform, Layer);
        }

        public static void SetLayerRecursively(Transform root, int targetLayer)
        {
            if (root == null) return;
            root.gameObject.layer = targetLayer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), targetLayer);
        }

        /// <summary>Forget the cached layer index (after the layer is added or renamed in the editor).</summary>
        public static void ResetCache() => layer = Unresolved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => ResetCache();
    }
}
