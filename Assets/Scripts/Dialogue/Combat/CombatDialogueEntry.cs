using System;
using PixelCrushers.DialogueSystem;
using RythmRPG.Combat;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    public enum CombatDialogueRepeat
    {
        /// <summary>Every time the moment comes and the conditions match.</summary>
        EveryTime,
        /// <summary>Once per battle; a retry from the result screen plays it again.</summary>
        OncePerBattle,
        /// <summary>Once per encounter; retries don't replay it.</summary>
        OncePerEncounter,
        /// <summary>Once for the whole game (a Dialogue System variable, so it is saved with the game).</summary>
        OnceEver,
    }

    public enum CombatDamageCondition
    {
        Any,
        /// <summary>EnemyTurnEnd: the player took no damage (a flawless dodge). PlayerAttackEnd: the attack did nothing.</summary>
        None,
        /// <summary>At least <see cref="CombatDialogueEntry.damageThreshold"/>.</summary>
        AtLeast,
    }

    /// <summary>Which conversation participant the enemy is (the player is the other one).</summary>
    public enum CombatDialogueSpeakers
    {
        /// <summary>Conversation Actor = player, Conversant = enemy (the usual Dialogue System setup).</summary>
        PlayerIsActor,
        /// <summary>Conversation Actor = enemy, Conversant = player.</summary>
        EnemyIsActor,
    }

    /// <summary>One conversation that plays at a moment of a battle when its conditions match.</summary>
    [Serializable]
    public sealed class CombatDialogueEntry
    {
        [Tooltip("Just a note for you (shown as the element name).")]
        public string label = string.Empty;
        public CombatStoryMoment moment = CombatStoryMoment.BattleIntro;
        [ConversationPopup(true, true)]
        public string conversation = string.Empty;
        public CombatDialogueSpeakers speakers = CombatDialogueSpeakers.PlayerIsActor;

        [Header("Conditions")]
        public CombatDialogueRepeat repeat = CombatDialogueRepeat.OncePerEncounter;
        [Tooltip("Round number (1 = first enemy turn and the player turn after it). 0 = any round.")]
        [Min(0)] public int round;
        [Tooltip("EnemyTurnStart / EnemyAttackStep: only for this attack sequence (its Id, or asset name). Empty = any.")]
        public string attackSequence = string.Empty;
        [Tooltip("EnemyAttackStep: only before this step (1 = first). 0 = any step.")]
        [Min(0)] public int attackStep;
        [Tooltip("EnemyPhaseChanged: only when entering this phase (asset name). Empty = any.")]
        public string phase = string.Empty;
        [Tooltip("Only when the enemy's health is at or below this (1 = any).")]
        [Range(0f, 1f)] public float enemyHealthAtMost = 1f;
        [Tooltip("Only when the player's health is at or below this (1 = any).")]
        [Range(0f, 1f)] public float playerHealthAtMost = 1f;
        [Tooltip("EnemyTurnEnd: damage taken. PlayerAttackEnd / EnemyDefeated: damage dealt.")]
        public CombatDamageCondition damage = CombatDamageCondition.Any;
        [Min(0)] public int damageThreshold;
        [Tooltip("Chance to play when everything else matches (1 = always).")]
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("Optional Lua condition, e.g. Variable[\"MetTheKing\"] == true. Combat_* variables are set first.")]
        public string luaCondition = string.Empty;
        [Tooltip("OnceEver: the Dialogue System variable that remembers it. Empty = made from the conversation and moment.")]
        public string saveVariable = string.Empty;

        public bool Matches(CombatStoryRequest request)
        {
            if (request == null || request.Moment != moment || string.IsNullOrWhiteSpace(conversation)) return false;
            if (round > 0 && request.Round != round) return false;
            if (!string.IsNullOrWhiteSpace(attackSequence) &&
                !string.Equals(attackSequence.Trim(), request.SequenceId, StringComparison.OrdinalIgnoreCase)) return false;
            if (attackStep > 0 && request.StepNumber != attackStep) return false;
            if (!string.IsNullOrWhiteSpace(phase) &&
                !string.Equals(phase.Trim(), request.PhaseName, StringComparison.OrdinalIgnoreCase)) return false;
            if (enemyHealthAtMost < 1f && request.EnemyHealth01 > enemyHealthAtMost + 0.0001f) return false;
            if (playerHealthAtMost < 1f && request.PlayerHealth01 > playerHealthAtMost + 0.0001f) return false;
            switch (damage)
            {
                case CombatDamageCondition.None when request.Amount > 0: return false;
                case CombatDamageCondition.AtLeast when request.Amount < damageThreshold: return false;
            }
            return true;
        }

        /// <summary>
        /// Unity fills a list element added with the Inspector's + button with zeros instead of the defaults above,
        /// so Chance 0 and both health limits 0 (an entry that could never play) means "not set yet": give those
        /// fields their defaults. Returns true when something changed.
        /// </summary>
        public bool FixUnsetDefaults()
        {
            if (chance > 0f || enemyHealthAtMost > 0f || playerHealthAtMost > 0f) return false;
            chance = 1f;
            enemyHealthAtMost = 1f;
            playerHealthAtMost = 1f;
            return true;
        }

        internal static bool FixUnsetDefaults(System.Collections.Generic.List<CombatDialogueEntry> entries)
        {
            bool changed = false;
            if (entries == null) return false;
            foreach (CombatDialogueEntry entry in entries)
                if (entry != null && entry.FixUnsetDefaults()) changed = true;
            return changed;
        }

        public string SaveVariableName =>
            !string.IsNullOrWhiteSpace(saveVariable) ? saveVariable.Trim() : $"CombatDialogue_{moment}_{conversation}".Replace(' ', '_').Replace('/', '_');
    }
}
