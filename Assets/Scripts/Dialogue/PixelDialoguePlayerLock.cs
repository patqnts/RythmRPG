using System.Collections.Generic;
using System.Reflection;
using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Freezes the player while a conversation is running: stops <see cref="PlayerMovement3D"/> /
    /// <see cref="PlayerMovement"/>, and turns off the Proximity Selector (so the interact key can't restart the
    /// conversation) and <see cref="PlayerEnemyInteractor3D"/> (so talking next to an enemy can't start a battle).
    /// Everything is put back the way it was a moment after the conversation ends, so the key press that closed
    /// the last line isn't also read as "interact again".
    /// <para>Lives on the Pixel Bubble Dialogue UI prefab, so it works in every scene that uses that UI.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelDialoguePlayerLock : MonoBehaviour
    {
        public bool freezePlayerMovement = true;
        public bool disableProximitySelectors = true;
        public bool disableEnemyEncounters = true;
        [Tooltip("Seconds after the conversation ends before control comes back.")]
        [Min(0f)] public float unlockDelay = 0.25f;
        public UnityEvent onLocked = new();
        public UnityEvent onUnlocked = new();

        private static readonly FieldInfo MovementEnabledField =
            typeof(PlayerMovement3D).GetField("movementEnabled", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<PlayerMovement3D> frozen3D = new();
        private readonly List<PlayerMovement> frozen2D = new();
        private readonly List<Behaviour> disabledBehaviours = new();
        private bool locked;
        private float endedAt = -1f;

        public bool IsLocked => locked;

        private void Update()
        {
            if (DialogueManager.hasInstance && DialogueManager.isConversationActive)
            {
                endedAt = -1f;
                if (!locked) Lock();
                return;
            }
            if (!locked) return;
            if (endedAt < 0f) endedAt = Time.unscaledTime;
            if (Time.unscaledTime - endedAt >= unlockDelay) Unlock();
        }

        private void OnDisable()
        {
            if (locked) Unlock();
        }

        private void Lock()
        {
            locked = true;

            if (freezePlayerMovement)
            {
                foreach (PlayerMovement3D movement in FindObjectsByType<PlayerMovement3D>(FindObjectsInactive.Exclude))
                {
                    if (!IsMovementEnabled(movement)) continue;
                    movement.DisableMovement();
                    frozen3D.Add(movement);
                }
                foreach (PlayerMovement movement in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude))
                {
                    if (!movement.isEnabled) continue;
                    movement.isEnabled = false;
                    if (movement.body != null) movement.body.linearVelocity = Vector2.zero;
                    if (movement.circleCollider != null) movement.circleCollider.enabled = false;
                    frozen2D.Add(movement);
                }
            }

            if (disableProximitySelectors)
            {
                foreach (ProximitySelector selector in FindObjectsByType<ProximitySelector>(FindObjectsInactive.Exclude))
                    Disable(selector);
            }

            if (disableEnemyEncounters)
            {
                foreach (PlayerEnemyInteractor3D interactor in FindObjectsByType<PlayerEnemyInteractor3D>(FindObjectsInactive.Exclude))
                    Disable(interactor);
            }

            onLocked.Invoke();
        }

        private void Unlock()
        {
            locked = false;
            endedAt = -1f;

            // A conversation may end by starting a battle; the battle gives movement back itself when it's over.
            bool battle = false;
            foreach (CombatController combat in FindObjectsByType<CombatController>(FindObjectsInactive.Exclude))
                battle |= combat.IsBattleActive;

            if (!battle)
            {
                foreach (PlayerMovement3D movement in frozen3D)
                    if (movement != null) movement.EnableMovement();
                foreach (PlayerMovement movement in frozen2D)
                    if (movement != null) movement.isEnabled = true;
            }
            frozen3D.Clear();
            frozen2D.Clear();

            foreach (Behaviour behaviour in disabledBehaviours)
                if (behaviour != null) behaviour.enabled = true;
            disabledBehaviours.Clear();

            onUnlocked.Invoke();
        }

        private void Disable(Behaviour behaviour)
        {
            if (behaviour == null || !behaviour.enabled) return;
            behaviour.enabled = false;
            disabledBehaviours.Add(behaviour);
        }

        private static bool IsMovementEnabled(PlayerMovement3D movement)
        {
            if (MovementEnabledField == null) return true;
            return MovementEnabledField.GetValue(movement) is bool value && value;
        }
    }
}
