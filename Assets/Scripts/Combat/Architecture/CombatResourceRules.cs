using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Balance knobs for player health and mana, weighted by judgement. Loaded from
    /// Resources/Combat/Balance/CombatResourceRules (defaults are used when the asset is missing).
    /// See the "Combat balance" project doc for the reasoning behind the numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Combat Resource Rules", fileName = "CombatResourceRules")]
    public sealed class CombatResourceRules : ScriptableObject
    {
        public const string ResourcePath = "Combat/Balance/CombatResourceRules";

        [Header("Damage taken on the enemy turn (x the note's Damage)")]
        [SerializeField, Min(0f)] private float missDamage = 1f;
        [SerializeField, Min(0f)] private float badDamage = 0.5f;
        [SerializeField, Min(0f)] private float goodDamage;
        [SerializeField, Min(0f)] private float perfectDamage;

        [Header("Mana source")]
        [Tooltip("Turn Accuracy: mana is paid once when the enemy turn ends, by accuracy tier, so denser patterns do not " +
                 "give more mana. Per Hit: every Perfect / Good hit gives mana (the weights below).")]
        [SerializeField] private ManaSource manaSource = ManaSource.TurnAccuracy;

        [Header("Turn Accuracy mana (paid at the end of the enemy turn)")]
        [Tooltip("How much each judgement counts toward accuracy (Miss = 0). Accuracy = average over the turn's notes.")]
        [SerializeField, Range(0f, 1f)] private float perfectAccuracy = 1f;
        [SerializeField, Range(0f, 1f)] private float goodAccuracy = 0.7f;
        [SerializeField, Range(0f, 1f)] private float badAccuracy = 0.3f;
        [Tooltip("Mana paid for the best tier the turn's accuracy reaches. Order does not matter.")]
        [SerializeField] private ManaAccuracyTier[] accuracyTiers = DefaultTiers();
        [Tooltip("Mana paid when no tier is reached (a turn with no notes pays nothing).")]
        [SerializeField, Min(0)] private int manaBelowTiers = 5;

        [Header("Per Hit mana (only when Mana Source = Per Hit)")]
        [SerializeField, Min(0f)] private float perfectMana = 1f;
        [SerializeField, Min(0f)] private float goodMana = 0.5f;
        [SerializeField, Min(0f)] private float badMana;
        [Tooltip("Multiplier for mana gained while playing your own ability charts (the enemy turn is the main source).")]
        [SerializeField, Range(0f, 2f)] private float abilityChartManaScale = 0.5f;
        [Tooltip("Mana restored at the start of each player turn, regardless of play.")]
        [SerializeField, Min(0)] private int manaPerPlayerTurn = 5;

        [Header("Defensive abilities")]
        [Tooltip("Below this ability performance (0-1 average judgement weight) a defensive effect fails.")]
        [SerializeField, Range(0f, 1f)] private float defensiveMinimumPerformance = 0.25f;

        private static CombatResourceRules fallback;

        public int ManaPerPlayerTurn => manaPerPlayerTurn;
        public ManaSource Source => manaSource;
        public bool UsesTurnAccuracy => manaSource == ManaSource.TurnAccuracy;
        public float DefensiveMinimumPerformance => defensiveMinimumPerformance;

        public static CombatResourceRules Load()
        {
            CombatResourceRules rules = Resources.Load<CombatResourceRules>(ResourcePath);
            if (rules != null) return rules;
            if (fallback == null) fallback = CreateInstance<CombatResourceRules>();
            return fallback;
        }

        public float DamageMultiplier(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectDamage,
            HitJudgement.Good => goodDamage,
            HitJudgement.Bad => badDamage,
            _ => missDamage
        };

        public int DefenseDamage(int noteDamage, HitJudgement judgement) =>
            Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0, noteDamage) * DamageMultiplier(judgement)));

        public float ManaGain(HitJudgement judgement, PatternRunMode mode)
        {
            float gain = judgement switch
            {
                HitJudgement.Perfect => perfectMana,
                HitJudgement.Good => goodMana,
                HitJudgement.Bad => badMana,
                _ => 0f
            };
            return mode == PatternRunMode.PlayerAbility ? gain * abilityChartManaScale : gain;
        }

        /// <summary>How much a judgement counts toward turn accuracy (0-1).</summary>
        public float AccuracyWeight(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Perfect => perfectAccuracy,
            HitJudgement.Good => goodAccuracy,
            HitJudgement.Bad => badAccuracy,
            _ => 0f
        };

        /// <summary>Mana for a finished enemy turn at this accuracy (0-1): the best tier reached.</summary>
        public int TurnAccuracyMana(float accuracy, int notes)
        {
            if (notes <= 0) return 0;
            int best = manaBelowTiers;
            float bestThreshold = float.NegativeInfinity;
            if (accuracyTiers != null)
                foreach (ManaAccuracyTier tier in accuracyTiers)
                    if (accuracy + 0.0001f >= tier.minAccuracy && tier.minAccuracy > bestThreshold)
                    {
                        bestThreshold = tier.minAccuracy;
                        best = tier.mana;
                    }
            return Mathf.Max(0, best);
        }

        /// <summary>For the HUD: the next tier above this accuracy, or false at the top.</summary>
        public bool NextTier(float accuracy, out ManaAccuracyTier next)
        {
            next = default;
            bool found = false;
            if (accuracyTiers == null) return false;
            foreach (ManaAccuracyTier tier in accuracyTiers)
                if (tier.minAccuracy > accuracy + 0.0001f && (!found || tier.minAccuracy < next.minAccuracy))
                {
                    next = tier;
                    found = true;
                }
            return found;
        }

        private static ManaAccuracyTier[] DefaultTiers() => new[]
        {
            new ManaAccuracyTier(0.95f, 25),
            new ManaAccuracyTier(0.85f, 18),
            new ManaAccuracyTier(0.70f, 12)
        };
    }

    public enum ManaSource
    {
        TurnAccuracy,
        PerHit
    }

    [System.Serializable]
    public struct ManaAccuracyTier
    {
        [Range(0f, 1f)] public float minAccuracy;
        [Min(0)] public int mana;

        public ManaAccuracyTier(float minAccuracy, int mana)
        {
            this.minAccuracy = minAccuracy;
            this.mana = mana;
        }
    }

    /// <summary>
    /// Running accuracy of one enemy turn: every note the player had to play (hits and timeouts; zapped / walled notes
    /// and notes cleared by the system do not count).
    /// </summary>
    public sealed class TurnAccuracyTally
    {
        private float score;

        public int Notes { get; private set; }
        public float Accuracy => Notes > 0 ? score / Notes : 0f;

        public void Reset()
        {
            score = 0f;
            Notes = 0;
        }

        public void Record(float weight)
        {
            score += Mathf.Clamp01(weight);
            Notes++;
        }

        public static bool Counts(NoteResolutionSource source) =>
            source == NoteResolutionSource.PlayerInput || source == NoteResolutionSource.Timeout;
    }
}
