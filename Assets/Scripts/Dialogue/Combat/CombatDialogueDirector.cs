using System.Collections;
using System.Collections.Generic;
using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Plays Dialogue System conversations at the story moments of a battle (<see cref="CombatStoryMoment"/>): the
    /// battle waits while the pixel bubbles play, then carries on. Installed automatically at startup.
    /// <para>
    /// Where the conversations come from, in order: the enemy's <see cref="EnemyCombatDialogue"/> (its entries, then its
    /// sets), then the Combat Dialogue Library's sets for that enemy type, then the library's sets for every enemy. The
    /// first entry whose conditions match plays.
    /// </para>
    /// <para>
    /// Before every conversation these Dialogue System variables are set, for conditions and text:
    /// Combat_Moment, Combat_Round, Combat_Enemy, Combat_EnemyHP / Combat_PlayerHP (percent), Combat_Sequence,
    /// Combat_Step, Combat_Phase, Combat_Amount (damage taken / dealt), Combat_Combo, Combat_IsRetry.
    /// Lua functions for conversations: CombatEndBattle(victory), CombatHealPlayer(amount), CombatGiveMana(amount).
    /// </para>
    /// </summary>
    public sealed class CombatDialogueDirector : ICombatStoryHandler
    {
        private const float WaitForFreeDialogueSeconds = 5f;

        private static CombatDialogueDirector instance;
        private static bool luaRegistered;

        private readonly HashSet<CombatDialogueEntry> playedThisBattle = new();
        private readonly HashSet<CombatDialogueEntry> playedThisEncounter = new();
        private int battleSerial = -1;
        private int encounterSerial = -1;
        private bool startedConversation;
        private CombatStoryRequest current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            instance = new CombatDialogueDirector();
            luaRegistered = false;
            // Keep a handler someone else installed on purpose.
            if (CombatStoryHooks.Handler == null) CombatStoryHooks.Handler = instance;
        }

        public IEnumerator Play(CombatStoryRequest request)
        {
            if (request == null || !DialogueManager.hasInstance) return null;
            if (request.BattleSerial != battleSerial)
            {
                battleSerial = request.BattleSerial;
                playedThisBattle.Clear();
            }
            if (request.EncounterSerial != encounterSerial)
            {
                encounterSerial = request.EncounterSerial;
                playedThisEncounter.Clear();
            }

            SetVariables(request);
            CombatDialogueEntry entry = FindEntry(request);
            return entry == null ? null : Run(entry, request);
        }

        public void Stop()
        {
            if (startedConversation && DialogueManager.hasInstance && DialogueManager.isConversationActive)
                DialogueManager.StopConversation();
            startedConversation = false;
            current = null;
        }

        private IEnumerator Run(CombatDialogueEntry entry, CombatStoryRequest request)
        {
            Remember(entry);
            RegisterLuaFunctions();
            current = request;

            // A conversation that is still closing (e.g. the one that started this battle) gets a moment to finish.
            float waitUntil = Time.unscaledTime + WaitForFreeDialogueSeconds;
            while (DialogueManager.isConversationActive && Time.unscaledTime < waitUntil) yield return null;
            if (DialogueManager.isConversationActive)
            {
                Debug.LogWarning($"[Combat Dialogue] Skipped '{entry.conversation}' ({request.Moment}): another conversation is still running.");
                current = null;
                yield break;
            }

            Transform player = request.Player != null ? request.Player.transform : null;
            Transform enemy = request.Enemy != null ? request.Enemy.transform : null;
            bool enemyIsActor = entry.speakers == CombatDialogueSpeakers.EnemyIsActor;
            DialogueManager.StartConversation(entry.conversation, enemyIsActor ? enemy : player, enemyIsActor ? player : enemy);
            startedConversation = DialogueManager.isConversationActive;
            if (!startedConversation)
            {
                Debug.LogWarning($"[Combat Dialogue] Conversation '{entry.conversation}' ({request.Moment}) didn't start. " +
                                 "Check the title and that its first lines' conditions can be true.");
                current = null;
                yield break;
            }

            while (startedConversation && DialogueManager.isConversationActive) yield return null;
            startedConversation = false;

            // A PixelCam command moved the camera and the conversation had no PixelCamReturn: bring it back before the
            // battle carries on. Otherwise the battle would take over a camera still pointed at the dialogue focus (and
            // later hand the camera back to that focus instead of the player).
            PixelDialogueCamera dialogueCamera = PixelDialogueCamera.Existing;
            if (dialogueCamera != null && dialogueCamera.IsEngaged) yield return dialogueCamera.ReturnAndWait();

            current = null;
            yield return null; // let the bubble close before the battle moves on
        }

        // ---------- Choosing ----------

        private CombatDialogueEntry FindEntry(CombatStoryRequest request)
        {
            EnemyDefinition definition = request.Enemy != null ? request.Enemy.Definition : null;
            EnemyCombatDialogue own = request.Enemy != null ? request.Enemy.GetComponentInParent<EnemyCombatDialogue>(true) : null;

            if (own != null)
            {
                CombatDialogueEntry found = FirstMatch(own.entries, request);
                if (found != null) return found;
                foreach (CombatDialogueSet set in own.sets)
                {
                    if (set == null || !set.AppliesTo(definition)) continue;
                    found = FirstMatch(set.entries, request);
                    if (found != null) return found;
                }
                if (!own.useLibrary) return null;
            }

            CombatDialogueLibrary library = CombatDialogueLibrary.Load();
            if (library == null) return null;
            // Sets for this enemy type first, then the ones for every enemy.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (CombatDialogueSet set in library.sets)
                {
                    if (set == null) continue;
                    bool specific = set.enemy != null;
                    if (pass == 0 ? !specific || set.enemy != definition : specific) continue;
                    CombatDialogueEntry found = FirstMatch(set.entries, request);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private CombatDialogueEntry FirstMatch(List<CombatDialogueEntry> entries, CombatStoryRequest request)
        {
            if (entries == null) return null;
            foreach (CombatDialogueEntry entry in entries)
            {
                if (entry == null || !entry.Matches(request) || AlreadyPlayed(entry)) continue;
                if (!string.IsNullOrWhiteSpace(entry.luaCondition) && !Lua.IsTrue(entry.luaCondition)) continue;
                if (entry.chance < 1f && Random.value > entry.chance) continue;
                return entry;
            }
            return null;
        }

        private bool AlreadyPlayed(CombatDialogueEntry entry)
        {
            switch (entry.repeat)
            {
                case CombatDialogueRepeat.OncePerBattle: return playedThisBattle.Contains(entry);
                case CombatDialogueRepeat.OncePerEncounter: return playedThisEncounter.Contains(entry);
                case CombatDialogueRepeat.OnceEver: return DialogueLua.GetVariable(entry.SaveVariableName).asBool;
                default: return false;
            }
        }

        private void Remember(CombatDialogueEntry entry)
        {
            playedThisBattle.Add(entry);
            playedThisEncounter.Add(entry);
            if (entry.repeat == CombatDialogueRepeat.OnceEver) DialogueLua.SetVariable(entry.SaveVariableName, true);
        }

        // ---------- Lua ----------

        private static void SetVariables(CombatStoryRequest request)
        {
            EnemyCombatant enemy = request.Enemy;
            string enemyName = enemy == null ? string.Empty
                : enemy.Definition != null ? enemy.Definition.DisplayName : enemy.name;
            DialogueLua.SetVariable("Combat_Moment", request.Moment.ToString());
            DialogueLua.SetVariable("Combat_Round", request.Round);
            DialogueLua.SetVariable("Combat_Enemy", enemyName);
            DialogueLua.SetVariable("Combat_EnemyHP", Mathf.RoundToInt(request.EnemyHealth01 * 100f));
            DialogueLua.SetVariable("Combat_PlayerHP", Mathf.RoundToInt(request.PlayerHealth01 * 100f));
            DialogueLua.SetVariable("Combat_Sequence", request.SequenceId);
            DialogueLua.SetVariable("Combat_Step", request.StepNumber);
            DialogueLua.SetVariable("Combat_Phase", request.PhaseName);
            DialogueLua.SetVariable("Combat_Amount", request.Amount);
            DialogueLua.SetVariable("Combat_Combo", request.Combo);
            DialogueLua.SetVariable("Combat_IsRetry", request.IsRetry);
        }

        private static void RegisterLuaFunctions()
        {
            if (luaRegistered) return;
            luaRegistered = true;
            Lua.RegisterFunction("CombatEndBattle", null, typeof(CombatDialogueDirector).GetMethod(nameof(LuaEndBattle)));
            Lua.RegisterFunction("CombatHealPlayer", null, typeof(CombatDialogueDirector).GetMethod(nameof(LuaHealPlayer)));
            Lua.RegisterFunction("CombatGiveMana", null, typeof(CombatDialogueDirector).GetMethod(nameof(LuaGiveMana)));
        }

        /// <summary>Lua: CombatEndBattle(true) = the enemy is defeated after this conversation; false = the player loses.</summary>
        public static void LuaEndBattle(bool victory) => CombatStoryHooks.RequestBattleEnd(victory);

        /// <summary>Lua: CombatHealPlayer(amount).</summary>
        public static void LuaHealPlayer(double amount)
        {
            PlayerCombatant player = instance?.current?.Player;
            if (player != null && amount > 0) player.Heal(Mathf.RoundToInt((float)amount));
        }

        /// <summary>Lua: CombatGiveMana(amount).</summary>
        public static void LuaGiveMana(double amount)
        {
            PlayerCombatant player = instance?.current?.Player;
            if (player != null && amount > 0) player.GainMana(Mathf.RoundToInt((float)amount));
        }
    }
}
