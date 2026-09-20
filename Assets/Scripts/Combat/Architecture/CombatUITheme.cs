using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/UI Theme", fileName = "CombatUITheme")]
    public sealed class CombatUITheme : ScriptableObject
    {
        [SerializeField] private string missText = "MISS";
        [SerializeField] private string badText = "BAD";
        [SerializeField] private string goodText = "GOOD";
        [SerializeField] private string perfectText = "PERFECT";
        [SerializeField] private Color missColor = new(1f, 0.2f, 0.35f);
        [SerializeField] private Color badColor = new(1f, 0.5f, 0.12f);
        [SerializeField] private Color goodColor = new(0.2f, 1f, 0.75f);
        [SerializeField] private Color perfectColor = new(1f, 0.84f, 0.2f);

        public string GetText(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Bad => badText,
            HitJudgement.Good => goodText,
            HitJudgement.Perfect => perfectText,
            _ => missText
        };

        public Color GetColor(HitJudgement judgement) => judgement switch
        {
            HitJudgement.Bad => badColor,
            HitJudgement.Good => goodColor,
            HitJudgement.Perfect => perfectColor,
            _ => missColor
        };
    }
}
