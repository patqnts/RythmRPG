using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HoldNoteObject : Note
{
    public Transform tailTransform; // Assign this in the Inspector
    public float length; // Length of the tail in units
    private bool isHoldingKey = false; // Track if the key is being held
    private float holdTime; // Total time the note should be held
    private float holdTimer; // Timer to track how long the key has been held
    private bool completed = false;
    private RhythmJudgementResult pressJudgement;
    private bool movementTweenStarted;
    private int movementTweenIdentity;

    // Start is called before the first frame update
    void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();
        isMoving = true;

        // Calculate hold time based on the length of the tail
        holdTime = length / speed;
        ScaleTailTransform();
    }

    // Update is called once per frame
    void Update()
    {
        // Check for key hold event
        if (isHoldingKey && activeKeyButton != null && activeKeyButton.GetInteractable())
        {
            isMoving = false;
            holdTimer += Time.deltaTime;
            ScaleTailBasedOnHoldTime();

            if (holdTimer >= holdTime)
            {
                if (!completed)
                {
                    // Complete the hold successfully
                    CompleteHoldNote(activeKeyButton.keyType);
                }
            }
        }

        if (isMoving)
        {
            EnsureMovementTween();
        }
    }

    private void EnsureMovementTween()
    {
        int currentIdentity = GetNoteIdentity();
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(currentIdentity);

        if (movementTweenStarted && movementTweenIdentity == currentIdentity)
        {
            return;
        }

        movementTweenStarted = true;
        movementTweenIdentity = currentIdentity;

        StopMovementTweens();
        TweenLaneFall(currentIdentity, -3f, speed);
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
            if (!isHoldingKey)
            {
                ReportMissAndDamage(GetIdentityButton());
                DestroyObject();
            }
        }
    }

    private void ScaleTailTransform()
    {
        if (tailTransform != null)
        {
            // Scale the tail transform horizontally based on the length of the note
            tailTransform.localScale = new Vector3(length, tailTransform.localScale.y, tailTransform.localScale.z);

            // Adjust the tail position so that it scales to the left
            tailTransform.localPosition = new Vector3(-length / 2, tailTransform.localPosition.y, tailTransform.localPosition.z);
        }
    }

    private void ScaleTailBasedOnHoldTime()
    {
        if (tailTransform != null)
        {
            // Calculate the remaining length based on the speed and hold timer
            float remainingLength = Mathf.Max(0, length - (holdTimer * speed));

            // Scale the tail transform horizontally based on the remaining length
            tailTransform.localScale = new Vector3(remainingLength, tailTransform.localScale.y, tailTransform.localScale.z);

            // Adjust the tail position so that it scales to the left
            tailTransform.localPosition = new Vector3(-remainingLength / 2, tailTransform.localPosition.y, tailTransform.localPosition.z);
        }
    }

    private void CompleteHoldNote(KeyType keyType)
    {
        completed = true;
        tailTransform.GetComponent<SpriteRenderer>().sprite = null;
        // Logic for completing the hold note successfully
        SetPlayerState(state, 0); // Example: Setting state to 0 (no damage)
        // You can add more effects or scoring logic here
        StartHitEffect(GetJudgedDamage(1, pressJudgement),keyType);
        DestroyObject();
        
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        pressJudgement = result;
        isHoldingKey = true;
        holdTimer = 0;
        isMoving = false;
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
            ReportMissAndDamage(keyButton, false);
        }

        isHoldingKey = false;
    }
}
