using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyData : MonoBehaviour, IEnemy
{
    private int _currentHealth = 100;
    private int _maxHealth = 100;
    public int MaxHealth { get { return _maxHealth; } set { _maxHealth = value; } }
    public int CurrentHealth { get { return _currentHealth; } set { _currentHealth = value; } }

    public GameObject generator;

    public void TakeDamage(int damage)
    {
        if (CurrentHealth > 0)
        {
            CurrentHealth -= damage;
            Debug.Log("Enemy Current Healht: " + CurrentHealth);
            if (CurrentHealth <= 0)
            {
                CurrentHealth = 0;
                Death();
            }
        }
    }

    public void Death()
    {
        Debug.Log("Win!");
    }
}
