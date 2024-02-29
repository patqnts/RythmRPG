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
    public event Action UpdateUIEvent;
    public event Action WinBattleEvent;

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
        WinBattleEvent += WinBattle;
    }

    private void CombatManager_WinBattleEvent()
    {
        throw new NotImplementedException();
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

            string folderPath = "Assets/Screenshots/";

            if (!System.IO.Directory.Exists(folderPath))
                System.IO.Directory.CreateDirectory(folderPath);

            // Set the desired width and height
            int width = 800;
            int height = 500;

            var screenshotName =
                "Screenshot_" +
                System.DateTime.Now.ToString("dd-MM-yyyy-HH-mm-ss") +
                ".png";

            // Set the resolution parameter to 1 to capture the screenshot at the native resolution
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folderPath, screenshotName), 1);

            Debug.Log(folderPath + screenshotName);
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            StopAttackEvent?.Invoke();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            FinalizeCombatEvent();
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
    

    public void UpdateUIEventInvoke()
    {
        UpdateUIEvent.Invoke();
    }
    public void InitalizeCombatEvent(GameObject player, GameObject enemy) 
    {
        EnterCombatEvent?.Invoke(player,enemy);
    }
    public void InitalizeCombat(GameObject player, GameObject enemy)
    {      
       StartCoroutine(CoroutineInitializeCombat(player, enemy));
    }

    IEnumerator CoroutineInitializeCombat(GameObject player, GameObject enemy)
    {
        SaveCharacterLastPosition(player, enemy);
        virtualCamera.Follow = null;

        if (enemy != null)
        {
            EnemyData data = enemy.gameObject.GetComponent<EnemyData>();
            SetEnemy(data);
            enemyData.isOnBattle = true;
            enemyData.Unsub();
        }

        if (player != null)
        {
            player.GetComponent<SpriteRenderer>().sortingOrder = 51;
        }

        StartCoroutine(MoveToPosition(player.transform, playerPos.position, 0.35f)); // You can adjust the duration as needed
        StartCoroutine(MoveToPosition(enemy.transform, enemyPos.position, 0.35f));  // You can adjust the duration as needed

        if (CombatSystemUI != null)
        {
            CombatSystemUI.SetActive(true);
            UpdateUIEvent.Invoke();
        }
        yield return new WaitForSeconds(3f);
        AttackEvent?.Invoke();
    }
    public void FinalizeCombatEvent()
    {
        StopAttackEvent?.Invoke();
        ExitCombatEvent.Invoke();
    }
    public void FinalizeCombat()
    {
        GameObject player = FindObjectOfType<PlayerMovement>().gameObject;
        if (player != null)
        {
            player.transform.position = playerLastPos;

            if(enemyData!= null) //ENEMY SURVIVED/WIN
            {
                enemyData.transform.position = enemyLastPos;
                enemyData.isOnBattle = false;
                enemyData.GetComponent<SpriteRenderer>().sortingOrder = 1;
                enemyData.Unsub();
            }

            if (player != null)
            {
                player.GetComponent<SpriteRenderer>().sortingOrder = 1;
            }
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
        if(enemyData != null)
        {
            enemyData.TakeDamage(damage);
        }
    }
    public void SaveCharacterLastPosition(GameObject player, GameObject enemy)
    {
        player.GetComponent<PlayerMovement>().isEnabled = false;
        playerLastPos = new Vector2(player.transform.position.x, player.transform.position.y); // Corrected Vector2 assignment
        enemyLastPos = new Vector2(enemy.transform.position.x, enemy.transform.position.y); // Corrected Vector2 assignment
    }
    public IEnumerator OpponentDeathPlay()
    {
        enemyData.DeathPlay();
        yield return new WaitForSeconds(3f);
        FinalizeCombatEvent();
    }
    public void WinBattle()
    {
        StopAttackEvent?.Invoke();
        StartCoroutine(OpponentDeathPlay());       
    }
    public void WinBattleEventInvoke()
    {
       WinBattleEvent.Invoke();
    }
}
