using UnityEngine;

public enum HitJudgement
{
    Miss,
    Bad,
    Good,
    Perfect
}

public readonly struct RhythmJudgementResult
{
    public readonly HitJudgement judgement;
    public readonly float timingError;
    public readonly int combo;
    public readonly Vector3 worldPosition;
    public readonly KeyButton keyButton;

    public RhythmJudgementResult(
        HitJudgement judgement,
        float timingError,
        int combo,
        Vector3 worldPosition,
        KeyButton keyButton)
    {
        this.judgement = judgement;
        this.timingError = timingError;
        this.combo = combo;
        this.worldPosition = worldPosition;
        this.keyButton = keyButton;
    }
}
