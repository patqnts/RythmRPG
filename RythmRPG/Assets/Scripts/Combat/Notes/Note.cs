using PrimeTween;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Note : MonoBehaviour
{
    [SerializeField] private int noteIdentity;
    [SerializeField] public float speed;
    [SerializeField] public int damage;
    [SerializeField] public KeyButton[] keys;
    public bool isMoving;
    public KeyCode keyCode;
    public Moveset moveset;
    public PlayerState state;
    public HitEffect hitEffect;


    public Animator animator;
    public bool canBePressed;

    public PlayerStateHandler stateHandler;
    protected KeyButton activeKeyButton;
    protected bool hitAccepted;
    private bool missReported;

    public virtual Vector3 GetJudgementWorldPosition()
    {
        return transform.position;
    }

    public virtual float GetTimingError(KeyButton keyButton)
    {
        if (keyButton == null)
        {
            return float.MaxValue;
        }

        return Mathf.Abs(GetJudgementWorldPosition().y - keyButton.transform.position.y);
    }

    protected bool TryJudgeHit(KeyButton keyButton, out RhythmJudgementResult result)
    {
        return CombatManager.instance.TryJudgeHit(this, keyButton, out result);
    }

    protected void JudgeMiss(KeyButton keyButton)
    {
        if (missReported)
        {
            return;
        }

        missReported = true;
        CombatManager.instance.JudgeMiss(this, keyButton);
    }

    protected int GetJudgedDamage(int baseDamage, RhythmJudgementResult result)
    {
        if (hitEffect == HitEffect.Ghost)
        {
            return baseDamage;
        }

        return CombatManager.instance.GetDamageForJudgement(baseDamage, result.judgement);
    }

    public virtual bool CanReceiveHit(KeyButton keyButton)
    {
        return isActiveAndEnabled
            && !hitAccepted
            && canBePressed
            && keyButton != null
            && keyButton.GetInteractable()
            && keyButton.keyIdentity == GetNoteIdentity();
    }

    public bool TryHitFromKey(KeyButton keyButton)
    {
        if (!CanReceiveHit(keyButton))
        {
            return false;
        }

        if (!TryJudgeHit(keyButton, out RhythmJudgementResult result))
        {
            return false;
        }

        hitAccepted = true;
        activeKeyButton = keyButton;
        OnHit(keyButton, result);
        return true;
    }

    public virtual bool IsUsingKey(KeyButton keyButton)
    {
        return keyButton != null && activeKeyButton == keyButton;
    }

    public virtual void OnKeyReleased(KeyButton keyButton)
    {
    }

    protected virtual int GetBaseHitDamage()
    {
        return 1;
    }

    protected virtual void OnHit(KeyButton keyButton, RhythmJudgementResult result)
    {
        StartHitEffect(GetJudgedDamage(GetBaseHitDamage(), result), keyButton.keyType);
    }

    protected KeyButton GetIdentityButton()
    {
        if (keys == null || keys.Length == 0)
        {
            keys = FindObjectsOfType<KeyButton>();
        }

        return keys.FirstOrDefault(x => x.keyIdentity == GetNoteIdentity());
    }

    protected KeyButton GetKeyButton(int keyIdentity)
    {
        if (keys == null || keys.Length == 0)
        {
            keys = FindObjectsOfType<KeyButton>();
        }

        return keys.FirstOrDefault(x => x.keyIdentity == keyIdentity);
    }

    protected void StopMovementTweens()
    {
        Tween.StopAll(transform);
    }

    protected bool TryGetLaneX(int keyIdentity, out float targetX)
    {
        KeyButton keyButton = GetKeyButton(keyIdentity);
        if (keyButton == null)
        {
            targetX = transform.position.x;
            return false;
        }

        targetX = keyButton.transform.position.x;
        return true;
    }

    protected bool TryGetMissTargetY(int keyIdentity, float offset, out float targetY)
    {
        KeyButton keyButton = GetKeyButton(keyIdentity);
        if (keyButton == null)
        {
            targetY = transform.position.y;
            return false;
        }

        targetY = keyButton.transform.position.y + offset;
        return true;
    }

    protected void TweenLaneX(int keyIdentity, float duration = 0.25f)
    {
        if (TryGetLaneX(keyIdentity, out float targetX))
        {
            Tween.PositionX(transform, targetX, Mathf.Max(0.01f, duration), Ease.OutSine);
        }
    }

    protected void TweenYTo(float targetY, float moveSpeed)
    {
        float duration = Mathf.Max(0.01f, Mathf.Abs(transform.position.y - targetY) / Mathf.Max(0.01f, moveSpeed));
        Tween.PositionY(transform, targetY, duration, Ease.Linear);
    }

    protected void TweenLaneFall(int keyIdentity, float targetYOffset, float moveSpeed, float laneDuration = 0.25f)
    {
        if (!TryGetMissTargetY(keyIdentity, targetYOffset, out float targetY))
        {
            return;
        }

        TweenLaneX(keyIdentity, laneDuration);
        TweenYTo(targetY, moveSpeed);
    }

    protected void ReportMissAndDamage(KeyButton keyButton, bool applyPlayerState = true)
    {
        JudgeMiss(keyButton);
        if (applyPlayerState)
        {
            SetPlayerState(state, 30);
        }

        PlayerData.instance.TakeDamage(damage);
    }

    public void SetPlayerState(PlayerState state, float duration)
    {
        stateHandler.SetPlayerState(state, duration);
    }

    public void StartHitEffect(int damage, KeyType keyType)
    {
        switch (hitEffect)
        {
            case HitEffect.Default:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Ghost:
                PlayerData.instance.TakeDamage(damage);
                DestroyObject();
                break;
            case HitEffect.DoubleHit:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Cluster:
                StartCoroutine(ClusterOut(1));
                DestroyObject();
                break;
            case HitEffect.Pong:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;

        }
        //Debug.Log($"Damage: {damage}");

        if (keyType != KeyType.DEFAULT)
        {
            switch (keyType)
            {
                case KeyType.LIGHTNING:
                    for(int i = 0; i < 3; i++)
                    {
                        FindObjectOfType<Note>().DestroyObject();
                    }
                    break;
                case KeyType.LANE_CLEAR:
                    foreach (Note note in FindObjectsOfType<Note>().Where(x => x.GetNoteIdentity() == this.noteIdentity))
                    {
                        note.DestroyObject();
                    }
                    break;
            }

        }
    }

    private IEnumerator ClusterOut(int count)
    {
        Vector3 pointPos = transform.position;
        int saveNoteIdentity = noteIdentity;
        for (int i = 0; i < count; i++)
        {          
            GameObject cluster = Instantiate(CombatManager.instance.notes.clusterNote, pointPos, Quaternion.identity);
            cluster.GetComponent<ClusterNote>().SetNoteIdentity(saveNoteIdentity);
            yield return new WaitForSeconds(.35f);
        }
    }
 
    public void SetNoteIdentity(int i)
    {
        noteIdentity = i;
    }

    public int GetNoteIdentity()
    {
        return noteIdentity;
    }


    public void SetSpeed(float i)
    {
        speed = i;
    }

    public float GetSpeed()
    {
        return speed;
    }

    public virtual void DestroyObject()
    {
        canBePressed = false;
        isMoving = false;
        if(animator!= null)
        {
            animator.SetTrigger("Hit");
        }
        StopMovementTweens();
        Destroy(gameObject, .25f);

    }
}
