using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using UnityEngine;

public class LaserNote : Note
{
    [Header("Laser Timing")]
    [SerializeField, Min(0.01f)] private float laneTweenDuration = 0.35f;
    [SerializeField, Min(0f)] private float hitGraceDistance = 1.25f;
    [SerializeField] private bool allowGraceHitOutsideActivator = true;
    [SerializeField] private bool enableColliderOnSpawn = true;

    private bool isHit;
    private bool laneTweenStarted;
    private int laneTweenIdentity;

    private void Start()
    {
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
        if (enableColliderOnSpawn && TryGetComponent(out Collider2D noteCollider))
            noteCollider.enabled = true;
        //animator.SetBool(moveset.ToString(), true);      
    }
    private void Update()
    {
        if (ShouldWaitForInitializeMovement())
        {
            return;
        }

        EnsureLaneTween();
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
            if (isHit)
            {                              
                DestroyObject();
            }
            else
            {
                ReportMiss(GetIdentityButton());
                StopMovementTweens();
                Destroy(gameObject,.5f);
            }
        }
    }

    public override void DestroyObject()
    {
        canBePressed = false;
        if (animator != null) animator.SetTrigger("LaserHit");
        StopMovementTweens();
        Destroy(gameObject, 1.5f);
        
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
        isHit = true;
        base.OnHit(keyButton, result);
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

    private bool IsInGraceWindow(KeyButton keyButton)
    {
        return allowGraceHitOutsideActivator && keyButton != null && GetTimingError(keyButton) <= hitGraceDistance;
    }

    private void OnDestroy()
    {
    }
}
