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

    // Start is called before the first frame update
    void Start()
    {
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
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
                holdTimer += Time.deltaTime;
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

        movementTweenStarted = true;
        movementTweenIdentity = currentIdentity;

        StopMovementTweens();
        TweenLaneFall(currentIdentity, -3f, speed);
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
