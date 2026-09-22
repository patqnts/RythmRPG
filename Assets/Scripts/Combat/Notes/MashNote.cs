using RythmRPG.Combat;
using RythmRPG.Rhythm;
using UnityEngine;

/// <summary>
/// Moving note the player destroys by pressing its lane key repeatedly before it reaches the hit line.
/// Clearing (required presses reached) resolves it immediately, graded by how early the clear happened
/// (see <see cref="MashRules.GradeClear(double, double)"/>); a final press while the note sits on the hit line
/// still counts as Perfect. If it is not cleared by the time the short line grace ends, it is a miss and deals
/// its damage like any unhit moving note.
/// </summary>
public class MashNote : NoteObject
{
    [Header("Mash")]
    [SerializeField, Min(1)] private int fallbackRequiredPresses = MashRules.DefaultRequiredPresses;
    [Tooltip("Clear within this share of the travel time (spawn to hit line) for Perfect.")]
    [SerializeField, Range(0f, 1f)] private float perfectProgress = MashRules.DefaultPerfectProgress;
    [Tooltip("Clear within this share of the travel time for Good; later but before the line is Bad.")]
    [SerializeField, Range(0f, 1f)] private float goodProgress = MashRules.DefaultGoodProgress;
    [SerializeField] private Color mashTint = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Optional: scaled on Y from 0 to 1 as presses accumulate.")]
    [SerializeField] private Transform meterVisual;
    [SerializeField, Min(0f)] private float pressPulseScale = 0.25f;
    [SerializeField, Min(0.01f)] private float pressPulseDecay = 6f;

    private int presses;
    private int requiredPresses = MashRules.DefaultRequiredPresses;
    private float travelSeconds = 2.5f;
    private float spawnedAt;
    private bool landed;
    private bool finished;
    private float pulse;
    private Vector3 baseScale = Vector3.one;

    public int Presses => presses;
    public int RequiredPresses => requiredPresses;

    // Negative until the note reaches the hit line. Locked to the chart (audio) clock while a chart runs;
    // falls back to spawn-relative time otherwise.
    private float SecondsIntoWindow => UsesChartClock
        ? (float)(ChartSeconds - Data.HitTime)
        : Time.time - spawnedAt - travelSeconds;
    private bool Pressable => !finished && SecondsIntoWindow <= MashRules.LineGraceSeconds;

    public override bool ShouldAutoMissByPosition => false;
    protected override bool ResolveImmediatelyOnPress => false;

    public override void Initialize(RhythmNoteSpawnContext context)
    {
        base.Initialize(context);
        spawnedAt = Time.time;
        presses = 0;
        landed = false;
        finished = false;
        pulse = 0f;
        baseScale = transform.localScale;

        int fromData = context.NoteData != null ? context.NoteData.MashRequiredPresses : 0;
        requiredPresses = fromData > 0 ? fromData : Mathf.Max(1, fallbackRequiredPresses);
        travelSeconds = Mathf.Max(0.01f, (float)(context.NoteData != null ? context.NoteData.TravelTime : 2.5d));

        SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null) sprite.color = mashTint;
        UpdateMeter();
    }

    public override void Update()
    {
        if (IsResolved || finished) return;

        if (pulse > 0f)
        {
            pulse = Mathf.Max(0f, pulse - pressPulseDecay * Time.deltaTime);
            transform.localScale = baseScale * (1f + pressPulseScale * pulse);
        }

        if (!landed)
        {
            if (SecondsIntoWindow < 0f)
            {
                base.Update(); // normal travel toward the hit line
                return;
            }

            Land();
        }

        if (SecondsIntoWindow >= MashRules.LineGraceSeconds)
        {
            Fail();
        }
    }

    private void Land()
    {
        landed = true;
        isMoving = false;
        StopMovementTweens();
        RhythmLaneTarget target = GetLaneTarget();
        if (target != null) transform.position = target.transform.position;
    }

    public override Vector3 GetJudgementWorldPosition()
    {
        RhythmLaneTarget target = GetLaneTarget();
        return target != null ? target.transform.position : transform.position;
    }

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return isActiveAndEnabled && !IsResolved && Pressable
            && keyButton != null && keyButton.GetInteractable() && keyButton.keyIdentity == GetNoteIdentity();
    }

    // Real distance to the line, so a normal note that is nearer the line still wins the press over a distant Mash.
    public override float GetTimingError(KeyButton keyButton)
    {
        RhythmLaneTarget target = GetLaneTarget();
        return target != null ? DistanceToSeconds(target.GetTimingDistance(transform.position)) : 0f;
    }

    // Any accepted press is fine for the runner; the real grade is decided when the note is cleared.
    public override HitJudgement AdjustJudgement(HitJudgement judgement, float timingError)
    {
        return Pressable ? HitJudgement.Perfect : HitJudgement.Miss;
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        if (finished || IsResolved) return;
        presses++;
        pulse = 1f;
        UpdateMeter();
        if (presses >= requiredPresses) Clear();
    }

    public override bool IsUsingKey(KeyButton keyButton)
    {
        return false; // presses are discrete; nothing to release
    }

    private void UpdateMeter()
    {
        if (meterVisual == null) return;
        Vector3 scale = meterVisual.localScale;
        scale.y = Mathf.Clamp01(presses / (float)Mathf.Max(1, requiredPresses));
        meterVisual.localScale = scale;
    }

    private void Clear()
    {
        if (finished || IsResolved) return;
        finished = true;
        transform.localScale = baseScale;

        MashGrade grade = MashRules.GradeClear(-SecondsIntoWindow, travelSeconds, perfectProgress, goodProgress);
        HitJudgement judgement;
        switch (grade)
        {
            case MashGrade.Perfect: judgement = HitJudgement.Perfect; break;
            case MashGrade.Good: judgement = HitJudgement.Good; break;
            case MashGrade.Bad: judgement = HitJudgement.Bad; break;
            default: judgement = HitJudgement.Miss; break;
        }

        ForceResolve(new RhythmJudgementResult(RuntimeNoteId, GetNoteIdentity(), judgement, 0f,
            GetJudgementWorldPosition(), NoteResolutionSource.PlayerInput));
    }

    private void Fail()
    {
        if (finished || IsResolved) return;
        finished = true;
        transform.localScale = baseScale;
        NoteResolutionSource source = presses > 0 ? NoteResolutionSource.PlayerInput : NoteResolutionSource.Timeout;
        ForceResolve(new RhythmJudgementResult(RuntimeNoteId, GetNoteIdentity(), HitJudgement.Miss, 0f,
            GetJudgementWorldPosition(), source));
    }
}
