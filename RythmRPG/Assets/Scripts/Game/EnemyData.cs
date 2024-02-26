using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyData : MonoBehaviour, IEnemy
{
    public int _currentHealth = 100;
    public int _maxHealth = 100;
    public NoteGenerator _noteGenerator;
    public Animator _animator;
    public int MaxHealth { get { return _maxHealth; } set { _maxHealth = value; } }
    public int CurrentHealth { get { return _currentHealth; } set { _currentHealth = value; } }
   
    public bool isOnBattle;
    private void Start()
    {
       
        _currentHealth = 100;
    }

    public void TakeDamage(int damage)
    {
        if (CurrentHealth > 0)
        {
            
            CurrentHealth -= damage;
            Debug.Log("Enemy Current Healht: " + CurrentHealth);

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
            _animator.SetTrigger("Death");
        }
    }
    public void Unsub()
    {
        _noteGenerator.enabled = isOnBattle;
    }
}
