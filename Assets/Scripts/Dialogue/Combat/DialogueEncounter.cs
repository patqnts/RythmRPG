using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Makes an enemy's battle start through the Dialogue System. Put it on the enemy (next to its EnemyCombatant) with
    /// a Dialogue System Trigger set to <b>On Use</b>:
    /// <list type="bullet">
    /// <item><b>Talk First</b>: add a Usable too. The player walks up and presses Interact, the trigger's conversation
    /// plays, then the battle starts.</item>
    /// <item><b>Ambush</b>: touching the enemy fires the trigger (its conversation, if any, then the battle).</item>
    /// </list>
    /// With <see cref="battleWhenTriggered"/> off, the conversation decides instead: put <c>StartBattle()</c> in the
    /// Script of the line that starts the fight. With it on, a line can still call <c>CancelBattle()</c>.
    /// <para>Tools > Rythm RPG > Dialogue > Encounters sets all this up on the selected enemies.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueEncounter : MonoBehaviour
    {
        public enum EncounterMode
        {
            /// <summary>Press Interact next to the enemy (needs a Usable).</summary>
            TalkFirst,
            /// <summary>Touching the enemy's trigger collider sets it off.</summary>
            Ambush,
        }

        public EncounterMode mode = EncounterMode.TalkFirst;
        [Tooltip("Start the battle whenever this object's Dialogue System Trigger fires (after its conversation). " +
                 "Off: the conversation decides, with StartBattle() in a line's Script.")]
        public bool battleWhenTriggered = true;
        [Tooltip("Show the '!' notice and the full encounter delay before the battle. Usually off after a conversation.")]
        public bool showNotice;

        [Header("Ambush")]
        [Tooltip("After a battle or conversation, the player must step out of the trigger and wait this long before " +
                 "touching the enemy sets it off again.")]
        [Min(0f)] public float rearmDelay = 1f;

        private EnemyCombatant enemy;
        private DialogueSystemTrigger dialogueTrigger;
        private Collider playerCollider;
        private bool armed = true;
        private float quietSince = -1f;

        public EnemyCombatant Enemy
        {
            get
            {
                if (enemy == null) enemy = DialogueBattleStarter.FindEnemy(transform);
                return enemy;
            }
        }

        private void Awake()
        {
            dialogueTrigger = GetComponent<DialogueSystemTrigger>();
        }

        private void OnEnable()
        {
            if (dialogueTrigger != null) dialogueTrigger.onExecute.AddListener(OnDialogueTriggerFired);
        }

        private void OnDisable()
        {
            if (dialogueTrigger != null) dialogueTrigger.onExecute.RemoveListener(OnDialogueTriggerFired);
        }

        /// <summary>For UnityEvents: start the battle with this enemy (after the current conversation).</summary>
        public void StartBattle() => DialogueBattleStarter.Request(Enemy, showNotice);

        // The Dialogue System Trigger's On Execute event (passes whoever set it off).
        private void OnDialogueTriggerFired(GameObject interactor) => BeginEncounter();

        private void BeginEncounter()
        {
            DialogueBattleStarter.NoteEncounter(this);
            if (battleWhenTriggered) DialogueBattleStarter.Request(Enemy, showNotice);
        }

        // ---------- Ambush ----------

        private void OnTriggerEnter(Collider other) => TryAmbush(other);
        private void OnTriggerStay(Collider other) => TryAmbush(other);

        private void TryAmbush(Collider other)
        {
            if (mode != EncounterMode.Ambush || !isActiveAndEnabled || !armed) return;
            PlayerEnemyInteractor3D player = other.GetComponentInParent<PlayerEnemyInteractor3D>();
            if (player == null || DialogueBattleStarter.IsBusy) return;

            armed = false;
            quietSince = -1f;
            playerCollider = other;
            if (dialogueTrigger != null && dialogueTrigger.enabled) dialogueTrigger.OnUse(player.transform);
            else BeginEncounter(); // no trigger: straight to the battle
        }

        private void Update()
        {
            if (armed || mode != EncounterMode.Ambush) return;
            if (DialogueBattleStarter.IsBusy)
            {
                quietSince = -1f;
                return;
            }
            if (quietSince < 0f) quietSince = Time.time;
            if (Time.time - quietSince >= rearmDelay && !PlayerInside()) armed = true;
        }

        private bool PlayerInside()
        {
            if (playerCollider == null || !playerCollider.gameObject.activeInHierarchy) return false;
            Bounds player = playerCollider.bounds;
            foreach (Collider zone in GetComponents<Collider>())
                if (zone.enabled && zone.isTrigger && zone.bounds.Intersects(player)) return true;
            return false;
        }
    }
}
