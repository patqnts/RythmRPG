using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CombatUI : MonoBehaviour
{
    public Text PlayerHealth;
    public Text EnemyHealth;
    void OnEnable()
    {
        CombatManager.instance.UpdateUIEvent += UpdateUI;
        UpdateUI();
    }

    public void UpdateUI()
    {
        if (PlayerData.instance == null && CombatManager.instance.enemyData == null)
        {
            return;
        }

        PlayerHealth.text = $"{PlayerData.instance.PlayerCurrentHealth}/{PlayerData.instance.PlayerMaxHealth}";
        EnemyHealth.text = $"{CombatManager.instance.enemyData.CurrentHealth}/{CombatManager.instance.enemyData.MaxHealth}";
    }

    private void OnDisable()
    {
        CombatManager.instance.UpdateUIEvent -= UpdateUI;
    }
}
