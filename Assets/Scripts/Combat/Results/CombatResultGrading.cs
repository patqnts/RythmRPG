using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>One grade letter and what it takes to earn it.</summary>
    [Serializable]
    public sealed class CombatGrade
    {
        public string label = "S";
        [Tooltip("Lowest grade value (0-1) that earns this grade. See Accuracy Weight / Health Weight.")]
        [Range(0f, 1f)] public float minimumValue = 0.9f;
        [Tooltip("Only with no combo breaks.")]
        public bool requireFullCombo;
        [Tooltip("Only when the battle was won.")]
        public bool requireVictory = true;
        public Color color = Color.white;
        [Tooltip("Short line shown under the grade (optional).")]
        public string comment = string.Empty;
    }

    /// <summary>
    /// How a battle is scored and graded. Everything is data: judgement weights, points, combo bonus, and the grade
    /// ladder. Loaded from Resources/Combat/UI/CombatResultGrading; missing = the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Result Grading", fileName = "CombatResultGrading")]
    public sealed class CombatResultGrading : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/CombatResultGrading";

        [Header("Accuracy (0-1 per note)")]
        [SerializeField, Range(0f, 1f)] private float perfectWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float goodWeight = 0.75f;
        [SerializeField, Range(0f, 1f)] private float badWeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float missWeight;

        [Header("Score (points per note)")]
        [SerializeField, Min(0)] private int perfectPoints = 300;
        [SerializeField, Min(0)] private int goodPoints = 200;
        [SerializeField, Min(0)] private int badPoints = 50;
        [SerializeField, Min(0)] private int missPoints;
        [Tooltip("Extra points per combo step, as a fraction of the note's points (0.01 = +1% per combo).")]
        [SerializeField, Min(0f)] private float comboBonusPerStep = 0.01f;
        [Tooltip("Combo count where the bonus stops growing.")]
        [SerializeField, Min(0)] private int comboBonusCap = 50;
        [SerializeField, Min(0)] private int victoryBonus = 10000;
        [Tooltip("Points for health left at the end, at full health.")]
        [SerializeField, Min(0)] private int healthBonus = 5000;

        [Header("Combo")]
        [Tooltip("A Bad hit also breaks the combo (a Miss always does).")]
        [SerializeField] private bool badBreaksCombo;

        [Header("Grade value = accuracy x Accuracy Weight + health left x Health Weight")]
        [SerializeField, Min(0f)] private float accuracyWeight = 0.85f;
        [SerializeField, Min(0f)] private float healthWeight = 0.15f;

        [Header("Grades (checked top to bottom, first match wins)")]
        [SerializeField] private List<CombatGrade> grades = new()
        {
            new CombatGrade { label = "SS", minimumValue = 0.97f, requireFullCombo = true, color = new Color(1f, 0.93f, 0.45f), comment = "FLAWLESS" },
            new CombatGrade { label = "S", minimumValue = 0.92f, color = new Color(1f, 0.82f, 0.25f), comment = "SUPERB" },
            new CombatGrade { label = "A", minimumValue = 0.85f, color = new Color(0.45f, 1f, 0.55f), comment = "GREAT" },
            new CombatGrade { label = "B", minimumValue = 0.75f, color = new Color(0.4f, 0.75f, 1f), comment = "GOOD" },
            new CombatGrade { label = "C", minimumValue = 0.6f, requireVictory = false, color = new Color(0.8f, 0.6f, 1f), comment = "OKAY" },
            new CombatGrade { label = "D", minimumValue = 0f, requireVictory = false, color = new Color(1f, 0.4f, 0.4f), comment = "KEEP PRACTICING" }
        };

        public bool BadBreaksCombo => badBreaksCombo;
        public IReadOnlyList<CombatGrade> Grades => grades;

        public float Weight(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectWeight,
            HitJudgement.Good => goodWeight,
            HitJudgement.Bad => badWeight,
            _ => missWeight
        };

        public int Points(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectPoints,
            HitJudgement.Good => goodPoints,
            HitJudgement.Bad => badPoints,
            _ => missPoints
        };

        public bool BreaksCombo(HitJudgement judgement) => judgement == HitJudgement.Miss || (badBreaksCombo && judgement == HitJudgement.Bad);

        /// <summary>Points for one note hit with the given combo (after this hit).</summary>
        public double NotePoints(HitJudgement judgement, int combo) =>
            Points(judgement) * (1d + Mathf.Min(combo, comboBonusCap) * comboBonusPerStep);

        public int EndBonus(bool victory, float healthLeft) =>
            (victory ? victoryBonus : 0) + Mathf.RoundToInt(healthBonus * Mathf.Clamp01(healthLeft));

        /// <summary>Weighted accuracy for judgement counts indexed by (int)HitJudgement. No notes = 1.</summary>
        public float Accuracy(int[] counts)
        {
            int total = 0;
            float sum = 0f;
            for (int i = 0; i < counts.Length; i++)
            {
                total += counts[i];
                sum += counts[i] * Weight((HitJudgement)i);
            }
            return total == 0 ? 1f : sum / total;
        }

        public float GradeValue(float accuracy, float healthLeft)
        {
            float totalWeight = accuracyWeight + healthWeight;
            if (totalWeight <= 0f) return accuracy;
            return Mathf.Clamp01((accuracy * accuracyWeight + Mathf.Clamp01(healthLeft) * healthWeight) / totalWeight);
        }

        public CombatGrade GradeFor(float value, bool fullCombo, bool victory)
        {
            foreach (CombatGrade grade in grades)
            {
                if (grade == null || value < grade.minimumValue) continue;
                if (grade.requireFullCombo && !fullCombo) continue;
                if (grade.requireVictory && !victory) continue;
                return grade;
            }
            return grades.Count > 0 ? grades[grades.Count - 1] : new CombatGrade { label = "-", minimumValue = 0f };
        }

        private static CombatResultGrading fallback;

        public static CombatResultGrading LoadOrDefault()
        {
            CombatResultGrading grading = Resources.Load<CombatResultGrading>(ResourcePath);
            if (grading != null) return grading;
            if (fallback == null)
            {
                fallback = CreateInstance<CombatResultGrading>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
