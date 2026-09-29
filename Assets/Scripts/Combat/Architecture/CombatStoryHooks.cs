using System;
using System.Collections;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>The points in a battle where a story scene (a conversation) may play. The battle waits for it.</summary>
    public enum CombatStoryMoment
    {
        /// <summary>The encounter was triggered; the player hasn't walked to its combat spot yet (not on a retry).</summary>
        EncounterStart,
        /// <summary>Player placed, intro music and the enemy's intro animation done; before the first enemy turn.</summary>
        BattleIntro,
        /// <summary>The enemy has picked its attack sequence (<see cref="CombatStoryRequest.SequenceId"/>); before it starts.</summary>
        EnemyTurnStart,
        /// <summary>Before each step of the enemy's attack sequence (<see cref="CombatStoryRequest.StepNumber"/>, 1-based).</summary>
        EnemyAttackStep,
        /// <summary>The enemy's attack is over and the player survived (<see cref="CombatStoryRequest.Amount"/> = damage taken).</summary>
        EnemyTurnEnd,
        /// <summary>The player's turn begins, before the ability icons appear.</summary>
        PlayerTurnStart,
        /// <summary>The player's ability has hit and the enemy survived (<see cref="CombatStoryRequest.Amount"/> = damage dealt).</summary>
        PlayerAttackEnd,
        /// <summary>The enemy's health moved it into another phase (<see cref="CombatStoryRequest.PhaseName"/>).</summary>
        EnemyPhaseChanged,
        /// <summary>The enemy reached 0 health: last words, before its death animation and the ending music.</summary>
        EnemyDefeated,
        /// <summary>The player reached 0 health, before the ending music.</summary>
        PlayerDefeated,
        /// <summary>The battle is won (after the death animation), before the result screen.</summary>
        Victory,
        /// <summary>The battle is lost, before the result screen.</summary>
        Defeat,
        /// <summary>Back in the world after the result screen (not after a retry), before the player can move again.</summary>
        AfterBattle,
    }

    /// <summary>Everything a story handler needs to decide what to play.</summary>
    public sealed class CombatStoryRequest
    {
        public CombatStoryMoment Moment { get; internal set; }
        public CombatController Controller { get; internal set; }
        public PlayerCombatant Player { get; internal set; }
        public EnemyCombatant Enemy { get; internal set; }
        /// <summary>1 = the first enemy turn and the player turn after it; 0 before the first enemy turn.</summary>
        public int Round { get; internal set; }
        /// <summary>Changes for every battle, including retries.</summary>
        public int BattleSerial { get; internal set; }
        /// <summary>Changes for every encounter; stays the same across retries of that encounter.</summary>
        public int EncounterSerial { get; internal set; }
        /// <summary>The battle was restarted from the result screen.</summary>
        public bool IsRetry { get; internal set; }
        /// <summary>EnemyTurnStart / EnemyAttackStep: the attack sequence's Id (or asset name when its Id is empty).</summary>
        public string SequenceId { get; internal set; } = string.Empty;
        /// <summary>EnemyAttackStep: 1-based step number.</summary>
        public int StepNumber { get; internal set; }
        /// <summary>EnemyTurnEnd: damage taken; PlayerAttackEnd / EnemyDefeated: damage dealt.</summary>
        public int Amount { get; internal set; }
        /// <summary>EnemyPhaseChanged: the new phase asset's name.</summary>
        public string PhaseName { get; internal set; } = string.Empty;
        public int Combo { get; internal set; }

        public float PlayerHealth01 => Player == null || Player.MaxHealth <= 0 ? 0f : (float)Player.CurrentHealth / Player.MaxHealth;
        public float EnemyHealth01 => Enemy == null || Enemy.MaxHealth <= 0 ? 0f : (float)Enemy.CurrentHealth / Enemy.MaxHealth;
    }

    /// <summary>Plays story scenes for a battle. Implemented outside the combat assembly (the dialogue lives there).</summary>
    public interface ICombatStoryHandler
    {
        /// <summary>A routine that plays the scene for this moment, or null when there is nothing to play.</summary>
        IEnumerator Play(CombatStoryRequest request);
        /// <summary>The battle was cancelled or restarted while a scene was playing: stop it now.</summary>
        void Stop();
    }

    /// <summary>
    /// The combat side of story scenes in battle. <see cref="CombatController"/> asks <see cref="Handler"/> at every
    /// <see cref="CombatStoryMoment"/> and waits for the scene to finish before it continues. Music keeps playing; the
    /// next attack is planned on the song's grid after the scene, so timing stays right.
    /// </summary>
    public static class CombatStoryHooks
    {
        private static bool endRequested;
        private static bool endVictory;

        public static ICombatStoryHandler Handler { get; set; }
        /// <summary>True while a story scene holds the battle.</summary>
        public static bool IsPlaying { get; private set; }

        /// <summary>
        /// Ends the battle once the current scene is over (e.g. the enemy gives up): victory (enemy defeated) or defeat.
        /// Honoured from BattleIntro on; ignored once the battle is already won or lost.
        /// </summary>
        public static void RequestBattleEnd(bool victory)
        {
            endRequested = true;
            endVictory = victory;
        }

        internal static void ClearRequests() => endRequested = false;

        internal static bool TryConsumeEndRequest(out bool victory)
        {
            victory = endVictory;
            if (!endRequested) return false;
            endRequested = false;
            return true;
        }

        internal static IEnumerator Play(CombatStoryRequest request)
        {
            if (Handler == null) yield break;
            IEnumerator scene = null;
            try
            {
                scene = Handler.Play(request);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            if (scene == null) yield break;

            IsPlaying = true;
            while (true)
            {
                bool more;
                try
                {
                    more = scene.MoveNext();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    more = false;
                }
                if (!more) break;
                yield return scene.Current;
            }
            IsPlaying = false;
        }

        internal static void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            try
            {
                Handler?.Stop();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
