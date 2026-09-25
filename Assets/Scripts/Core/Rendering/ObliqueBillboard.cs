using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Stands a camera-facing object (a character sprite tilted to the camera's pitch) upright while an
    /// <see cref="ObliqueProjection"/> is active, so it is drawn at its real size instead of stretched.
    /// <para>
    /// The change is render-only: just before rendering the object is turned from "facing the camera" to "upright,
    /// facing the camera's direction", and at the start of the next frame its own rotation is put back. Other scripts
    /// and animations therefore still read and write the original rotation (extra tilts or flips are kept).
    /// </para>
    /// <para>Usually added automatically by <see cref="ObliqueProjection"/> in Play mode; can also be added by hand.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-10000)] // restore before any other script runs this frame
    public sealed class ObliqueBillboard : MonoBehaviour
    {
        private static readonly List<ObliqueBillboard> Active = new();

        private Quaternion ownLocalRotation;
        private bool applied;

        private void OnEnable()
        {
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable()
        {
            Restore();
            Active.Remove(this);
        }

        // Restored before physics and before every script, so gameplay never sees the render-only rotation.
        private void FixedUpdate() => Restore();
        private void Update() => Restore();

        private void Restore()
        {
            if (!applied) return;
            applied = false;
            transform.localRotation = ownLocalRotation;
        }

        private void Apply(Quaternion correction)
        {
            if (applied) return;
            ownLocalRotation = transform.localRotation;
            transform.rotation = correction * transform.rotation;
            applied = true;
        }

        /// <summary>Stands every billboard upright for the coming render (called by <see cref="ObliqueProjection"/>).</summary>
        internal static void ApplyAll(Camera camera)
        {
            if (camera == null || Active.Count == 0) return;
            // Maps "facing the camera" onto "upright, facing the camera's direction"; any extra rotation the object has
            // relative to the camera (a hit-reaction tilt, a flip) is carried over.
            Quaternion correction = ObliqueProjection.BillboardRotation(camera) * Quaternion.Inverse(camera.transform.rotation);
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                ObliqueBillboard billboard = Active[i];
                if (billboard == null) { Active.RemoveAt(i); continue; }
                billboard.Apply(correction);
            }
        }

        /// <summary>Puts every billboard back to its own rotation (when the oblique projection is turned off).</summary>
        internal static void RestoreAll()
        {
            foreach (ObliqueBillboard billboard in Active)
                if (billboard != null) billboard.Restore();
        }
    }
}
