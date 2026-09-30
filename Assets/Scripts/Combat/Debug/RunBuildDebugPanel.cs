using System.Collections.Generic;
using System.Linq;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Developer panel for the run build system (Editor and Development Builds only; installs itself like
    /// <see cref="CombatDebugTools"/>). Press F11 in play mode.
    ///
    ///   Build   – pick a sample build (Parry, Glass Cannon, Tank, Adaptable, Hybrid, Blank Slate) or the legacy loadout,
    ///             move abilities between slots / reserve, add content, save / load.
    ///   Combat  – live counters, shields, buffs, statuses, the last cast's breakdown and the event log.
    ///   Rewards – the offer generated after each victory (saved, claim once), with replacement previews.
    ///   Enemy   – current affinities; swap in test affinity profiles (fire-resistant, armored, burn-immune...).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunBuildDebugPanel : MonoBehaviour
    {
        private enum Tab { Build, Combat, Rewards, Enemy }

        [SerializeField] private bool allowInReleaseBuilds;
        [SerializeField] private KeyCode toggleKey = KeyCode.F11;
        [SerializeField] private bool openOnStart;
        [Tooltip("Generate a reward offer after every victory while a run build is active.")]
        [SerializeField] private bool offerAfterVictory = true;
        [Tooltip("Load the build saved with the panel's Save button when play mode starts.")]
        [SerializeField] private bool loadSavedBuildOnStart;

        private CombatController controller;
        private CombatController subscribedController;
        private float nextLookup;
        private bool open;
        private Tab tab;
        private Vector2 scroll;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;
        private GUIStyle headerStyle;
        private GUIStyle smallStyle;
        private string toast;
        private float toastUntil;
        private int addPassiveIndex;
        private int addAbilityIndex;
        private int addUpgradeIndex;
        private int debugOfferCounter;
        private int seed = 12345;
        private string pendingRestartReason;

        private static bool Allowed(bool release) => Debug.isDebugBuild || Application.isEditor || release;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Allowed(false) || FindAnyObjectByType<RunBuildDebugPanel>() != null) return;
            var host = new GameObject("[Run Build Panel]");
            DontDestroyOnLoad(host);
            host.AddComponent<RunBuildDebugPanel>();
        }

        private void Awake()
        {
            if (!Allowed(allowInReleaseBuilds))
            {
                Destroy(this);
                return;
            }
            foreach (RunBuildDebugPanel other in FindObjectsByType<RunBuildDebugPanel>(FindObjectsInactive.Exclude))
                if (other != this && other.gameObject.name == "[Run Build Panel]") Destroy(other.gameObject);
            open = openOnStart;
            if (loadSavedBuildOnStart && RunBuild.Load()) Show("Loaded saved build: " + RunBuild.Current.DisplayName);
        }

        private void OnDestroy() => Subscribe(null);

        private CombatController Controller
        {
            get
            {
                if (controller == null && Time.unscaledTime >= nextLookup)
                {
                    controller = FindAnyObjectByType<CombatController>();
                    nextLookup = Time.unscaledTime + 1f;
                }
                if (controller != subscribedController) Subscribe(controller);
                return controller;
            }
        }

        private void Subscribe(CombatController target)
        {
            if (subscribedController != null) subscribedController.ResultReady -= HandleResult;
            subscribedController = target;
            if (subscribedController != null) subscribedController.ResultReady += HandleResult;
        }

        // Buttons only queue changes; they run in Update so the IMGUI layout never changes mid-event.
        private readonly List<System.Action> deferred = new();

        private void Defer(System.Action action)
        {
            if (action != null) deferred.Add(action);
        }

        private void Update()
        {
            if (deferred.Count > 0)
            {
                System.Action[] actions = deferred.ToArray();
                deferred.Clear();
                foreach (System.Action action in actions) action();
            }
            if (LegacyKeys.WasPressed(toggleKey)) open = !open;
            _ = Controller; // keeps the result subscription alive even while the panel is closed
        }

        // ---------- Actions ----------

        private void HandleResult(CombatReport report)
        {
            RunBuildState build = RunBuild.Current;
            if (!offerAfterVictory || build == null || report == null || !report.Victory || controller == null) return;
            RewardOfferData offer = RewardDirector.GetOrCreateOffer(build, "victory:" + controller.EncounterKey, BuildContentRegistry.Instance);
            if (offer == null || offer.claimed) return;
            if (controller.ShowsRewardScreen) return; // the in-game reward screen handles it
            open = true;
            tab = Tab.Rewards;
            Show("Reward ready: pick one in the Rewards tab (F11)");
        }

        private void ApplyBuild(RunBuildState build)
        {
            RunBuild.Current = build;
            PlayerCombatant player = controller != null && controller.IsBattleActive ? controller.Encounter.Player : FindAnyObjectByType<PlayerCombatant>();
            if (player != null && (controller == null || !controller.IsBattleActive))
            {
                player.SetMaxHealthOverride(build != null ? CombatBuildRuntime.ComputeMaxHealth(build, player.BaseMaxHealth) : 0, fill: true);
                player.GainMana(player.MaxMana);
            }
            string name = build != null ? build.DisplayName : "Legacy loadout";
            if (controller != null && controller.IsBattleActive) RestartIfPossible("Build applied: " + name);
            else Show("Build applied: " + name + " (used from the next battle)");
        }

        private void RestartIfPossible(string message)
        {
            if (controller == null || !controller.IsBattleActive)
            {
                Show(message);
                return;
            }
            if (controller.CurrentState == CombatState.PlayerAbilityExecuting || controller.CurrentState == CombatState.BattleStart)
            {
                pendingRestartReason = message;
                Show(message + " - restarting when the current action ends");
                return;
            }
            controller.RestartBattle();
            Show(message + " - battle restarted");
        }

        private void LateUpdate()
        {
            if (pendingRestartReason == null || controller == null) return;
            if (!controller.IsBattleActive) { pendingRestartReason = null; return; }
            if (controller.CurrentState == CombatState.PlayerAbilityExecuting || controller.CurrentState == CombatState.BattleStart) return;
            string reason = pendingRestartReason;
            pendingRestartReason = null;
            RestartIfPossible(reason);
        }

        private void Show(string message)
        {
            toast = message;
            toastUntil = Time.unscaledTime + 3f;
            Debug.Log("[Build] " + message, this);
        }

        // ---------- GUI ----------

        private void OnGUI()
        {
            if (!open && (string.IsNullOrEmpty(toast) || Time.unscaledTime >= toastUntil)) return;
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            EnsureStyles();
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            if (open)
            {
                Rect area = new(12f, 12f, Mathf.Min(620f, width - 24f), height - 24f);
                GUI.Box(area, GUIContent.none, boxStyle);
                GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>RUN BUILD</b>  ({toggleKey} to hide)", headerStyle);
                GUILayout.FlexibleSpace();
                foreach (Tab candidate in new[] { Tab.Build, Tab.Combat, Tab.Rewards, Tab.Enemy })
                    if (GUILayout.Toggle(tab == candidate, candidate.ToString(), GUI.skin.button, GUILayout.Width(80f))) tab = candidate;
                GUILayout.EndHorizontal();
                scroll = GUILayout.BeginScrollView(scroll);
                switch (tab)
                {
                    case Tab.Build: DrawBuildTab(); break;
                    case Tab.Combat: DrawCombatTab(); break;
                    case Tab.Rewards: DrawRewardsTab(); break;
                    case Tab.Enemy: DrawEnemyTab(); break;
                }
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            if (!string.IsNullOrEmpty(toast) && Time.unscaledTime < toastUntil)
            {
                Rect rect = new(width * 0.5f - 300f, height - 60f, 600f, 34f);
                GUI.Box(rect, GUIContent.none, boxStyle);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 24f), toast, labelStyle);
            }
            GUI.matrix = previous;
        }

        private void DrawBuildTab()
        {
            RunBuildState build = RunBuild.Current;
            BuildContentRegistry registry = BuildContentRegistry.Instance;
            _ = Controller;

            Label(build != null ? $"<b>{build.DisplayName}</b>" : "<b>Legacy loadout</b> (scene / DefaultLoadout abilities, no passives)");
            BuildPreset current = build != null ? registry.Preset(build.PresetId) : null;
            if (current != null && !string.IsNullOrEmpty(current.Description)) Small(current.Description);

            Header("Sample builds");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", labelStyle, GUILayout.Width(40f));
            if (int.TryParse(GUILayout.TextField(seed.ToString(), GUILayout.Width(80f)), out int parsed)) seed = parsed;
            GUILayout.EndHorizontal();
            foreach (BuildPreset preset in registry.Presets)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Use", GUILayout.Width(50f))) { BuildPreset chosen = preset; Defer(() => ApplyBuild(chosen.CreateState(seed))); }
                GUILayout.Label(preset.DisplayName, labelStyle);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Use legacy loadout (no build)")) Defer(() => ApplyBuild(null));
            if (controller != null && controller.IsBattleActive && build != null && GUILayout.Button("Restart battle with this build"))
                Defer(() => RestartIfPossible("Restart"));

            if (build == null) return;

            Header("Ability slots");
            if (!build.HasAffordableBasicAction()) Label("<color=#ff7070>No 0 MP action equipped: you can be locked out of acting.</color>");
            for (int slot = 0; slot < RunBuildState.SlotCount; slot++)
            {
                AbilityInstance instance = build.GetSlot(slot);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>[{slot + 1}]</b>", labelStyle, GUILayout.Width(34f));
                if (instance == null) GUILayout.Label("(empty)", labelStyle);
                else
                {
                    AbilityQuote quote = build.Quote(instance);
                    GUILayout.Label(QuoteLine(quote), labelStyle);
                    GUILayout.FlexibleSpace();
                    for (int target = 0; target < RunBuildState.SlotCount; target++)
                    {
                        int to = target;
                        if (target != slot && GUILayout.Button((target + 1).ToString(), GUILayout.Width(26f))) Defer(() => build.Equip(instance, to));
                    }
                    int from = slot;
                    if (GUILayout.Button("x", GUILayout.Width(24f))) Defer(() => build.Unequip(from));
                }
                GUILayout.EndHorizontal();
                if (instance != null)
                {
                    AbilityQuote quote = build.Quote(instance);
                    foreach (AbilityEffect effect in quote.Effects) Small("      • " + effect.Describe(instance.Definition));
                    foreach (string change in quote.Changes) Small("      - " + change);
                }
            }
            List<AbilityInstance> reserve = build.Reserve.ToList();
            if (reserve.Count > 0)
            {
                Header("Reserve (owned, not equipped)");
                foreach (AbilityInstance instance in reserve)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(QuoteLine(build.Quote(instance)), labelStyle);
                    GUILayout.FlexibleSpace();
                    for (int target = 0; target < RunBuildState.SlotCount; target++)
                    {
                        int to = target;
                        AbilityInstance moving = instance;
                        if (GUILayout.Button("→" + (target + 1), GUILayout.Width(34f))) Defer(() => build.Equip(moving, to));
                    }
                    GUILayout.EndHorizontal();
                }
            }

            Header("Passives");
            if (build.Passives.Count == 0) Small("None yet.");
            List<AbilityQuote> equipped = build.EquippedQuotes();
            foreach (PassiveInstance passive in build.Passives)
            {
                bool active = RunBuildState.IsPassiveActive(passive, equipped);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{passive.Definition.DisplayName}</b> Lv {passive.Level}/{passive.Definition.MaxLevel} " +
                                (active ? "<color=#8f8>ACTIVE</color>" : "<color=#f88>INACTIVE</color> (needs " + passive.Definition.Requirement.Describe() + ")"), labelStyle);
                GUILayout.FlexibleSpace();
                if (!passive.IsMaxLevel && GUILayout.Button("+Lv", GUILayout.Width(40f))) { PassiveDefinition definition = passive.Definition; Defer(() => build.AddPassive(definition)); }
                GUILayout.EndHorizontal();
                Small("      " + passive.Definition.DescribeLevel(passive.Level));
            }

            Header("Add content (dev)");
            List<PassiveDefinition> passives = registry.Passives.OrderBy(p => p.DisplayName).ToList();
            List<AbilityDefinition> abilities = registry.Abilities.OrderBy(a => a.DisplayName).ToList();
            var upgrades = build.Equipped.SelectMany(instance => registry.Upgrades.Where(u => u.IsCompatible(instance) && !instance.HasUpgrade(u))
                .Select(u => (instance, upgrade: u))).ToList();
            Cycler("Passive", passives.Select(p => p.DisplayName + (build.CanAddPassive(p) ? "" : " (blocked)")).ToList(), ref addPassiveIndex,
                () => { if (build.AddPassive(passives[addPassiveIndex]) == null) Show("Not allowed (max level or exclusive)"); });
            Cycler("Ability", abilities.Select(a => a.DisplayName + (build.OwnsAbility(a.Id) ? " (owned)" : "")).ToList(), ref addAbilityIndex,
                () => build.AddAbility(abilities[addAbilityIndex]));
            Cycler("Upgrade", upgrades.Select(pair => $"{pair.upgrade.DisplayName} → {pair.instance}").ToList(), ref addUpgradeIndex,
                () => build.AddUpgrade(upgrades[addUpgradeIndex].instance, upgrades[addUpgradeIndex].upgrade));

            Header("Save");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save build")) Defer(() => { RunBuild.Save(); Show("Build saved"); });
            if (GUILayout.Button("Load build")) Defer(() => Show(RunBuild.Load() ? "Loaded " + RunBuild.Current.DisplayName : "No saved build"));
            GUILayout.EndHorizontal();
            loadSavedBuildOnStart = GUILayout.Toggle(loadSavedBuildOnStart, "Load saved build when play starts (this session)");
        }

        private void DrawCombatTab()
        {
            CombatController combat = Controller;
            if (combat == null || !combat.IsBattleActive || combat.BuildRuntime == null)
            {
                Label("No battle running.");
                DrawLastReport(combat);
                return;
            }
            CombatBuildRuntime runtime = combat.BuildRuntime;
            PlayerCombatant player = combat.Encounter.Player;
            EnemyCombatant enemy = combat.Encounter.Enemy;
            Label($"State <b>{combat.CurrentState}</b>   player turn {runtime.PlayerTurn}, enemy turn {runtime.EnemyTurn}");
            if (player != null) Label($"Player HP {player.CurrentHealth}/{player.MaxHealth} (base {player.BaseMaxHealth})   MP {player.CurrentMana}/{player.MaxMana}");
            if (enemy != null) Label($"Enemy HP {enemy.CurrentHealth}/{enemy.MaxHealth}   [{enemy.Responses?.Describe() ?? "no affinities"}]");
            Label($"Counter charges <b>{runtime.Counters.Charges}/{runtime.Counters.Max}</b>");

            Header("Buffs / statuses");
            bool any = false;
            foreach (ICombatModifierRuntime modifier in combat.Modifiers.ActiveModifiers.Where(m => m != null && !m.IsExpired))
            {
                any = true;
                Small("• " + (modifier is ITurnBoundaryModifier timed ? timed.Describe()
                    : modifier is LaneWardModifier ward ? $"Lane ward ({(ward.Lanes == null ? "all lanes" : string.Join(",", ward.Lanes))}, {ward.EnemyTurnsRemaining} enemy turns)"
                    : modifier.GetType().Name));
            }
            if (!any) Small("None.");

            Header("Active passives this encounter");
            if (runtime.ActivePassives.Count == 0) Small("None.");
            foreach (PassiveHook hook in runtime.ActivePassives)
                Small($"• {hook.Label} Lv{hook.Level}" + (hook.State.Count > 0 ? "  " + string.Join(", ", hook.State.Select(p => p.Key + "=" + p.Value)) : ""));

            CastSnapshot cast = runtime.CurrentCast ?? runtime.LastCast;
            if (cast != null)
            {
                Header(runtime.CurrentCast != null ? "Current cast" : "Last cast");
                Label($"<b>{cast.Definition?.DisplayName}</b> slot {cast.SlotIndex + 1}, paid {cast.PaidCost} MP, perf {cast.PerformanceWeight:0.00}" +
                      (cast.ExecutionEligible ? "" : " (no execution bonus)"));
                Small($"Damage {cast.DamageBeforeModifiers} base → {cast.DamageAfterModifiers} modified → {cast.DamageAfterAffinity} after affinity → {cast.DamageDealt} dealt");
                if (cast.Healed + cast.Overheal > 0) Small($"Healed {cast.Healed} (overheal {cast.Overheal})");
                if (cast.ShieldGained > 0) Small($"Shield +{cast.ShieldGained}");
                foreach (string line in cast.Log) Small("   " + line);
                foreach (KeyValuePair<string, int> pair in cast.Contributions) Small($"   contribution: {pair.Key} +{pair.Value}");
            }

            EnemyTurnSummary turn = runtime.LastEnemyTurn;
            if (turn != null)
            {
                Header($"Last enemy turn ({turn.EnemyTurn})");
                Small($"{turn.Opportunities} notes: P{turn.PlayerJudgements[3]} G{turn.PlayerJudgements[2]} B{turn.PlayerJudgements[1]} M{turn.PlayerJudgements[0]}" +
                      $" (+{turn.NonPlayerResolutions} auto), damage taken {turn.DamageTaken}" + (turn.Interrupted ? ", interrupted" : ""));
            }

            BuildCombatStats stats = runtime.Stats;
            Header("Encounter totals");
            Small($"Casts {stats.Casts}, MP paid {stats.ManaPaid}, MP restored {stats.ManaRestored}, reflected {stats.Reflected}, status {stats.StatusDamage}, " +
                  $"resisted {stats.Resisted}, prevented {stats.Prevented}, absorbed {stats.Absorbed}, shield +{stats.ShieldGained}, overheal {stats.Overheal}, " +
                  $"counters +{stats.CountersGained}/-{stats.CountersSpent}/faded {stats.CountersExpired}");
            foreach (KeyValuePair<string, int> pair in stats.Contributions.OrderByDescending(p => p.Value)) Small($"   {pair.Key}: {pair.Value}");

            Header("Events");
            foreach (CombatEvent combatEvent in runtime.Events.Recent(14).Reverse()) Small(combatEvent.ToString());
        }

        private void DrawLastReport(CombatController combat)
        {
            CombatReport report = combat != null ? combat.LastReport : null;
            if (report == null) return;
            Header($"Last battle ({(report.Victory ? "victory" : "defeat")}) - {report.BuildName}");
            Small($"Dealt {report.DamageDealt}, taken {report.DamageTaken}, reflected {report.DamageReflected}, status {report.StatusDamage}, " +
                  $"resisted {report.DamageResisted}, prevented {report.DamagePrevented}, absorbed {report.DamageAbsorbed}, overheal {report.Overheal}, " +
                  $"MP restored {report.ManaRestoredByBuild}, counters spent {report.CounterChargesSpent}");
            foreach (KeyValuePair<string, int> pair in report.BuildContributions.OrderByDescending(p => p.Value)) Small($"   {pair.Key}: {pair.Value}");
            foreach (string unused in report.UnusedBenefits) Small("   unused: " + unused);
        }

        private void DrawRewardsTab()
        {
            RunBuildState build = RunBuild.Current;
            if (build == null)
            {
                Label("Pick a build first (Build tab). Rewards are part of a run build.");
                return;
            }
            BuildContentRegistry registry = BuildContentRegistry.Instance;
            offerAfterVictory = GUILayout.Toggle(offerAfterVictory, "Offer a reward after each victory");
            if (GUILayout.Button("Generate a test offer now")) Defer(() => RewardDirector.GetOrCreateOffer(build, $"debug:{++debugOfferCounter}:{Time.frameCount}", registry));

            List<RewardOfferData> offers = build.Offers.Reverse().ToList();
            if (offers.Count == 0) Small("No offers yet. Win a battle (or F4 to kill the enemy) with a build active.");
            foreach (RewardOfferData offer in offers.Take(4))
            {
                Header($"{offer.offerId}  <size=12>({offer.sourceKey})</size>" + (offer.claimed ? "  <color=#8f8>CLAIMED</color>" : ""));
                if (!offer.claimed && GUILayout.Button("Open the reward screen for this offer"))
                {
                    RewardOfferData target = offer;
                    Defer(() => OpenRewardScreen(build, target));
                }
                foreach (RewardOptionData option in offer.options)
                {
                    RewardPreview preview = RewardDirector.Preview(build, option, registry);
                    bool picked = offer.claimed && offer.claimedOptionId == option.optionId;
                    Label($"{(picked ? "[x] " : "")}<b>{preview.Title}</b>  <i>{preview.Kind}</i>");
                    if (!string.IsNullOrEmpty(preview.Summary)) Small("   " + preview.Summary);
                    foreach (string detail in preview.Details) Small("   " + detail);
                    if (offer.claimed) continue;

                    if (!preview.NeedsReplacement)
                    {
                        if (GUILayout.Button("Claim " + preview.Title)) { RewardOptionData chosenOption = option; RewardOfferData from = offer; Defer(() => Claim(build, from, chosenOption, -1)); }
                        continue;
                    }
                    for (int slot = 0; slot < RunBuildState.SlotCount; slot++)
                    {
                        bool allowed = preview.ReplaceableSlots.Contains(slot);
                        GUILayout.BeginHorizontal();
                        GUI.enabled = allowed;
                        if (GUILayout.Button($"Replace [{slot + 1}] {build.GetSlot(slot)}", GUILayout.Width(250f))) { int replace = slot; RewardOptionData chosenOption = option; RewardOfferData from = offer; Defer(() => Claim(build, from, chosenOption, replace)); }
                        GUI.enabled = true;
                        if (preview.ReplacementConsequences.TryGetValue(slot, out List<string> lines))
                            GUILayout.Label(string.Join(" ", lines), smallStyle);
                        GUILayout.EndHorizontal();
                    }
                    if (GUILayout.Button("Claim and keep it in reserve")) { RewardOptionData chosenOption = option; RewardOfferData from = offer; Defer(() => Claim(build, from, chosenOption, -1)); }
                }
            }
        }

        private void OpenRewardScreen(RunBuildState build, RewardOfferData offer)
        {
            RewardSelectionScreen screen = FindAnyObjectByType<RewardSelectionScreen>(FindObjectsInactive.Include);
            if (screen == null)
            {
                RewardSelectionStyle style = RewardSelectionStyle.LoadOrDefault();
                screen = style.ScreenPrefab != null ? Instantiate(style.ScreenPrefab) : RewardSelectionScreen.CreateTemplate(style);
            }
            if (screen.IsOpen) return;
            open = false;
            StartCoroutine(screen.Show(build, offer, BuildContentRegistry.Instance));
        }

        private void Claim(RunBuildState build, RewardOfferData offer, RewardOptionData option, int replaceSlot)
        {
            ClaimStatus status = RewardDirector.Claim(build, offer, option.optionId, BuildContentRegistry.Instance, replaceSlot);
            Show(status == ClaimStatus.Claimed ? "Claimed. Applies from the next battle (or Restart)." : "Claim failed: " + status);
        }

        private void DrawEnemyTab()
        {
            CombatController combat = Controller;
            EnemyCombatant enemy = combat != null && combat.IsBattleActive ? combat.Encounter.Enemy : FindAnyObjectByType<EnemyCombatant>();
            if (enemy == null)
            {
                Label("No enemy found.");
                return;
            }
            BuildBalanceRules rules = BuildBalanceRules.Load();
            Label($"<b>{(enemy.Definition != null ? enemy.Definition.DisplayName : enemy.name)}</b>  " +
                  (enemy.HasResponseOverride ? "<color=#ffd84d>test profile: " + enemy.Responses.Label + "</color>" : "definition profile"));
            foreach (ElementType element in System.Enum.GetValues(typeof(ElementType)))
            {
                float raw = enemy.AffinityFor(element);
                Small($"   {BuildTagUtility.ElementName(element),-10} x{raw:0.##}" + (Mathf.Approximately(raw, Mathf.Clamp(raw, rules.MinAffinity, rules.MaxAffinity)) ? "" : $" (clamped to x{Mathf.Clamp(raw, rules.MinAffinity, rules.MaxAffinity):0.##})"));
            }
            IReadOnlyList<StatusResponse> statuses = enemy.Responses?.Statuses;
            if (statuses != null)
                foreach (StatusResponse status in statuses.Where(s => s != null))
                    Small($"   status {status.statusId}: " + (status.immune ? "IMMUNE" : $"duration x{status.durationScale:0.##}, potency x{status.potencyScale:0.##}"));

            Header("Test affinity profiles");
            if (GUILayout.Button("Use the enemy definition's profile")) Defer(() => enemy.SetResponseOverride(null));
            foreach (EnemyResponseProfile profile in SampleBuildLibrary.EnemyProfiles)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(profile.Label, GUILayout.Width(150f))) { EnemyResponseProfile chosen = profile; Defer(() => enemy.SetResponseOverride(chosen)); }
                GUILayout.Label(profile.Describe(), smallStyle);
                GUILayout.EndHorizontal();
            }
            Small("Profiles apply immediately to incoming damage; statuses already applied keep their snapshot.");
        }

        // ---------- GUI helpers ----------

        private static string QuoteLine(AbilityQuote quote)
        {
            if (quote?.Definition == null) return "-";
            string upgrades = quote.Instance != null && quote.Instance.Upgrades.Count > 0
                ? "  <color=#9cf>+" + string.Join(", ", quote.Instance.Upgrades.Select(u => u.DisplayName)) + "</color>" : "";
            string cost = quote.ManaCost == quote.BaseManaCost ? $"{quote.ManaCost} MP" : $"<color=#8f8>{quote.ManaCost} MP</color> (base {quote.BaseManaCost})";
            return $"<b>{quote.Definition.DisplayName}</b>  {cost}, cd {quote.Cooldown}  " +
                   $"<size=12>{quote.Roles.ToString().Replace(", ", "/")} · {quote.Delivery.ToString().Replace(", ", "/")}" +
                   (quote.Element != ElementType.None ? " · " + quote.Element : "") + "</size>" + upgrades;
        }

        private void Cycler(string label, List<string> names, ref int index, System.Action add)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(70f));
            if (names.Count == 0)
            {
                GUILayout.Label("(nothing eligible)", smallStyle);
                GUILayout.EndHorizontal();
                return;
            }
            index = Mathf.Clamp(index, 0, names.Count - 1);
            if (GUILayout.Button("<", GUILayout.Width(26f))) index = (index + names.Count - 1) % names.Count;
            GUILayout.Label(names[index], labelStyle, GUILayout.Width(300f));
            if (GUILayout.Button(">", GUILayout.Width(26f))) index = (index + 1) % names.Count;
            if (GUILayout.Button("Add", GUILayout.Width(50f))) Defer(add);
            GUILayout.EndHorizontal();
        }

        private void Header(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label(text, headerStyle);
        }

        private void Label(string text) => GUILayout.Label(text, labelStyle);
        private void Small(string text) => GUILayout.Label(text, smallStyle);

        private void EnsureStyles()
        {
            if (boxStyle != null) return;
            var background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
            background.Apply();
            boxStyle = new GUIStyle(GUI.skin.box) { normal = { background = background } };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            smallStyle = new GUIStyle(labelStyle) { fontSize = 13, normal = { textColor = new Color(0.82f, 0.85f, 0.9f) } };
            headerStyle = new GUIStyle(labelStyle) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
        }
    }
}
