using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Marks an object (character, enemy, note...) whose sprites and meshes cover crisp world UI that opts into
    /// occlusion (the hit line and its key markers), the way they did when that UI was drawn inside the pixel render.
    /// <para>
    /// The crisp UI is drawn by a separate camera, so it cannot sort against the pixel world. Instead,
    /// <see cref="CrispWorldUICamera"/> draws the silhouettes of these objects into a low-resolution mask with the pixel
    /// camera's exact view, and the occludable UI is hidden where the mask is set. The mask has the same pixel grid as
    /// the pixel render, so the cut-out follows the pixel art edge exactly.
    /// </para>
    /// Sprite, mesh and skinned mesh renderers in the children are used (particles, lines and trails are not).
    /// Only renderers the pixel camera can see (layer in its culling mask, enabled, not forced off) count.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrispWorldUIOccluder : MonoBehaviour
    {
        private static readonly List<CrispWorldUIOccluder> active = new();

        public static IReadOnlyList<CrispWorldUIOccluder> Active => active;

        /// <summary>Adds the component to <paramref name="target"/> if it does not have one yet.</summary>
        public static CrispWorldUIOccluder Ensure(GameObject target)
        {
            if (target == null) return null;
            return target.TryGetComponent(out CrispWorldUIOccluder existing)
                ? existing
                : target.AddComponent<CrispWorldUIOccluder>();
        }

        /// <summary>Fills <paramref name="results"/> with this object's renderers (children included, inactive skipped).</summary>
        public void GetRenderers(List<Renderer> results) => GetComponentsInChildren(false, results);

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable() => active.Remove(this);
    }
}
