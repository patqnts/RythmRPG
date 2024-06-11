using System.Collections;
using System.Collections.Generic;
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
    public void SetPlayerState(PlayerState state, float duration)
    {
        stateHandler.SetPlayerState(state, duration);
    }

    public void StartHitEffect(int damage)
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
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;
            case HitEffect.Pong:
                CombatManager.instance.DamageOpponent(damage);
                DestroyObject();
                break;

        }
        Debug.Log($"Damage: {damage}");
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

    public void DestroyObject()
    {
        isMoving = false;
        animator.SetTrigger("Hit");
        Destroy(gameObject, .25f);
    }
}
