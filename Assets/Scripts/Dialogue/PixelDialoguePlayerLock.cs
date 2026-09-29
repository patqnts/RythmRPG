using System.Collections.Generic;
using System.Reflection;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.Events;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Freezes the player while a conversation is running: stops <see cref="PlayerMovement3D"/> /
    /// <see cref="PlayerMovement"/>, and turns off <see cref="PlayerEnemyInteractor3D"/> (so talking next to an enemy
    /// can't start a touch battle). Everything is put back the way it was a moment after the conversation ends, so the
    /// key press that closed the last line isn't also read as "interact again". When the conversation ends by starting
    /// a battle (StartBattle()), movement stays frozen: the battle gives it back when it's over.
    /// <para>
    /// Proximity Selectors (talk / use) are also off during conversations, battles and while a battle is about to
    /// start, so Interact can't start a conversation in the middle of a fight.
    /// </para>
    /// <para>Lives on the Pixel Bubble Dialogue UI prefab, so it works in every scene that uses that UI.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelDialoguePlayerLock : MonoBehaviour
    {
        public bool freezePlayerMovement = true;
        [Tooltip("Turn off Proximity Selectors during conversations and battles.")]
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
        private readonly List<Behaviour> blockedSelectors = new();
        private bool locked;
        private bool selectorsBlocked;
        private float endedAt = -1f;

        public bool IsLocked => locked;

        private void Update()
        {
            if (DialogueManager.hasInstance && DialogueManager.isConversationActive)
            {
                endedAt = -1f;
                if (!locked) Lock();
            }
            else if (locked)
            {
                if (endedAt < 0f) endedAt = Time.unscaledTime;
                if (Time.unscaledTime - endedAt >= unlockDelay) Unlock();
            }

            BlockSelectors(disableProximitySelectors
                           && (locked || DialogueBattleStarter.IsPending || DialogueBattleStarter.AnyBattleActive()));
        }

        private void OnDisable()
        {
            if (locked) Unlock();
            BlockSelectors(false);
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

            // Not during a battle (story scenes in combat): the interactor must stay subscribed to BattleEnded, which
            // gives movement back and closes the battle background when the fight is over.
            if (disableEnemyEncounters && !DialogueBattleStarter.AnyBattleActive())
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
            bool battle = DialogueBattleStarter.AnyBattleActive() || DialogueBattleStarter.IsPending;

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

        private void BlockSelectors(bool block)
        {
            if (block == selectorsBlocked) return;
            selectorsBlocked = block;
            if (block)
            {
                foreach (ProximitySelector selector in FindObjectsByType<ProximitySelector>(FindObjectsInactive.Exclude))
                {
                    if (!selector.enabled) continue;
                    selector.enabled = false;
                    blockedSelectors.Add(selector);
                }
                return;
            }
            foreach (Behaviour selector in blockedSelectors)
                if (selector != null) selector.enabled = true;
            blockedSelectors.Clear();
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
