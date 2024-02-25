using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum BattleState
{
        START, WON, LOST, NONE
}
public class CombatManager : MonoBehaviour
{
    public BattleState state;
    public static CombatManager instance;
    public EnemyData enemyData;

    public Transform playerPos;
    public Transform enemyPos;

    public CinemachineVirtualCamera virtualCamera;
    public GameObject CombatSystemUI;

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

    public void InitalizeCombat(GameObject player, GameObject enemy)
    {
        state = BattleState.START;
        virtualCamera.Follow = null;
        EnemyData data = enemy.gameObject.GetComponent<EnemyData>();
        SetEnemy(data);

        StartCoroutine(MoveToPosition(player.transform, playerPos.position, 0.65f)); // You can adjust the duration as needed
        StartCoroutine(MoveToPosition(enemy.transform, enemyPos.position, 0.65f));  // You can adjust the duration as needed
        CombatSystemUI.SetActive(true);
        enemyData.generator.SetActive(true);
    }

    private IEnumerator MoveToPosition(Transform transform, Vector2 targetPosition, float duration)
    {
        float elapsedTime = 0;
        Vector2 startingPos = transform.position;

        while (elapsedTime < duration)
        {
            transform.position = new Vector2(Mathf.Lerp(startingPos.x, targetPosition.x, elapsedTime / duration),
                                             Mathf.Lerp(startingPos.y, targetPosition.y, elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPosition; // Ensure final position is exact
    }
    public void SetEnemy(EnemyData enemy)
    {
        enemyData = enemy;
    }

    public void DamageOpponent(int damage)
    {
        enemyData.TakeDamage(damage);
    }
}
