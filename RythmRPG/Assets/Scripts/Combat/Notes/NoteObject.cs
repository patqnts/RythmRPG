using PrimeTween;
using UnityEngine;

public class NoteObject : Note, INote
{
    bool INote.canBePressed { get => this.canBePressed; }

    public bool isSpecial;
    private bool movementTweenStarted;
    private int movementTweenIdentity;

    private void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();
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
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(currentIdentity);

        if (movementTweenStarted && movementTweenIdentity == currentIdentity)
        {
            return;
        }

        if (!TryGetMissTargetY(currentIdentity, -3f, out float targetY))
        {
            return;
        }

        StopMovementTweens();
        movementTweenIdentity = currentIdentity;
        movementTweenStarted = true;

        TweenLaneX(currentIdentity);
        Tween.PositionY(transform, targetY, 2.5f, Ease.Linear);
    }

    protected override void OnInitializeMovementComplete()
    {
        ResetMovementTween();
        if (isMoving)
        {
            EnsureMovementTween();
        }
    }

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
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
            ReportMissAndDamage(GetIdentityButton());
            DestroyObject();
        }
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        FindObjectOfType<ScreenshakeManager>().ShakeLight();
        base.OnHit(keyButton, result);
    }
}
