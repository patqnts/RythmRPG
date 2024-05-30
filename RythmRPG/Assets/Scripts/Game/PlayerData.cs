using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerData : MonoBehaviour
{
    public static PlayerData instance;

    private void Awake()
    {
        if(instance != null && instance!= this)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    public int PlayerCurrentHealth = 1000;
    public int PlayerMaxHealth = 1000;

    public void TakeDamage(int damage)
    {
        if (PlayerCurrentHealth > 0)
        {          
            PlayerCurrentHealth -= damage;
            
        }
        if (PlayerCurrentHealth <= 0)
        {
            PlayerCurrentHealth = 0;
            Death();
        }
        CombatManager.instance.UpdateUIEventInvoke();
    }

    private void Death()
    {
        CombatManager.instance.WinBattleEventInvoke();
    }
}
