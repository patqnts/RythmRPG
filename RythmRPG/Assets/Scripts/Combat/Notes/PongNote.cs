using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PongNote : Note
{
    // Start is called before the first frame update
    [SerializeField] private bool isDeflect;
    private bool movementTweenStarted;
    private int movementTweenIdentity;
    private bool movementTweenDeflect;

    void Start()
    {
        isDeflect = false;
        isMoving = true;
        CombatManager.instance.StopAttackEvent += DestroyObject;
        keys = FindObjectsOfType<KeyButton>();
        stateHandler = FindObjectOfType<PlayerStateHandler>();      
    }

    // Update is called once per frame
    void Update()
    {
        if (isMoving)
        {
            EnsureMovementTween();
        }
    }

    private void EnsureMovementTween()
    {
        int currentIdentity = GetNoteIdentity();
        keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(currentIdentity);

        if (movementTweenStarted && movementTweenIdentity == currentIdentity && movementTweenDeflect == isDeflect)
        {
            return;
        }

        movementTweenStarted = true;
        movementTweenIdentity = currentIdentity;
        movementTweenDeflect = isDeflect;

        StopMovementTweens();

        if (isDeflect)
        {
            int enemyLaneIdentity = 3;
            float targetY = GetEnemyPassThroughY();
            if (TryGetLaneX(enemyLaneIdentity, out float enemyX))
            {
                float horizontalDuration = Mathf.Abs(transform.position.x - enemyX) / Mathf.Max(0.01f, speed * 0.5f);
                TweenLaneX(enemyLaneIdentity, horizontalDuration);
            }

            TweenYTo(targetY, speed);
            return;
        }

        TweenLaneFall(currentIdentity, -3f, speed);
    }

    private float GetEnemyPassThroughY()
    {
        EnemyData enemy = FindObjectOfType<EnemyData>();
        if (enemy != null)
        {
            return Mathf.Max(enemy.transform.position.y + 3f, transform.position.y + 30f);
        }

        return transform.position.y + 30f;
    }

    public void DeflectEffect(bool shouldDeflect)
    {
        isDeflect = shouldDeflect;
        SoundHandler.Instance.PlaySlideSound();
        SetNoteIdentity(UnityEngine.Random.Range(1, 6));
        GetComponent<SpriteRenderer>().flipX = shouldDeflect;
        float newSpeed = GetSpeed() + .25f;
        SetSpeed(newSpeed);
        movementTweenStarted = false;
        isMoving = true;
        StopMovementTweens();
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
        else if(other.gameObject.tag == "Enemy" && isDeflect)
        {
            //Logic handle
            DeflectEffect(false);
            hitAccepted = false;
            other.GetComponentInParent<EnemyData>().AttackAnimate();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.tag != "Activator")
        {
            return;
        }

        canBePressed = false;

        if (!isDeflect)
        {
            ReportMissAndDamage(GetIdentityButton());
            DestroyObject();
        }
    }

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return !isDeflect && base.CanReceiveHit(keyButton);
    }

    protected override void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        DeflectEffect(true);
        HitStop hitStop = FindObjectOfType<HitStop>();
        if (hitStop != null)
        {
            hitStop.Stop(gameObject, 0.08F);
        }
    }
}
