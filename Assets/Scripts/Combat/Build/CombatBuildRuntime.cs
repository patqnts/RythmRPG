using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Stored counterpower (parry / deflect playstyle). Capped, and fades after a fixed number of idle player turns.</summary>
    public sealed class CounterPool
    {
        private readonly CombatBuildRuntime runtime;
        private int turnsSinceGain;

        public CounterPool(CombatBuildRuntime runtime) => this.runtime = runtime;

        public int Charges { get; private set; }
        public int Max => runtime.Rules.MaxCounterCharges;

        public int Gain(int amount, string sourceId, string rootId)
        {
            if (amount <= 0 || runtime.CombatOver) return 0;
            int added = Mathf.Min(amount, Max - Charges);
            if (added <= 0) return 0;
            Charges += added;
            turnsSinceGain = 0;
            runtime.Stats.CountersGained += added;
            runtime.Record(new CombatEvent { Kind = CombatEventKind.CounterGained, SourceId = sourceId, RootCauseId = rootId, Attempted = amount, Actual = added });
            runtime.RaiseChanged();
            return added;
        }

        /// <summary>Spends up to <paramref name="maximum"/> charges (consumed when the cast's totals freeze).</summary>
        public int Spend(int maximum, string castId, string label)
        {
            int spent = Mathf.Min(Mathf.Max(0, maximum), Charges);
            if (spent <= 0) return 0;
            Charges -= spent;
            runtime.Stats.CountersSpent += spent;
            runtime.Record(new CombatEvent { Kind = CombatEventKind.CounterSpent, SourceId = label, RootCauseId = castId, CastId = castId, Actual = spent });
            runtime.RaiseChanged();
            return spent;
        }

        internal void OnPlayerTurnEnded()
        {
            if (Charges <= 0) return;
            if (++turnsSinceGain < runtime.Rules.CounterExpiryPlayerTurns) return;
            runtime.Stats.CountersExpired += Charges;
            runtime.Stats.UnusedBenefits.Add($"{Charges} counter charge(s) faded");
            Charges = 0;
            runtime.RaiseChanged();
        }

        internal void Reset()
        {
            Charges = 0;
            turnsSinceGain = 0;
        }
    }

    /// <summary>
    /// The build inside one encounter: rebuilt from the run's <see cref="RunBuildState"/> at encounter start. Runs the
    /// passive hooks, owns the damage service, counterpower and event log, and tracks cast snapshots.
    ///
    /// Resolution order of a player action:
    ///   1. <see cref="BeginCast"/> at commitment (cost already paid once by the slot; next-attack bonuses reserved)
    ///   2. the rhythm chart plays and raw judgements are collected
    ///   3. <see cref="PreOutcome"/>: performance final, reserved bonuses + passive pre-outcome modifiers added
    ///   4. AbilityResolution freezes totals, then distributes them over the attack's hits through the damage service
    ///   5. <see cref="CastResolved"/>: post-outcome passive triggers (bounded, once per cast)
    ///
    /// Turn boundaries (called by the controller) run passive hooks first, then buff / status countdowns in the
    /// modifier system, in acquisition / application order.
    /// </summary>
    public sealed class CombatBuildRuntime : MonoBehaviour
    {
        private static int encounterCounter;

        private readonly List<PassiveHook> hooks = new();
        private readonly Dictionary<string, DefenseNoteOutcome> pendingDefense = new();
        private Action<string, Vector3, Color> showText;
        private EnemyTurnSummary enemyTurn;
        private int castCounter;
        private bool ended;
        // Stagger (Tremor, Mudlock): queued for the enemy's next attack, limited per battle by the enemy's profile.
        private bool staggerPending;
        private string staggerLabel;
        private int staggersThisBattle;
        private int lastStaggerEnemyTurn = -99;
        private int zapsThisTurn;
        // One-shot mana discount on the next ability (Tailwind).
        private float pendingDiscount;
        private string pendingDiscountLabel;

        /// <summary>The runtime of the battle in progress (null outside battle). Quotes use it for live cost changes.</summary>
        public static CombatBuildRuntime Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        public RunBuildState Build { get; private set; }
        public PlayerCombatant Player { get; private set; }
        public EnemyCombatant Enemy { get; private set; }
        public CombatModifierSystem Modifiers { get; private set; }
        public BuildBalanceRules Rules { get; private set; }
        public CombatDamageService Damage { get; private set; }
        public CounterPool Counters { get; private set; }
        public CombatEventLog Events { get; } = new();
        /// <summary>Elemental marks on the enemy and their reactions.</summary>
        public ElementalMarks Marks { get; private set; }
        /// <summary>The rhythm board (enemy notes) for zaps and walls. Set by the combat controller; null in tests / legacy.</summary>
        public ICombatNoteBoard Board { get; set; }
        /// <summary>Enemy note damage multiplier for the current enemy turn (a staggered single-step attack).</summary>
        public float EnemyDamageScale { get; private set; } = 1f;
        /// <summary>Enemy note damage multiplier from run depth (set at encounter start).</summary>
        public float NoteDamageScale { get; private set; } = 1f;
        public int ZapsThisTurn => zapsThisTurn;
        public bool StaggerPending => staggerPending;
        public BuildCombatStats Stats { get; private set; } = new();
        public int EncounterId { get; private set; }
        public int PlayerTurn { get; private set; }
        public int EnemyTurn { get; private set; }
        public CombatState Phase { get; set; }
        public bool IsActive { get; private set; }
        public CastSnapshot CurrentCast { get; private set; }
        public CastSnapshot LastCast { get; private set; }
        public EnemyTurnSummary LastEnemyTurn { get; private set; }
        /// <summary>Primary role of the previous resolved cast this encounter (None before the first).</summary>
        public AbilityRole PreviousCastRole { get; private set; }
        public IReadOnlyList<PassiveHook> ActivePassives => hooks;
        /// <summary>Payouts stop as soon as either side is defeated or the encounter ended.</summary>
        public bool CombatOver => ended || (Player != null && Player.IsDefeated) || (Enemy != null && Enemy.IsDefeated);

        public event Action Changed;

        private void Awake() => EnsureServices();

        private void EnsureServices()
        {
            Rules ??= BuildBalanceRules.Load();
            Damage ??= new CombatDamageService(this);
            Counters ??= new CounterPool(this);
            Marks ??= new ElementalMarks(this);
        }

        // ---------- Encounter lifecycle ----------

        /// <summary>
        /// Reconstructs the encounter runtime from the retained build (null build = legacy: no passives). Call before
        /// the combatants capture their battle-start state (max health may change).
        /// </summary>
        public void BeginEncounter(PlayerCombatant player, EnemyCombatant enemy, CombatModifierSystem modifiers,
            RunBuildState build, Action<string, Vector3, Color> floatingText = null)
        {
            EnsureServices();
            Rules = BuildBalanceRules.Load();
            Player = player;
            Enemy = enemy;
            Modifiers = modifiers;
            Build = build;
            showText = floatingText;
            EncounterId = ++encounterCounter;
            PlayerTurn = 0;
            EnemyTurn = 0;
            castCounter = 0;
            ended = false;
            IsActive = true;
            CurrentCast = null;
            LastCast = null;
            LastEnemyTurn = null;
            enemyTurn = null;
            PreviousCastRole = AbilityRole.None;
            staggerPending = false;
            staggerLabel = null;
            staggersThisBattle = 0;
            lastStaggerEnemyTurn = -99;
            zapsThisTurn = 0;
            pendingDiscount = 0f;
            pendingDiscountLabel = null;
            EnemyDamageScale = 1f;
            Active = this;
            pendingDefense.Clear();
            Events.Clear();
            Counters.Reset();
            Stats = new BuildCombatStats();
            hooks.Clear();

            if (build != null)
            {
                List<AbilityQuote> equipped = build.EquippedQuotes();
                foreach (PassiveInstance passive in build.Passives)
                {
                    // Inactive passives (no compatible ability equipped) stay owned but do nothing this encounter.
                    if (passive?.Definition == null || !RunBuildState.IsPassiveActive(passive, equipped)) continue;
                    hooks.Add(new PassiveHook { Runtime = this, Instance = passive, State = new Dictionary<string, int>() });
                }
            }
            if (player != null)
            {
                player.SetMaxHealthOverride(build != null ? ComputeMaxHealth(build, player.BaseMaxHealth) : 0);
                player.SetMaxManaOverride(build != null && build.BonusMaxMana > 0 ? player.BaseMaxMana + build.BonusMaxMana : 0);
            }
            // Run depth: enemies get tougher as the run goes on (numbers in the balance rules' Progression).
            ProgressionRules progression = Rules.Progression;
            int depth = build != null ? build.Depth : 0;
            NoteDamageScale = progression.NoteDamageScale(depth);
            if (enemy != null) enemy.SetMaxHealthScale(progression.EnemyHealthScale(depth));
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnEncounterStart(hook));
            RaiseChanged();
        }

        public void EndEncounter(bool victory)
        {
            if (!IsActive) return;
            ended = true;
            IsActive = false;
            if (Active == this) Active = null;
            if (Counters.Charges > 0) Stats.UnusedBenefits.Add($"{Counters.Charges} counter charge(s) unspent");
            if (Modifiers != null)
            {
                foreach (NextAttackBonusBuff pending in Modifiers.OfType<NextAttackBonusBuff>())
                    Stats.UnusedBenefits.Add($"{pending.Label} ({BuildTagUtility.Percent(pending.Strength)}) unused");
                ShieldBuff shield = Modifiers.Find<ShieldBuff>(ShieldBuff.Key);
                if (shield != null && shield.Capacity > 0) Stats.UnusedBenefits.Add($"{shield.Capacity} shield left");
            }
            RaiseChanged();
        }

        /// <summary>Copies the build attribution into the battle report (base output vs. passive contributions, unused benefits).</summary>
        public void WriteTo(CombatReport report)
        {
            if (report == null) return;
            report.BuildName = Build != null ? Build.DisplayName : "Default loadout";
            foreach (KeyValuePair<string, int> pair in Stats.Contributions) report.BuildContributions[pair.Key] = pair.Value;
            report.UnusedBenefits.AddRange(Stats.UnusedBenefits);
            report.DamageReflected = Stats.Reflected;
            report.StatusDamage = Stats.StatusDamage;
            report.DamageResisted = Stats.Resisted;
            report.DamageAbsorbed = Stats.Absorbed;
            report.DamagePrevented = Stats.Prevented;
            report.Overheal = Stats.Overheal;
            report.ManaRestoredByBuild = Stats.ManaRestored;
            report.CounterChargesSpent = Stats.CountersSpent;
        }

        /// <summary>Max health after the build's passives (only active ones count).</summary>
        public static int ComputeMaxHealth(RunBuildState build, int baseMaxHealth)
        {
            int max = Mathf.Max(1, baseMaxHealth);
            if (build == null) return max;
            // Growth rewards first, so percentage passives scale them too.
            max += build.BonusMaxHealth;
            List<AbilityQuote> equipped = build.EquippedQuotes();
            foreach (PassiveInstance passive in build.Passives)
            {
                if (passive?.Definition == null || !RunBuildState.IsPassiveActive(passive, equipped)) continue;
                foreach (PassiveEffect effect in passive.Definition.Effects)
                    if (effect != null) max = effect.ModifyMaxHealth(max, passive.Level);
            }
            return Mathf.Max(1, max);
        }

        // ---------- Turn boundaries ----------

        public void OnEnemyTurnStarted()
        {
            EnemyTurn++;
            enemyTurn = new EnemyTurnSummary { EnemyTurn = EnemyTurn };
            zapsThisTurn = 0;
            pendingDefense.Clear();
            Boundary(TurnBoundary.EnemyTurnStart);
        }

        /// <summary>Whole-enemy-turn triggers (once, spanning every attack step), then passive countdowns.</summary>
        public void OnEnemyTurnEnded(bool interrupted)
        {
            if (enemyTurn != null)
            {
                enemyTurn.Interrupted = interrupted;
                if (!CombatOver)
                    foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnEnemyTurnEnded(hook, enemyTurn));
                LastEnemyTurn = enemyTurn;
            }
            enemyTurn = null;
            EnemyDamageScale = 1f;
            Boundary(TurnBoundary.EnemyTurnEnd);
        }

        public void OnPlayerTurnStarted()
        {
            PlayerTurn++;
            Boundary(TurnBoundary.PlayerTurnStart);
        }

        public void OnPlayerTurnEnded()
        {
            Counters.OnPlayerTurnEnded();
            Boundary(TurnBoundary.PlayerTurnEnd);
        }

        private void Boundary(TurnBoundary boundary)
        {
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnTurnBoundary(hook, boundary));
            RaiseChanged();
        }

        // ---------- Casts ----------

        /// <summary>At commitment: snapshot the paid cost / quote, reserve pending next-attack bonuses for damaging casts.</summary>
        public CastSnapshot BeginCast(int inputLane, int slotIndex, AbilityRuntimeInstance ability)
        {
            if (ability == null) return null;
            AbilityCommit commit = ability.LastCommit;
            var cast = new CastSnapshot
            {
                CastId = $"e{EncounterId}-c{++castCounter}",
                EncounterId = EncounterId,
                PlayerTurn = PlayerTurn,
                Ability = ability,
                Quote = commit?.Quote ?? ability.Quote(),
                AbilityInstanceId = ability.BuildInstance?.InstanceId ?? ability.Definition?.Id ?? "ability",
                SlotIndex = slotIndex,
                InputLane = inputLane,
                PaidCost = commit?.PaidCost ?? 0,
                Cooldown = commit?.Cooldown ?? 0
            };
            Stats.Casts++;
            Stats.ManaPaid += cast.PaidCost;
            // A pending one-shot discount (Tailwind) was in this cast's quote: it is used up now.
            if (pendingDiscount > 0f)
            {
                cast.AddLog($"{pendingDiscountLabel}: -{Mathf.RoundToInt(pendingDiscount * 100f)}% MP");
                pendingDiscount = 0f;
                pendingDiscountLabel = null;
            }
            Record(new CombatEvent
            {
                Kind = CombatEventKind.CastCommitted, SourceId = cast.AbilityInstanceId, RootCauseId = cast.CastId, CastId = cast.CastId,
                Roles = cast.Roles, Delivery = cast.Delivery, Element = cast.Element, SlotIndex = slotIndex, InputLane = inputLane,
                Note = cast.Definition != null ? cast.Definition.DisplayName : null
            });
            if (cast.PaidCost > 0)
                Record(new CombatEvent { Kind = CombatEventKind.ManaPaid, SourceId = cast.AbilityInstanceId, RootCauseId = cast.CastId, CastId = cast.CastId, Actual = cast.PaidCost });

            if (cast.HasRole(AbilityRole.Damage) && Modifiers != null)
            {
                foreach (NextAttackBonusBuff pending in Modifiers.OfType<NextAttackBonusBuff>().ToList())
                {
                    cast.Reserved.Add(pending);
                    Modifiers.Remove(pending);
                }
            }
            CurrentCast = cast;
            RaiseChanged();
            return cast;
        }

        /// <summary>
        /// Performance is final: add reserved bonuses and passive pre-outcome modifiers. <paramref name="completed"/> is
        /// false for a cancelled / interrupted chart (no execution-based bonuses).
        /// </summary>
        public void PreOutcome(CastSnapshot cast, RhythmPerformanceResult performance, bool completed)
        {
            if (cast == null || cast.Frozen) return;
            cast.Performance = performance;
            cast.ExecutionEligible = completed && performance.ExpectedNoteCount > 0;
            cast.AddLog($"Performance {performance.AverageWeight:0.00} ({(cast.ExecutionEligible ? "eligible" : "no execution bonus")})");
            foreach (ICombatModifierRuntime reserved in cast.Reserved)
                if (reserved is NextAttackBonusBuff bonus)
                    cast.AddModifier(ModifierGroup.Buff, EffectKind.Damage, bonus.Strength, bonus.SourceId, bonus.Label);
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnPreOutcome(hook, cast));
        }

        public void CastResolved(CastSnapshot cast)
        {
            if (cast == null) return;
            Record(new CombatEvent
            {
                Kind = CombatEventKind.CastResolved, SourceId = cast.AbilityInstanceId, RootCauseId = cast.CastId, CastId = cast.CastId,
                Roles = cast.Roles, Delivery = cast.Delivery, Element = cast.Element, Actual = cast.DamageDealt,
                Note = $"dmg {cast.DamageDealt}, heal {cast.Healed}, shield {cast.ShieldGained}"
            });
            foreach (KeyValuePair<string, int> pair in cast.Contributions) Stats.Contribute(pair.Key, pair.Value);
            if (!CombatOver)
                foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnCastResolved(hook, cast));
            PreviousCastRole = BuildTagUtility.PrimaryRole(cast.Roles);
            LastCast = cast;
            CurrentCast = null;
            RaiseChanged();
        }

        // ---------- Enemy turn defense ----------

        /// <summary>Damage transaction for one enemy note (called by the pattern runner when the note would hurt).</summary>
        public int ApplyDefenseDamage(RhythmJudgementResult result, int noteDamage, int attempted)
        {
            DefenseNoteOutcome outcome = PendingOutcome(result, noteDamage, attempted);
            return Damage.ApplyIncomingNoteDamage(outcome);
        }

        /// <summary>After every defense note's damage transaction (also for notes that dealt no damage).</summary>
        public void DefenseNoteSettled(RhythmJudgementResult result, int noteDamage, int attempted, int actual)
        {
            DefenseNoteOutcome outcome = PendingOutcome(result, noteDamage, attempted);
            pendingDefense.Remove(result.NoteId ?? string.Empty);
            outcome.Actual = actual;
            // Cleared by a board effect (zap, wall): not an opportunity, not a miss, triggers nothing.
            if (result.Source == NoteResolutionSource.Modifier)
            {
                if (enemyTurn != null) enemyTurn.Cleared++;
                return;
            }
            if (enemyTurn != null)
            {
                enemyTurn.Opportunities++;
                if (outcome.IsPlayerExecution) enemyTurn.PlayerJudgements[(int)result.Judgement]++;
                else enemyTurn.NonPlayerResolutions++;
                enemyTurn.DamageTaken += actual;
            }
            if (CombatOver) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnDefenseNoteSettled(hook, outcome));
            ApplyReflectBuffs(outcome);
            if (outcome.IsPlayerExecution && Modifiers != null && !CombatOver)
                foreach (IDefenseExecutionModifier reactive in Modifiers.OfType<IDefenseExecutionModifier>().ToList())
                    reactive.OnPlayerDefense(this, outcome);
            RaiseChanged();
        }

        private DefenseNoteOutcome PendingOutcome(RhythmJudgementResult result, int noteDamage, int attempted)
        {
            string key = result.NoteId ?? string.Empty;
            if (pendingDefense.TryGetValue(key, out DefenseNoteOutcome outcome)) return outcome;
            outcome = new DefenseNoteOutcome
            {
                Result = result, NoteDamage = noteDamage, Attempted = attempted,
                RootCauseId = $"e{EncounterId}-et{EnemyTurn}-{key}"
            };
            pendingDefense[key] = outcome;
            return outcome;
        }

        private void ApplyReflectBuffs(DefenseNoteOutcome outcome)
        {
            if (!outcome.IsPlayerExecution || outcome.NoteDamage <= 0 || Modifiers == null) return;
            foreach (ReflectBuff reflect in Modifiers.OfType<ReflectBuff>().ToList())
            {
                float fraction = reflect.FractionFor(outcome.Result.Judgement);
                if (fraction <= 0f) continue;
                int amount = reflect.Take(Mathf.RoundToInt(outcome.NoteDamage * fraction));
                if (amount > 0)
                    Damage.DamageEnemy(amount, ElementType.None, reflect.SourceId, outcome.RootCauseId, CombatEventKind.ReflectDamage,
                        applyAffinity: true, secondary: true, label: reflect.Label);
            }
        }

        internal int ModifyIncomingDamage(RhythmJudgementResult result, int amount)
        {
            foreach (PassiveHook hook in hooks)
                foreach (PassiveEffect effect in hook.Definition.Effects)
                    if (effect != null) amount = Mathf.Max(0, effect.ModifyIncomingDamage(hook, result, amount));
            return amount;
        }

        // ---------- Buffs & statuses ----------

        public void ApplyBuff(BuffSpec spec, string rootId)
        {
            if (spec == null || Modifiers == null || CombatOver) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.ModifyBuff(hook, spec));
            spec.Turns = Mathf.Max(1, spec.Turns);
            switch (spec.Kind)
            {
                case BuffKind.NextAttackBonus:
                    NextAttackBonusBuff pending = Modifiers.Find<NextAttackBonusBuff>("next-attack:" + spec.SourceId);
                    if (pending != null)
                    {
                        pending.Replace(spec);
                        if (spec.Icon != null) pending.Icon = spec.Icon;
                    }
                    else Modifiers.Add(new NextAttackBonusBuff(spec) { Icon = spec.Icon });
                    break;
                case BuffKind.Reflect:
                    Modifiers.Find<ReflectBuff>("reflect:" + spec.SourceId)?.Expire();
                    Modifiers.Add(new ReflectBuff(spec) { Icon = spec.Icon });
                    break;
                case BuffKind.DamageReduction:
                    Modifiers.Find<DamageReductionBuff>("reduction:" + spec.SourceId)?.Expire();
                    Modifiers.Add(new DamageReductionBuff(spec) { Icon = spec.Icon });
                    break;
            }
            Modifiers.NotifyChanged();
            Record(new CombatEvent { Kind = CombatEventKind.BuffApplied, SourceId = spec.SourceId, RootCauseId = rootId, Note = $"{spec.Label} {spec.Kind} {spec.Strength:0.##} x{spec.Turns}" });
            ShowAtPlayer(spec.Kind switch
            {
                BuffKind.NextAttackBonus => $"NEXT ATTACK {BuildTagUtility.Percent(spec.Strength)}",
                BuffKind.Reflect => $"REFLECT {Mathf.RoundToInt(spec.Strength * 100f)}%",
                _ => $"GUARD {Mathf.RoundToInt(spec.Strength * 100f)}%"
            }, new Color(1f, 0.9f, 0.5f));
        }

        /// <summary>
        /// Applies a status to the enemy (or re-applies it by its stacking policy). <paramref name="stacks"/> &gt; 1 adds
        /// several stacks at once (marks). Returns false when the enemy is immune or combat is over.
        /// </summary>
        public bool ApplyStatus(StatusSpec spec, int damagePerTick, string rootId, Sprite icon = null, int stacks = 1)
        {
            if (spec == null || Modifiers == null || Enemy == null || CombatOver) return false;
            StatusResponse response = Enemy.Responses?.Status(spec.statusId);
            if (response != null && response.immune)
            {
                Record(new CombatEvent { Kind = CombatEventKind.StatusApplied, SourceId = rootId, RootCauseId = rootId, Note = spec.displayName + " (immune)" });
                ShowAtEnemy("IMMUNE", new Color(0.7f, 0.7f, 0.7f));
                return false;
            }
            int ticks = spec.ticks;
            foreach (PassiveHook hook in hooks)
                foreach (PassiveEffect effect in hook.Definition.Effects)
                    effect?.ModifyStatus(hook, spec, ref damagePerTick, ref ticks);
            int maxStacks = StatusStackCap(spec);
            if (response != null)
            {
                ticks = Mathf.RoundToInt(ticks * response.durationScale);
                damagePerTick = Mathf.RoundToInt(damagePerTick * response.potencyScale);
                if (response.maxStacks > 0) maxStacks = response.maxStacks;
            }
            ticks = Mathf.Max(1, ticks);
            stacks = Mathf.Max(1, stacks);
            StatusInstance existing = Modifiers.Find<StatusInstance>("status:" + spec.statusId);
            if (existing != null)
            {
                existing.MaxStacks = Mathf.Max(existing.MaxStacks, maxStacks);
                if (spec.stacking == StackPolicy.CappedAdd || stacks > 1) existing.Add(stacks, damagePerTick, ticks, rootId);
                else existing.Reapply(damagePerTick, ticks, rootId);
                if (icon != null) existing.Icon = icon;
            }
            else
            {
                var created = new StatusInstance(this, spec, damagePerTick, ticks, maxStacks, rootId) { Icon = icon };
                if (stacks > 1) created.AddStacks(stacks - 1);
                Modifiers.Add(created);
            }
            Modifiers.NotifyChanged();
            Record(new CombatEvent
            {
                Kind = CombatEventKind.StatusApplied, SourceId = rootId, RootCauseId = rootId, Element = spec.element,
                Actual = damagePerTick, Note = $"{spec.displayName} {damagePerTick}/tick x{ticks}"
            });
            ShowAtEnemy(spec.displayName.ToUpperInvariant() + (stacks > 1 ? " x" + stacks : string.Empty), new Color(1f, 0.6f, 0.3f));
            if (ElementalMarks.IsMark(spec.statusId)) Marks.OnMarkApplied(spec.statusId, rootId);
            return true;
        }

        /// <summary>A status's stack cap after passives (Pyre Keeper, Capacitor).</summary>
        public int StatusStackCap(StatusSpec spec)
        {
            if (spec == null) return 1;
            int cap = spec.maxStacks;
            if (spec.stacking == StackPolicy.CappedAdd)
                foreach (PassiveHook hook in hooks)
                    foreach (PassiveEffect effect in hook.Definition.Effects)
                        if (effect != null) cap += effect.StatusStackBonus(hook, spec.statusId);
            return Mathf.Max(1, cap);
        }

        /// <summary>Called by a status at its tick boundary. Affinity applies once per tick transaction.</summary>
        internal void TickStatus(StatusInstance status, int amount)
        {
            if (status == null || CombatOver) return;
            Damage.DamageEnemy(amount, status.Spec.element, "status:" + status.Spec.statusId, status.RootCauseId,
                CombatEventKind.StatusDamage, applyAffinity: true, secondary: false, label: status.Spec.displayName);
        }

        // ---------- Elements, board, control ----------

        /// <summary>Passives adjust a reaction (Catalyst).</summary>
        internal void ModifyReaction(ReactionContext context)
        {
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.ModifyReaction(hook, context));
        }

        /// <summary>A lethal note is about to land: true when a passive (Unyielding) keeps the player at 1 HP.</summary>
        internal bool PreventLethal()
        {
            if (CombatOver) return false;
            foreach (PassiveHook hook in hooks)
                foreach (PassiveEffect effect in hook.Definition.Effects)
                    if (effect != null && effect.PreventLethal(hook))
                    {
                        Record(new CombatEvent { Kind = CombatEventKind.PassiveTriggered, SourceId = hook.SourceId, Note = hook.Label + ": survived at 1 HP" });
                        ShowAtPlayer(hook.Label.ToUpperInvariant() + "!", new Color(1f, 0.85f, 0.35f));
                        return true;
                    }
            return false;
        }

        /// <summary>
        /// Queues a stagger: the enemy's next attack loses its last step (a single-step attack deals less damage).
        /// Refused when the enemy's control limit is used up or it was staggered on its last turn.
        /// </summary>
        public bool TryStagger(string sourceId, string rootId, string label)
        {
            if (CombatOver || Enemy == null) return false;
            int limit = Enemy.Responses?.MaxStaggers ?? 0;
            bool resisted = staggerPending
                            || (limit > 0 && staggersThisBattle >= limit)
                            || (Rules.Elements.noBackToBackStagger && lastStaggerEnemyTurn >= EnemyTurn && EnemyTurn > 0);
            Record(new CombatEvent { Kind = CombatEventKind.Stagger, SourceId = sourceId, RootCauseId = rootId, Note = resisted ? label + " (resisted)" : label });
            if (resisted)
            {
                ShowAtEnemy("RESISTED", new Color(0.7f, 0.7f, 0.7f));
                return false;
            }
            staggerPending = true;
            staggerLabel = label;
            ShowAtEnemy("STAGGER", new Color(0.85f, 0.7f, 0.45f));
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Called when the enemy's attack is chosen: true = drop its last step. A single-step attack keeps its notes,
        /// but they deal less damage this enemy turn (<see cref="EnemyDamageScale"/>).
        /// </summary>
        public bool ConsumeStagger(int stepCount)
        {
            if (!staggerPending) return false;
            staggerPending = false;
            staggersThisBattle++;
            lastStaggerEnemyTurn = EnemyTurn;
            Stats.Staggers++;
            bool skipStep = stepCount > 1;
            if (!skipStep) EnemyDamageScale = Mathf.Clamp01(Rules.Elements.singleStepStaggerDamage);
            Record(new CombatEvent
            {
                Kind = CombatEventKind.Stagger, SourceId = "stagger", Note = (staggerLabel ?? "Stagger") + (skipStep ? ": last step skipped" : ": weakened")
            });
            ShowAtEnemy(skipStep ? "STAGGERED" : "OFF BALANCE", new Color(0.85f, 0.7f, 0.45f));
            RaiseChanged();
            return skipStep;
        }

        /// <summary>
        /// Clears up to <paramref name="count"/> upcoming enemy notes (soonest first) and deals Lightning damage per
        /// note. Chains visually from <paramref name="from"/>. Bounded by the per-turn zap limit. Returns notes cleared.
        /// </summary>
        public int Zap(int count, int damagePerNote, Vector3 from, string sourceId, string rootId, string label, bool secondary = false)
        {
            if (Board == null || !Board.IsDefending || count <= 0 || CombatOver) return 0;
            int limit = Rules.Elements.maxZapsPerEnemyTurn;
            int profileLimit = Enemy != null && Enemy.Responses != null ? Enemy.Responses.MaxZapsPerTurn : 0;
            if (profileLimit > 0) limit = Mathf.Min(limit, profileLimit);
            count = Mathf.Min(count, limit - zapsThisTurn);
            if (count <= 0) return 0;
            if (secondary && !Events.TrySpendSecondary(rootId, Rules.MaxSecondaryPerRoot)) return 0;
            int zapped = 0;
            Vector3 origin = from;
            var arc = new Color(0.65f, 0.85f, 1f);
            foreach (BoardNote note in Board.Upcoming(Rules.Elements.zapWindowSeconds).Take(count).ToList())
            {
                if (!Board.Clear(note)) continue;
                zapped++;
                zapsThisTurn++;
                Stats.NotesCleared++;
                Board.ShowArc(origin, note.Position, arc);
                origin = note.Position;
                Record(new CombatEvent { Kind = CombatEventKind.NoteCleared, SourceId = sourceId, RootCauseId = rootId, ChartLane = note.Lane, Note = label + " zap" });
                if (showText != null) showText("ZAP", note.Position + Vector3.up * 0.4f, arc);
                if (damagePerNote > 0)
                    Damage.DamageEnemy(damagePerNote, ElementType.Lightning, sourceId, rootId, CombatEventKind.ZapDamage,
                        applyAffinity: true, secondary: false, label: label);
                if (CombatOver) break;
            }
            if (zapped > 0) RaiseChanged();
            return zapped;
        }

        /// <summary>Clears one specific note (Stone Wall). No damage.</summary>
        public bool ClearNote(BoardNote note, string sourceId, string label)
        {
            if (Board == null || !Board.Clear(note)) return false;
            Stats.NotesCleared++;
            Record(new CombatEvent { Kind = CombatEventKind.NoteCleared, SourceId = sourceId, ChartLane = note.Lane, Note = label });
            if (showText != null) showText("BLOCK", note.Position + Vector3.up * 0.4f, new Color(0.85f, 0.7f, 0.45f));
            return true;
        }

        /// <summary>Per-frame board effects while an enemy pattern runs (Stone Wall). Called from Update; public for tests.</summary>
        public void TickBoard()
        {
            if (Board == null || Modifiers == null || !Board.IsDefending || CombatOver) return;
            foreach (IBoardModifier modifier in Modifiers.OfType<IBoardModifier>().ToList()) modifier.TickBoard(this, Board);
        }

        private void Update()
        {
            if (IsActive) TickBoard();
        }

        /// <summary>
        /// A judgement from any chart. Player-ability charts: live effects of the casting ability (Chain Spark arcs fire
        /// on each Perfect). Only player input counts.
        /// </summary>
        public void OnChartJudgement(RhythmJudgementResult result, PatternRunMode mode)
        {
            if (result.Source != NoteResolutionSource.PlayerInput || CombatOver) return;
            if (mode != PatternRunMode.PlayerAbility || CurrentCast?.Quote == null) return;
            foreach (ILiveChartEffect live in CurrentCast.Quote.Effects.OfType<ILiveChartEffect>().ToList())
                live.OnChartJudgement(this, CurrentCast, result);
        }

        /// <summary>The next ability costs this much less mana (one pending discount; the larger one wins).</summary>
        public void GrantNextCostDiscount(float fraction, string label)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction <= pendingDiscount) return;
            pendingDiscount = fraction;
            pendingDiscountLabel = label;
            ShowAtPlayer($"NEXT -{Mathf.RoundToInt(fraction * 100f)}% MP", new Color(0.6f, 0.95f, 0.85f));
            RaiseChanged();
        }

        public float PendingCostDiscount => pendingDiscount;

        /// <summary>Live cost changes on a quote (pending discount). Called by the ability resolver while a battle runs.</summary>
        internal void AdjustQuote(AbilityQuote quote)
        {
            if (quote == null || pendingDiscount <= 0f || quote.ManaCost <= 0) return;
            int before = quote.ManaCost;
            quote.ManaCost = Mathf.Max(0, Mathf.RoundToInt(quote.ManaCost * (1f - pendingDiscount)));
            if (quote.ManaCost != before) quote.Changes.Add($"{pendingDiscountLabel}: {before} -> {quote.ManaCost} MP (next ability)");
        }

        // ---------- Helpers ----------

        internal void NotifyHealed(HealOutcome heal)
        {
            if (CombatOver) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnHealed(hook, heal));
        }

        public void RecordContribution(string label, int amount) => Stats.Contribute(label, amount);

        public CombatEvent Record(CombatEvent combatEvent)
        {
            if (combatEvent == null) return null;
            combatEvent.EncounterId = EncounterId;
            combatEvent.Turn = Phase is CombatState.EnemyTurnStart or CombatState.EnemyTurnExecuting or CombatState.EnemyTurnEnd
                ? EnemyTurn : PlayerTurn;
            combatEvent.Phase = Phase;
            return Events.Record(combatEvent);
        }

        public void Toast(string text, Color color) => ShowAtPlayer(text, color);

        internal void ShowAtPlayer(string text, Color color)
        {
            if (showText != null && Player != null) showText(text, Player.transform.position + Vector3.up * 1.5f, color);
        }

        internal void ShowAtEnemy(string text, Color color)
        {
            if (showText != null && Enemy != null) showText(text, Enemy.transform.position + Vector3.up * 1.4f, color);
        }

        internal void RaiseChanged() => Changed?.Invoke();

        private static void ForEachEffect(PassiveHook hook, Action<PassiveEffect> action)
        {
            if (hook?.Definition == null) return;
            foreach (PassiveEffect effect in hook.Definition.Effects)
                if (effect != null) action(effect);
        }
    }
}
