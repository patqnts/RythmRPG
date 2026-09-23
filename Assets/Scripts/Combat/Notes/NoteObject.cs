using PrimeTween;
using RythmRPG.Combat;
using UnityEngine;

public class NoteObject : Note
{
    public bool isSpecial;
    private bool movementTweenStarted;
    private int movementTweenIdentity;

    // Clock-driven travel: while a chart runs the position is a function of the audio clock, so spawn lateness,
    // frame hitches and timeScale changes cannot pull a note off the beat.
    private bool clockDriven;
    private bool restartFromNow;
    private Transform clockSpace;
    private Vector3 clockStartLocal;
    private Vector3 clockKeyLocal;
    private Vector3 clockTargetLocal;
    private float clockDuration;
    private double clockStartSeconds;

    private void Start()
    {
        keys = FindObjectsByType<KeyButton>();
        //animator.SetBool(moveset.ToString(), true);
        isMoving = true;
    }

    public virtual void Update()
    {
        if (isMoving)
        {
            if (ShouldWaitForInitializeMovement())
            {
                return;
            }

            EnsureMovementTween();
            if (clockDriven) ApplyClockPosition();
        }
    }

    private void ApplyClockPosition()
    {
        if (IsResolved || !UsesChartClock) return;
        float elapsed = (float)(ChartSeconds - clockStartSeconds);
        transform.position = EvaluateLaneTravel(clockSpace, clockStartLocal, clockKeyLocal, clockTargetLocal,
            Mathf.Max(0f, elapsed), clockDuration);
    }

    protected void ResetMovementTween()
    {
        movementTweenStarted = false;
        clockDriven = false;
        StopMovementTweens();
    }

    protected void EnsureMovementTween()
    {
        int currentIdentity = GetNoteIdentity();

        if (movementTweenStarted && movementTweenIdentity == currentIdentity)
        {
            return;
        }

        // Reaches the hit line exactly TravelTime after it starts, measured along the lane (see TryPlanLaneTravel).
        float travelTime = Mathf.Max(0.01f, (float)(Data?.TravelTime ?? 2.5d));
        if (!TryPlanLaneTravel(currentIdentity, travelTime, 3f, out Transform movementSpace,
                out Vector3 startLocal, out Vector3 keyLocal, out Vector3 targetLocal,
                out float laneSpeed, out float totalDuration))
        {
            return;
        }

        StopMovementTweens();
        movementTweenIdentity = currentIdentity;
        movementTweenStarted = true;
        SetTravelSpeed(laneSpeed); // timing windows are in seconds: distance along the lane / this speed
        if (UsesChartClock)
        {
            clockDriven = true;
            clockSpace = movementSpace;
            clockStartLocal = startLocal;
            clockKeyLocal = keyLocal;
            clockTargetLocal = targetLocal;
            clockDuration = Mathf.Max(0.01f, totalDuration);
            // First start: the note was due to spawn at HitTime - TravelTime, so a late spawn catches up.
            // After an initialize movement the travel starts from the current moment instead.
            clockStartSeconds = restartFromNow ? ChartSeconds : Data.SpawnTime;
            restartFromNow = false;
            return;
        }

        TweenLaneTravel(movementSpace, startLocal, keyLocal, targetLocal, totalDuration);
    }

    protected override void OnInitializeMovementComplete()
    {
        restartFromNow = true;
        ResetMovementTween();
        if (isMoving)
        {
            EnsureMovementTween();
        }
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        base.OnHit(keyButton, result);
    }
}
