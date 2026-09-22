using System;
using System.Collections.Generic;

namespace RythmRPG.Combat
{
    public interface ICombatState
    {
        CombatState State { get; }
        void Enter();
        void Exit();
        void Tick();
    }

    public sealed class CombatTurnStateMachine
    {
        private static readonly IReadOnlyDictionary<CombatState, HashSet<CombatState>> AllowedTransitions =
            new Dictionary<CombatState, HashSet<CombatState>>
            {
                [CombatState.BattleStart] = new() { CombatState.EnemyTurnStart, CombatState.Victory, CombatState.Defeat },
                // Victory during the enemy turn: the enemy can die on its own turn (dev tools, future reflect damage).
                [CombatState.EnemyTurnStart] = new() { CombatState.EnemyTurnExecuting, CombatState.Victory, CombatState.Defeat },
                [CombatState.EnemyTurnExecuting] = new() { CombatState.EnemyTurnEnd, CombatState.Victory, CombatState.Defeat },
                [CombatState.EnemyTurnEnd] = new() { CombatState.PlayerTurnStart, CombatState.Victory, CombatState.Defeat },
                [CombatState.PlayerTurnStart] = new() { CombatState.PlayerAbilitySelection, CombatState.Victory, CombatState.Defeat },
                // PlayerTurnEnd from selection = the player passes the turn without acting.
                [CombatState.PlayerAbilitySelection] = new() { CombatState.PlayerAbilityExecuting, CombatState.PlayerTurnEnd, CombatState.Victory, CombatState.Defeat },
                [CombatState.PlayerAbilityExecuting] = new() { CombatState.PlayerTurnEnd, CombatState.Victory, CombatState.Defeat },
                [CombatState.PlayerTurnEnd] = new() { CombatState.EnemyTurnStart, CombatState.Victory, CombatState.Defeat },
                [CombatState.Victory] = new(),
                [CombatState.Defeat] = new()
            };

        private readonly Dictionary<CombatState, ICombatState> handlers = new();
        public CombatState CurrentState { get; private set; }
        public bool IsStarted { get; private set; }
        public event Action<CombatState, CombatState> StateChanged;

        public void Register(ICombatState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            handlers[state.State] = state;
        }

        public void Start()
        {
            if (IsStarted) throw new InvalidOperationException("The combat state machine is already running.");
            IsStarted = true;
            CurrentState = CombatState.BattleStart;
            handlers.GetValueOrDefault(CurrentState)?.Enter();
            StateChanged?.Invoke(CurrentState, CurrentState);
        }

        public bool CanTransitionTo(CombatState next) => IsStarted
            && AllowedTransitions.TryGetValue(CurrentState, out HashSet<CombatState> allowed)
            && allowed.Contains(next);

        public bool TryTransition(CombatState next)
        {
            if (!CanTransitionTo(next)) return false;
            CombatState previous = CurrentState;
            handlers.GetValueOrDefault(previous)?.Exit();
            CurrentState = next;
            handlers.GetValueOrDefault(next)?.Enter();
            StateChanged?.Invoke(previous, next);
            return true;
        }

        public void Tick()
        {
            if (IsStarted) handlers.GetValueOrDefault(CurrentState)?.Tick();
        }
    }
}
