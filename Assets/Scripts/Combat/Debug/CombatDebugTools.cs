using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// In-game developer shortcuts for combat. Runs in the Editor and in Development Builds only; it installs itself
    /// when a scene loads, so nothing needs to be added to the scene. To change keys, add this component to any
    /// object in the scene (that copy is used instead of the automatic one) and edit the bindings in the inspector.
    ///
    /// Default keys (F1 shows the list in game):
    ///   F1 overlay, F2 skip enemy turn, F3 skip player turn, F4 kill enemy (win), F5 kill player (lose),
    ///   F6 full HP + MP, F7 god mode, F8 damage enemy 25%, F9 reset cooldowns, F10 damage player 10%.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDebugTools : MonoBehaviour
    {
        [Serializable]
        private sealed class Binding
        {
            public string label;
            public KeyCode key;
            [NonSerialized] public Func<CombatDebugTools, string> run;
        }

        [Tooltip("Also run in non-development player builds. Leave off so release builds never have cheats.")]
        [SerializeField] private bool allowInReleaseBuilds;
        [Tooltip("Hold this key together with the shortcut (None = shortcut alone).")]
        [SerializeField] private KeyCode modifier = KeyCode.None;
        [SerializeField] private KeyCode toggleOverlay = KeyCode.F1;
        [SerializeField] private KeyCode skipEnemyTurn = KeyCode.F2;
        [SerializeField] private KeyCode skipPlayerTurn = KeyCode.F3;
        [SerializeField] private KeyCode killEnemy = KeyCode.F4;
        [SerializeField] private KeyCode killPlayer = KeyCode.F5;
        [SerializeField] private KeyCode fullRestore = KeyCode.F6;
        [SerializeField] private KeyCode godMode = KeyCode.F7;
        [SerializeField] private KeyCode damageEnemy = KeyCode.F8;
        [SerializeField] private KeyCode resetCooldowns = KeyCode.F9;
        [SerializeField] private KeyCode damagePlayer = KeyCode.F10;
        [SerializeField, Range(0.01f, 1f)] private float enemyDamageFraction = 0.25f;
        [SerializeField, Range(0.01f, 1f)] private float playerDamageFraction = 0.1f;
        [SerializeField] private bool showOverlayOnStart;

        private readonly List<Binding> bindings = new();
        private CombatController controller;
        private float nextLookup;
        private bool overlayOpen;
        private string toast;
        private float toastUntil;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        private static bool Allowed(bool release) => Debug.isDebugBuild || Application.isEditor || release;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Allowed(false) || FindAnyObjectByType<CombatDebugTools>() != null) return;
            var host = new GameObject("[Combat Debug Tools]");
            DontDestroyOnLoad(host);
            host.AddComponent<CombatDebugTools>();
        }

        private void Awake()
        {
            if (!Allowed(allowInReleaseBuilds))
            {
                Destroy(this);
                return;
            }
            // A copy placed in the scene wins over the automatic one.
            foreach (CombatDebugTools other in FindObjectsByType<CombatDebugTools>(FindObjectsInactive.Exclude))
                if (other != this && other.gameObject.name == "[Combat Debug Tools]") Destroy(other.gameObject);
            overlayOpen = showOverlayOnStart;
            BuildBindings();
        }

        private void BuildBindings()
        {
            bindings.Clear();
            Add("Skip enemy turn", skipEnemyTurn, t => t.Controller.DebugSkipEnemyTurn() ? "Enemy turn skipped" : "Not the enemy's turn");
            Add("Skip player turn", skipPlayerTurn, t => t.Controller.DebugSkipPlayerTurn() ? "Player turn skipped" : "Only while choosing an ability");
            Add("Kill enemy (win)", killEnemy, t => t.Controller.DebugEndBattle(true) ? "Enemy killed" : "Can't end the battle right now");
            Add("Kill player (lose)", killPlayer, t => t.Controller.DebugEndBattle(false) ? "Player killed" : "Can't end the battle right now");
            Add("Full HP + MP", fullRestore, t => t.FullRestore());
            Add("God mode", godMode, t =>
            {
                t.Controller.DebugInvulnerable = !t.Controller.DebugInvulnerable;
                return t.Controller.DebugInvulnerable ? "God mode ON" : "God mode OFF";
            });
            Add($"Damage enemy {Mathf.RoundToInt(enemyDamageFraction * 100f)}%", damageEnemy, t => t.DamageEnemy());
            Add("Reset cooldowns", resetCooldowns, t =>
            {
                if (t.Controller.AbilitySlots == null) return "No ability slots";
                t.Controller.AbilitySlots.ResetRuntime();
                return "Cooldowns reset";
            });
            Add($"Damage player {Mathf.RoundToInt(playerDamageFraction * 100f)}%", damagePlayer, t => t.DamagePlayer());
        }

        private void Add(string label, KeyCode key, Func<CombatDebugTools, string> run)
        {
            if (key != KeyCode.None) bindings.Add(new Binding { label = label, key = key, run = run });
        }

        private CombatController Controller
        {
            get
            {
                if (controller == null && Time.unscaledTime >= nextLookup)
                {
                    controller = FindAnyObjectByType<CombatController>();
                    nextLookup = Time.unscaledTime + 1f;
                }
                return controller;
            }
        }

        private void Update()
        {
            bool modifierHeld = modifier == KeyCode.None || Input.GetKey(modifier);
            if (!modifierHeld) return;
            if (Input.GetKeyDown(toggleOverlay)) overlayOpen = !overlayOpen;

            foreach (Binding binding in bindings)
            {
                if (!Input.GetKeyDown(binding.key)) continue;
                CombatController combat = Controller;
                string result = combat == null ? "No CombatController in the scene"
                    : !combat.IsBattleActive ? "No battle running"
                    : binding.run(this);
                Show($"{binding.label}: {result}");
            }
        }

        private string FullRestore()
        {
            PlayerCombatant player = Controller.Encounter.Player;
            if (player == null) return "No player";
            player.Heal(player.MaxHealth);
            player.GainMana(player.MaxMana);
            return $"HP {player.CurrentHealth}/{player.MaxHealth}, MP {player.CurrentMana}/{player.MaxMana}";
        }

        private string DamageEnemy()
        {
            EnemyCombatant enemy = Controller.Encounter.Enemy;
            if (enemy == null || enemy.IsDefeated) return "No enemy";
            // Leaves at least 1 HP: use Kill enemy to end the battle through the normal victory flow.
            int amount = Mathf.Min(Mathf.CeilToInt(enemy.MaxHealth * enemyDamageFraction), enemy.CurrentHealth - 1);
            if (amount <= 0) return "Enemy is at 1 HP (use Kill enemy)";
            enemy.ApplyDamage(amount);
            return $"-{amount}, enemy HP {enemy.CurrentHealth}/{enemy.MaxHealth}";
        }

        private string DamagePlayer()
        {
            PlayerCombatant player = Controller.Encounter.Player;
            if (player == null) return "No player";
            int amount = Mathf.Min(Mathf.CeilToInt(player.MaxHealth * playerDamageFraction), player.CurrentHealth - 1);
            if (amount <= 0) return "Player is at 1 HP (use Kill player)";
            player.ApplyDamage(amount);
            return $"-{amount}, player HP {player.CurrentHealth}/{player.MaxHealth}";
        }

        private void Show(string message)
        {
            toast = message;
            toastUntil = Time.unscaledTime + 2.5f;
            Debug.Log("[Dev] " + message, this);
        }

        private void OnGUI()
        {
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();
            float width = Screen.width / scale;

            if (overlayOpen)
            {
                var lines = new List<string> { "<b>DEV TOOLS</b>  (" + KeyName(toggleOverlay) + " to hide)" };
                CombatController combat = Controller;
                if (combat != null && combat.IsBattleActive)
                {
                    lines.Add($"State: {combat.CurrentState}" + (combat.DebugInvulnerable ? "   <color=#ffd84d>GOD MODE</color>" : ""));
                    PlayerCombatant player = combat.Encounter.Player;
                    EnemyCombatant enemy = combat.Encounter.Enemy;
                    if (player != null) lines.Add($"Player HP {player.CurrentHealth}/{player.MaxHealth}  MP {player.CurrentMana}/{player.MaxMana}");
                    if (enemy != null) lines.Add($"Enemy HP {enemy.CurrentHealth}/{enemy.MaxHealth}");
                }
                else lines.Add("No battle running");
                lines.Add("");
                foreach (Binding binding in bindings) lines.Add($"{KeyName(binding.key)}  {binding.label}");

                float height = lines.Count * 22f + 16f;
                Rect box = new(width - 380f, 12f, 368f, height);
                GUI.Box(box, GUIContent.none, boxStyle);
                GUI.Label(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, height - 16f), string.Join("\n", lines), labelStyle);
            }
            else if (Controller != null && Controller.DebugInvulnerable)
            {
                GUI.Label(new Rect(width - 190f, 12f, 180f, 24f), "<color=#ffd84d>GOD MODE</color>", labelStyle);
            }

            if (!string.IsNullOrEmpty(toast) && Time.unscaledTime < toastUntil)
            {
                Rect rect = new(width * 0.5f - 260f, 12f, 520f, 34f);
                GUI.Box(rect, GUIContent.none, boxStyle);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 24f), toast, labelStyle);
            }
            GUI.matrix = previous;
        }

        private string KeyName(KeyCode key) => modifier == KeyCode.None ? key.ToString() : $"{modifier}+{key}";

        private void EnsureStyles()
        {
            if (boxStyle != null) return;
            var background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            background.Apply();
            boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = background } };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                richText = true,
                wordWrap = false,
                normal = { textColor = Color.white }
            };
        }
    }
}
