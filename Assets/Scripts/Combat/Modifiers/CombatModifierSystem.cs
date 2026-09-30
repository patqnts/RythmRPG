using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    public interface ICombatModifierRuntime
    {
        bool IsExpired { get; }
        void OnEnemyTurnStarted();
        void OnEnemyTurnEnded();
        void OnJudgementResolved(RhythmJudgementResult result, RhythmPatternRunner runner);
    }

    /// <summary>
    /// A temporary effect (buff, status, shield...) that counts down at declared turn boundaries. Boundaries are raised
    /// by the combat controller; time spent choosing an ability never consumes a duration.
    /// </summary>
    public interface ITurnBoundaryModifier
    {
        /// <summary>Stable key used for refresh / replace stacking (e.g. "shield", "burn").</summary>
        string StackKey { get; }
        string Label { get; }
        void OnTurnBoundary(TurnBoundary boundary);
        string Describe();
    }

    public sealed class CombatModifierSystem : MonoBehaviour
    {
        private readonly List<ICombatModifierRuntime> activeModifiers = new();

        public IReadOnlyList<ICombatModifierRuntime> ActiveModifiers => activeModifiers;
        /// <summary>A modifier cancelled the damage of this enemy note.</summary>
        public event Action<RhythmJudgementResult> DamageBlocked;
        /// <summary>A modifier was added or expired.</summary>
        public event Action Changed;

        public void Add(ICombatModifierRuntime modifier)
        {
            if (modifier == null || activeModifiers.Contains(modifier)) return;
            activeModifiers.Add(modifier);
            Changed?.Invoke();
        }

        /// <summary>True (and DamageBlocked raised) when an active modifier cancels this note's damage.</summary>
        public bool TryBlockDamage(RhythmJudgementResult result)
        {
            foreach (ICombatModifierRuntime modifier in activeModifiers)
            {
                if (modifier is IDamageBlockingModifier blocker && !modifier.IsExpired && blocker.BlocksDamage(result))
                {
                    DamageBlocked?.Invoke(result);
                    return true;
                }
            }
            return false;
        }

        public void OnEnemyTurnStarted()
        {
            foreach (ICombatModifierRuntime modifier in activeModifiers.ToArray()) modifier.OnEnemyTurnStarted();
            RaiseBoundary(TurnBoundary.EnemyTurnStart);
        }

        public void OnEnemyTurnEnded()
        {
            foreach (ICombatModifierRuntime modifier in activeModifiers.ToArray()) modifier.OnEnemyTurnEnded();
            RaiseBoundary(TurnBoundary.EnemyTurnEnd);
        }

        public void OnPlayerTurnStarted() => RaiseBoundary(TurnBoundary.PlayerTurnStart);
        public void OnPlayerTurnEnded() => RaiseBoundary(TurnBoundary.PlayerTurnEnd);

        /// <summary>
        /// Ticks every boundary-aware modifier in the order they were added (stable), then removes expired ones.
        /// A tick can end the battle (damage over time); later modifiers still count down but deal no payouts
        /// because the build runtime stops payouts once combat is over.
        /// </summary>
        private void RaiseBoundary(TurnBoundary boundary)
        {
            foreach (ICombatModifierRuntime modifier in activeModifiers.ToArray())
                if (modifier is ITurnBoundaryModifier timed && !modifier.IsExpired) timed.OnTurnBoundary(boundary);
            RemoveExpired();
            Changed?.Invoke();
        }

        public IEnumerable<T> OfType<T>() => activeModifiers.Where(modifier => modifier != null && !modifier.IsExpired).OfType<T>();

        public T Find<T>(string stackKey) where T : class, ITurnBoundaryModifier =>
            OfType<T>().FirstOrDefault(modifier => modifier.StackKey == stackKey);

        public bool Remove(ICombatModifierRuntime modifier)
        {
            if (modifier == null || !activeModifiers.Remove(modifier)) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Raise <see cref="Changed"/> after a modifier changed its own state (a shield absorbed damage...).</summary>
        public void NotifyChanged() => Changed?.Invoke();

        public void OnJudgementResolved(RhythmJudgementResult result, RhythmPatternRunner runner)
        {
            if (result.Source != NoteResolutionSource.PlayerInput) return;
            foreach (ICombatModifierRuntime modifier in activeModifiers.ToArray()) modifier.OnJudgementResolved(result, runner);
            RemoveExpired();
        }

        public void Clear()
        {
            activeModifiers.Clear();
            Changed?.Invoke();
        }

        private void RemoveExpired()
        {
            if (activeModifiers.RemoveAll(modifier => modifier == null || modifier.IsExpired) > 0) Changed?.Invoke();
        }
    }
}
