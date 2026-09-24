using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Add to any scene object (world-space canvas, image, sprite, text) that should be drawn crisp at screen
    /// resolution instead of inside the pixel render texture. Puts it and its children on the CrispWorldUI layer,
    /// including children added later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrispWorldUIObject : MonoBehaviour
    {
        private void OnEnable() => CrispWorldUI.Apply(gameObject);

        // The crisp camera may enable after this object.
        private void Start() => CrispWorldUI.Apply(gameObject);

        private void OnTransformChildrenChanged() => CrispWorldUI.Apply(gameObject);
    }
}
