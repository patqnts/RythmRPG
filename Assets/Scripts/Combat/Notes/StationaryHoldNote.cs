using RythmRPG.Combat;
using UnityEngine;

public class StationaryHoldNote : StationaryNote
{
    [Header("Stationary Hold")]
    [SerializeField] private bool damagePlayerOnEarlyRelease = true;
    public float length;

    private bool isHolding;
    private bool earlyReleased;
    private float holdTimer;

    protected override bool ResolveImmediatelyOnPress => false;

    protected override void Update()
    {
        base.Update();
        if (!isHolding || IsResolved)
        {
            return;
        }

        // While a chart runs the hold ends at the authored EndTime (song seconds), as placed in the composer.
        bool done = UsesChartClock
            ? ChartSeconds >= Data.EndTime
            : (holdTimer += Time.deltaTime) >= GetHoldDuration();
        if (done)
        {
            CompleteHold();
        }
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        isHolding = true;
        holdTimer = 0f;
        EnterHoldPhase();
    }

    public override bool IsUsingKey(KeyButton keyButton)
    {
        return isHolding && base.IsUsingKey(keyButton);
    }

    public override void OnKeyReleased(KeyButton keyButton)
    {
        if (!isHolding || IsResolved)
        {
            return;
        }

        earlyReleased = true;
        isHolding = false;
        PlayEndPhase(true);
        RhythmJudgementResult press = GetPressJudgement();
        ForceResolve(new RhythmJudgementResult(press.NoteId, press.LaneId, HitJudgement.Bad,
            press.Distance, press.WorldPosition, NoteResolutionSource.PlayerInput));
    }

    public override bool ShouldDamagePlayerOnResolve(RhythmJudgementResult result)
    {
        return base.ShouldDamagePlayerOnResolve(result)
            || (damagePlayerOnEarlyRelease && earlyReleased && result.Judgement == HitJudgement.Bad);
    }

    private void CompleteHold()
    {
        if (!isHolding || IsResolved)
        {
            return;
        }

        isHolding = false;
        PlayEndPhase(false);
        ForceResolve(ClampCompletedHoldJudgement(GetPressJudgement()));
    }

    private float GetHoldDuration()
    {
        if (Data != null && Data.HoldDuration > 0d)
        {
            return Mathf.Max(0.01f, (float)Data.HoldDuration);
        }

        return Mathf.Max(0.01f, length / Mathf.Max(0.01f, speed));
    }
}
