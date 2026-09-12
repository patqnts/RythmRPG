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

        public void Add(ICombatModifierRuntime modifier)
        {
            if (modifier != null && !activeModifiers.Contains(modifier)) activeModifiers.Add(modifier);
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

        public void Clear() => activeModifiers.Clear();

        private void RemoveExpired() => activeModifiers.RemoveAll(modifier => modifier == null || modifier.IsExpired);
    }
}
