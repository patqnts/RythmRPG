using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public static CombatManager instance;
    public EnemyData enemyData;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    void Start()
    {
        enemyData = FindObjectOfType<EnemyData>();
    }

    public void DamageOpponent(int damage)
    {
        enemyData.TakeDamage(damage);
    }
}
