using DG.Tweening;
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
        isMoving = false;
        if(animator!= null)
        {
            animator.SetTrigger("Hit");
        }
        transform.DOKill();
        Destroy(gameObject, .25f);

    }
}
