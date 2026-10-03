using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    /// <summary>
    /// Run build system (docs/design/run-resonance-architecture.md): passives, dedicated upgrades, filters, affinity,
    /// counters, reflection, statuses, rewards. Casts and enemy turns are driven directly through the build runtime, the
    /// same calls CombatController / RhythmAbilitySystem make.
    /// </summary>
    public sealed class RunBuildTests
    {
        private readonly List<Object> created = new();
        private RhythmChart chart;

        [SetUp]
        public void SetUp()
        {
            chart = ScriptableObject.CreateInstance<RhythmChart>();
            created.Add(chart);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        // ---------------- helpers ----------------

        private sealed class Rig
        {
            public GameObject Root;
            public PlayerCombatant Player;
            public EnemyCombatant Enemy;
            public CombatModifierSystem Modifiers;
            public CombatBuildRuntime Runtime;
            public RunBuildState Build;
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private Rig CreateRig(RunBuildState build, int enemyHealth = 5000, EnemyResponseProfile responses = null)
        {
            var root = new GameObject("Build Test Rig");
            created.Add(root);
            var rig = new Rig
            {
                Root = root,
                Player = root.AddComponent<PlayerCombatant>(),
                Enemy = new GameObject("Build Test Enemy").AddComponent<EnemyCombatant>(),
                Modifiers = root.AddComponent<CombatModifierSystem>(),
                Runtime = root.AddComponent<CombatBuildRuntime>(),
                Build = build
            };
            created.Add(rig.Enemy.gameObject);
            SetField(rig.Enemy, "fallbackMaxHealth", enemyHealth);
            SetField(rig.Enemy, "currentHealth", enemyHealth);
            if (responses != null) rig.Enemy.SetResponseOverride(responses);
            rig.Runtime.BeginEncounter(rig.Player, rig.Enemy, rig.Modifiers, build);
            return rig;
        }

        private AbilityDefinition Ability(string id, AbilityRole roles, AbilityDelivery delivery, int cost, int power,
            ElementType element = ElementType.None, params AbilityEffect[] effects)
        {
            var builder = new AbilityDefinition.Builder(id, id).Tags(roles, delivery).Cost(cost).Power(power).Element(element).Chart(chart);
            foreach (AbilityEffect effect in effects) builder.Effect(effect);
            AbilityDefinition definition = builder.Build();
            created.Add(definition);
            return definition;
        }

        private PassiveDefinition Passive(string id, params PassiveEffect[] effects)
        {
            var builder = new PassiveDefinition.Builder(id, id, PassiveCategory.DamageEnhancement);
            foreach (PassiveEffect effect in effects) builder.Effect(effect);
            PassiveDefinition definition = builder.Build();
            created.Add(definition);
            return definition;
        }

        private static RhythmPerformanceResult Performance(params HitJudgement[] judgements) =>
            RhythmPerformanceCalculator.Calculate(judgements.Length,
                judgements.Select((j, i) => new RhythmJudgementResult("a" + i, 1, j, 0f, Vector3.zero, NoteResolutionSource.PlayerInput)).ToList(), null);

        private static RhythmPerformanceResult AllPerfect(int notes = 4) =>
            Performance(Enumerable.Repeat(HitJudgement.Perfect, notes).ToArray());

        /// <summary>Commit (pay), snapshot, pre-outcome, freeze, deliver over hits, post-outcome.</summary>
        private static CastSnapshot Cast(Rig rig, AbilityInstance instance, RhythmPerformanceResult performance, int hits = 1, bool completed = true)
        {
            var runtimeAbility = new AbilityRuntimeInstance(instance, r => AbilityResolver.Resolve(r.Definition, r.BuildInstance, rig.Build));
            Assert.IsTrue(runtimeAbility.Commit(rig.Player), "commit");
            int slot = rig.Build != null ? rig.Build.SlotOf(instance) : -1;
            CastSnapshot cast = rig.Runtime.BeginCast(slot + 1, slot, runtimeAbility);
            rig.Runtime.PreOutcome(cast, performance, completed);
            var context = new AbilityEffectContext
            {
                Player = rig.Player, Enemy = rig.Enemy, Ability = instance.Definition, Performance = performance,
                Modifiers = rig.Modifiers, Rules = CombatResourceRules.Load(), Build = rig.Runtime, Cast = cast
            };
            var resolution = new AbilityResolution(context, cast.Quote.Effects, hits);
            for (int i = 0; i < hits; i++) resolution.Hit(1f);
            resolution.Finish();
            rig.Runtime.CastResolved(cast);
            return cast;
        }

        private static int enemyNote;

        /// <summary>One enemy turn: every judgement is one 10-damage note.</summary>
        private static void EnemyTurn(Rig rig, NoteResolutionSource source, params HitJudgement[] judgements)
        {
            CombatResourceRules rules = CombatResourceRules.Load();
            rig.Runtime.OnEnemyTurnStarted();
            rig.Modifiers.OnEnemyTurnStarted();
            foreach (HitJudgement judgement in judgements)
            {
                var result = new RhythmJudgementResult("n" + enemyNote++, 1, judgement, 0f, Vector3.zero, source);
                int attempted = rules.DefenseDamage(10, judgement);
                int actual = attempted > 0 ? rig.Runtime.ApplyDefenseDamage(result, 10, attempted) : 0;
                rig.Runtime.DefenseNoteSettled(result, 10, attempted, actual);
            }
            rig.Runtime.OnEnemyTurnEnded(false);
            rig.Modifiers.OnEnemyTurnEnded();
        }

        private static void EnemyTurn(Rig rig, params HitJudgement[] judgements) => EnemyTurn(rig, NoteResolutionSource.PlayerInput, judgements);

        private static void PlayerTurnBoundaries(Rig rig)
        {
            rig.Runtime.OnPlayerTurnEnded();
            rig.Modifiers.OnPlayerTurnEnded();
            rig.Runtime.OnPlayerTurnStarted();
            rig.Modifiers.OnPlayerTurnStarted();
        }

        // ---------------- costs / slots / ownership ----------------

        [Test]
        public void EffectiveCost_ShownInQuote_IsWhatCommitPays()
        {
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 20, 50);
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(bolt);
            build.AddPassive(Passive("thrift", new ManaCostPassive(new EffectFilter(AbilityDelivery.Spell), 0.25f, 0f, 1)));
            Rig rig = CreateRig(build);

            AbilityQuote quote = build.Quote(instance);
            Assert.That(quote.ManaCost, Is.EqualTo(15));
            int before = rig.Player.CurrentMana;
            CastSnapshot cast = Cast(rig, instance, AllPerfect());
            Assert.That(cast.PaidCost, Is.EqualTo(15));
            Assert.That(before - rig.Player.CurrentMana, Is.EqualTo(15));
        }

        [Test]
        public void SpellEfficiency_DoesNotTouchMeleeCosts()
        {
            AbilityDefinition slash = Ability("slash", AbilityRole.Damage, AbilityDelivery.Melee, 10, 50);
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(slash);
            build.AddPassive(Passive("thrift", new ManaCostPassive(new EffectFilter(AbilityDelivery.Spell), 0.5f, 0f, 1)));
            Assert.That(build.Quote(instance).ManaCost, Is.EqualTo(10));
        }

        [Test]
        public void DedicatedUpgrades_FollowTheInstanceBetweenSlots_AndNeverTransfer()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            AbilityDefinition other = Ability("other", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            AbilityUpgradeDefinition honed = new AbilityUpgradeDefinition.Builder("honed", "Honed").For("strike").Power(0.5f).Build();
            created.Add(honed);
            var build = new RunBuildState();
            AbilityInstance a = build.AddAbility(strike, 0);
            AbilityInstance b = build.AddAbility(other, 1);
            Assert.IsTrue(build.AddUpgrade(a, honed));
            Assert.IsFalse(build.AddUpgrade(b, honed), "incompatible ability");
            Assert.IsTrue(build.Equip(a, 3));
            Assert.That(build.SlotOf(a), Is.EqualTo(3));
            Assert.That(build.Quote(a).PowerScale, Is.EqualTo(1.5f).Within(0.0001));
            Assert.That(build.Quote(b).PowerScale, Is.EqualTo(1f).Within(0.0001));
        }

        [Test]
        public void PassiveOwnership_SurvivesEncounterRebuilds_AndNeverUsesSlots()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("vitality", new MaxHealthPassive(0.1f, 0f)));
            Rig rig = CreateRig(build);
            Assert.That(rig.Runtime.ActivePassives, Has.Count.EqualTo(1));
            rig.Runtime.EndEncounter(true);
            rig.Runtime.BeginEncounter(rig.Player, rig.Enemy, rig.Modifiers, build);
            Assert.That(rig.Runtime.ActivePassives, Has.Count.EqualTo(1));
            Assert.That(build.Equipped.Count(), Is.EqualTo(1));
            Assert.That(rig.Player.MaxHealth, Is.EqualTo(1100));
        }

        [Test]
        public void Passive_WithoutCompatibleAbility_StaysOwnedButInactive()
        {
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 20, 50);
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 50);
            PassiveDefinition glass = new PassiveDefinition.Builder("glass", "Glass", PassiveCategory.DamageEnhancement)
                .Requires(new AbilityRequirement { delivery = AbilityDelivery.Spell })
                .Effect(new MaxHealthPassive(-0.3f, 0f)).Build();
            created.Add(glass);
            var build = new RunBuildState();
            AbilityInstance boltInstance = build.AddAbility(bolt, 0);
            build.AddAbility(strike, 1);
            PassiveInstance owned = build.AddPassive(glass);
            Assert.IsTrue(build.IsPassiveActive(owned));
            build.Unequip(build.SlotOf(boltInstance));
            Assert.IsFalse(build.IsPassiveActive(owned));
            Assert.That(build.Passives, Has.Count.EqualTo(1));
            Assert.That(CombatBuildRuntime.ComputeMaxHealth(build, 1000), Is.EqualTo(1000), "inactive passive costs nothing");
        }

        // ---------------- modifier scope ----------------

        [Test]
        public void MeleeBonus_AffectsMeleeOnly_ElementBonus_AffectsMatchingComponentOnly()
        {
            // Spellblade-like: physical 50 + fire 50, melee + spell.
            AbilityDefinition blade = Ability("blade", AbilityRole.Damage, AbilityDelivery.Melee | AbilityDelivery.Spell, 0, 100,
                ElementType.Fire, new DealDamageEffect(0.5f, ElementType.None), new DealDamageEffect(0.5f, ElementType.Fire));
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance bladeInstance = build.AddAbility(blade);
            AbilityInstance boltInstance = build.AddAbility(bolt);
            build.AddPassive(Passive("melee", new ActionModifierPassive(EffectKind.Damage, new EffectFilter(AbilityDelivery.Melee), 0.2f, 0f)));
            build.AddPassive(Passive("fire", new ActionModifierPassive(EffectKind.Damage, EffectFilter.ForElement(ElementType.Fire), 0.5f, 0f)));
            Rig rig = CreateRig(build);

            CastSnapshot bladeCast = Cast(rig, bladeInstance, AllPerfect());
            // physical 50 x1.2 = 60; fire 50 x (1 + 0.2 + 0.5) = 85 (same Passive group adds).
            Assert.That(bladeCast.DamageDealt, Is.EqualTo(145));
            CastSnapshot boltCast = Cast(rig, boltInstance, AllPerfect());
            Assert.That(boltCast.DamageDealt, Is.EqualTo(150), "melee bonus must not touch a spell");
        }

        [Test]
        public void DamageBonus_NeverImprovesHealing_AndEnemyAffinityNeverTouchesSelfHealing()
        {
            AbilityDefinition drain = Ability("drain", AbilityRole.Damage | AbilityRole.Healing, AbilityDelivery.Spell, 0, 100,
                ElementType.Fire, new DealDamageEffect(1f), new HealEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(drain);
            build.AddPassive(Passive("dmg", new ActionModifierPassive(EffectKind.Damage, new EffectFilter(), 0.5f, 0f)));
            var fireResistant = new EnemyResponseProfile("FR", new[] { new DamageAffinity(ElementType.Fire, 0.5f) });
            Rig rig = CreateRig(build, responses: fireResistant);
            rig.Player.ApplyDamage(500);

            CastSnapshot cast = Cast(rig, instance, AllPerfect());
            Assert.That(cast.DamageDealt, Is.EqualTo(75), "100 x1.5 x0.5 fire affinity");
            Assert.That(cast.Healed, Is.EqualTo(100), "heal ignores damage bonus and enemy affinity");
        }

        [Test]
        public void Totals_AreFrozenBeforeHits_AndMultiHitDoesNotChangeThem()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.None, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(strike);
            Rig rig = CreateRig(build);
            CastSnapshot single = Cast(rig, instance, AllPerfect(), hits: 1);
            CastSnapshot multi = Cast(rig, instance, AllPerfect(), hits: 7);
            Assert.That(multi.DamageDealt, Is.EqualTo(single.DamageDealt));
            Assert.IsFalse(multi.AddModifier(ModifierGroup.Buff, EffectKind.Damage, 1f, "late", "late"), "frozen casts reject modifiers");
        }

        [Test]
        public void ModifierGroups_AreCapped()
        {
            var cast = new CastSnapshot();
            BuildBalanceRules rules = BuildBalanceRules.Load();
            for (int i = 0; i < 10; i++) cast.AddModifier(ModifierGroup.Passive, EffectKind.Damage, 1f, "s" + i, "s" + i);
            Assert.That(cast.Multiplier(EffectKind.Damage, ElementType.None, rules), Is.EqualTo(1f + rules.GroupBonusCap).Within(0.0001));
            cast.AddModifier(ModifierGroup.Execution, EffectKind.Damage, 5f, "x", "x");
            Assert.That(cast.Multiplier(EffectKind.Damage, ElementType.None, rules), Is.EqualTo(rules.TotalMultiplierCap).Within(0.0001));
        }

        [Test]
        public void StackingGroup_CapsSharedBonuses()
        {
            var cast = new CastSnapshot();
            cast.AddModifier(ModifierGroup.Passive, EffectKind.Damage, 0.4f, "a", "a", null, "melee", 0.5f);
            cast.AddModifier(ModifierGroup.Passive, EffectKind.Damage, 0.4f, "b", "b", null, "melee", 0.5f);
            Assert.That(cast.Multiplier(EffectKind.Damage, ElementType.None), Is.EqualTo(1.5f).Within(0.0001));
        }

        // ---------------- rhythm conditions ----------------

        [Test]
        public void MeasuredExecution_NeedsThreshold_AndACompletedChart()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.None, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(strike);
            build.AddPassive(Passive("measured", new MeasuredExecutionPassive(0.85f, 0.2f, 0f)));
            Rig rig = CreateRig(build);
            Assert.That(Cast(rig, instance, AllPerfect()).DamageDealt, Is.EqualTo(120));
            Assert.That(Cast(rig, instance, Performance(HitJudgement.Good, HitJudgement.Good)).DamageDealt, Is.EqualTo(80), "0.8 < threshold");
            Assert.That(Cast(rig, instance, AllPerfect(), completed: false).DamageDealt, Is.EqualTo(100), "cancelled chart");
            Assert.That(Cast(rig, instance, RhythmPerformanceCalculator.Calculate(0, new List<RhythmJudgementResult>(), null)).DamageDealt,
                Is.EqualTo(0), "zero-opportunity chart");
        }

        [Test]
        public void CounterPreparation_OnlyPlayerPerfects_PerTurnBudget_SpentByRiposte()
        {
            AbilityDefinition riposte = Ability("riposte", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.None,
                new DealDamageEffect(1f), new SpendCountersEffect(5, 0.3f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(riposte);
            build.AddPassive(Passive("counter", new CounterPreparationPassive(2, 0, 0.1f, 3)));
            Rig rig = CreateRig(build);

            EnemyTurn(rig, NoteResolutionSource.Timeout, HitJudgement.Perfect, HitJudgement.Perfect);
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(0), "generated judgements never count");
            EnemyTurn(rig, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Miss);
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(2), "per-enemy-turn budget");
            CastSnapshot cast = Cast(rig, instance, AllPerfect());
            Assert.That(cast.DamageDealt, Is.EqualTo(160), "2 charges x 30% (Riposte's own spender, not the passive's)");
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(0));
        }

        [Test]
        public void CounterCharges_FadeAfterIdlePlayerTurns()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("counter", new CounterPreparationPassive(2, 0, 0f, 0)));
            Rig rig = CreateRig(build);
            EnemyTurn(rig, HitJudgement.Perfect);
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(1));
            PlayerTurnBoundaries(rig);
            EnemyTurn(rig, HitJudgement.Miss);
            PlayerTurnBoundaries(rig);
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(0));
            Assert.That(rig.Runtime.Stats.CountersExpired, Is.EqualTo(1));
        }

        [Test]
        public void Deflection_ReflectsOnPerfectOnly_WithPerTurnCap()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("deflect", new DeflectionPassive(0.5f, 0f, 12, 0)));
            Rig rig = CreateRig(build, enemyHealth: 1000);
            EnemyTurn(rig, HitJudgement.Miss, HitJudgement.Good);
            Assert.That(rig.Enemy.CurrentHealth, Is.EqualTo(1000), "prevented / missed damage is not a parry");
            EnemyTurn(rig, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Perfect);
            Assert.That(1000 - rig.Enemy.CurrentHealth, Is.EqualTo(12), "5 per Perfect, capped at 12 per enemy turn");
            EnemyTurn(rig, HitJudgement.Perfect);
            Assert.That(1000 - rig.Enemy.CurrentHealth, Is.EqualTo(17), "cap resets each enemy turn");
        }

        [Test]
        public void Deflection_AccumulatesFractionsAcrossLowDamageProjectiles()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("deflect", new DeflectionPassive(0.3f, 0f, 40, 0)));
            Rig rig = CreateRig(build, enemyHealth: 1000,
                responses: new EnemyResponseProfile("Physical immune",
                    new[] { new DamageAffinity(ElementType.None, 0f) }));

            rig.Runtime.OnEnemyTurnStarted();
            rig.Modifiers.OnEnemyTurnStarted();
            for (int i = 0; i < 10; i++)
            {
                var result = new RhythmJudgementResult("low-deflect-" + i, 1, HitJudgement.Perfect, 0f, Vector3.zero,
                    NoteResolutionSource.PlayerInput);
                rig.Runtime.DefenseNoteSettled(result, 1, 0, 0);
            }

            Assert.That(1000 - rig.Enemy.CurrentHealth, Is.EqualTo(3),
                "reflection returns the projectile damage and must not be erased by the enemy's physical affinity");
        }

        [Test]
        public void MirrorReflection_AccumulatesPerformanceScaledFractionsAcrossLowDamageProjectiles()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            Rig rig = CreateRig(build, enemyHealth: 1000,
                responses: new EnemyResponseProfile("Physical immune",
                    new[] { new DamageAffinity(ElementType.None, 0f) }));
            rig.Runtime.ApplyBuff(new BuffSpec
            {
                Kind = BuffKind.Reflect,
                Strength = 0.42f,
                Turns = 2,
                PerTurnCap = 60,
                SourceId = "mirror-test",
                Label = "Mirror Stance"
            }, "mirror-cast");

            rig.Runtime.OnEnemyTurnStarted();
            rig.Modifiers.OnEnemyTurnStarted();
            for (int i = 0; i < 10; i++)
            {
                var result = new RhythmJudgementResult("low-mirror-" + i, 1, HitJudgement.Perfect, 0f, Vector3.zero,
                    NoteResolutionSource.PlayerInput);
                rig.Runtime.DefenseNoteSettled(result, 1, 0, 0);
            }

            Assert.That(1000 - rig.Enemy.CurrentHealth, Is.EqualTo(4),
                "70%-strength Mirror Stance should preserve 4.2 reflected damage regardless of physical affinity");
        }

        [Test]
        public void Reflection_CanDefeatTheEnemyOnItsTurn_ExactlyOnce_ThenPayoutsStop()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("counter", new CounterPreparationPassive(5, 0, 0f, 0)));
            build.AddPassive(Passive("deflect", new DeflectionPassive(1f, 0f, 1000, 0)));
            Rig rig = CreateRig(build, enemyHealth: 15);
            int defeats = 0;
            rig.Enemy.Defeated += () => defeats++;
            EnemyTurn(rig, HitJudgement.Perfect, HitJudgement.Perfect, HitJudgement.Perfect);
            Assert.IsTrue(rig.Enemy.IsDefeated);
            Assert.That(defeats, Is.EqualTo(1));
            Assert.IsTrue(rig.Runtime.CombatOver);
            Assert.That(rig.Runtime.Counters.Charges, Is.EqualTo(2), "no payouts after the lethal transaction");
        }

        [Test]
        public void Conservation_OncePerCast_UsesPaidCost()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 10, ElementType.None, new DealDamageEffect(1f));
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 20, 10, ElementType.None, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance strikeInstance = build.AddAbility(strike);
            AbilityInstance boltInstance = build.AddAbility(bolt);
            build.AddPassive(Passive("conserve", new ConservationPassive(5, 0.6f, 7, 0)));
            Rig rig = CreateRig(build);
            rig.Player.SpendMana(50);
            Cast(rig, strikeInstance, AllPerfect(), hits: 3);
            Assert.That(rig.Player.CurrentMana, Is.EqualTo(57));
            Cast(rig, boltInstance, AllPerfect());
            Assert.That(rig.Player.CurrentMana, Is.EqualTo(37), "costly casts do not conserve");
        }

        [Test]
        public void WholeEnemyTurnTrigger_FiresOnce_AndNeedsOpportunities()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("steady", new SteadyGuardPassive(0, 30, 0, 1)));
            Rig rig = CreateRig(build);
            EnemyTurn(rig);
            Assert.That(rig.Runtime.Stats.ShieldGained, Is.EqualTo(0), "zero-opportunity turn");
            EnemyTurn(rig, HitJudgement.Perfect, HitJudgement.Good, HitJudgement.Perfect);
            Assert.That(rig.Runtime.Stats.ShieldGained, Is.EqualTo(30));
            EnemyTurn(rig, HitJudgement.Perfect, HitJudgement.Miss);
            Assert.That(rig.Runtime.Stats.ShieldGained, Is.EqualTo(30), "a miss breaks it");
        }

        // ---------------- protection / sustain ----------------

        [Test]
        public void Shield_AbsorbsBeforeHealth_IsCapped_AndMitigationHasATurnBudget()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.AddPassive(Passive("iron", new MitigationPassive(0.5f, 0f, 10, 0)));
            Rig rig = CreateRig(build);
            int added = rig.Runtime.Damage.AddShield(5000, 2, "test", "root", secondary: false);
            Assert.That(added, Is.EqualTo(Mathf.RoundToInt(rig.Player.MaxHealth * BuildBalanceRules.Load().ShieldCapFraction)));
            rig.Modifiers.Find<ShieldBuff>(ShieldBuff.Key).ClampTo(8);
            // 4 misses of 10: mitigation 5,5 (budget 10), then 0,0 -> 5,5,10,10 = 30 incoming; shield takes 8.
            EnemyTurn(rig, HitJudgement.Miss, HitJudgement.Miss, HitJudgement.Miss, HitJudgement.Miss);
            Assert.That(rig.Player.MaxHealth - rig.Player.CurrentHealth, Is.EqualTo(22));
            Assert.That(rig.Runtime.Stats.Prevented, Is.EqualTo(10));
            Assert.That(rig.Runtime.Stats.Absorbed, Is.EqualTo(8));
        }

        [Test]
        public void RecoveryConversion_TurnsOverhealIntoShield()
        {
            AbilityDefinition mend = Ability("mend", AbilityRole.Healing, AbilityDelivery.Spell, 0, 100, ElementType.None, new HealEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(mend);
            build.AddPassive(Passive("wind", new RecoveryConversionPassive(0.5f, 0f, 2)));
            Rig rig = CreateRig(build);
            rig.Player.ApplyDamage(40);
            CastSnapshot cast = Cast(rig, instance, AllPerfect());
            Assert.That(cast.Healed, Is.EqualTo(40));
            Assert.That(cast.Overheal, Is.EqualTo(60));
            Assert.That(rig.Modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(30));
        }

        [Test]
        public void GlassHeart_LowersMaxHealth_AndBoostsSpells()
        {
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(bolt);
            build.AddPassive(Passive("glass", new MaxHealthPassive(-0.35f, 0f),
                new ActionModifierPassive(EffectKind.Damage, new EffectFilter(AbilityDelivery.Spell), 0.3f, 0f)));
            Rig rig = CreateRig(build);
            Assert.That(rig.Player.MaxHealth, Is.EqualTo(650));
            Assert.That(Cast(rig, instance, AllPerfect()).DamageDealt, Is.EqualTo(130));
        }

        // ---------------- statuses / buffs ----------------

        [Test]
        public void Burn_TicksAtItsBoundary_UsesAffinityOnce_AndRespectsImmunity()
        {
            var spec = new StatusSpec { statusId = "burn", element = ElementType.Fire, tickAt = TurnBoundary.EnemyTurnStart, powerScalePerTick = 0.1f, ticks = 2 };
            AbilityDefinition inferno = Ability("inferno", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new ApplyStatusEffect(spec));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(inferno);
            Rig rig = CreateRig(build, enemyHealth: 1000,
                responses: new EnemyResponseProfile("Weak", new[] { new DamageAffinity(ElementType.Fire, 2f) }));
            Cast(rig, instance, AllPerfect());
            Assert.That(rig.Enemy.CurrentHealth, Is.EqualTo(1000));
            PlayerTurnBoundaries(rig);
            EnemyTurn(rig);
            EnemyTurn(rig);
            EnemyTurn(rig);
            Assert.That(1000 - rig.Enemy.CurrentHealth, Is.EqualTo(40), "2 ticks of 10 x2 affinity");

            Rig immune = CreateRig(build, enemyHealth: 1000,
                responses: new EnemyResponseProfile("Immune", null, new[] { new StatusResponse { statusId = "burn", immune = true } }));
            Cast(immune, instance, AllPerfect());
            EnemyTurn(immune);
            Assert.That(immune.Enemy.CurrentHealth, Is.EqualTo(1000));
        }

        [Test]
        public void EffectIcons_ShowTheSourceAbility_AndItsTurnsLeft()
        {
            var texture = new Texture2D(2, 2);
            Sprite icon = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            created.Add(texture);
            created.Add(icon);
            var spec = new StatusSpec { statusId = "burn", displayName = "Burn", element = ElementType.Fire, powerScalePerTick = 0.1f, ticks = 3 };
            AbilityDefinition inferno = Ability("inferno", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new ApplyStatusEffect(spec));
            AbilityDefinition guard = Ability("guard", AbilityRole.Defense, AbilityDelivery.Spell, 0, 40, ElementType.None, new GainShieldEffect(1f, 2));
            SetField(inferno, "icon", icon);
            SetField(guard, "icon", icon);
            var build = new RunBuildState();
            AbilityInstance burnCast = build.AddAbility(inferno);
            AbilityInstance guardCast = build.AddAbility(guard);
            Rig rig = CreateRig(build);
            Cast(rig, burnCast, AllPerfect());
            Cast(rig, guardCast, AllPerfect());

            ICombatEffectIcon burn = rig.Modifiers.ActiveModifiers.OfType<StatusInstance>().Single();
            Assert.That(burn.Icon, Is.SameAs(icon));
            Assert.IsTrue(burn.IconOnEnemy && burn.IconIsDebuff, "statuses sit on the enemy");
            Assert.That(burn.IconCount, Is.EqualTo(3));
            Assert.That(burn.IconLabel, Is.EqualTo("Burn"));

            ICombatEffectIcon shield = rig.Modifiers.Find<ShieldBuff>(ShieldBuff.Key);
            Assert.That(shield.Icon, Is.SameAs(icon));
            Assert.IsFalse(shield.IconOnEnemy);
            Assert.That(shield.IconCount, Is.EqualTo(2));
            EnemyTurn(rig);
            Assert.That(shield.IconCount, Is.EqualTo(1), "counts down at enemy turn end");
            Assert.That(burn.IconCount, Is.EqualTo(2), "ticked once at enemy turn start");
        }

        [Test]
        public void LoadoutProblem_KeepsAnAbilityAndA0MpActionEquipped()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 50));
            build.AddAbility(Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 10, 90));
            Assert.IsNull(build.LoadoutProblem());
            Assert.IsTrue(build.Equip(build.GetSlot(1), 0), "slots swap");
            Assert.That(build.GetSlot(0).Definition.Id, Is.EqualTo("bolt"));
            Assert.IsNull(build.LoadoutProblem(), "order does not matter");
            build.Unequip(1);
            Assert.That(build.LoadoutProblem(), Does.Contain("0 MP"));
            build.Unequip(0);
            Assert.That(build.LoadoutProblem(), Does.Contain("at least one"));
            Assert.That(build.Reserve.Count(), Is.EqualTo(2), "unequipped abilities stay owned");
        }

        [Test]
        public void NextAttackBonus_IsReservedAtCommit_AndConsumedOnce()
        {
            AbilityDefinition focus = Ability("focus", AbilityRole.Buff, AbilityDelivery.Technique, 0, 0, ElementType.None,
                new ApplyBuffEffect(BuffKind.NextAttackBonus, 0.5f, 2, scaleByPerformance: false));
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.None, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance focusInstance = build.AddAbility(focus);
            AbilityInstance strikeInstance = build.AddAbility(strike);
            Rig rig = CreateRig(build);
            Cast(rig, focusInstance, AllPerfect());
            PlayerTurnBoundaries(rig);
            Assert.That(Cast(rig, strikeInstance, AllPerfect()).DamageDealt, Is.EqualTo(150));
            Assert.That(Cast(rig, strikeInstance, AllPerfect()).DamageDealt, Is.EqualTo(100));
        }

        [Test]
        public void Versatility_RewardsRoleChanges_NotRepeats()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.None, new DealDamageEffect(1f));
            AbilityDefinition mend = Ability("mend", AbilityRole.Healing, AbilityDelivery.Spell, 0, 100, ElementType.None, new HealEffect(1f));
            var build = new RunBuildState();
            AbilityInstance strikeInstance = build.AddAbility(strike);
            AbilityInstance mendInstance = build.AddAbility(mend);
            build.AddPassive(Passive("versatile", new VersatilityPassive(0.2f, 0f)));
            Rig rig = CreateRig(build);
            Assert.That(Cast(rig, strikeInstance, AllPerfect()).DamageDealt, Is.EqualTo(100), "first action");
            Assert.That(Cast(rig, strikeInstance, AllPerfect()).DamageDealt, Is.EqualTo(100), "repeat");
            Cast(rig, mendInstance, AllPerfect());
            Assert.That(Cast(rig, strikeInstance, AllPerfect()).DamageDealt, Is.EqualTo(120), "role switch");
        }

        // ---------------- rewards ----------------

        [Test]
        public void RewardOffer_IsSavedPerSource_AndClaimsExactlyOnce()
        {
            BuildContentRegistry registry = new();
            SampleBuildLibrary.RegisterInto(registry);
            RunBuildState build = registry.Preset("preset-blank").CreateState();
            RewardOfferData offer = RewardDirector.GetOrCreateOffer(build, "victory:1", registry, 3);
            Assert.That(offer.options, Has.Count.EqualTo(4), "3 options + the growth card");
            Assert.That(offer.options.Last().kind, Is.EqualTo(RewardKind.Growth));
            Assert.That(RewardDirector.GetOrCreateOffer(build, "victory:1", registry, 3), Is.EqualTo(offer), "reopening = same offer");

            RewardOptionData passive = offer.options.FirstOrDefault(o => o.kind == RewardKind.Passive) ?? offer.options[0];
            Assert.That(RewardDirector.Claim(build, offer, passive.optionId, registry), Is.EqualTo(ClaimStatus.Claimed));
            Assert.That(RewardDirector.Claim(build, offer, passive.optionId, registry), Is.EqualTo(ClaimStatus.AlreadyClaimed));
            Assert.That(RewardDirector.Claim(build, offer, offer.options.Last().optionId, registry), Is.EqualTo(ClaimStatus.AlreadyClaimed));

            // Reload: the offer and its claim survive, so there is no free reroll.
            RunBuildState reloaded = RunBuildState.FromJson(build.ToJson(), registry);
            RewardOfferData again = RewardDirector.GetOrCreateOffer(reloaded, "victory:1", registry, 3);
            Assert.IsTrue(again.claimed);
            Assert.That(again.options.Select(o => o.contentId), Is.EqualTo(offer.options.Select(o => o.contentId)));
            Assert.That(RewardDirector.Claim(reloaded, again, again.options[0].optionId, registry), Is.EqualTo(ClaimStatus.AlreadyClaimed));
        }

        [Test]
        public void GrowthRewards_RaiseMaxHealthAndMana_BeforePercentPassives_AndSurviveASave()
        {
            BuildContentRegistry registry = new();
            SampleBuildLibrary.RegisterInto(registry);
            RunBuildState build = registry.Preset("preset-blank").CreateState();
            ProgressionRules rules = BuildBalanceRules.Load().Progression;
            RewardOfferData offer = RewardDirector.GetOrCreateOffer(build, "victory:growth", registry, 3);
            RewardOptionData growth = offer.options.Single(o => o.kind == RewardKind.Growth);
            GrowthRewards.Amounts(growth.contentId, rules, out int health, out int mana);
            Assert.That(health + mana, Is.GreaterThan(0));
            RewardPreview preview = RewardDirector.Preview(build, growth, registry);
            Assert.That(preview.Summary, Does.Contain("for the rest of the run"));
            Assert.That(RewardDirector.Claim(build, offer, growth.optionId, registry), Is.EqualTo(ClaimStatus.Claimed));
            Assert.That(build.BonusMaxHealth, Is.EqualTo(health));
            Assert.That(build.BonusMaxMana, Is.EqualTo(mana));
            build.AddGrowth(rules.healthGrowth, rules.manaGrowth);

            build.AddPassive(Passive("fort", new MaxHealthPassive(0.5f, 0f)));
            Rig rig = CreateRig(build);
            int baseHealth = rig.Player.BaseMaxHealth;
            Assert.That(rig.Player.MaxHealth, Is.EqualTo(Mathf.RoundToInt((baseHealth + build.BonusMaxHealth) * 1.5f)), "growth, then +50%");
            Assert.That(rig.Player.MaxMana, Is.EqualTo(rig.Player.BaseMaxMana + build.BonusMaxMana));

            build.RecordVictory();
            RunBuildState reloaded = RunBuildState.FromJson(build.ToJson(), registry);
            Assert.That(reloaded.BonusMaxHealth, Is.EqualTo(build.BonusMaxHealth));
            Assert.That(reloaded.BonusMaxMana, Is.EqualTo(build.BonusMaxMana));
            Assert.That(reloaded.Depth, Is.EqualTo(1));
        }

        [Test]
        public void RunDepth_ScalesEnemyHealthAndNoteDamage_AndGatesAttackSequences()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            build.SetDepth(4);
            ProgressionRules rules = BuildBalanceRules.Load().Progression;
            Rig rig = CreateRig(build, enemyHealth: 1000);
            Assert.That(rig.Enemy.MaxHealth, Is.EqualTo(Mathf.RoundToInt(1000 * rules.EnemyHealthScale(4))));
            Assert.That(rig.Enemy.CurrentHealth, Is.EqualTo(rig.Enemy.MaxHealth), "starts full");
            Assert.That(rig.Runtime.NoteDamageScale, Is.EqualTo(rules.NoteDamageScale(4)).Within(0.0001));
            Assert.That(rules.NoteDamageScale(1000), Is.EqualTo(rules.maxNoteDamageScale).Within(0.0001), "capped");

            var dense = ScriptableObject.CreateInstance<EnemyAttackSequenceDefinition>();
            created.Add(dense);
            dense.GetType().GetField("minRunDepth", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dense, 3);
            Assert.IsFalse(dense.AllowedAtDepth(2));
            Assert.IsTrue(dense.AllowedAtDepth(3));
        }

        [Test]
        public void Replacement_CannotRemoveTheLastAffordableAction_AndKeepsUpgradesInReserve()
        {
            BuildContentRegistry registry = new();
            SampleBuildLibrary.RegisterInto(registry);
            RunBuildState build = registry.Preset("preset-glass").CreateState();
            int strikeSlot = build.Equipped.First(a => a.Definition.Id == "sample-strike").Let(build.SlotOf);
            var option = new RewardOptionData { optionId = "x", kind = RewardKind.NewAbility, contentId = "sample-frost-lance" };
            RewardPreview preview = RewardDirector.Preview(build, option, registry);
            Assert.IsTrue(preview.NeedsReplacement);
            Assert.IsFalse(preview.ReplaceableSlots.Contains(strikeSlot), "Strike is the only 0 MP action");

            AbilityInstance bolt = build.Equipped.First(a => a.Definition.Id == "sample-fire-bolt");
            build.AddUpgrade(bolt, registry.Upgrade("up-fire-bolt-quickcast"));
            var offer = new RewardOfferData { offerId = "manual", sourceKey = "manual", options = new List<RewardOptionData> { option } };
            Assert.That(RewardDirector.Claim(build, offer, "x", registry, build.SlotOf(bolt)), Is.EqualTo(ClaimStatus.Claimed));
            Assert.That(build.SlotOf(bolt), Is.EqualTo(-1), "replaced ability goes to reserve");
            Assert.That(bolt.Upgrades, Has.Count.EqualTo(1), "its upgrade stays with it");
        }

        [Test]
        public void Build_SurvivesItsDefinitionsBeingDestroyed_ByRebuildingFromIds()
        {
            // Enter Play Mode Options (no domain reload): code-made definitions die when play mode ends while the
            // static build survives. Its ids must still serialize so it can be rebuilt from fresh definitions.
            AbilityDefinition oldStrike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            PassiveDefinition oldPassive = Passive("vitality", new MaxHealthPassive(0.1f, 0f));
            var build = new RunBuildState();
            build.AddAbility(oldStrike, 0);
            build.AddPassive(oldPassive);
            Object.DestroyImmediate(oldStrike);
            Object.DestroyImmediate(oldPassive);

            var registry = new BuildContentRegistry();
            AbilityDefinition newStrike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            PassiveDefinition newPassive = Passive("vitality", new MaxHealthPassive(0.1f, 0f));
            registry.Register(newStrike);
            registry.Register(newPassive);
            RunBuildState rebuilt = RunBuildState.FromJson(build.ToJson(), registry);
            Assert.IsTrue(rebuilt.GetSlot(0) != null && rebuilt.GetSlot(0).Definition == newStrike);
            Assert.IsTrue(rebuilt.Passives.Count == 1 && rebuilt.Passives[0].Definition == newPassive);
        }

        [Test]
        public void SamplePresets_AreFunctionalBuilds()
        {
            BuildContentRegistry registry = new();
            SampleBuildLibrary.RegisterInto(registry);
            Assert.That(registry.Presets.Count(), Is.AtLeast(5));
            foreach (BuildPreset preset in registry.Presets)
            {
                RunBuildState build = preset.CreateState();
                Assert.IsTrue(build.HasAffordableBasicAction(), preset.DisplayName + " has a 0 MP action");
                Assert.That(build.Equipped.Count(), Is.AtLeast(2), preset.DisplayName);
                foreach (PassiveInstance passive in build.Passives)
                    Assert.IsTrue(build.IsPassiveActive(passive), $"{preset.DisplayName}: {passive.Definition.DisplayName} active");
            }
        }
    }

    internal static class TestExtensions
    {
        public static TResult Let<T, TResult>(this T value, System.Func<T, TResult> map) => map(value);
    }
}
