using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using UnityEngine;

public class HoldLaserNote : Note
{
    [Header("Laser Timing")]
    [SerializeField, Min(0.01f)] private float laneTweenDuration = 0.35f;
    [SerializeField, Min(0f)] private float hitGraceDistance = 1.25f;
    [SerializeField] private bool allowGraceHitOutsideActivator = true;
    [SerializeField] private bool enableColliderOnSpawn = true;

    // Start is called before the first frame update
    public float length;
    private float holdTime; // Total time the note should be held
    private float holdTimer; // Timer to track how long the key has been held
    public bool isHoldingKey = false;
    private bool completed = false;
    private bool isHit;
    private bool laneTweenStarted;
    private int laneTweenIdentity;


    void Start()
    {
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
        if (enableColliderOnSpawn && TryGetComponent(out Collider2D noteCollider))
            noteCollider.enabled = true;
        holdTime = length / Mathf.Max(0.01f, speed);
    }

    // Update is called once per frame
    void Update()
    {
        // Check for key hold event
        if (isHoldingKey && activeKeyButton != null && activeKeyButton.GetInteractable())
        {
            if (animator != null) animator.SetBool("Hold", isHoldingKey);
            isMoving = false;
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= holdTime)
            {
                if (!completed)
                {
                    // Complete the hold successfully
                    CompleteHoldNote();
                }
            }
        }

        if (!ShouldWaitForInitializeMovement())
        {
            EnsureLaneTween();
        }
    }

    private void EnsureLaneTween()
    {
        int currentIdentity = GetNoteIdentity();

        if (laneTweenStarted && laneTweenIdentity == currentIdentity)
        {
            return;
        }

        laneTweenStarted = true;
        laneTweenIdentity = currentIdentity;

        StopMovementTweens();
        TweenLaneX(currentIdentity, laneTweenDuration);
    }

    private void CompleteHoldNote()
    {
        completed = true;
        isHoldingKey = false;
        CompleteHeldHit();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.tag == "Activator")
        {
            canBePressed = true;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.tag == "Activator")
        {
            canBePressed = false;

            if (!completed)
            {
                DestroyObject();
                ReportMiss(GetIdentityButton());
                return;
            }

            DestroyObject();
           
            //Destroy(gameObject, .5f);
            
        }
    }

    public override void DestroyObject()
    {
        StopMovementTweens();
        base.DestroyObject();
    }

    private void OnDestroy()
    {
    }

    public override Vector3 GetJudgementWorldPosition()
    {
        Collider2D noteCollider = GetComponent<Collider2D>();
        if (noteCollider == null)
        {
            return base.GetJudgementWorldPosition();
        }

        Vector3 position = transform.position;
        position.y = noteCollider.bounds.min.y;
        return position;
    }

    public override float GetTimingError(KeyButton keyButton)
    {
        if (keyButton == null)
        {
            return float.MaxValue;
        }

        Collider2D noteCollider = GetComponent<Collider2D>();
        if (noteCollider == null)
        {
            return base.GetTimingError(keyButton);
        }

        float keyY = keyButton.transform.position.y;
        if (keyY >= noteCollider.bounds.min.y && keyY <= noteCollider.bounds.max.y)
        {
            return 0f;
        }

        return Mathf.Min(
            Mathf.Abs(keyY - noteCollider.bounds.min.y),
            Mathf.Abs(keyY - noteCollider.bounds.max.y));
    }

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return !isHit && base.CanReceiveHit(keyButton);
    }

    public override HitJudgement AdjustJudgement(HitJudgement judgement, float timingError)
    {
        return judgement == HitJudgement.Miss && timingError <= hitGraceDistance ? HitJudgement.Bad : judgement;
    }

    protected override bool IsWithinPressWindow(KeyButton keyButton)
    {
        return canBePressed || IsInGraceWindow(keyButton);
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        isHoldingKey = true;
        isHit = true;
        holdTimer = 0;
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

        if (!completed)
        {
            ReportMiss(keyButton);
        }

        DestroyObject();
        isHoldingKey = false;
    }

    private bool IsInGraceWindow(KeyButton keyButton)
    {
        return allowGraceHitOutsideActivator && keyButton != null && GetTimingError(keyButton) <= hitGraceDistance;
    }
}
