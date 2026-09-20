using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Judgement Config", fileName = "JudgementConfig")]
    public sealed class JudgementConfig : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float perfectWindow = 0.15f;
        [SerializeField, Min(0.01f)] private float goodWindow = 0.45f;
        [SerializeField, Min(0.01f)] private float badWindow = 0.9f;

        public float PerfectWindow => perfectWindow;
        public float GoodWindow => goodWindow;
        public float BadWindow => badWindow;

        public HitJudgement Evaluate(float distance)
        {
            float absoluteDistance = Mathf.Abs(distance);
            if (absoluteDistance <= perfectWindow) return HitJudgement.Perfect;
            if (absoluteDistance <= goodWindow) return HitJudgement.Good;
            if (absoluteDistance <= badWindow) return HitJudgement.Bad;
            return HitJudgement.Miss;
        }

        private void OnValidate()
        {
            perfectWindow = Mathf.Max(0.01f, perfectWindow);
            goodWindow = Mathf.Max(perfectWindow, goodWindow);
            badWindow = Mathf.Max(goodWindow, badWindow);
        }
    }
}
