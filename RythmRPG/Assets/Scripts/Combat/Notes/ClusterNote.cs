using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ClusterNote : Note
{
    // Start is called before the first frame update
    private Rigidbody2D body;
    public int force;
    void Start()
    {
        body = GetComponent<Rigidbody2D>();
        body.AddForce(new Vector2(0, force), ForceMode2D.Impulse);
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();
        CombatManager.instance.StopAttackEvent += DestroyObject;
    }
    private void Update()
    {
        KeyButton identityButton = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault();
        if (canBePressed)
        {
            if (Input.GetKeyDown(keyCode) && identityButton.GetInteractable())
            {
            
                body.bodyType = RigidbodyType2D.Static;
                StartHitEffect(damage);
            }
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
        if (other.gameObject.tag == "Activator")
        {
            canBePressed = false;
            SetPlayerState(state, 30);
            PlayerData.instance.TakeDamage(damage);
            //DestroyObject();
        }
    }
}
