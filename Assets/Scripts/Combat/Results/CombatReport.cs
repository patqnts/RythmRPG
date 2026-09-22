using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Every value the result screen can show. Pick and order them in the CombatResultStyle.</summary>
    public enum CombatStat
    {
        Perfect,
        Good,
        Bad,
        Miss,
        MaxCombo,
        Accuracy,
        Score,
        TotalNotes,
        DamageDealt,
        DamageTaken,
        Healed,
        ManaSpent,
        Turns,
        BattleTime,
        AbilitiesUsed,
        MostUsedAbility,
        Guards,
        HealthLeft,
        DefenseAccuracy,
        AbilityAccuracy
    }

    /// <summary>The outcome of one battle: judgement counts, combo, score, grade and combat totals.</summary>
    public sealed class CombatReport
    {
        public bool Victory;
        public string EnemyName = string.Empty;

        // Judgement counts, indexed by (int)HitJudgement (Miss, Bad, Good, Perfect).
        public readonly int[] DefenseJudgements = new int[4];
        public readonly int[] AbilityJudgements = new int[4];

        public int MaxCombo;
        public long Score;
        public float Accuracy;          // 0-1, weighted by the grading's judgement weights
        public float DefenseAccuracy;
        public float AbilityAccuracy;
        public float GradeValue;        // 0-1, what the grade thresholds are compared with
        public CombatGrade Grade;
        public bool FullCombo;
        public bool AllPerfect;

        public int DamageDealt;
        public int DamageTaken;
        public int Healed;
        public int ManaSpent;
        public int Turns;
        public int Guards;
        public float BattleSeconds;
        public float HealthLeft;        // 0-1 of max health at the end (before a defeat restores it)
        public readonly Dictionary<string, int> AbilityUses = new();

        public int Count(HitJudgement judgement) => DefenseJudgements[(int)judgement] + AbilityJudgements[(int)judgement];
        public int TotalNotes => DefenseJudgements.Sum() + AbilityJudgements.Sum();
        public int AbilitiesUsed => AbilityUses.Values.Sum();

        public string MostUsedAbility => AbilityUses.Count == 0
            ? "-"
            : AbilityUses.OrderByDescending(pair => pair.Value).First().Key;

        /// <summary>True when the stat is a number that can count up; false for text (e.g. an ability name).</summary>
        public static bool IsNumeric(CombatStat stat) => stat != CombatStat.MostUsedAbility;

        public float GetNumber(CombatStat stat) => stat switch
        {
            CombatStat.Perfect => Count(HitJudgement.Perfect),
            CombatStat.Good => Count(HitJudgement.Good),
            CombatStat.Bad => Count(HitJudgement.Bad),
            CombatStat.Miss => Count(HitJudgement.Miss),
            CombatStat.MaxCombo => MaxCombo,
            CombatStat.Accuracy => Accuracy,
            CombatStat.Score => Score,
            CombatStat.TotalNotes => TotalNotes,
            CombatStat.DamageDealt => DamageDealt,
            CombatStat.DamageTaken => DamageTaken,
            CombatStat.Healed => Healed,
            CombatStat.ManaSpent => ManaSpent,
            CombatStat.Turns => Turns,
            CombatStat.BattleTime => BattleSeconds,
            CombatStat.AbilitiesUsed => AbilitiesUsed,
            CombatStat.Guards => Guards,
            CombatStat.HealthLeft => HealthLeft,
            CombatStat.DefenseAccuracy => DefenseAccuracy,
            CombatStat.AbilityAccuracy => AbilityAccuracy,
            _ => 0f
        };

        /// <summary>The stat as display text. fraction (0-1) is how far a count-up has got.</summary>
        public string Format(CombatStat stat, float fraction = 1f)
        {
            if (!IsNumeric(stat)) return MostUsedAbility;
            float value = GetNumber(stat) * Mathf.Clamp01(fraction);
            switch (stat)
            {
                case CombatStat.Accuracy:
                case CombatStat.DefenseAccuracy:
                case CombatStat.AbilityAccuracy:
                case CombatStat.HealthLeft:
                    return (value * 100f).ToString("0.00") + "%";
                case CombatStat.BattleTime:
                    int seconds = Mathf.FloorToInt(value);
                    return $"{seconds / 60}:{seconds % 60:00}";
                case CombatStat.MaxCombo:
                    return Mathf.RoundToInt(value) + "x";
                default:
                    return Mathf.RoundToInt(value).ToString("N0");
            }
        }
    }
}
