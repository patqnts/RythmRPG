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


    void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        holdTime = length / speed;
    }

    // Update is called once per frame
    void Update()
    {
        
        KeyButton identityButton = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault();

        if (Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
        {
            if (canBePressed && !isHit)
            {
                isHoldingKey = true;
                holdTimer = 0; // Reset the hold timer
            }
        }

        if (Input.GetKeyUp(keyCode))
        {
            if (isHoldingKey)
            {
                DestroyObject();
            }

            isHoldingKey = false;
        }

        // Check for key hold event
        if (isHoldingKey && identityButton.GetInteractable())
        {
            animator.SetBool("Hold", isHoldingKey);
            isMoving = false;
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= holdTime)
            {
                if (!completed)
                {
                    // Complete the hold successfully
                    CompleteHoldNote(identityButton.keyType);
                }
            }
        }

        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
        float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
        transform.position = new Vector2(targetX, transform.position.y);
    }

    private void CompleteHoldNote(KeyType keyType)
    {
        completed = true;
        isHoldingKey = false;
        StartHitEffect(1,keyType);
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
                PlayerData.instance.TakeDamage(damage);
            }
           
            //Destroy(gameObject, .5f);
            
        }
    }

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
    }
}
