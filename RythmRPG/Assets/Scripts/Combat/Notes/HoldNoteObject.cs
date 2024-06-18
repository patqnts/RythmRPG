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
    private KeyCode keyCode; // The key code for this note
    private bool completed = false;

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
        KeyButton identityButton = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault();

        // Check for key down event
        if (Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
        {
            if (canBePressed)
            {
                isHoldingKey = true;
                holdTimer = 0; // Reset the hold timer
               
            }
        }

        // Check for key up event to stop holding
        if (Input.GetKeyUp(keyCode))
        {
            if (isHoldingKey)
            {
                DestroyObject();
                if (!completed)
                {
                    PlayerData.instance.TakeDamage(damage);
                }               
            }

            isHoldingKey = false;
        }

        // Check for key hold event
        if (isHoldingKey && identityButton.GetInteractable())
        {
            isMoving = false;
            holdTimer += Time.deltaTime;
            ScaleTailBasedOnHoldTime();

            if (holdTimer >= holdTime)
            {
                if (!completed)
                {
                    // Complete the hold successfully
                    CompleteHoldNote();               
                }
            }
        }

        if (isMoving)
        {
            keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
            float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
            float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

            // Smoothly interpolate between current position and target position
            transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);

            transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);
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
            if (!isHoldingKey)
            {
                SetPlayerState(state, 30);
                PlayerData.instance.TakeDamage(damage);
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

    private void CompleteHoldNote()
    {
        completed = true;
        tailTransform.GetComponent<SpriteRenderer>().sprite = null;
        // Logic for completing the hold note successfully
        SetPlayerState(state, 0); // Example: Setting state to 0 (no damage)
        // You can add more effects or scoring logic here
        StartHitEffect(1);
        DestroyObject();
        
    }
}
