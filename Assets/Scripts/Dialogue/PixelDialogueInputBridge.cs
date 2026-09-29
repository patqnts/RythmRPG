using PixelCrushers;
using PixelCrushers.DialogueSystem;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Lets the Dialogue System use the game's own controls.
    /// <para>
    /// The project defines USE_NEW_INPUT, so the Dialogue System reads a button name (a Proximity Selector's
    /// "Use Button", e.g. "Fire2") only if an Input System action was registered under that name. Nothing registered
    /// "Fire2", so controllers could never press it (the keyboard still worked through the Use Key). This registers
    /// <see cref="GameInput.Interact"/> (Enter / gamepad South, rebindable in Settings > Controls) as "Interact" and
    /// points any selector whose Use Button isn't a registered action at it.
    /// </para>
    /// <para>Lives on the Pixel Bubble Dialogue UI prefab.</para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-100)]
    public sealed class PixelDialogueInputBridge : MonoBehaviour
    {
        public const string InteractButton = "Interact";

        [Tooltip("Point Proximity Selectors / Selectors whose Use Button isn't a registered action at \"Interact\".")]
        public bool fixSelectorUseButtons = true;
        [Min(0.1f)] public float selectorCheckInterval = 1f;

#if USE_NEW_INPUT // Standalone defines it; without it the Dialogue System reads the legacy Input Manager instead.
        private float nextCheck;

        private void OnEnable()
        {
            Register();
            nextCheck = 0f;
        }

        private void Update()
        {
            // Rebinding in Settings > Controls may rebuild the actions; keep the registration current.
            if (!InputDeviceManager.inputActionDict.TryGetValue(InteractButton, out var registered) ||
                registered != GameInput.Interact)
            {
                Register();
            }

            if (!fixSelectorUseButtons || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + selectorCheckInterval;
            foreach (ProximitySelector selector in FindObjectsByType<ProximitySelector>(FindObjectsInactive.Include))
            {
                if (!IsRegistered(selector.useButton)) selector.useButton = InteractButton;
            }
            foreach (Selector selector in FindObjectsByType<Selector>(FindObjectsInactive.Include))
            {
                if (!IsRegistered(selector.useButton)) selector.useButton = InteractButton;
            }
        }

        private static void Register()
        {
            if (GameInput.Interact != null) InputDeviceManager.RegisterInputAction(InteractButton, GameInput.Interact);
        }

        private static bool IsRegistered(string button) =>
            !string.IsNullOrEmpty(button) && InputDeviceManager.inputActionDict.ContainsKey(button);
#endif
    }
}
