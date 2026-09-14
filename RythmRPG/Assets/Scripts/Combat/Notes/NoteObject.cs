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

        if (!TryGetLaneTravelPositions(currentIdentity, 3f, out Transform movementSpace,
                out Vector3 startLocal, out Vector3 keyLocal, out Vector3 targetLocal))
        {
            return;
        }

        KeyButton targetKey = GetIdentityButton();
        if (targetKey == null) return;

        StopMovementTweens();
        movementTweenIdentity = currentIdentity;
        movementTweenStarted = true;

        float travelTime = Mathf.Max(0.01f, (float)(Data?.TravelTime ?? 2.5d));
        float distanceToKey = Vector3.Distance(transform.position, targetKey.transform.position);
        float worldSpeed = distanceToKey > 0.01f ? distanceToKey / travelTime : Mathf.Max(0.01f, speed);
        float totalDuration = Vector3.Distance(transform.position, FromMovementLocal(targetLocal, movementSpace)) / worldSpeed;
        TweenLaneTravel(movementSpace, startLocal, keyLocal, targetLocal, totalDuration);
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
