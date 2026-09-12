using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LaserNote : Note, INote
{
    private bool isHit;
    private bool laneTweenStarted;
    private int laneTweenIdentity;
    bool INote.canBePressed { get => this.canBePressed; }

    private void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
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
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(currentIdentity);

        if (laneTweenStarted && laneTweenIdentity == currentIdentity)
        {
            return;
        }

        laneTweenStarted = true;
        laneTweenIdentity = currentIdentity;

        StopMovementTweens();
        TweenLaneX(currentIdentity, 0.1f);
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
                ReportMissAndDamage(GetIdentityButton(), false);
                StopMovementTweens();
                Destroy(gameObject,.5f);
            }
        }
    }

    public override void DestroyObject()
    {
        canBePressed = false;
        animator.SetTrigger("LaserHit");
        StopMovementTweens();
        Destroy(gameObject, 1.5f);
        
    }

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return !isHit && base.CanReceiveHit(keyButton);
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

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
    }
}
