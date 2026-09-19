using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Keeps both sprite and mesh note visuals above the world judgement line. Materials already
    /// in the transparent queue are reused; opaque materials get per-note runtime copies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RhythmNoteVisualLayer : MonoBehaviour
    {
        private const int MinimumRenderQueue = 3000;
        private const int MinimumSortingOrder = 0;
        private readonly List<Material> ownedMaterials = new();

        public void Configure()
        {
            foreach (Renderer visualRenderer in GetComponentsInChildren<Renderer>(true))
            {
                visualRenderer.sortingOrder = Mathf.Max(visualRenderer.sortingOrder, MinimumSortingOrder);
                Material[] materials = visualRenderer.sharedMaterials;
                bool changed = false;

                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null || source.renderQueue >= MinimumRenderQueue) continue;

                    Material runtimeMaterial = new(source)
                    {
                        name = $"{source.name} (Rhythm Note Runtime)",
                        hideFlags = HideFlags.DontSave
                    };
                    runtimeMaterial.renderQueue = MinimumRenderQueue;
                    materials[index] = runtimeMaterial;
                    ownedMaterials.Add(runtimeMaterial);
                    changed = true;
                }

                if (changed) visualRenderer.sharedMaterials = materials;
            }
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            ownedMaterials.Clear();
        }
    }
}
