namespace RythmRPG.Combat
{
    public enum CombatState
    {
        BattleStart,
        EnemyTurnStart,
        EnemyTurnExecuting,
        EnemyTurnEnd,
        PlayerTurnStart,
        PlayerAbilitySelection,
        PlayerAbilityExecuting,
        PlayerTurnEnd,
        Victory,
        Defeat
    }

    public enum AbilityType
    {
        BasicAttack,
        SpecialAttack,
        Defensive,
        Healing
    }

    public enum ElementType
    {
        None,
        Fire,
        Water,
        Lightning,
        Earth,
        Wind
    }

    public enum PatternRunMode
    {
        EnemyDefense,
        PlayerAbility
    }

    public enum AttackStepEndPolicy
    {
        WaitForResolvedNotes,
        ClearAsMisses,
        ClearWithoutPenalty
    }

    public enum NoteResolutionSource
    {
        PlayerInput,
        Timeout,
        Modifier,
        SystemClear
    }
}
