using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LaserNote : MonoBehaviour, INote
{
    [SerializeField]
    private int noteIdentity;

    [SerializeField] public float speed;
    [SerializeField] public int damage;

    [SerializeField] public KeyButton[] keys;

    public bool canBePressed;

    public KeyCode keyCode;
    public Moveset moveset;
    bool INote.canBePressed { get => this.canBePressed; }
    public Animator animator;
    private void Start()
    {
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        //animator.SetBool(moveset.ToString(), true);      
    }
    public void SetNoteIdentity(int i)
    {
        noteIdentity = i;
    }

    public int GetNoteIdentity()
    {
        return noteIdentity;
    }
    private void Update()
    {
        if (Input.GetKeyDown(keyCode))
        {
            if (canBePressed)
            {
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
            }
        }

        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(noteIdentity);
        float targetX = keys.Where(x => x.keyIdentity == noteIdentity).FirstOrDefault().gameObject.transform.position.x;
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

            PlayerData.instance.TakeDamage(damage);
            DestroyObject();
        }
    }

    public void DestroyObject()
    {
        //animator.SetTrigger("Hit");
        Destroy(gameObject, .25f);
    }

    private void OnDestroy()
    {
        CombatManager.instance.StopAttackEvent -= DestroyObject;
    }
}
