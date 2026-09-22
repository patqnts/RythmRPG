using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Combat HUD: animated player health, player mana and enemy health bars, plus the turn label.
    ///
    /// Bars come from, in order: bars assigned here (placed in your scene, any layout), the prefabs in the
    /// <see cref="CombatHudStyle"/>, or the generated pixel template. Colors, layout, motion and font all live in the
    /// style asset (Resources/Combat/UI/CombatHudStyle), so the look can change without touching code.
    /// Tools > Rythm RPG > Combat > Create HUD Bars In Scene builds editable copies of the template.
    /// </summary>
    public sealed class CombatUIController : MonoBehaviour
    {
        [Header("Animated HUD")]
        [Tooltip("Empty = Resources/Combat/UI/CombatHudStyle (or built-in defaults).")]
        [SerializeField] private CombatHudStyle hudStyle;
        [Tooltip("Bars placed in the scene. Empty ones are created from the style.")]
        [SerializeField] private ResourceBarView playerHealthBar;
        [SerializeField] private ResourceBarView playerManaBar;
        [SerializeField] private ResourceBarView enemyHealthBar;
        [Tooltip("Show the enemy's display name above its bar.")]
        [SerializeField] private bool showEnemyName = true;
        [SerializeField] private bool slideInOnBattleStart = true;
        [SerializeField] private bool hideOnBattleEnd = true;

        [Header("Old number labels (optional)")]
        [FormerlySerializedAs("PlayerHealth"), SerializeField] private Text playerHealth;
        [FormerlySerializedAs("EnemyHealth"), SerializeField] private Text enemyHealth;
        [SerializeField] private Text playerMana;
        [SerializeField] private Text turnText;
        [Tooltip("Keep the old number-only labels visible next to the bars.")]
        [SerializeField] private bool showLegacyLabels;

        private CombatController controller;
        private PlayerCombatant player;
        private EnemyCombatant enemy;
        private RectTransform hudRoot;
        private Coroutine hudRoutine;
        private float hudAlpha;
        private bool battleShown;
        private readonly List<SlideTarget> slideTargets = new();

        private struct SlideTarget
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Rest;
            public Vector2 Offset;
        }

        private CombatHudStyle Style
        {
            get
            {
                if (hudStyle == null) hudStyle = CombatHudStyle.LoadOrDefault();
                return hudStyle;
            }
        }

        private void Awake()
        {
            EnsureCanvas();
            EnsureOptionalLabels();
            // Bars placed in the scene stay hidden until a battle starts, like the generated ones.
            // Only when no battle has bound yet: if this object starts inactive (e.g. under CombatSystemUI),
            // Awake runs the first time it is switched on, which is AFTER Bind, and must not hide a live HUD.
            RegisterAssignedBars();
            if (hideOnBattleEnd && !battleShown) ShowHud(false, false);
        }

        private void EnsureOptionalLabels()
        {
            if (playerMana == null && showLegacyLabels)
                playerMana = CreateFallbackLabel("Player Mana", new Vector2(-420f, 180f), TextAnchor.MiddleLeft);
            //if (turnText == null) turnText = CreateFallbackLabel("Turn State", new Vector2(0f, 250f), TextAnchor.MiddleCenter);
            SetLegacyVisible(playerHealth);
            SetLegacyVisible(enemyHealth);
            SetLegacyVisible(playerMana);
        }

        private void SetLegacyVisible(Text label)
        {
            if (label != null) label.enabled = showLegacyLabels;
        }

        public void Bind(CombatController combatController, PlayerCombatant playerCombatant, EnemyCombatant enemyCombatant)
        {
            EnsureCanvas();
            EnsureOptionalLabels();
            EnsureBars();
            RegisterAssignedBars();
            Unbind();
            controller = combatController;
            player = playerCombatant;
            enemy = enemyCombatant;
            if (controller != null)
            {
                controller.StateChanged += HandleStateChanged;
                controller.BattleEnded += HandleBattleEnded;
            }
            if (player != null)
            {
                player.HealthChanged += HandlePlayerHealth;
                player.ManaChanged += HandlePlayerMana;
            }
            if (enemy != null) enemy.HealthChanged += HandleEnemyHealth;
            if (enemyHealthBar != null)
                enemyHealthBar.Title = showEnemyName && enemy != null
                    ? enemy.Definition != null ? enemy.Definition.DisplayName : enemy.name
                    : string.Empty;
            Refresh(false);
            battleShown = true;
            ShowHud(true, slideInOnBattleStart);
        }

        public void Refresh() => Refresh(false);

        /// <summary>Push current values to every bar and label. animate=false snaps.</summary>
        public void Refresh(bool animate)
        {
            if (player != null)
            {
                SetPlayerHealth(player.CurrentHealth, player.MaxHealth, animate);
                SetPlayerMana(player.CurrentMana, player.MaxMana, animate);
            }
            if (enemy != null) SetEnemyHealth(enemy.CurrentHealth, enemy.MaxHealth, animate);
            if (controller != null) HandleStateChanged(controller.CurrentState);
        }

        private void HandlePlayerHealth(int current, int maximum) => SetPlayerHealth(current, maximum, true);
        private void HandlePlayerMana(int current, int maximum) => SetPlayerMana(current, maximum, true);
        private void HandleEnemyHealth(int current, int maximum) => SetEnemyHealth(current, maximum, true);

        private void SetPlayerHealth(int current, int maximum, bool animate)
        {
            if (playerHealthBar != null) playerHealthBar.Set(current, maximum, animate);
            if (playerHealth != null) playerHealth.text = $"{current}/{maximum}";
        }

        private void SetPlayerMana(int current, int maximum, bool animate)
        {
            if (playerManaBar != null) playerManaBar.Set(current, maximum, animate);
            if (playerMana != null) playerMana.text = $"{current}/{maximum}";
        }

        private void SetEnemyHealth(int current, int maximum, bool animate)
        {
            if (enemyHealthBar != null) enemyHealthBar.Set(current, maximum, animate);
            if (enemyHealth != null) enemyHealth.text = $"{current}/{maximum}";
        }

        private void HandleStateChanged(CombatState state)
        {
            if (turnText != null) turnText.text = GetTurnLabel(state);
        }

        private void HandleBattleEnded(CombatState _)
        {
            battleShown = false;
            if (hideOnBattleEnd) ShowHud(false, slideInOnBattleStart);
        }

        private void Unbind()
        {
            if (controller != null)
            {
                controller.StateChanged -= HandleStateChanged;
                controller.BattleEnded -= HandleBattleEnded;
            }
            if (player != null)
            {
                player.HealthChanged -= HandlePlayerHealth;
                player.ManaChanged -= HandlePlayerMana;
            }
            if (enemy != null) enemy.HealthChanged -= HandleEnemyHealth;
        }

        // ---------- bars ----------

        private void EnsureBars()
        {
            CombatHudStyle style = Style;
            if (playerHealthBar == null) playerHealthBar = CreateBar("Player Health", style.PlayerHealthPrefab, style.PlayerHealth, style.PlayerHealthLayout);
            if (playerManaBar == null) playerManaBar = CreateBar("Player Mana", style.PlayerManaPrefab, style.PlayerMana, style.PlayerManaLayout);
            if (enemyHealthBar == null) enemyHealthBar = CreateBar("Enemy Health", style.EnemyHealthPrefab, style.EnemyHealth, style.EnemyHealthLayout);
        }

        private ResourceBarView CreateBar(string barName, ResourceBarView prefab, ResourceBarStyle barStyle, HudBarLayout layout)
        {
            RectTransform root = EnsureHudRoot();
            if (root == null) return null;
            ResourceBarView bar;
            if (prefab != null)
            {
                bar = Instantiate(prefab, root, false);
                bar.name = barName;
            }
            else bar = ResourceBarView.CreateTemplate(root, barName, barStyle, Style);

            ResourceBarView.ApplyLayout((RectTransform)bar.transform, layout);
            RegisterSlide(bar, layout);
            return bar;
        }

        // Every bar fades and slides on its own CanvasGroup, wherever it came from (scene, prefab or template).
        // Before this, only generated bars were faded through the HUD root, so scene bars never hid.
        private void RegisterAssignedBars()
        {
            CombatHudStyle style = Style;
            RegisterSlide(playerHealthBar, style.PlayerHealthLayout);
            RegisterSlide(playerManaBar, style.PlayerManaLayout);
            RegisterSlide(enemyHealthBar, style.EnemyHealthLayout);
            // The turn label fades in and out with the bars (no slide).
            if (turnText != null) RegisterSlide(turnText.gameObject, Vector2.zero);
        }

        private void RegisterSlide(ResourceBarView bar, HudBarLayout layout)
        {
            if (bar != null) RegisterSlide(bar.gameObject, layout != null ? layout.introOffset : Vector2.zero);
        }

        private void RegisterSlide(GameObject target, Vector2 introOffset)
        {
            if (target == null || !(target.transform is RectTransform rect)) return;
            foreach (SlideTarget existing in slideTargets)
                if (existing.Rect == rect) return;
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            slideTargets.Add(new SlideTarget
            {
                Rect = rect,
                Group = group,
                Rest = rect.anchoredPosition,
                Offset = introOffset
            });
        }

        private RectTransform EnsureHudRoot()
        {
            if (hudRoot != null) return hudRoot;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            var go = new GameObject("Combat HUD", typeof(RectTransform), typeof(CanvasGroup));
            hudRoot = (RectTransform)go.transform;
            hudRoot.SetParent(canvas.rootCanvas.transform, false);
            hudRoot.anchorMin = Vector2.zero;
            hudRoot.anchorMax = Vector2.one;
            hudRoot.offsetMin = hudRoot.offsetMax = Vector2.zero;
            hudRoot.SetAsLastSibling();
            CanvasGroup rootGroup = go.GetComponent<CanvasGroup>();
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;
            return hudRoot;
        }

        private void ShowHud(bool show, bool animate)
        {
            if (hudRoutine != null) StopCoroutine(hudRoutine);
            hudRoutine = null;
            float seconds = Style.IntroSeconds;
            if (!animate || seconds <= 0f || !isActiveAndEnabled)
            {
                hudAlpha = show ? 1f : 0f;
                foreach (SlideTarget target in slideTargets)
                {
                    if (target.Rect == null) continue;
                    target.Rect.anchoredPosition = target.Rest + (show ? Vector2.zero : target.Offset);
                    if (target.Group != null) target.Group.alpha = hudAlpha;
                }
                return;
            }
            hudRoutine = StartCoroutine(SlideHud(show, seconds));
        }

        private IEnumerator SlideHud(bool show, float seconds)
        {
            float from = hudAlpha;
            float to = show ? 1f : 0f;
            float start = Time.unscaledTime;
            while (true)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                float eased = show ? 1f - (1f - t) * (1f - t) * (1f - t) : t * t;
                hudAlpha = Mathf.Lerp(from, to, eased);
                float away = show ? 1f - eased : eased;
                foreach (SlideTarget target in slideTargets)
                {
                    if (target.Rect == null) continue;
                    Vector2 offset = target.Offset * away;
                    target.Rect.anchoredPosition = target.Rest + new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
                    if (target.Group != null) target.Group.alpha = hudAlpha;
                }
                if (t >= 1f) break;
                yield return null;
            }
            hudRoutine = null;
        }

        // ---------- canvas / labels ----------

        private Text CreateFallbackLabel(string objectName, Vector2 anchoredPosition, TextAnchor alignment)
        {
            GameObject label = new(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = label.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(360f, 48f);
            Text text = label.GetComponent<Text>();
            text.font = Style.Font;
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

        private void OnEnable()
        {
            // Re-subscribe after a disable/enable cycle mid-battle.
            if (controller != null && player != null && enemy != null)
            {
                Unbind();
                controller.StateChanged += HandleStateChanged;
                controller.BattleEnded += HandleBattleEnded;
                player.HealthChanged += HandlePlayerHealth;
                player.ManaChanged += HandlePlayerMana;
                enemy.HealthChanged += HandleEnemyHealth;
            }
            Refresh(false);
            // A slide cut short by the object being switched off would leave the bars half faded.
            if (battleShown) ShowHud(true, false);
        }

#if UNITY_EDITOR
        /// <summary>Editor tool: fills the bar slots with scene objects so they can be moved and restyled by hand.</summary>
        public void EditorAssignBars(ResourceBarView health, ResourceBarView mana, ResourceBarView enemyBar)
        {
            playerHealthBar = health;
            playerManaBar = mana;
            enemyHealthBar = enemyBar;
        }
#endif
    }
}
