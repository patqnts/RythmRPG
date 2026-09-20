using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    public sealed class CombatUIController : MonoBehaviour
    {
        [FormerlySerializedAs("PlayerHealth"), SerializeField] private Text playerHealth;
        [FormerlySerializedAs("EnemyHealth"), SerializeField] private Text enemyHealth;
        [SerializeField] private Text playerMana;
        [SerializeField] private Text turnText;

        private CombatController controller;
        private PlayerCombatant player;
        private EnemyCombatant enemy;

        private void Awake()
        {
            EnsureCanvas();
            EnsureOptionalLabels();
        }

        private void EnsureOptionalLabels()
        {
            playerMana ??= CreateFallbackLabel("Player Mana", new Vector2(-420f, 180f), TextAnchor.MiddleLeft);
            turnText ??= CreateFallbackLabel("Turn State", new Vector2(0f, 250f), TextAnchor.MiddleCenter);
        }

        public void Bind(CombatController combatController, PlayerCombatant playerCombatant, EnemyCombatant enemyCombatant)
        {
            EnsureOptionalLabels();
            Unbind();
            controller = combatController;
            player = playerCombatant;
            enemy = enemyCombatant;
            controller.StateChanged += HandleStateChanged;
            player.HealthChanged += HandlePlayerHealth;
            player.ManaChanged += HandlePlayerMana;
            enemy.HealthChanged += HandleEnemyHealth;
            Refresh();
        }

        public void Refresh()
        {
            if (player != null)
            {
                HandlePlayerHealth(player.CurrentHealth, player.MaxHealth);
                HandlePlayerMana(player.CurrentMana, player.MaxMana);
            }
            if (enemy != null) HandleEnemyHealth(enemy.CurrentHealth, enemy.MaxHealth);
            if (controller != null) HandleStateChanged(controller.CurrentState);
        }

        private void HandlePlayerHealth(int current, int maximum)
        {
            if (playerHealth != null) playerHealth.text = $"{current}/{maximum}";
        }

        private void HandleEnemyHealth(int current, int maximum)
        {
            if (enemyHealth != null) enemyHealth.text = $"{current}/{maximum}";
        }

        private void HandlePlayerMana(int current, int maximum)
        {
            if (playerMana != null) playerMana.text = $"{current}/{maximum}";
        }

        private void HandleStateChanged(CombatState state)
        {
            if (turnText != null) turnText.text = GetTurnLabel(state);
        }

        private void Unbind()
        {
            if (controller != null) controller.StateChanged -= HandleStateChanged;
            if (player != null)
            {
                player.HealthChanged -= HandlePlayerHealth;
                player.ManaChanged -= HandlePlayerMana;
            }
            if (enemy != null) enemy.HealthChanged -= HandleEnemyHealth;
        }

        private Text CreateFallbackLabel(string objectName, Vector2 anchoredPosition, TextAnchor alignment)
        {
            GameObject label = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(360f, 48f);
            Text text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private void EnsureCanvas()
        {
            if (GetComponentInParent<Canvas>() != null) return;
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            gameObject.AddComponent<GraphicRaycaster>();
        }

        private static string GetTurnLabel(CombatState state)
        {
            return state switch
            {
                CombatState.BattleStart => "BATTLE START",
                CombatState.EnemyTurnStart or CombatState.EnemyTurnExecuting or CombatState.EnemyTurnEnd => "ENEMY TURN",
                CombatState.PlayerTurnStart or CombatState.PlayerAbilitySelection => "PLAYER TURN: CHOOSE ABILITY",
                CombatState.PlayerAbilityExecuting => "PLAYER TURN: RHYTHM ACTION",
                CombatState.PlayerTurnEnd => "PLAYER TURN END",
                CombatState.Victory => "VICTORY",
                CombatState.Defeat => "DEFEAT",
                _ => state.ToString()
            };
        }

        private void OnDisable() => Unbind();
        private void OnEnable() => Refresh();
    }
}
