using System.Collections;
using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Starts battles from the Dialogue System. A battle asked for during a conversation waits until the conversation
    /// has ended (and the dialogue camera is back), then starts through the player's <see cref="PlayerEnemyInteractor3D"/>
    /// (encounter sound, battle background, movement handling).
    /// <para>Ways to ask for one:</para>
    /// <list type="bullet">
    /// <item>A <see cref="DialogueEncounter"/> on the enemy (fires with the enemy's Dialogue System Trigger).</item>
    /// <item>Lua in a dialogue entry's Script: <c>StartBattle()</c> (the enemy taking part in the conversation),
    /// <c>StartBattleWith("WHO")</c> (by GameObject name), <c>CancelBattle()</c> (drop a battle asked for earlier, e.g.
    /// a "bribe" answer).</item>
    /// <item>Sequencer: <c>StartBattle()</c> or <c>StartBattle(WHO)</c>.</item>
    /// </list>
    /// Installed automatically.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DialogueBattleStarter : MonoBehaviour
    {
        private const float EncounterMemorySeconds = 2f;

        private static DialogueBattleStarter instance;
        private static CombatController[] controllers = System.Array.Empty<CombatController>();
        private static float nextControllerSearch;

        private bool pending;
        private EnemyCombatant pendingEnemy;
        private bool pendingNotice;
        private Coroutine routine;
        private DialogueEncounter lastEncounter;
        private float lastEncounterTime = -999f;

        /// <summary>A battle was asked for and hasn't started yet (e.g. its conversation is still running).</summary>
        public static bool IsPending => instance != null && instance.pending;

        /// <summary>A battle is running, about to start, or a conversation is playing.</summary>
        public static bool IsBusy => IsPending || AnyBattleActive()
                                     || (DialogueManager.hasInstance && DialogueManager.isConversationActive);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            controllers = System.Array.Empty<CombatController>();
            nextControllerSearch = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Ensure();
            GameSceneLoader.CanTransition -= SceneGuard;
            GameSceneLoader.CanTransition += SceneGuard;
            GameSceneLoader.TransitionStarted -= SceneTransition;
            GameSceneLoader.TransitionStarted += SceneTransition;
        }
        private static string SceneGuard(SceneLoadRequest request) =>
            request.Intent != SceneLoadIntent.ReturnToMenu && IsBusy ? "Finish the conversation or encounter before leaving." : null;
        private static void SceneTransition(SceneLoadRequest request)
        {
            Cancel();
            if (request.Intent == SceneLoadIntent.ReturnToMenu && DialogueManager.hasInstance && DialogueManager.isConversationActive)
                DialogueManager.StopConversation();
        }

        private static DialogueBattleStarter Ensure()
        {
            if (instance != null) return instance;
            var host = new GameObject("Dialogue Battle Starter") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            instance = host.AddComponent<DialogueBattleStarter>();
            return instance;
        }

        // ---------- API ----------

        /// <summary>Starts a battle with <paramref name="enemy"/> once the current conversation (if any) is over.</summary>
        /// <param name="showNotice">Null = the enemy's <see cref="DialogueEncounter.showNotice"/> (off when it has none).</param>
        public static bool Request(EnemyCombatant enemy, bool? showNotice = null)
        {
            if (GameSceneLoader.IsLoading) return false;
            if (enemy == null)
            {
                Debug.LogWarning("[Dialogue Battle] StartBattle: no enemy found. Use it in a conversation with the enemy as " +
                                 "Actor or Conversant, or name it: StartBattleWith(\"EnemyName\").");
                return false;
            }
            if (AnyBattleActive())
            {
                Debug.LogWarning($"[Dialogue Battle] StartBattle({enemy.name}) ignored: a battle is already running.", enemy);
                return false;
            }

            DialogueBattleStarter starter = Ensure();
            if (starter.pending && starter.pendingEnemy == enemy) return true;
            if (starter.routine != null) starter.StopCoroutine(starter.routine);

            DialogueEncounter encounter = enemy.GetComponentInParent<DialogueEncounter>();
            starter.pending = true;
            starter.pendingEnemy = enemy;
            starter.pendingNotice = showNotice ?? (encounter != null && encounter.showNotice);
            starter.routine = starter.StartCoroutine(starter.Run());
            return true;
        }

        /// <summary>Drops a battle that was asked for but hasn't started yet.</summary>
        public static void Cancel()
        {
            if (instance == null || !instance.pending) return;
            if (instance.routine != null) instance.StopCoroutine(instance.routine);
            instance.Clear();
            // Outside a conversation the player lock has already handed control back without movement: give it back.
            if (!(DialogueManager.hasInstance && DialogueManager.isConversationActive)) RestoreMovement(FindInteractor());
        }

        /// <summary>Called by <see cref="DialogueEncounter"/> when its trigger fires, so a StartBattle() in the
        /// conversation it starts knows which enemy it's about.</summary>
        internal static void NoteEncounter(DialogueEncounter encounter)
        {
            DialogueBattleStarter starter = Ensure();
            starter.lastEncounter = encounter;
            starter.lastEncounterTime = Time.unscaledTime;
        }

        public static bool AnyBattleActive()
        {
            if (Time.unscaledTime >= nextControllerSearch)
            {
                nextControllerSearch = Time.unscaledTime + 1f;
                controllers = FindObjectsByType<CombatController>(FindObjectsInactive.Exclude);
            }
            foreach (CombatController controller in controllers)
                if (controller != null && controller.IsBattleActive) return true;
            return false;
        }

        public static EnemyCombatant FindEnemy(Transform subject)
        {
            if (subject == null) return null;
            EnemyCombatant enemy = subject.GetComponentInParent<EnemyCombatant>();
            if (enemy == null) enemy = subject.GetComponentInChildren<EnemyCombatant>();
            return enemy;
        }

        /// <summary>The enemy among two conversation participants (the second one is checked first: usually the conversant).</summary>
        public static EnemyCombatant EnemyAmong(Transform actor, Transform conversant)
        {
            EnemyCombatant enemy = FindEnemy(conversant);
            if (enemy == null) enemy = FindEnemy(actor);
            return enemy;
        }

        // ---------- Starting ----------

        private IEnumerator Run()
        {
            yield return null; // a conversation started by the same trigger begins this frame
            while (DialogueManager.hasInstance && DialogueManager.isConversationActive) yield return null;

            // A PixelCam move without PixelCamReturn glides back after the conversation; the battle must not take a
            // camera that still points at the dialogue focus.
            PixelDialogueCamera dialogueCamera = PixelDialogueCamera.Existing;
            if (dialogueCamera != null && dialogueCamera.IsEngaged) yield return dialogueCamera.ReturnAndWait();

            // The player lock turns the interactor off during conversations and back on a moment after.
            PlayerEnemyInteractor3D interactor = FindInteractor();
            float giveUpAt = Time.unscaledTime + 2f;
            while (interactor != null && !interactor.isActiveAndEnabled && Time.unscaledTime < giveUpAt) yield return null;

            EnemyCombatant enemy = pendingEnemy;
            bool started = false;
            if (interactor == null)
                Debug.LogWarning("[Dialogue Battle] Can't start the battle: no PlayerEnemyInteractor3D on the player.");
            else if (enemy == null || !enemy.isActiveAndEnabled)
                Debug.LogWarning("[Dialogue Battle] Can't start the battle: the enemy is gone or disabled.");
            else if (!interactor.isActiveAndEnabled)
                Debug.LogWarning("[Dialogue Battle] Can't start the battle: the player's PlayerEnemyInteractor3D is disabled.", interactor);
            else
                started = interactor.TryBeginBattle(enemy, pendingNotice);

            // Stay "pending" through the encounter transition so nothing (selectors, ambushes) fires before the fight.
            if (started)
                while (interactor != null && interactor.IsEncounterStarting) yield return null;

            Clear();
            if (!started) RestoreMovement(interactor);
        }

        private void Clear()
        {
            pending = false;
            pendingEnemy = null;
            routine = null;
        }

        private static PlayerEnemyInteractor3D FindInteractor() =>
            FindAnyObjectByType<PlayerEnemyInteractor3D>(FindObjectsInactive.Exclude);

        private static void RestoreMovement(PlayerEnemyInteractor3D interactor)
        {
            if (AnyBattleActive()) return;
            if (interactor != null)
            {
                interactor.SendMessage("EnableMovement", SendMessageOptions.DontRequireReceiver);
                return;
            }
            foreach (PlayerMovement3D movement in FindObjectsByType<PlayerMovement3D>(FindObjectsInactive.Exclude))
                movement.EnableMovement();
        }

        // ---------- Lua ----------

        private void OnEnable()
        {
            Lua.RegisterFunction("StartBattle", null, typeof(DialogueBattleStarter).GetMethod(nameof(LuaStartBattle)));
            Lua.RegisterFunction("StartBattleWith", null, typeof(DialogueBattleStarter).GetMethod(nameof(LuaStartBattleWith)));
            Lua.RegisterFunction("CancelBattle", null, typeof(DialogueBattleStarter).GetMethod(nameof(LuaCancelBattle)));
        }

        private void OnDisable()
        {
            Lua.UnregisterFunction("StartBattle");
            Lua.UnregisterFunction("StartBattleWith");
            Lua.UnregisterFunction("CancelBattle");
        }

        /// <summary>Lua StartBattle(): fight the enemy in this conversation once it ends.</summary>
        public static void LuaStartBattle()
        {
            EnemyCombatant enemy = null;
            if (DialogueManager.hasInstance && DialogueManager.isConversationActive)
                enemy = EnemyAmong(DialogueManager.currentActor, DialogueManager.currentConversant);
            if (enemy == null && instance != null && instance.lastEncounter != null
                && Time.unscaledTime - instance.lastEncounterTime <= EncounterMemorySeconds)
                enemy = instance.lastEncounter.Enemy;
            Request(enemy);
        }

        /// <summary>Lua StartBattleWith("GameObject name").</summary>
        public static void LuaStartBattleWith(string enemyName)
        {
            EnemyCombatant found = null;
            foreach (EnemyCombatant enemy in FindObjectsByType<EnemyCombatant>(FindObjectsInactive.Exclude))
            {
                if (enemy.name != enemyName) continue;
                found = enemy;
                break;
            }
            if (found == null)
            {
                GameObject named = GameObject.Find(enemyName);
                if (named != null) found = FindEnemy(named.transform);
            }
            if (found == null) Debug.LogWarning($"[Dialogue Battle] StartBattleWith(\"{enemyName}\"): no enemy with that name.");
            else Request(found);
        }

        /// <summary>Lua CancelBattle().</summary>
        public static void LuaCancelBattle() => Cancel();
    }
}
