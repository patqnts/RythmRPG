using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;


public enum BattleState
{
        START, WON, LOST, NONE
}
public class CombatManager : MonoBehaviour
{
    public BattleState state;
    public static CombatManager instance;

    public event Action AttackEvent;
    public event Action StopAttackEvent;
    public event Action ExitCombatEvent;
    public event Action<GameObject, GameObject> EnterCombatEvent;

    public EnemyData enemyData;

    public Transform playerPos;
    private Vector2 playerLastPos;

    public Transform enemyPos;
    private Vector2 enemyLastPos;

    public CinemachineVirtualCamera virtualCamera;
    public GameObject CombatSystemUI;

    public KeyCode[] keyCodes;


    private void Start()
    {
        ExitCombatEvent += FinalizeCombat;
        EnterCombatEvent += InitalizeCombat;
    }

    private void Update()
    {
        foreach (KeyCode keyCode in keyCodes)
        {
            KeyButton key = FindObjectsOfType<KeyButton>().FirstOrDefault(x => x.keyIdentity == Array.IndexOf(keyCodes, keyCode) + 1);

            if (key != null)
            {
                key.isPressed = Input.GetKey(keyCode);                
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            AttackEvent?.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            StopAttackEvent?.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitCombatEvent.Invoke();
        }
    }
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
        SaveCharacterLastPosition(player, enemy);
        virtualCamera.Follow = null;
        EnemyData data = enemy.gameObject.GetComponent<EnemyData>();
        SetEnemy(data);

        StartCoroutine(MoveToPosition(player.transform, playerPos.position, 0.65f)); // You can adjust the duration as needed
        StartCoroutine(MoveToPosition(enemy.transform, enemyPos.position, 0.65f));  // You can adjust the duration as needed
        CombatSystemUI.SetActive(true);      
    }

    public void FinalizeCombat()
    {
        GameObject player = FindObjectOfType<PlayerMovement>().gameObject;
        if (player != null)
        {
            player.transform.position = playerLastPos;
            enemyData.transform.position = enemyLastPos;
        }

        enemyData = null;
        virtualCamera.Follow = player.transform;
        CombatSystemUI.SetActive(false);
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

    public void SaveCharacterLastPosition(GameObject player, GameObject enemy)
    {
        player.GetComponent<PlayerMovement>().isEnabled = false;
        playerLastPos = new Vector2(player.transform.position.x, player.transform.position.y); // Corrected Vector2 assignment
        enemyLastPos = new Vector2(enemy.transform.position.x, enemy.transform.position.y); // Corrected Vector2 assignment
    }
}
