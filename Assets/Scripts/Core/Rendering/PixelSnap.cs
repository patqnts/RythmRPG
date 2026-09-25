using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Marks a moving object (a character, an NPC, a prop that slides) to be snapped to whole render-texture pixels
    /// while the frame is rendered, so its sprite never wobbles between pixels. The snap is render-only:
    /// <see cref="PixelPerfectRig"/> moves the object just before rendering and puts it back right after, so gameplay,
    /// physics and animation always see the real position.
    /// <para>
    /// Objects with a CharacterController get one automatically, and the combat's players, enemies and notes are snapped
    /// without it (see <see cref="PixelPerfectRig"/>). Put it on the root of whatever moves.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelSnap : MonoBehaviour
    {
        private static readonly List<PixelSnap> active = new();

        public static IReadOnlyList<PixelSnap> Active => active;

        /// <summary>Adds the component to <paramref name="target"/> if it does not have one yet.</summary>
        public static PixelSnap Ensure(GameObject target)
        {
            if (target == null) return null;
            return target.TryGetComponent(out PixelSnap existing) ? existing : target.AddComponent<PixelSnap>();
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable() => active.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => active.Clear();
    }
}
