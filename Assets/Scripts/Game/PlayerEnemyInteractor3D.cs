using System.Collections;
using System.Linq;
using RythmRPG.Core;
using RythmRPG.Combat;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerEnemyInteractor3D : MonoBehaviour
{
    [Header("Combat")]
    [SerializeField] private CombatController combatController;
    [SerializeField] private PlayerCombatant playerCombatant;

    [Header("3D Detection")]
    [SerializeField] private Transform detectionOrigin;
    [SerializeField, Min(0.05f)] private float interactionRadius = 1.25f;
    [SerializeField] private LayerMask enemyLayers = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [Tooltip("Needs the Interact control (Enter / gamepad South by default, rebindable in Settings > Controls).")]
    [SerializeField] private bool requireInteractKey = true;
    [SerializeField] private bool useOverlapCheck = true;
    [SerializeField] private bool beginBattleOnTriggerEnter;

    [Header("Presentation")]
    [SerializeField] private GameObject noticePrefab;
    [SerializeField] private GameObject battleBackground;
    [SerializeField, Min(0f)] private float encounterDelay = 1.5f;
    [Tooltip("Delay before a battle started by a conversation (no '!' notice), e.g. StartBattle() in the Dialogue System.")]
    [SerializeField, Min(0f)] private float dialogueEncounterDelay = 0.35f;

    [Header("Movement Control")]
    [SerializeField] private bool sendMovementMessages = true;
    [SerializeField] private string disableMovementMessage = "DisableMovement";
    [SerializeField] private string enableMovementMessage = "EnableMovement";

    private bool encounterStarting;

    private Transform DetectionOrigin => detectionOrigin != null ? detectionOrigin : transform;

    /// <summary>Between the start of the encounter transition (sound, notice, background) and the battle beginning.</summary>
    public bool IsEncounterStarting => encounterStarting;

    private void Awake()
    {
        combatController ??= FindAnyObjectByType<CombatController>();
        playerCombatant ??= GetComponent<PlayerCombatant>() ?? gameObject.AddComponent<PlayerCombatant>();
    }

    private void OnEnable()
    {
        GameSceneLoader.DestinationReady += HandleDestinationReady;
        RefreshSceneCombatController();
    }

    private void OnDisable()
    {
        GameSceneLoader.DestinationReady -= HandleDestinationReady;
        if (combatController != null) combatController.BattleEnded -= HandleBattleEnded;
    }

    private void HandleDestinationReady(SceneLoadRequest request)
    {
        encounterStarting = false;
        RefreshSceneCombatController();
    }

    private void RefreshSceneCombatController()
    {
        if (combatController != null) combatController.BattleEnded -= HandleBattleEnded;
        combatController = FindAnyObjectByType<CombatController>();
        if (combatController != null) combatController.BattleEnded += HandleBattleEnded;
    }

    private void Update()
    {
        if (!useOverlapCheck || encounterStarting || IsCombatBusy()) return;
        if (requireInteractKey && !GameInput.InteractPressed) return;
        TryBeginBattle(FindNearestEnemyInRange());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!beginBattleOnTriggerEnter || encounterStarting || IsCombatBusy()) return;
        if (!IsInEnemyLayer(other.gameObject.layer)) return;
        if (requireInteractKey && !GameInput.InteractPressed) return;
        TryBeginBattle(ResolveEnemy(other));
    }

    private void OnTriggerStay(Collider other)
    {
        if (!beginBattleOnTriggerEnter || !requireInteractKey || encounterStarting || IsCombatBusy()) return;
        if (!IsInEnemyLayer(other.gameObject.layer)) return;
        if (!GameInput.InteractPressed) return;
        TryBeginBattle(ResolveEnemy(other));
    }

    public bool TryBeginBattle(EnemyCombatant enemy) => TryBeginBattle(enemy, true);

    /// <param name="showNotice">False for battles a conversation started: no "!" notice, and the shorter
    /// dialogue encounter delay.</param>
    public bool TryBeginBattle(EnemyCombatant enemy, bool showNotice)
    {
            if (enemy == null || !isActiveAndEnabled || encounterStarting || GameSceneLoader.IsLoading || IsCombatBusy()) return false;
        StartCoroutine(EncounterTransition(enemy, showNotice));
        return true;
    }

    private EnemyCombatant FindNearestEnemyInRange()
    {
        Vector3 origin = DetectionOrigin.position;
        Collider[] hits = Physics.OverlapSphere(origin, interactionRadius, enemyLayers, triggerInteraction);
        return hits.Select(ResolveEnemy)
            .Where(enemy => enemy != null && enemy.isActiveAndEnabled)
            .OrderBy(enemy => (enemy.transform.position - origin).sqrMagnitude)
            .FirstOrDefault();
    }

    private static EnemyCombatant ResolveEnemy(Collider collider)
    {
        if (collider == null) return null;
        return collider.GetComponentInParent<EnemyCombatant>() ?? collider.GetComponentInChildren<EnemyCombatant>();
    }

    private bool IsInEnemyLayer(int layer)
    {
        return (enemyLayers.value & (1 << layer)) != 0;
    }

    private IEnumerator EncounterTransition(EnemyCombatant enemy, bool showNotice)
    {
        encounterStarting = true;

        if (SoundHandler.Instance != null)
        {
            SoundHandler.Instance.PlayEncounterSound();
        }

        if (battleBackground != null) battleBackground.SetActive(true);
        SendMovementMessage(disableMovementMessage);

        GameObject notice = null;
        if (showNotice && noticePrefab != null && enemy != null)
        {
            notice = Instantiate(noticePrefab, enemy.transform);
        }

        float delay = showNotice ? encounterDelay : dialogueEncounterDelay;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (notice != null) Destroy(notice);

        combatController ??= FindAnyObjectByType<CombatController>();
        playerCombatant ??= GetComponent<PlayerCombatant>() ?? gameObject.AddComponent<PlayerCombatant>();

        if (combatController != null && enemy != null)
        {
            combatController.BeginBattle(new CombatEncounterContext(playerCombatant, enemy));
        }

        encounterStarting = false;
    }

    private bool IsCombatBusy()
    {
        return combatController != null && combatController.IsBattleActive;
    }

    private void HandleBattleEnded(CombatState _)
    {
        encounterStarting = false;
        if (battleBackground != null) battleBackground.SetActive(false);
        SendMovementMessage(enableMovementMessage);
    }

    /// <summary>
    /// Combat Preview: the battle background and the movement lock of an encounter, without the notice or the delay.
    /// Ending the preview battle undoes both (the same as any battle ending).
    /// </summary>
    public void SetBattlePresentation(bool active)
    {
        if (battleBackground != null) battleBackground.SetActive(active);
        SendMovementMessage(active ? disableMovementMessage : enableMovementMessage);
    }

    private void SendMovementMessage(string message)
    {
        if (!sendMovementMessages || string.IsNullOrWhiteSpace(message)) return;
        SendMessage(message, SendMessageOptions.DontRequireReceiver);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.35f);
        Gizmos.DrawSphere(DetectionOrigin.position, interactionRadius);
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 1f);
        Gizmos.DrawWireSphere(DetectionOrigin.position, interactionRadius);
    }
}
