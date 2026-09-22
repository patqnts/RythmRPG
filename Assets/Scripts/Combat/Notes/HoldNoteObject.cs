using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using UnityEngine;

public class HoldNoteObject : Note
{
    [Header("Hold Tail")]
    [SerializeField] private HoldNoteTailMode tailMode = HoldNoteTailMode.Sprite;
    [SerializeField] private HoldNoteTailVisual[] tailVisuals;
    public Transform tailTransform; // Legacy fallback. Prefer assigning HoldNoteTailVisual components.
    public float length; // Length of the tail in units

    private bool isHoldingKey = false; // Track if the key is being held
    private float holdTime; // Total time the note should be held
    private float holdTimer; // Timer to track how long the key has been held
    private bool completed = false;
    private bool movementTweenStarted;
    private int movementTweenIdentity;
    private HoldNoteTailVisual activeTailVisual;
    private float revealedTailLength;
    private float holdStartTailLength;

    // Clock-driven while a chart runs: the head reaches the hit line exactly at HitTime and the hold completes at
    // EndTime (both song seconds), like the notes' positions in the composer timeline.
    private bool clockDriven;
    private Transform clockSpace;
    private Vector3 clockStartLocal;
    private Vector3 clockKeyLocal;
    private Vector3 clockTargetLocal;
    private float clockDuration;
    private double pressedAtSeconds;

    // Start is called before the first frame update
    void Start()
    {
        keys = FindObjectsByType<KeyButton>();
        isMoving = true;

        // Calculate hold time based on the length of the tail
        holdTime = GetHoldDuration();
        InitializeTailVisual();
    }

    // Update is called once per frame
    void Update()
    {
        // Check for key hold event
        if (isHoldingKey)
        {
            if (activeKeyButton != null && activeKeyButton.GetInteractable())
            {
                isMoving = false;
                holdTimer = clockDriven && UsesChartClock
                    ? (float)(ChartSeconds - pressedAtSeconds)
                    : holdTimer + Time.deltaTime;
                UpdateTailVisual();

                if (holdTimer >= holdTime)
                {
                    if (!completed)
                    {
                        // Complete the hold successfully
                        CompleteHoldNote();
                    }
                }
            }
        }
        else
        {
            RevealTailVisual(Time.deltaTime);
        }

        if (isMoving)
        {
            if (ShouldWaitForInitializeMovement())
            {
                return;
            }

            EnsureMovementTween();
            if (clockDriven && UsesChartClock && !IsResolved)
            {
                float elapsed = Mathf.Max(0f, (float)(ChartSeconds - Data.SpawnTime));
                transform.position = EvaluateLaneTravel(clockSpace, clockStartLocal, clockKeyLocal, clockTargetLocal,
                    elapsed, clockDuration);
            }
        }
    }

    private void LateUpdate()
    {
        if (!isHoldingKey && activeTailVisual != null)
        {
            SetTailLength(revealedTailLength);
        }
    }

    private void EnsureMovementTween()
    {
        int currentIdentity = GetNoteIdentity();

        if (movementTweenStarted && movementTweenIdentity == currentIdentity)
        {
            return;
        }

        if (UsesChartClock && TryStartClockTravel(currentIdentity))
        {
            movementTweenStarted = true;
            movementTweenIdentity = currentIdentity;
            return;
        }

        movementTweenStarted = true;
        movementTweenIdentity = currentIdentity;

        StopMovementTweens();
        TweenLaneFall(currentIdentity, -3f, speed);
    }

    // Travel-time driven (like NoteObject): speed = distance to the line / TravelTime, so the head arrives at HitTime.
    // The tail length and hold time follow from the authored hold duration at that same speed.
    private bool TryStartClockTravel(int identity)
    {
        if (!TryGetLaneTravelPositions(identity, 3f, out Transform movementSpace,
                out Vector3 startLocal, out Vector3 keyLocal, out Vector3 targetLocal)) return false;
        RhythmLaneTarget target = GetLaneTarget();
        if (target == null) return false;

        float travelTime = Mathf.Max(0.01f, (float)Data.TravelTime);
        float distanceToKey = Vector3.Distance(transform.position, target.transform.position);
        if (distanceToKey <= 0.01f) return false;
        float worldSpeed = distanceToKey / travelTime;

        StopMovementTweens();
        clockDriven = true;
        clockSpace = movementSpace;
        clockStartLocal = startLocal;
        clockKeyLocal = keyLocal;
        clockTargetLocal = targetLocal;
        clockDuration = Mathf.Max(0.01f,
            Vector3.Distance(transform.position, FromMovementLocal(targetLocal, movementSpace)) / worldSpeed);

        float holdSeconds = Mathf.Max(0.01f, (float)(Data.EndTime - Data.HitTime));
        speed = worldSpeed;
        length = holdSeconds * worldSpeed;
        holdTime = holdSeconds;
        if (activeTailVisual != null)
        {
            activeTailVisual.Initialize(length, speed);
            SetTailLength(revealedTailLength = Mathf.Min(revealedTailLength, length));
        }
        return true;
    }

    private void InitializeTailVisual()
    {
        activeTailVisual = ResolveTailVisual();

        if (activeTailVisual == null)
        {
            return;
        }

        SetOnlyActiveTailVisual(activeTailVisual);
        activeTailVisual.Initialize(length, speed);
        SetTailLength(0f);
    }

    private void UpdateTailVisual()
    {
        if (activeTailVisual == null)
        {
            return;
        }

        float normalizedRemaining = holdTime <= 0f ? 0f : Mathf.Clamp01(1f - holdTimer / holdTime);
        float remainingLength = Mathf.Max(0f, holdStartTailLength * normalizedRemaining);
        activeTailVisual.SetRemainingLength(remainingLength, normalizedRemaining);
    }

    private void RevealTailVisual(float deltaTime)
    {
        if (activeTailVisual == null || length <= 0f)
        {
            return;
        }

        revealedTailLength = Mathf.Min(length, revealedTailLength + Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));
        SetTailLength(revealedTailLength);
    }

    private void SetTailLength(float tailLength)
    {
        float normalizedTailLength = length <= 0f ? 0f : Mathf.Clamp01(tailLength / length);
        activeTailVisual.SetRemainingLength(tailLength, normalizedTailLength);
    }

    private HoldNoteTailVisual ResolveTailVisual()
    {
        if (tailVisuals == null || tailVisuals.Length == 0)
        {
            tailVisuals = GetComponentsInChildren<HoldNoteTailVisual>(true);
        }

        HoldNoteTailVisual selectedTail = tailVisuals.FirstOrDefault(tail => tail != null && tail.TailMode == tailMode);
        if (selectedTail != null)
        {
            return selectedTail;
        }

        HoldNoteTailVisual[] childTailVisuals = GetComponentsInChildren<HoldNoteTailVisual>(true);
        selectedTail = childTailVisuals.FirstOrDefault(tail => tail != null && tail.TailMode == tailMode);
        if (selectedTail != null)
        {
            tailVisuals = childTailVisuals;
            return selectedTail;
        }

        selectedTail = tailVisuals.FirstOrDefault(tail => tail != null);
        if (selectedTail != null)
        {
            return selectedTail;
        }

        selectedTail = childTailVisuals.FirstOrDefault(tail => tail != null);
        if (selectedTail != null)
        {
            tailVisuals = childTailVisuals;
            return selectedTail;
        }

        if (tailTransform == null)
        {
            return null;
        }

        selectedTail = tailTransform.GetComponent<HoldNoteTailVisual>();
        if (selectedTail != null)
        {
            return selectedTail;
        }

        HoldNoteSpriteTailVisual spriteTail = tailTransform.gameObject.AddComponent<HoldNoteSpriteTailVisual>();
        tailVisuals = new HoldNoteTailVisual[] { spriteTail };
        return spriteTail;
    }

    private void SetOnlyActiveTailVisual(HoldNoteTailVisual activeTail)
    {
        foreach (HoldNoteTailVisual tailVisual in tailVisuals)
        {
            if (tailVisual == null)
            {
                continue;
            }

            if (tailVisual == activeTail)
            {
                tailVisual.gameObject.SetActive(true);
                continue;
            }

            tailVisual.Hide();
            tailVisual.gameObject.SetActive(false);
        }
    }

    private float GetHoldDuration()
    {
        return Mathf.Max(0.01f, length / Mathf.Max(0.01f, speed));
    }

    private void CompleteHoldNote()
    {
        completed = true;
        HideTailVisual();
        CompleteHeldHit();
        DestroyObject();
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        isHoldingKey = true;
        holdTimer = 0;
        if (clockDriven && UsesChartClock)
        {
            // The hold ends at the authored EndTime, however early or late the press was.
            pressedAtSeconds = ChartSeconds;
            holdTime = Mathf.Max(0.01f, (float)(Data.EndTime - pressedAtSeconds));
        }
        isMoving = false;
        holdStartTailLength = Mathf.Max(0f, revealedTailLength);
        StopMovementTweens();
    }

    public override bool IsUsingKey(KeyButton keyButton)
    {
        return isHoldingKey && base.IsUsingKey(keyButton);
    }

    public override void OnKeyReleased(KeyButton keyButton)
    {
        if (!isHoldingKey)
        {
            return;
        }

        DestroyObject();
        if (!completed)
        {
            ReportMiss(keyButton);
        }

        isHoldingKey = false;
    }

    public override void DestroyObject()
    {
        HideTailVisual();
        base.DestroyObject();
    }

    private void HideTailVisual()
    {
        if (activeTailVisual != null)
        {
            activeTailVisual.Hide();
        }
    }
}
