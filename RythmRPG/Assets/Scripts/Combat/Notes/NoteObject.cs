using PrimeTween;
using RythmRPG.Combat;
using UnityEngine;

public class NoteObject : Note
{
    public bool isSpecial;
    private bool movementTweenStarted;
    private int movementTweenIdentity;

    private void Start()
    {
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
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
        }
    }

    protected void ResetMovementTween()
    {
        movementTweenStarted = false;
        StopMovementTweens();
    }

    protected void EnsureMovementTween()
    {
        int currentIdentity = GetNoteIdentity();

        if (movementTweenStarted && movementTweenIdentity == currentIdentity)
        {
            return;
        }

        if (!TryGetMissTargetY(currentIdentity, -3f, out float targetY))
        {
            return;
        }

        KeyButton targetKey = GetIdentityButton();
        if (targetKey == null) return;

        StopMovementTweens();
        movementTweenIdentity = currentIdentity;
        movementTweenStarted = true;

        TweenLaneX(currentIdentity);
        float travelTime = Mathf.Max(0.01f, (float)(Data?.TravelTime ?? 2.5d));
        float distanceToKey = Mathf.Abs(transform.position.y - targetKey.transform.position.y);
        float worldSpeed = distanceToKey > 0.01f ? distanceToKey / travelTime : Mathf.Max(0.01f, speed);
        float totalDuration = Mathf.Abs(transform.position.y - targetY) / worldSpeed;
        Tween.PositionY(transform, targetY, Mathf.Max(0.01f, totalDuration), Ease.Linear);
    }

    protected override void OnInitializeMovementComplete()
    {
        ResetMovementTween();
        if (isMoving)
        {
            EnsureMovementTween();
        }
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
        if (other.gameObject.tag == "Activator" && isMoving)
        {
            canBePressed = false;
            ReportMiss(GetIdentityButton());
            DestroyObject();
        }
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        base.OnHit(keyButton, result);
    }
}
