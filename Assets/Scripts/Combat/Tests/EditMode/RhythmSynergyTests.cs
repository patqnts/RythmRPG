using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Rhythm;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat.Tests
{
    public sealed class RhythmSynergyTests
    {
        private readonly List<Object> created = new();
        private RunBuildState previousBuild;
        private PlayerCombatant player;
        private EnemyCombatant enemy;
        private CombatModifierSystem modifiers;
        private CombatBuildRuntime runtime;
        private RunBuildState build;
        private RhythmChart chart;

        [SetUp] public void Setup()
        {
            previousBuild = RunBuild.Current;
            RunBuild.Current = null;
            chart = Own(ScriptableObject.CreateInstance<RhythmChart>());
            chart.Bpm = 120;
            chart.Notes.Add(new RhythmNoteData("lane", .25, RhythmNoteType.Normal));
            chart.Notes.Add(new RhythmNoteData("lane", .75, RhythmNoteType.Normal));
            var root = Own(new GameObject("Rhythm synergy rig"));
            player = root.AddComponent<PlayerCombatant>();
            enemy = root.AddComponent<EnemyCombatant>();
            Set(enemy, "fallbackMaxHealth", 5000);
            Set(enemy, "currentHealth", 5000);
            modifiers = root.AddComponent<CombatModifierSystem>();
            runtime = root.AddComponent<CombatBuildRuntime>();
            build = new RunBuildState();
        }
        [TearDown] public void Cleanup()
        {
            RunBuild.Current = previousBuild;
            foreach (Object value in created) if (value != null) Object.DestroyImmediate(value);
            created.Clear();
        }
        private T Own<T>(T value) where T : Object { created.Add(value); return value; }
        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private AbilityInstance Ability(params AbilityEffect[] effects) => Ability(AbilityRole.Damage, ElementType.Water, 12, 100, effects);
        private AbilityInstance Ability(AbilityRole role, ElementType element, int cost, int power, params AbilityEffect[] effects)
        {
            var definition = new AbilityDefinition.Builder("test" + build.Abilities.Count, "Test")
                .Tags(role, role == AbilityRole.Healing ? AbilityDelivery.Spell : AbilityDelivery.Melee)
                .Element(element).Cost(cost).Power(power).Chart(chart);
            foreach (AbilityEffect effect in effects) definition.Effect(effect);
            return build.AddAbility(Own(definition.Build()), build.Equipped.Count());
        }
        private PassiveDefinition Passive(string id, PassiveEffect effect)
        {
            var passive = Own(new PassiveDefinition.Builder(id, id, PassiveCategory.Conversion).Effect(effect).Build());
            build.AddPassive(passive);
            return passive;
        }
        private void Begin() => runtime.BeginEncounter(player, enemy, modifiers, build);
        private static RhythmPerformanceResult Performance(float weight = 1) => new(2, 2, 0, weight, new List<RhythmJudgementResult>());
        private CastSnapshot Commit(AbilityInstance instance, CastChoice choice = null)
        {
            var ability = new AbilityRuntimeInstance(instance, a => AbilityResolver.Resolve(a.Definition, a.BuildInstance, build));
            Assert.That(ability.Commit(player), Is.True);
            return runtime.BeginCast(1, build.SlotOf(instance), ability, choice);
        }
        private void Resolve(CastSnapshot cast, float weight = 1)
        {
            runtime.PreOutcome(cast, Performance(weight), true);
            var context = new AbilityEffectContext { Player = player, Enemy = enemy, Ability = cast.Definition,
                Performance = Performance(weight), Build = runtime, Cast = cast, Modifiers = modifiers, Rules = CombatResourceRules.Load() };
            var resolution = new AbilityResolution(context, cast.Quote.Effects, 1);
            resolution.Hit(1);
            resolution.Finish();
            runtime.CastResolved(cast);
        }
        private static RhythmJudgementResult Note(string id, HitJudgement judgement, NoteResolutionSource source = NoteResolutionSource.PlayerInput) =>
            new(id, 1, judgement, 0, Vector3.zero, source);
        private void Phrase(HitJudgement judgement)
        {
            runtime.BeginRhythmPattern(chart, new PatternRunContext(PatternRunMode.EnemyDefense, player));
            foreach (RhythmNoteData note in chart.Notes) runtime.RecordPhraseJudgement(Note(note.Id, judgement));
            runtime.EndRhythmPattern(new PatternRunResult(PatternRunMode.EnemyDefense, Performance(.8f), false));
        }

        [Test] public void AutomaticClearsCannotRedistributePhraseAccuracy()
        {
            var tracker = new RhythmPhraseTracker(chart, true);
            tracker.Record(Note(chart.Notes[0].Id, HitJudgement.Perfect, NoteResolutionSource.Modifier));
            PhraseOutcome result = tracker.Record(Note(chart.Notes[1].Id, HitJudgement.Perfect));
            Assert.That(result.Accuracy, Is.EqualTo(.5f));
            Assert.That(result.Successful(.7f), Is.False);
            Assert.That(tracker.Record(Note(chart.Notes[1].Id, HitJudgement.Perfect)), Is.Null);
        }
        [Test] public void CancelledAndEmptyPhrasesDoNotQualify()
        {
            var tracker = new RhythmPhraseTracker(chart, false);
            tracker.Record(Note(chart.Notes[0].Id, HitJudgement.Perfect));
            tracker.Cancel();
            Assert.That(tracker.Outcomes.Single().Successful(.7f), Is.False);
            Assert.That(new RhythmPhraseTracker(null, false).Outcomes, Is.Empty);
        }
        [Test] public void GoodDefensePhrasesAndPerfectsShareCounterAllowance()
        {
            Ability(new DealDamageEffect(1));
            Passive("prep", new CounterPreparationPassive(2, 1, .1f, 3));
            Begin(); runtime.OnEnemyTurnStarted();
            Phrase(HitJudgement.Good); Phrase(HitJudgement.Good); Phrase(HitJudgement.Perfect);
            runtime.DefenseNoteSettled(Note("extra", HitJudgement.Perfect), 10, 0, 0);
            Assert.That(runtime.Counters.Charges, Is.EqualTo(2));
        }
        [Test] public void ActiveAbilityChargesWorkOnOrdinaryAttacksWithoutCounterPreparation()
        {
            AbilityInstance attack = Ability(new DealDamageEffect(1));
            Begin(); runtime.Counters.Gain(2, "ability", "root");
            Resolve(Commit(attack));
            Assert.That(runtime.LastCast.DamageDealt, Is.EqualTo(120));
            Assert.That(runtime.Counters.Charges, Is.Zero);
        }
        [Test] public void ImprovisationConnectsCountersToFireHealing()
        {
            AbilityInstance healing = Ability(AbilityRole.Healing, ElementType.Fire, 18, 75, new CauterizeEffect());
            Passive("improv", new ImprovisationPassive());
            Begin(); player.ApplyDamage(200); runtime.Counters.Gain(2, "ability", "root");
            Resolve(Commit(healing));
            Assert.That(runtime.LastCast.Healed, Is.EqualTo(90));
            Assert.That(runtime.Counters.Charges, Is.Zero);
        }
        [Test] public void SmallShieldSourcesShareLimitWhilePaidShieldRemainsSeparate()
        {
            Ability(new DealDamageEffect(1));
            Passive("guard", new SustainedGuardPassive()); Passive("reservoir", new ReservoirPassive());
            Begin(); runtime.OnEnemyTurnStarted();
            runtime.CompleteRhythmChallenge(PatternRunMode.EnemyDefense, "hold1");
            runtime.CompleteRhythmChallenge(PatternRunMode.EnemyDefense, "hold2");
            PassiveHook hook = runtime.ActivePassives.First();
            Assert.That(runtime.GrantSmallShield(10, hook, "reaction"), Is.EqualTo(10));
            runtime.NotifyDefenseManaOverflow(20);
            Assert.That(modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(20));
            runtime.Damage.AddShield(40, 2, "paid", "cast", false);
            Assert.That(modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(60));
        }
        [Test] public void ShieldSpendingCannotTriggerBreakHealing()
        {
            Begin(); player.ApplyDamage(200);
            runtime.Damage.AddShield(90, 2, "veil", "cast", false, healOnBreak: .5f);
            Assert.That(runtime.Damage.SpendShield(30, "spend"), Is.EqualTo(30));
            ShieldBuff shield = modifiers.Find<ShieldBuff>(ShieldBuff.Key);
            Assert.That(shield.Capacity, Is.EqualTo(60));
            Assert.That(shield.BreakHeal, Is.EqualTo(30));
            runtime.Damage.SpendShield(60, "spend2");
            Assert.That(player.CurrentHealth, Is.EqualTo(800));
            Assert.That(shield.BreakHeal, Is.Zero);
        }
        [Test] public void BreakwaterAndPressureCastSpendShieldOnlyOnce()
        {
            AbilityInstance attack = Ability(new DealDamageEffect(1), new BreakwaterEffect());
            Passive("pressure", new PressureCastPassive()); Begin();
            runtime.Damage.AddShield(90, 2, "shield", "old", false);
            CastSnapshot cast = Commit(attack, new CastChoice { SpendShield = true });
            Assert.That(cast.ShieldSpent, Is.EqualTo(30));
            Resolve(cast);
            Assert.That(modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(60));
            Assert.That(cast.DamageDealt, Is.EqualTo(130));
        }
        [Test] public void BreakwaterGuardWorksWithoutExistingShield()
        {
            AbilityInstance attack = Ability(new DealDamageEffect(1), new BreakwaterEffect()); Begin();
            Resolve(Commit(attack));
            Assert.That(modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(70));
        }
        [Test] public void ShieldDamageDoesNotCreateAnExtraReaction()
        {
            AbilityInstance attack = Ability(AbilityRole.Damage, ElementType.Fire, 12, 100, new DealDamageEffect(1), new BreakwaterEffect()); Begin();
            runtime.Damage.AddShield(90, 2, "shield", "old", false);
            CastSnapshot cast = Commit(attack, new CastChoice { SpendShield = true });
            runtime.Marks.Apply(ElementalMarks.Static, 1, 10, cast.CastId, turns: 3);
            var context = new AbilityEffectContext { Player = player, Enemy = enemy, Ability = cast.Definition, Performance = Performance(), Build = runtime, Cast = cast };
            new ShieldSpendDamageEffect().ApplyAmount(context, 30);
            Assert.That(runtime.Marks.Get(ElementalMarks.Static).Stacks, Is.EqualTo(1));
        }
        [Test] public void CauterizeConsumesOneBurnAndHealingDoesNotReact()
        {
            AbilityInstance healing = Ability(AbilityRole.Healing, ElementType.Fire, 18, 75, new CauterizeEffect()); Begin();
            runtime.Marks.Apply(ElementalMarks.Burn, 3, 5, "burn");
            runtime.Marks.Apply(ElementalMarks.Static, 1, 10, "static");
            player.ApplyDamage(200);
            Resolve(Commit(healing, new CastChoice { ConsumeBurn = true }), .8f);
            Assert.That(player.CurrentHealth, Is.EqualTo(884));
            Assert.That(runtime.Marks.Get(ElementalMarks.Burn).Stacks, Is.EqualTo(2));
            Assert.That(runtime.Marks.Get(ElementalMarks.Static).Stacks, Is.EqualTo(1));
        }
        [Test] public void StokeAddsExistingMarksOnceWithoutRefreshingDuration()
        {
            Ability(AbilityRole.Damage, ElementType.Fire, 0, 10, new ApplyMarkEffect(ApplyMarkEffect.Mark.Burn));
            Passive("stoke", new StokePassive()); Begin(); runtime.OnEnemyTurnStarted();
            Phrase(HitJudgement.Good);
            Assert.That(runtime.Marks.Has(ElementalMarks.Burn), Is.False);
            // Next enemy turn: a successful phrase adds existing marks without refreshing them.
            runtime.OnEnemyTurnStarted();
            runtime.Marks.Apply(ElementalMarks.Burn, 1, 5, "burn", turns: 2);
            runtime.Marks.Apply(ElementalMarks.Static, 1, 10, "static", turns: 2);
            Phrase(HitJudgement.Good); Phrase(HitJudgement.Good);
            Assert.That(runtime.Marks.Get(ElementalMarks.Burn).Stacks, Is.EqualTo(2));
            Assert.That(runtime.Marks.Get(ElementalMarks.Static).Stacks, Is.EqualTo(2));
            Assert.That(runtime.Marks.Get(ElementalMarks.Burn).TurnsRemaining, Is.EqualTo(2));
        }
        [Test] public void BackbeatUsesClosingPhraseAndGainsChargesAfterDamage()
        {
            AbilityInstance attack = Ability(AbilityRole.Damage, ElementType.Wind, 8, 80, new DealDamageEffect(1), new BackbeatEffect()); Begin();
            CastSnapshot cast = Commit(attack);
            runtime.BeginRhythmPattern(chart, new PatternRunContext(PatternRunMode.PlayerAbility, player));
            foreach (RhythmNoteData note in chart.Notes) runtime.RecordPhraseJudgement(Note(note.Id, HitJudgement.Good));
            runtime.EndRhythmPattern(new PatternRunResult(PatternRunMode.PlayerAbility, Performance(.8f), false));
            Assert.That(runtime.Counters.Charges, Is.Zero);
            Resolve(cast, .8f);
            Assert.That(cast.DamageDealt, Is.EqualTo(64));
            Assert.That(runtime.Counters.Charges, Is.EqualTo(1));
        }
        [Test] public void RepriseRespectsOncePerApplicationAndSharedDurationLimit()
        {
            var buff = new DamageReductionBuff(new BuffSpec { Kind = BuffKind.DamageReduction, Turns = 4, ExtraTurns = 2, SourceId = "a" });
            Assert.That(buff.Reprise(2), Is.False);
            var ward = new LaneWardModifier(null, 3, extraTurns: 1);
            Assert.That(ward.Reprise(2), Is.True);
            Assert.That(ward.EnemyTurnsRemaining, Is.EqualTo(4));
            Assert.That(ward.Reprise(2), Is.False);
        }
        [Test] public void RepriseDoesNotOfferEnemyMarksOrExpiredBuffs()
        {
            AbilityInstance ability = Ability(AbilityRole.Defense, ElementType.None, 12, 0, new RepriseEffect()); Begin();
            var ward = new LaneWardModifier(null, 1); ward.OnEnemyTurnEnded(); modifiers.Add(ward);
            Assert.That(runtime.ChoicesFor(new AbilityRuntimeInstance(ability, _ => build.Quote(ability))), Is.Empty);
            runtime.Damage.AddShield(10, 2, "shield", "old", false);
            Assert.That(runtime.ChoicesFor(new AbilityRuntimeInstance(ability, _ => build.Quote(ability))).Count, Is.EqualTo(1));
        }
        [Test] public void CombinedDiscountsKeepFortyPercentOfUpgradedListedCost()
        {
            AbilityInstance attack = Ability(AbilityRole.Damage, ElementType.Fire, 30, 100, new DealDamageEffect(1));
            var discount = new EffectFilter(AbilityDelivery.Melee);
            Passive("cost1", new ManaCostPassive(discount, .8f, 0));
            Passive("cost2", new ManaCostPassive(discount, .8f, 0));
            Begin();
            Assert.That(build.Quote(attack).ManaCost, Is.EqualTo(12));
            Assert.That(Commit(attack).PaidCost, Is.EqualTo(12));
        }
        [Test] public void CloseoutSharesRefundLimitWithConservation()
        {
            AbilityInstance attack = Ability(AbilityRole.Damage, ElementType.None, 8, 100, new DealDamageEffect(1));
            Passive("conservation", new ConservationPassive(8, .6f, 12, 0)); Passive("closeout", new CloseoutPassive());
            Begin(); player.SpendMana(50);
            CastSnapshot cast = Commit(attack);
            cast.ClosingPhrase = new PhraseOutcome { Completed = true, Played = 2, Expected = 2, Accuracy = 1 };
            Resolve(cast);
            Assert.That(player.CurrentMana, Is.EqualTo(54)); // 50 - 8 + shared 12
        }
        [Test] public void CancelledChoiceAndDuplicateConfirmationDoNotPay()
        {
            AbilityInstance instance = Ability(new DealDamageEffect(1)); Begin();
            RunBuild.Current = build;
            AbilitySlotController slots = player.gameObject.AddComponent<AbilitySlotController>();
            slots.Initialize(null, player);
            AbilityRuntimeInstance selected = slots.Slots[1];
            Set(slots, "candidateLane", 1); Set(slots, "waitingForCommit", true);
            slots.BeginSelection();
            Assert.That(slots.ConfirmChoice(1, selected, new CastChoice()), Is.False);
            Assert.That(player.CurrentMana, Is.EqualTo(100));
            Assert.That(selected.RemainingCooldown, Is.Zero);
            Set(slots, "candidateLane", 1); Set(slots, "waitingForCommit", true);
            Assert.That(slots.ConfirmChoice(1, selected, new CastChoice()), Is.True);
            Assert.That(slots.ConfirmChoice(1, selected, new CastChoice()), Is.False);
            Assert.That(player.CurrentMana, Is.EqualTo(88));
        }
        [Test] public void NewPresetsUseExactlyFourAbilitiesAndCanGainMorePassives()
        {
            foreach (string id in new[] { "preset-improvising-duelist", "preset-armored-spellcaster", "preset-living-furnace", "preset-breakwater-knight", "preset-storm-conductor" })
            {
                BuildPreset preset = BuildContentRegistry.Instance.Preset(id);
                Assert.That(preset.Slots.Count, Is.EqualTo(4));
                RunBuildState state = preset.CreateState();
                foreach (PassiveDefinition passive in BuildContentRegistry.Instance.Passives.Where(p => string.IsNullOrEmpty(p.ExclusivityGroup))) state.AddPassive(passive);
                Assert.That(state.Passives.Count, Is.GreaterThan(4));
                Assert.That(state.Equipped.Count(), Is.EqualTo(4));
            }
        }
        [Test] public void EverySavedAbilityAndPassiveHasAnIcon()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:AbilityDefinition"))
                Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<AbilityDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)).Icon, Is.Not.Null);
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:PassiveDefinition"))
                Assert.That(UnityEditor.AssetDatabase.LoadAssetAtPath<PassiveDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)).Icon, Is.Not.Null);
        }
    }
}
