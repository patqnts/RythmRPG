using System;

namespace RythmRPG.Core
{
    /// <summary>
    /// Whether the exploration world is currently hidden (for example while combat shows the space backdrop).
    /// Things that draw without a Renderer component (GPU grass) check this, because code that hides the scene by
    /// switching Renderers off cannot see them.
    /// </summary>
    public static class SceneVisibility
    {
        public static bool WorldHidden { get; private set; }

        /// <summary>Raised with the new value whenever <see cref="WorldHidden"/> changes.</summary>
        public static event Action<bool> Changed;

        // Enter Play Mode without a domain reload keeps statics: start every session visible.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            WorldHidden = false;
            Changed = null;
        }

        public static void SetWorldHidden(bool hidden)
        {
            if (WorldHidden == hidden) return;
            WorldHidden = hidden;
            Changed?.Invoke(hidden);
        }
    }
}
