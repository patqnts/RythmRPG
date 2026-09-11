using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PongNote : Note
{
    // Start is called before the first frame update
    [SerializeField] private bool isDeflect;
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
            keyCode = CombatManager.instance.GetKeyCodeFromNoteIdentity(GetNoteIdentity());
            float targetX = keys.Where(x => x.keyIdentity == GetNoteIdentity()).FirstOrDefault().gameObject.transform.position.x;
            float smoothSpeed = 10f; // Adjust the smoothSpeed as needed

            if (!isDeflect)
            {
                // Smoothly interpolate between current position and target position
                transform.position = new Vector2(Mathf.Lerp(transform.position.x, targetX, Time.deltaTime * smoothSpeed), transform.position.y);
                transform.position -= new Vector3(0, speed * Time.deltaTime, 0f);
            }
            else 
            {
                float enemyX = keys.Where(x => x.keyIdentity == 3).FirstOrDefault().gameObject.transform.position.x;

                transform.position = new Vector2(Mathf.Lerp(transform.position.x, enemyX, Time.deltaTime * speed/2), transform.position.y);
                transform.position += new Vector3(0, speed * Time.deltaTime, 0f);
            }
        }
    }

    public void DeflectEffect(bool shouldDeflect)
    {
        isDeflect = shouldDeflect;
        SoundHandler.Instance.PlaySlideSound();
        SetNoteIdentity(UnityEngine.Random.Range(1, 6));
        GetComponent<SpriteRenderer>().flipX = shouldDeflect;
        float newSpeed = GetSpeed() + .25f;
        SetSpeed(newSpeed);
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
