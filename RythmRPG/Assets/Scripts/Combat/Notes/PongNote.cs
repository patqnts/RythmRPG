using RythmRPG.Combat;
using UnityEngine;

public class PongNote : Note
{
    // Start is called before the first frame update
    [SerializeField] private bool isDeflect;
    private bool movementTweenStarted;
    private int movementTweenIdentity;

    void Start()
    {
        isDeflect = false;
        isMoving = true;
        keys = FindObjectsByType<KeyButton>(FindObjectsSortMode.None);
    }

    // Update is called once per frame
    void Update()
    {
        if (isMoving)
        {
            if (!isDeflect && ShouldWaitForInitializeMovement())
            {
                return;
            }

            EnsureMovementTween();
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.tag == "Activator")
        {
            canBePressed = true;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.tag != "Activator")
        {
            return;
        }

        canBePressed = false;

        ReportMiss(GetIdentityButton());
        DestroyObject();
    }

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return base.CanReceiveHit(keyButton);
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        base.OnHit(keyButton, result);
    }
}
