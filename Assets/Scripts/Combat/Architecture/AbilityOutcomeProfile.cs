using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability Outcome Profile", fileName = "AbilityOutcomeProfile")]
    public sealed class AbilityOutcomeProfile : ScriptableObject
    {
        [SerializeField, Range(0f, 2f)] private float missWeight;
        [SerializeField, Range(0f, 2f)] private float badWeight = 0.5f;
        [SerializeField, Range(0f, 2f)] private float goodWeight = 0.8f;
        [SerializeField, Range(0f, 2f)] private float perfectWeight = 1f;

        public float GetWeight(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Bad => badWeight,
            HitJudgement.Good => goodWeight,
            HitJudgement.Perfect => perfectWeight,
            _ => missWeight
        };
    }
}
