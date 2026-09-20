using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class RhythmJudgementSystem : MonoBehaviour
    {
        private RhythmPatternRunner runner;
        public event Action<RhythmJudgementResult> OnNoteHit;
        public event Action<RhythmJudgementResult> OnNoteMiss;
        public event Action<RhythmJudgementResult> OnJudgementResolved;
        public event Action<RhythmJudgementResult> OnPerfectHit;

        public void Bind(RhythmPatternRunner patternRunner)
        {
            if (runner != null) runner.NoteResolved -= HandleResolved;
            runner = patternRunner;
            if (runner != null) runner.NoteResolved += HandleResolved;
        }

        private void OnDisable()
        {
            if (runner != null) runner.NoteResolved -= HandleResolved;
        }

        private void HandleResolved(RhythmJudgementResult result)
        {
            OnJudgementResolved?.Invoke(result);
            if (result.Judgement == HitJudgement.Miss) OnNoteMiss?.Invoke(result);
            else OnNoteHit?.Invoke(result);
            if (result.Judgement == HitJudgement.Perfect) OnPerfectHit?.Invoke(result);
        }
    }
}
