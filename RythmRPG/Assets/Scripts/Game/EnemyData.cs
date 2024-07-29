using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyData : MonoBehaviour, IEnemy
{
    [SerializeField]private int _currentHealth;
    [SerializeField] private int _maxHealth;
    public Sprite[] attackPoses;
    public NoteGenerator _noteGenerator;
    public Animator _animator;
    public SpriteRenderer _spriteRenderer;
    public int MaxHealth { get { return _maxHealth; } set { _maxHealth = value; } }
    public int CurrentHealth { get { return _currentHealth; } set { _currentHealth = value; } }
    private int poseIndex;
    public bool isOnBattle;
    private void Start()
    {
        isOnBattle = false;
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }
    public void TakeDamage(int damage)
    {
        if (CurrentHealth > 0)
        {            
            CurrentHealth -= damage;
        }

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            Death();
        }
        CombatManager.instance.UpdateUIEventInvoke();
    }

    public void Death()
    {
        Unsub();
        //CombatManager.instance.FinalizeCombatEvent();      
        CombatManager.instance.WinBattleEventInvoke();  
    }

    public void DeathPlay()
    {
        if (_animator != null)
        {
            _animator.Play("Death");
        }
        else
        {
            Debug.Log("Enemy has no Animator");
        }
    }
    public void Unsub()
    {
        
        if(_noteGenerator!= null)
        {
            Debug.Log("IsOnBattle :" + isOnBattle);

            _noteGenerator.enabled = isOnBattle;
            _animator.SetBool("isOnBattle", isOnBattle);
            _animator.CrossFade("intro", 3f);
        }
        else
        {
            Debug.Log("Enemy has no Note Generator");
        }
        
        
    }

    public void AttackAnimate()
    {
        // Set a random attack pose from the attackPoses array
        if (attackPoses != null &&  _spriteRenderer != null)
        {
            _animator.enabled = false;
            int randomIndex;
            do
            {
                randomIndex = Random.Range(0, attackPoses.Length);
            } while (randomIndex == poseIndex);

            poseIndex = randomIndex;
            _spriteRenderer.sprite = attackPoses[poseIndex];
        }
        else
        {
            _animator.SetTrigger("Attack");
        }       
    }

    public void DeathAnimate()
    {
        throw new System.NotImplementedException();
    }

    public void PoseAnimate()
    {
        throw new System.NotImplementedException();
    }

    public void IdleAnimate()
    {
        throw new System.NotImplementedException();
    }

    private void OnEnable()
    {
        _noteGenerator.AttackAnimate += AttackAnimate;
    }
    private void OnDisable()
    {
        _noteGenerator.AttackAnimate -= AttackAnimate;
    }
}
