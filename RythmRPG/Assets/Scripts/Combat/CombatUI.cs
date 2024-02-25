using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CombatUI : MonoBehaviour
{
    public Text PlayerHealth;
    public Text EnemyHealth;
    void Start()
    {
        CombatManager.instance.UpdateUIEvent += UpdateUI;
        UpdateUI();
    }

    public void UpdateUI()
    {
        PlayerHealth.text = $"{PlayerData.instance.PlayerCurrentHealth}/{PlayerData.instance.PlayerMaxHealth}";
        EnemyHealth.text = $"{CombatManager.instance.enemyData.CurrentHealth}/{CombatManager.instance.enemyData.MaxHealth}";
    }
}
