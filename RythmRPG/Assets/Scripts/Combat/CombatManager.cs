using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;


public enum BattleState
{
    START, 
    WON, 
    LOST, 
    NONE
}

public enum Moveset
{
    Default,
    Special,
    Arrow
}
public class CombatManager : MonoBehaviour
{
    public BattleState state;
    public NoteObjectsManager notes;
    public static CombatManager instance;

    public event Action AttackEvent;
    public event Action StopAttackEvent;
    public event Action ExitCombatEvent;
    public event Action<GameObject, GameObject> EnterCombatEvent;
    public event Action UpdateUIEvent;
    public event Action WinBattleEvent;
    public event Action<RhythmJudgementResult> HitJudgedEvent;

    [Header("Rhythm Judgement (world units from key center)")]
    [SerializeField, Min(0.01f)] private float perfectWindow = 0.15f;
    [SerializeField, Min(0.01f)] private float goodWindow = 0.45f;
    [SerializeField, Min(0.01f)] private float badWindow = 0.9f;
    [SerializeField, Min(1)] private int badDamageMultiplier = 1;
    [SerializeField, Min(1)] private int goodDamageMultiplier = 2;
    [SerializeField, Min(1)] private int perfectDamageMultiplier = 3;

    public int CurrentCombo { get; private set; }
    public int BestCombo { get; private set; }
    public int MissCount { get; private set; }
    public HitJudgement LastJudgement { get; private set; }

    public int charactersSortOrder;
    private Vector2 enemyLastPos;
    private Vector2 playerLastPos;

    public EnemyData enemyData;
    public Transform playerPos;
    public Transform enemyPos;
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
        if (Input.GetKeyDown(KeyCode.Space))
        {
            AttackEvent?.Invoke();

            //string folderPath = "Assets/Screenshots/";

            //if (!System.IO.Directory.Exists(folderPath))
            //    System.IO.Directory.CreateDirectory(folderPath);

            //// Set the desired width and height
            //int width = 800;
            //int height = 500;

            //var screenshotName =
            //    "Screenshot_" +
            //    System.DateTime.Now.ToString("dd-MM-yyyy-HH-mm-ss") +
            //    ".png";

            //// Set the resolution parameter to 1 to capture the screenshot at the native resolution
            //ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folderPath, screenshotName), 1);

            //Debug.Log(folderPath + screenshotName);
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

    public void HandleKeyPressed(KeyButton keyButton)
    {
        if (keyButton == null || !keyButton.GetInteractable())
        {
            return;
        }

        Note note = FindBestNoteForKey(keyButton);
        if (note != null)
        {
            note.TryHitFromKey(keyButton);
        }
    }

    public void HandleKeyReleased(KeyButton keyButton)
    {
        if (keyButton == null)
        {
            return;
        }

        foreach (Note note in FindObjectsOfType<Note>())
        {
            if (note.IsUsingKey(keyButton))
            {
                note.OnKeyReleased(keyButton);
            }
        }
    }

    private Note FindBestNoteForKey(KeyButton keyButton)
    {
        return FindObjectsOfType<Note>()
            .Where(note => note.CanReceiveHit(keyButton))
            .Where(note => GetTimingError(note, keyButton) <= badWindow)
            .OrderBy(note => GetTimingError(note, keyButton))
            .FirstOrDefault();
    }

    public float GetTimingError(Note note, KeyButton keyButton)
    {
        if (note == null || keyButton == null)
        {
            return float.MaxValue;
        }

        return note.GetTimingError(keyButton);
    }
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        else
        {
            instance = this;
        }

        if (GetComponent<HitFeedbackVFX>() == null)
        {
            gameObject.AddComponent<HitFeedbackVFX>();
        }
    }

    private void OnValidate()
    {
        perfectWindow = Mathf.Max(0.01f, perfectWindow);
        goodWindow = Mathf.Max(perfectWindow, goodWindow);
        badWindow = Mathf.Max(goodWindow, badWindow);
    }
    

    public void UpdateUIEventInvoke()
    {
        UpdateUIEvent?.Invoke();
    }
    public void InitalizeCombatEvent(GameObject player, GameObject enemy) 
    {
        EnterCombatEvent?.Invoke(player,enemy);
    }
    public void InitalizeCombat(GameObject player, GameObject enemy)
    {
       ResetJudgementStats();
       StartCoroutine(CoroutineInitializeCombat(player, enemy));
    }

    public bool TryJudgeHit(Note note, KeyButton keyButton, out RhythmJudgementResult result)
    {
        result = default;
        if (note == null || keyButton == null)
        {
            return false;
        }

        float timingError = GetTimingError(note, keyButton);
        if (timingError > badWindow)
        {
            return false;
        }

        HitJudgement judgement = EvaluateJudgement(timingError);

        CurrentCombo++;
        BestCombo = Mathf.Max(BestCombo, CurrentCombo);
        LastJudgement = judgement;

        result = new RhythmJudgementResult(
            judgement,
            timingError,
            CurrentCombo,
            keyButton.transform.position,
            keyButton);
        HitJudgedEvent?.Invoke(result);
        return true;
    }

    public void JudgeMiss(Note note, KeyButton keyButton)
    {
        CurrentCombo = 0;
        MissCount++;
        LastJudgement = HitJudgement.Miss;

        Vector3 feedbackPosition = keyButton != null
            ? keyButton.transform.position
            : note != null ? note.transform.position : transform.position;
        HitJudgedEvent?.Invoke(new RhythmJudgementResult(
            HitJudgement.Miss,
            badWindow,
            0,
            feedbackPosition,
            keyButton));
    }

    public HitJudgement EvaluateJudgement(float timingError)
    {
        float absoluteError = Mathf.Abs(timingError);
        if (absoluteError <= perfectWindow)
        {
            return HitJudgement.Perfect;
        }

        if (absoluteError <= goodWindow)
        {
            return HitJudgement.Good;
        }

        return HitJudgement.Bad;
    }

    public int GetDamageForJudgement(int baseDamage, HitJudgement judgement)
    {
        int multiplier;
        switch (judgement)
        {
            case HitJudgement.Perfect:
                multiplier = perfectDamageMultiplier;
                break;
            case HitJudgement.Good:
                multiplier = goodDamageMultiplier;
                break;
            case HitJudgement.Bad:
                multiplier = badDamageMultiplier;
                break;
            default:
                return 0;
        }

        return Mathf.Max(1, baseDamage) * multiplier;
    }

    private void ResetJudgementStats()
    {
        CurrentCombo = 0;
        BestCombo = 0;
        MissCount = 0;
        LastJudgement = HitJudgement.Miss;
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
            player.GetComponent<SpriteRenderer>().sortingOrder = charactersSortOrder;
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
        SoundHandler.Instance.StopSound();
    }
    private IEnumerator MoveToPosition(Transform transform, Vector2 targetPosition, float duration)
    {
        float elapsedTime = 0;
        Vector2 startingPos = transform.position;
        SoundHandler.Instance.PlaySlideSound();
        while (elapsedTime < duration)
        {
            transform.position = new Vector2(Mathf.Lerp(startingPos.x, targetPosition.x, elapsedTime / duration),
                                             Mathf.Lerp(startingPos.y, targetPosition.y, elapsedTime / duration));
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.position = targetPosition; // Ensure final position is exact     
        SoundHandler.Instance.PlayCombatSound();

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
        enemyData._noteGenerator.enabled = false;
        yield return new WaitForSeconds(5f);
        FinalizeCombatEvent();
    }
    public void WinBattle()
    {
        StopAttackEvent?.Invoke();
        StartCoroutine(OpponentDeathPlay());       
    }
    public void WinBattleEventInvoke()
    {
       WinBattleEvent?.Invoke();
    }

    public KeyCode GetKeyCodeFromNoteIdentity(int identity)
    {
        // Assuming noteIdentity is in the range of 1 to 5
        switch (identity)
        {
            case 1:
                return keyCodes[0];
            case 2:
                return keyCodes[1];
            case 3:
                return keyCodes[2];
            case 4:
                return keyCodes[3];
            case 5:
                return keyCodes[4];
            default:
                return KeyCode.None;
        }
    }
}
