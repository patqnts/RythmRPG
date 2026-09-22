using System.Collections.Generic;

namespace RythmRPG.Combat
{
    /// <summary>A modifier that can cancel the damage of a resolved enemy note.</summary>
    public interface IDamageBlockingModifier
    {
        bool BlocksDamage(RhythmJudgementResult result);
    }

    /// <summary>
    /// Lanes become invulnerable: Bad / Miss notes in them deal no damage. Lasts a number of enemy turns (counted
    /// when an enemy turn ends). lanes == null means every lane.
    /// </summary>
    public sealed class LaneWardModifier : ICombatModifierRuntime, IDamageBlockingModifier
    {
        private readonly HashSet<int> lanes;
        private readonly bool blockBad;
        private readonly bool blockMiss;

        public int EnemyTurnsRemaining { get; private set; }
        public IReadOnlyCollection<int> Lanes => lanes;
        public bool IsExpired => EnemyTurnsRemaining <= 0;

        public LaneWardModifier(HashSet<int> lanes, int enemyTurns, bool blockBad = true, bool blockMiss = true)
        {
            this.lanes = lanes;
            EnemyTurnsRemaining = enemyTurns;
            this.blockBad = blockBad;
            this.blockMiss = blockMiss;
        }

        public bool Covers(int laneId) => lanes == null || lanes.Contains(laneId);

        public bool BlocksDamage(RhythmJudgementResult result)
        {
            if (IsExpired || !Covers(result.LaneId)) return false;
            return result.Judgement switch
            {
                HitJudgement.Bad => blockBad,
                HitJudgement.Miss => blockMiss,
                _ => false
            };
        }

        public void OnEnemyTurnStarted() { }
        public void OnEnemyTurnEnded() => EnemyTurnsRemaining--;
        public void OnJudgementResolved(RhythmJudgementResult result, RhythmPatternRunner runner) { }
    }
}
