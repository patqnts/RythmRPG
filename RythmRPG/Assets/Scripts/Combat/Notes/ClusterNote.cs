using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ClusterNote : Note
{
    // Start is called before the first frame update
    private Rigidbody2D body;
    public int force;
    private bool isMovingUp;
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
        isMovingUp = body.linearVelocity.y > 0;
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
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
        if (other.gameObject.tag != "Activator")
        {
            return;
        }

        canBePressed = false;
        if (!isMovingUp && body.bodyType != RigidbodyType2D.Static)
        {
            ReportMissAndDamage(GetIdentityButton());
            DestroyObject();
        }
    }

    protected override int GetBaseHitDamage()
    {
        return damage;
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        body.bodyType = RigidbodyType2D.Static;
        base.OnHit(keyButton, result);
    }
}
