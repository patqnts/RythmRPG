using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HoldLaserNote : Note
{
    // Start is called before the first frame update
    public float length;
    private float holdTime; // Total time the note should be held
    private float holdTimer; // Timer to track how long the key has been held
    public bool isHoldingKey = false;
    private bool completed = false;
    private bool isHit;
    private RhythmJudgementResult pressJudgement;
    private bool laneTweenStarted;
    private int laneTweenIdentity;


    void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        holdTime = length / speed;
    }

    // Update is called once per frame
    void Update()
    {
        // Check for key hold event
        if (isHoldingKey && activeKeyButton != null && activeKeyButton.GetInteractable())
        {
            animator.SetBool("Hold", isHoldingKey);
            isMoving = false;
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= holdTime)
            {
                if (!completed)
                {
                    // Complete the hold successfully
                    CompleteHoldNote(activeKeyButton.keyType);
                }
            }
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

    private void CompleteHoldNote(KeyType keyType)
    {
        completed = true;
        isHoldingKey = false;
        StartHitEffect(GetJudgedDamage(1, pressJudgement), keyType, activeKeyButton);
        //SetPlayerState(state, 1);
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
            
            DestroyObject();
            if (!completed)
            {
                ReportMissAndDamage(GetIdentityButton(), false);
            }
           
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
        CombatManager.instance.StopAttackEvent -= DestroyObject;
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

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        pressJudgement = result;
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
            ReportMissAndDamage(keyButton, false);
        }

        DestroyObject();
        isHoldingKey = false;
    }
}
