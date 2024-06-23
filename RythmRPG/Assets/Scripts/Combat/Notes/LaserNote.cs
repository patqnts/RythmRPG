using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LaserNote : Note, INote
{
 private bool isHit;
    bool INote.canBePressed { get => this.canBePressed; }

    private void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        //animator.SetBool(moveset.ToString(), true);      
    }
    private void Update()
    {
        KeyButton identityButton = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault();
        if (Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
        {
            if (canBePressed && !isHit)
            {
                CombatManager.instance.DamageOpponent(damage);
                isHit = true;
                DestroyObject();
            }
        }

        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
        float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
        transform.position = new Vector2(targetX, transform.position.y);
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
                PlayerData.instance.TakeDamage(damage);
                Destroy(gameObject,.5f);
            }
        }
    }

    public void DestroyObject()
    {
        animator.SetTrigger("LaserHit");
        Destroy(gameObject, 1.5f);
        
    }

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
    }
}
