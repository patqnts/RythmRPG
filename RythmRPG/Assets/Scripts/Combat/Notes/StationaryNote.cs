using RythmRPG.Combat;
using UnityEngine;
using UnityEngine.Serialization;

public class StationaryNote : Note
{
    [Header("Stationary Timing")]
    [FormerlySerializedAs("alignToKeyPosition")]
    [SerializeField] private bool alignToLaneX = true;
    [FormerlySerializedAs("keyPositionOffset")]
    [SerializeField] private Vector3 lanePositionOffset;
    [FormerlySerializedAs("hitGraceDistance")]
    [SerializeField, Min(0f)] private float badWindow = 0.9f;
    [SerializeField, Min(0f)] private float goodWindow = 0.45f;
    [SerializeField, Min(0f)] private float perfectWindow = 0.15f;

    [Header("Stationary Visuals")]
    [SerializeField] private Transform anticipationRootVisual;
    [SerializeField] private Transform anticipationFillVisual;
    [SerializeField] private Transform hitWindowVisual;
    [SerializeField] private Transform holdVisual;
    [SerializeField] private Transform endVisual;
    [SerializeField] private Vector3 anticipationStartScale = Vector3.one * 0.15f;
    [SerializeField] private Vector3 anticipationEndScale = Vector3.one;
    [SerializeField, Min(0f)] private float destroyDelay = 0.25f;

    [Header("Animation States")]
    [SerializeField] private string anticipationStateName = "Anticipation";
    [SerializeField] private string hitWindowStateName = "HitWindow";
    [SerializeField] private string holdStateName = "Hold";
    [SerializeField] private string endStateName = "End";
    [SerializeField] private string missStateName = "Miss";

    private float spawnedAtTime;
    private float anticipationDuration = 0.01f;
    private bool ending;
    private bool desiredAnticipationVisible;
    private bool desiredHitWindowVisible;
    private bool desiredHoldVisible;
    private bool desiredEndVisible;

    public override bool ShouldAutoMissByPosition => false;
    public override bool ShouldResolveMissOnPlayerInput => true;
    protected float ElapsedSinceSpawn => Time.time - spawnedAtTime;
    protected float SecondsUntilAnticipationComplete => anticipationDuration - ElapsedSinceSpawn;
    protected bool IsInsideAnticipationWindow => ElapsedSinceSpawn <= anticipationDuration;
    protected bool HasPassedAnticipationWindow => ElapsedSinceSpawn > anticipationDuration;

    public override void Initialize(RhythmNoteSpawnContext context)
    {
        base.Initialize(context);
        spawnedAtTime = Time.time;
        anticipationDuration = Mathf.Max(0.01f, (float)(context.NoteData?.TravelTime ?? 0.01d));
        if (context.NoteData != null)
        {
            badWindow = Mathf.Max(0f, context.NoteData.StationaryBadWindow);
            goodWindow = Mathf.Max(0f, context.NoteData.StationaryGoodWindow);
            perfectWindow = Mathf.Max(0f, context.NoteData.StationaryPerfectWindow);
        }

        keys = context.Keys;
        AlignToKey();
        ResolveVisualTemplateReferences();
        SetPhaseVisuals(showAnticipation: true, showHitWindow: false, showHold: false, showEnd: false);
        PlayState(anticipationStateName, "Charge");
        UpdateAnticipationVisual();
    }

    protected virtual void Update()
    {
        if (IsResolved || ending)
        {
            return;
        }

        UpdateAnticipationVisual();
        if (!hitAccepted && HasPassedAnticipationWindow)
        {
            ForceMiss(GetIdentityButton());
        }
    }

    protected virtual void LateUpdate()
    {
        ApplyPhaseVisuals();
        if (desiredAnticipationVisible)
        {
            UpdateAnticipationVisual();
        }
    }

    public override float GetTimingError(KeyButton keyButton)
    {
        return Mathf.Abs(SecondsUntilAnticipationComplete);
    }

    public override HitJudgement AdjustJudgement(HitJudgement judgement, float timingError)
    {
        if (HasPassedAnticipationWindow) return HitJudgement.Miss;
        if (timingError <= perfectWindow) return HitJudgement.Perfect;
        if (timingError <= goodWindow) return HitJudgement.Good;
        if (timingError <= badWindow) return HitJudgement.Bad;
        return HitJudgement.Miss;
    }

    public override Vector3 GetJudgementWorldPosition()
    {
        KeyButton key = GetIdentityButton();
        return key != null ? key.transform.position : transform.position;
    }

    protected override bool IsWithinPressWindow(KeyButton keyButton)
    {
        return IsInsideAnticipationWindow;
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        PlayEndPhase(result.Judgement == HitJudgement.Miss);
        base.OnHit(keyButton, result);
    }

    public override void DestroyObject()
    {
        canBePressed = false;
        isMoving = false;
        PlayEndPhase(!hitAccepted);
        StopMovementTweens();
        Destroy(gameObject, Mathf.Max(0f, destroyDelay));
    }

    protected void EnterHoldPhase()
    {
        SetPhaseVisuals(showAnticipation: false, showHitWindow: false, showHold: true, showEnd: false);
        PlayState(holdStateName);
    }

    protected void PlayEndPhase(bool missed)
    {
        if (ending) return;
        ending = true;
        SetPhaseVisuals(showAnticipation: false, showHitWindow: false, showHold: false, showEnd: true);
        PlayState(missed ? missStateName : endStateName);
    }

    protected RhythmJudgementResult ClampCompletedHoldJudgement(RhythmJudgementResult pressResult)
    {
        HitJudgement finalJudgement = pressResult.Judgement == HitJudgement.Perfect
            ? HitJudgement.Perfect
            : HitJudgement.Good;
        return new RhythmJudgementResult(pressResult.NoteId, pressResult.LaneId, finalJudgement,
            pressResult.Distance, pressResult.WorldPosition, pressResult.Source);
    }

    private void AlignToKey()
    {
        if (!alignToLaneX) return;
        KeyButton key = GetIdentityButton();
        if (key == null) return;

        Vector3 position = transform.position;
        position.x = key.transform.position.x;
        position += lanePositionOffset;
        transform.position = position;
    }

    private void ResolveVisualTemplateReferences()
    {
        anticipationRootVisual ??= FindChildByName("Anticipator", "Anticipation Root", "Charge Root", "Anticipation", "Charge");
        anticipationFillVisual ??= FindChildByName("Anticipation Fill", "Charge Fill", "Fill", "FIll");
        hitWindowVisual ??= FindChildByName("Hit Window", "HitWindow", "Window");
        holdVisual ??= FindChildByName("Hold", "Hold Visual");
        endVisual ??= FindChildByName("End", "End Visual", "Release", "Miss");
    }

    private void UpdateAnticipationVisual()
    {
        if (anticipationFillVisual == null) return;
        float t = Mathf.Clamp01(ElapsedSinceSpawn / Mathf.Max(0.01f, anticipationDuration));
        anticipationFillVisual.localScale = Vector3.LerpUnclamped(anticipationStartScale, anticipationEndScale, t);
    }

    private void SetPhaseVisuals(bool showAnticipation, bool showHitWindow, bool showHold, bool showEnd)
    {
        desiredAnticipationVisible = showAnticipation;
        desiredHitWindowVisible = showHitWindow;
        desiredHoldVisible = showHold;
        desiredEndVisible = showEnd;
        ApplyPhaseVisuals();
    }

    private void ApplyPhaseVisuals()
    {
        SetVisualActive(anticipationRootVisual, desiredAnticipationVisible);
        if (anticipationFillVisual != null) anticipationFillVisual.gameObject.SetActive(desiredAnticipationVisible);
        SetVisualActive(hitWindowVisual, desiredHitWindowVisible);
        SetVisualActive(holdVisual, desiredHoldVisible);
        SetVisualActive(endVisual, desiredEndVisible);
    }

    private void PlayState(string stateName, string fallbackStateName = null)
    {
        if (animator == null) return;
        if (TryPlayState(stateName)) return;
        TryPlayState(fallbackStateName);
    }

    private bool TryPlayState(string stateName)
    {
        if (animator == null || string.IsNullOrWhiteSpace(stateName)) return false;
        int hash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, hash)) return false;
        animator.Play(hash, 0, 0f);
        return true;
    }

    private void SetVisualActive(Transform visual, bool active)
    {
        if (visual == null || visual == transform) return;
        visual.gameObject.SetActive(active);
    }

    private Transform FindChildByName(params string[] names)
    {
        if (names == null || names.Length == 0) return null;
        Transform[] children = GetComponentsInChildren<Transform>(true);
        foreach (string candidateName in names)
        {
            foreach (Transform child in children)
            {
                if (child != null && child != transform && child.name == candidateName)
                {
                    return child;
                }
            }
        }

        return null;
    }
}
