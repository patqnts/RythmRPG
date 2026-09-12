using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PrimeTween;
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
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();
        CombatManager.instance.StopAttackEvent += DestroyObject;
        if (!TryStartInitializeMovement(StartMovementArc))
        {
            StartMovementArc();
        }
    }
    private void Update()
    {
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
    }

    private void StartMovementArc()
    {
        isMovingUp = true;

        float mass = body != null ? Mathf.Max(0.01f, body.mass) : 1f;
        float gravityScale = body != null ? body.gravityScale : 1f;
        float gravity = Mathf.Abs(Physics2D.gravity.y * gravityScale);
        float initialVelocity = force / mass;
        float riseDuration = Mathf.Max(0.01f, initialVelocity / Mathf.Max(0.01f, gravity));
        float riseHeight = initialVelocity * riseDuration - 0.5f * gravity * riseDuration * riseDuration;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        Tween.PositionY(transform, transform.position.y + riseHeight, riseDuration, Ease.OutSine)
            .OnComplete(this, note => note.StartFalling());
    }

    private void StartFalling()
    {
        isMovingUp = false;
        TweenLaneFall(GetNoteIdentity(), -3f, Mathf.Max(1f, force / Mathf.Max(0.01f, body != null ? body.mass : 1f)));
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
        StopMovementTweens();
        base.OnHit(keyButton, result);
    }
}
