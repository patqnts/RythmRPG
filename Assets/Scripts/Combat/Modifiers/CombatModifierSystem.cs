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
            RemoveExpired();
        }

        public void OnEnemyTurnEnded()
        {
            foreach (ICombatModifierRuntime modifier in activeModifiers.ToArray()) modifier.OnEnemyTurnEnded();
            RemoveExpired();
        }

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
