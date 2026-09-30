using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    /// <summary>
    /// Elemental marks, reactions and board effects: Burn / Soaked / Static / Cracked, Wind feeding the marks, the six
    /// reactions, zaps (Storm Ward, Conductor), Stone Wall, stagger limits, and the elemental passives.
    /// </summary>
    public sealed class ElementalTests
    {
        private readonly List<Object> created = new();
        private RhythmChart chart;
        private int noteCounter;

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

        // ---------------- harness ----------------

        private sealed class Rig
        {
            public PlayerCombatant Player;
            public EnemyCombatant Enemy;
            public CombatModifierSystem Modifiers;
            public CombatBuildRuntime Runtime;
            public RunBuildState Build;
            public FakeBoard Board;
            public int EnemyLost(int start) => start - Enemy.CurrentHealth;
        }

        private sealed class FakeBoard : ICombatNoteBoard
        {
            public readonly List<BoardNote> Notes = new();
            public readonly List<string> Cleared = new();
            public bool IsDefending { get; set; } = true;
            public int PatternSerial { get; set; } = 1;

            public IReadOnlyList<BoardNote> Upcoming(float withinSeconds) =>
                Notes.Where(n => !Cleared.Contains(n.NoteId) && n.SecondsUntilHit <= withinSeconds).OrderBy(n => n.SecondsUntilHit).ToList();

            public int BusiestLane() => Notes.Where(n => !Cleared.Contains(n.NoteId)).GroupBy(n => n.Lane)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

            public bool Clear(BoardNote note)
            {
                if (Cleared.Contains(note.NoteId)) return false;
                Cleared.Add(note.NoteId);
                return true;
            }

            public void ShowArc(Vector3 from, Vector3 to, Color color) { }
        }

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private Rig CreateRig(RunBuildState build, int enemyHealth = 5000, EnemyResponseProfile responses = null)
        {
            var root = new GameObject("Elemental Test Rig");
            created.Add(root);
            var rig = new Rig
            {
                Player = root.AddComponent<PlayerCombatant>(),
                Enemy = new GameObject("Elemental Test Enemy").AddComponent<EnemyCombatant>(),
                Modifiers = root.AddComponent<CombatModifierSystem>(),
                Runtime = root.AddComponent<CombatBuildRuntime>(),
                Build = build,
                Board = new FakeBoard()
            };
            created.Add(rig.Enemy.gameObject);
            SetField(rig.Enemy, "fallbackMaxHealth", enemyHealth);
            SetField(rig.Enemy, "currentHealth", enemyHealth);
            if (responses != null) rig.Enemy.SetResponseOverride(responses);
            rig.Runtime.BeginEncounter(rig.Player, rig.Enemy, rig.Modifiers, build);
            rig.Runtime.Board = rig.Board;
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
            var builder = new PassiveDefinition.Builder(id, id, PassiveCategory.ElementalEnhancement);
            foreach (PassiveEffect effect in effects) builder.Effect(effect);
            PassiveDefinition definition = builder.Build();
            created.Add(definition);
            return definition;
        }

        private static RhythmPerformanceResult Performance(params HitJudgement[] judgements) =>
            RhythmPerformanceCalculator.Calculate(judgements.Length,
                judgements.Select((j, i) => new RhythmJudgementResult("a" + i, 1, j, 0f, Vector3.zero, NoteResolutionSource.PlayerInput)).ToList(), null);

        private static RhythmPerformanceResult AllPerfect(int notes = 4) => Performance(Enumerable.Repeat(HitJudgement.Perfect, notes).ToArray());

        private static CastSnapshot Cast(Rig rig, AbilityInstance instance, RhythmPerformanceResult performance,
            IEnumerable<RhythmJudgementResult> liveJudgements = null)
        {
            var runtimeAbility = new AbilityRuntimeInstance(instance, r => AbilityResolver.Resolve(r.Definition, r.BuildInstance, rig.Build));
            Assert.IsTrue(runtimeAbility.Commit(rig.Player), "commit");
            int slot = rig.Build != null ? rig.Build.SlotOf(instance) : -1;
            CastSnapshot cast = rig.Runtime.BeginCast(slot + 1, slot, runtimeAbility);
            if (liveJudgements != null)
                foreach (RhythmJudgementResult judgement in liveJudgements) rig.Runtime.OnChartJudgement(judgement, PatternRunMode.PlayerAbility);
            rig.Runtime.PreOutcome(cast, performance, true);
            var context = new AbilityEffectContext
            {
                Player = rig.Player, Enemy = rig.Enemy, Ability = instance.Definition, Performance = performance,
                Modifiers = rig.Modifiers, Rules = CombatResourceRules.Load(), Build = rig.Runtime, Cast = cast
            };
            var resolution = new AbilityResolution(context, cast.Quote.Effects, 1);
            resolution.Hit(1f);
            resolution.Finish();
            rig.Runtime.CastResolved(cast);
            return cast;
        }

        private RhythmJudgementResult Note(HitJudgement judgement, NoteResolutionSource source = NoteResolutionSource.PlayerInput) =>
            new("n" + noteCounter++, 1, judgement, 0f, Vector3.zero, source);

        /// <summary>One enemy note of 10 damage through the full defense transaction.</summary>
        private static void Defend(Rig rig, RhythmJudgementResult result)
        {
            int attempted = CombatResourceRules.Load().DefenseDamage(10, result.Judgement);
            if (result.Source == NoteResolutionSource.Modifier) attempted = 0;
            if (attempted > 0 && rig.Modifiers.TryBlockDamage(result)) attempted = 0;
            int actual = attempted > 0 ? rig.Runtime.ApplyDefenseDamage(result, 10, attempted) : 0;
            rig.Runtime.DefenseNoteSettled(result, 10, attempted, actual);
        }

        private static void StartEnemyTurn(Rig rig)
        {
            rig.Runtime.OnEnemyTurnStarted();
            rig.Modifiers.OnEnemyTurnStarted();
        }

        private static void EndEnemyTurn(Rig rig)
        {
            rig.Runtime.OnEnemyTurnEnded(false);
            rig.Modifiers.OnEnemyTurnEnded();
        }

        private static void PlayerTurn(Rig rig)
        {
            rig.Runtime.OnPlayerTurnEnded();
            rig.Modifiers.OnPlayerTurnEnded();
            rig.Runtime.OnPlayerTurnStarted();
            rig.Modifiers.OnPlayerTurnStarted();
        }

        private static void Hit(Rig rig, ElementType element, int amount, string root, CastSnapshot cast = null) =>
            rig.Runtime.Damage.DamageEnemy(amount, element, "test", root, CombatEventKind.AbilityDamage, applyAffinity: false, cast: cast);

        private static void AddNotes(FakeBoard board, int lane, params float[] seconds)
        {
            foreach (float s in seconds)
                board.Notes.Add(new BoardNote(null, "b" + board.Notes.Count, lane, s, 10, Vector3.zero));
        }

        // ---------------- marks ----------------

        [Test]
        public void EmberLash_AddsBurnPerPerfect_CappedPerCastAndByTheMarkCap_PyreKeeperRaisesIt()
        {
            AbilityDefinition lash = Ability("lash", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.Fire,
                new DealDamageEffect(1f), new MarkPerPerfectEffect(ApplyMarkEffect.Mark.Burn, 1, 3, 0.1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(lash);
            Rig rig = CreateRig(build);
            Cast(rig, instance, AllPerfect(4));
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Burn), Is.EqualTo(3), "max 3 per cast");
            Cast(rig, instance, AllPerfect(4));
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Burn), Is.EqualTo(5), "mark cap 5");

            int health = rig.Enemy.CurrentHealth;
            StartEnemyTurn(rig);
            Assert.That(rig.EnemyLost(health), Is.EqualTo(50), "10 per stack per tick");

            build.AddPassive(Passive("pyre", new MarkMasteryPassive(ApplyMarkEffect.Mark.Burn, 2, 1, 0.1f, 0.1f)));
            Rig keeper = CreateRig(build);
            for (int i = 0; i < 3; i++) Cast(keeper, instance, AllPerfect(4));
            Assert.That(keeper.Runtime.Marks.Stacks(ElementalMarks.Burn), Is.EqualTo(7), "Pyre Keeper: cap 7");
        }

        [Test]
        public void Soaked_SoftensEnemyNotes_AndCracked_BoostsMeleeOnly()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100));
            Rig rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Soaked, 1, 0, "setup");
            StartEnemyTurn(rig);
            Defend(rig, Note(HitJudgement.Miss));
            Assert.That(rig.Player.MaxHealth - rig.Player.CurrentHealth, Is.EqualTo(8), "20% softer");

            // (Earth on a Soaked enemy would react: Mudlock. Use a fresh enemy.)
            rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Cracked, 1, 0, "setup");
            AbilityDefinition slam = Ability("slam", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100);
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, build.AddAbility(slam), AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(120), "melee +20%");
            health = rig.Enemy.CurrentHealth;
            Cast(rig, build.AddAbility(bolt), AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(100), "spells unaffected");
        }

        [Test]
        public void Static_DischargesOnALightningHitAtTheCap()
        {
            Rig rig = CreateRig(new RunBuildState());
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 4, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Hit(rig, ElementType.Lightning, 5, "hit1");
            Assert.That(rig.EnemyLost(health), Is.EqualTo(5), "4 stacks: below the cap, no discharge");
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 1, 10, "setup2");
            health = rig.Enemy.CurrentHealth;
            Hit(rig, ElementType.Lightning, 5, "hit2");
            Assert.That(rig.EnemyLost(health), Is.EqualTo(55), "5 x 10 discharge");
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Static));
        }

        [Test]
        public void Wind_FeedsBurnAndStatic_AndExtendsMarksOncePerCast()
        {
            Rig rig = CreateRig(new RunBuildState());
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 1, 10, "setup");
            rig.Runtime.Marks.Apply(ElementalMarks.Soaked, 1, 0, "setup");
            int soakedTurns = rig.Runtime.Marks.Get(ElementalMarks.Soaked).TurnsRemaining;
            for (int i = 0; i < 3; i++) Hit(rig, ElementType.Wind, 5, "cyclone");
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Static), Is.EqualTo(4), "+1 per Wind hit");
            Assert.That(rig.Runtime.Marks.Get(ElementalMarks.Soaked).TurnsRemaining, Is.EqualTo(soakedTurns + 1), "extended once per cast");
        }

        // ---------------- reactions ----------------

        [Test]
        public void Steam_WaterOnBurning_BurstsFromTheBurnStacks_AndUsesUpBothMarks()
        {
            AbilityDefinition undertow = Ability("undertow", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Water,
                new DealDamageEffect(1f), new ApplyMarkEffect(ApplyMarkEffect.Mark.Soaked));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(undertow);
            Rig rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Burn, 3, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, instance, AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(100 + 60), "hit + Steam (10 x 3 stacks x 2)");
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Burn), "Burn used up");
            Assert.IsTrue(rig.Runtime.Marks.Has(ElementalMarks.Soaked), "Undertow soaks after the hit");
            Assert.That(rig.Runtime.Stats.Reactions, Is.EqualTo(1));
        }

        [Test]
        public void Overload_FireOnStatic_DischargesDouble()
        {
            Rig rig = CreateRig(new RunBuildState());
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 2, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Hit(rig, ElementType.Fire, 10, "fire");
            Assert.That(rig.EnemyLost(health), Is.EqualTo(10 + 40), "2 stacks x 10 x 2");
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Static));
        }

        [Test]
        public void Wildfire_WindOnBurning_DoublesStacks_OncePerCast()
        {
            Rig rig = CreateRig(new RunBuildState());
            rig.Runtime.Marks.Apply(ElementalMarks.Burn, 2, 10, "setup");
            Hit(rig, ElementType.Wind, 5, "gust");
            // +1 from the Wind hit, then Wildfire doubles: 3 x 2 = 6, capped at 5.
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Burn), Is.EqualTo(5));
            rig.Runtime.Marks.Get(ElementalMarks.Burn).SetStacks(2);
            Hit(rig, ElementType.Wind, 5, "gust");
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Burn), Is.EqualTo(3), "same cast: no second Wildfire");
        }

        [Test]
        public void Mudlock_StaggersTheNextAttack_BossLimitsAndNoBackToBack()
        {
            Rig rig = CreateRig(new RunBuildState(), responses: new EnemyResponseProfile("Boss", null, null, maxStaggers: 1));
            rig.Runtime.Marks.Apply(ElementalMarks.Soaked, 1, 0, "setup");
            Hit(rig, ElementType.Earth, 10, "quake");
            Assert.IsTrue(rig.Runtime.StaggerPending, "Earth on Soaked");
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Soaked));
            StartEnemyTurn(rig);
            Assert.IsTrue(rig.Runtime.ConsumeStagger(3), "3-step attack loses a step");
            EndEnemyTurn(rig);
            Assert.IsFalse(rig.Runtime.TryStagger("tremor", "c1", "Tremor"), "boss limit reached (and back-to-back)");

            Rig normal = CreateRig(new RunBuildState());
            Assert.IsTrue(normal.Runtime.TryStagger("tremor", "c1", "Tremor"));
            StartEnemyTurn(normal);
            Assert.IsFalse(normal.Runtime.ConsumeStagger(1), "single step: weakened instead");
            Defend(normal, Note(HitJudgement.Miss));
            Assert.That(normal.Player.MaxHealth - normal.Player.CurrentHealth, Is.EqualTo(5));
            Assert.IsFalse(normal.Runtime.TryStagger("tremor", "c2", "Tremor"), "not two enemy turns in a row");
            EndEnemyTurn(normal);
            PlayerTurn(normal);
            StartEnemyTurn(normal);
            EndEnemyTurn(normal);
            Assert.IsTrue(normal.Runtime.TryStagger("tremor", "c3", "Tremor"), "allowed again a turn later");
        }

        [Test]
        public void Magnetize_EarthOnStatic_BecomesAShield()
        {
            Rig rig = CreateRig(new RunBuildState());
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 3, 10, "setup");
            Hit(rig, ElementType.Earth, 10, "quake");
            Assert.That(rig.Modifiers.Find<ShieldBuff>(ShieldBuff.Key).Capacity, Is.EqualTo(30));
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Static));
        }

        [Test]
        public void Catalyst_StrengthensReactions_AndTheFirstOneKeepsItsMarks()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new DealDamageEffect(1f)));
            build.AddAbility(Ability("spark", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Lightning, new DealDamageEffect(1f)));
            build.AddPassive(Passive("catalyst", new CatalystPassive(0.5f, 0f)));
            Rig rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 2, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Hit(rig, ElementType.Fire, 10, "fire1");
            Assert.That(rig.EnemyLost(health), Is.EqualTo(10 + 60), "Overload 40 x 1.5");
            Assert.IsTrue(rig.Runtime.Marks.Has(ElementalMarks.Static), "first reaction keeps the marks");
            Hit(rig, ElementType.Fire, 10, "fire2");
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Static), "second one uses them up");
        }

        [Test]
        public void Combust_DetonatesTheRemainingBurn()
        {
            AbilityDefinition combust = Ability("combust", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire,
                new ConsumeBurnEffect(1.5f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(combust);
            Rig rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Burn, 2, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, instance, AllPerfect());
            // 10 x 2 stacks x 3 turns left = 60, x1.5.
            Assert.That(rig.EnemyLost(health), Is.EqualTo(90));
            Assert.IsFalse(rig.Runtime.Marks.Has(ElementalMarks.Burn));
        }

        // ---------------- board ----------------

        [Test]
        public void StormWard_PerfectZapsTheNextNotes_ConductChainsFurther_CappedPerTurn()
        {
            AbilityDefinition ward = Ability("ward", AbilityRole.Defense, AbilityDelivery.Spell, 0, 100, ElementType.Lightning,
                new StormWardEffect(2, 2, 0.1f, 4));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(ward);
            Rig rig = CreateRig(build);
            AddNotes(rig.Board, 1, 0.2f, 0.4f, 0.6f, 0.8f, 0.9f, 1.5f);
            Cast(rig, instance, AllPerfect());
            rig.Runtime.Marks.Apply(ElementalMarks.Soaked, 1, 0, "setup");
            StartEnemyTurn(rig);
            int health = rig.Enemy.CurrentHealth;
            Defend(rig, Note(HitJudgement.Perfect));
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(3), "2 + 1 (Conduct)");
            Assert.That(rig.EnemyLost(health), Is.EqualTo(30), "10 Lightning per zapped note");
            Defend(rig, Note(HitJudgement.Perfect));
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(4), "4 per turn");
            Defend(rig, Note(HitJudgement.Good));
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(4), "Good does not zap");
        }

        [Test]
        public void ClearedNotes_AreNotMissesOrOpportunities()
        {
            var build = new RunBuildState();
            build.AddAbility(Ability("spark", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Lightning, new DealDamageEffect(1f)));
            build.AddPassive(Passive("conductor", new ConductorPassive(1, 0, 1, 8, 0)));
            Rig rig = CreateRig(build);
            AddNotes(rig.Board, 2, 0.3f, 0.5f);
            StartEnemyTurn(rig);
            Defend(rig, Note(HitJudgement.Perfect));
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(1));
            Defend(rig, Note(HitJudgement.Perfect, NoteResolutionSource.Modifier));
            Defend(rig, Note(HitJudgement.Perfect));
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(1), "Conductor: 1 Perfect per turn at level 1");
            EndEnemyTurn(rig);
            Assert.That(rig.Runtime.LastEnemyTurn.Opportunities, Is.EqualTo(2));
            Assert.That(rig.Runtime.LastEnemyTurn.Cleared, Is.EqualTo(1));
            Assert.That(rig.Runtime.LastEnemyTurn.Misses, Is.EqualTo(0));
        }

        [Test]
        public void StoneWall_BlocksNotesInTheBusiestLane_UntilItsChargesRunOut()
        {
            AbilityDefinition wall = Ability("wall", AbilityRole.Defense, AbilityDelivery.Technique, 0, 0, ElementType.Earth,
                new StoneWallEffect(2, 2));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(wall);
            Rig rig = CreateRig(build);
            Cast(rig, instance, AllPerfect());
            AddNotes(rig.Board, 3, 0.05f, 0.1f, 0.12f);
            AddNotes(rig.Board, 1, 0.05f);
            StartEnemyTurn(rig);
            rig.Runtime.TickBoard();
            Assert.That(rig.Board.Cleared.Count, Is.EqualTo(2), "2 charges, lane 3 only");
            Assert.IsTrue(rig.Board.Notes.Where(n => rig.Board.Cleared.Contains(n.NoteId)).All(n => n.Lane == 3));
            Assert.That(rig.Modifiers.OfType<StoneWallBuff>().Count(), Is.EqualTo(0), "used up");
        }

        [Test]
        public void GaleStep_DodgesTheFirstMiss()
        {
            AbilityDefinition gale = Ability("gale", AbilityRole.Damage, AbilityDelivery.Technique, 0, 50, ElementType.Wind,
                new DealDamageEffect(1f), new DodgeEffect(1, 1));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(gale);
            Rig rig = CreateRig(build);
            Cast(rig, instance, AllPerfect());
            StartEnemyTurn(rig);
            Defend(rig, Note(HitJudgement.Miss));
            Defend(rig, Note(HitJudgement.Miss));
            Assert.That(rig.Player.MaxHealth - rig.Player.CurrentHealth, Is.EqualTo(10), "first Miss dodged");
        }

        [Test]
        public void ChainSpark_FiresArcsLiveOnPerfects_AndAddsStatic()
        {
            AbilityDefinition spark = Ability("spark", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Lightning,
                new ChainArcEffect(0.2f, 1, 0.1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(spark);
            Rig rig = CreateRig(build);
            int health = rig.Enemy.CurrentHealth;
            var live = new[] { Note(HitJudgement.Perfect), Note(HitJudgement.Good), Note(HitJudgement.Perfect) };
            Cast(rig, instance, Performance(HitJudgement.Perfect, HitJudgement.Good, HitJudgement.Perfect), live);
            Assert.That(rig.EnemyLost(health), Is.EqualTo(40), "2 arcs x 20");
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Static), Is.EqualTo(2));
        }

        // ---------------- sustain / passives ----------------

        [Test]
        public void TidalVeil_HealsWhenTheShieldBreaks_RainDanceHealsEachTurn()
        {
            AbilityDefinition veil = Ability("veil", AbilityRole.Defense, AbilityDelivery.Spell, 0, 20, ElementType.Water,
                new GainShieldEffect(1f, 2, healOnBreak: 0.5f));
            AbilityDefinition rain = Ability("rain", AbilityRole.Healing, AbilityDelivery.Spell, 0, 30, ElementType.Water,
                new RegenEffect(1f, 3, ElementType.Water));
            var build = new RunBuildState();
            AbilityInstance veilCast = build.AddAbility(veil);
            AbilityInstance rainCast = build.AddAbility(rain);
            Rig rig = CreateRig(build);
            rig.Player.ApplyDamage(100);
            Cast(rig, veilCast, AllPerfect());
            StartEnemyTurn(rig);
            Defend(rig, Note(HitJudgement.Miss));
            Defend(rig, Note(HitJudgement.Miss));
            // 20 shield absorbs both notes, breaks, heals 10.
            Assert.That(rig.Player.MaxHealth - rig.Player.CurrentHealth, Is.EqualTo(90));
            EndEnemyTurn(rig);
            Cast(rig, rainCast, AllPerfect());
            PlayerTurn(rig);
            Assert.That(rig.Player.MaxHealth - rig.Player.CurrentHealth, Is.EqualTo(80), "10 per turn");
        }

        [Test]
        public void Unyielding_SurvivesOneLethalHitPerBattle()
        {
            var build = new RunBuildState();
            build.AddPassive(Passive("unyielding", new UnyieldingPassive()));
            Rig rig = CreateRig(build);
            rig.Player.ApplyDamage(rig.Player.CurrentHealth - 5);
            StartEnemyTurn(rig);
            Defend(rig, Note(HitJudgement.Miss));
            Assert.That(rig.Player.CurrentHealth, Is.EqualTo(1));
            Defend(rig, Note(HitJudgement.Miss));
            Assert.IsTrue(rig.Player.IsDefeated, "only once");
        }

        [Test]
        public void Tailwind_MakesTheNextAbilityCheaper_Once()
        {
            AbilityDefinition gust = Ability("gust", AbilityRole.Damage, AbilityDelivery.Technique, 0, 50, ElementType.Wind, new DealDamageEffect(1f));
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 20, 50, ElementType.Fire, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance gustCast = build.AddAbility(gust);
            AbilityInstance boltCast = build.AddAbility(bolt);
            build.AddPassive(Passive("tailwind", new TailwindPassive(0.5f, 0f)));
            Rig rig = CreateRig(build);
            Cast(rig, gustCast, AllPerfect());
            Assert.That(build.Quote(boltCast).ManaCost, Is.EqualTo(10));
            CastSnapshot cast = Cast(rig, boltCast, AllPerfect());
            Assert.That(cast.PaidCost, Is.EqualTo(10));
            Assert.That(build.Quote(boltCast).ManaCost, Is.EqualTo(20), "used up");
        }

        [Test]
        public void Riptide_TurnsWaterOverhealIntoDamage_WhichCanReact()
        {
            AbilityDefinition mend = Ability("mend", AbilityRole.Healing, AbilityDelivery.Spell, 0, 100, ElementType.Water,
                new HealEffect(1f, ElementType.Water));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(mend);
            build.AddPassive(Passive("riptide", new RiptidePassive(0.5f, 0f)));
            Rig rig = CreateRig(build);
            rig.Player.ApplyDamage(40);
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, instance, AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(30), "60 overheal x 50%");
        }

        [Test]
        public void Aftershock_EchoesEarthDamage_AtTheNextEnemyTurn()
        {
            AbilityDefinition slam = Ability("slam", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100, ElementType.Earth, new DealDamageEffect(1f));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(slam);
            build.AddPassive(Passive("aftershock", new AftershockPassive(0.3f, 0f)));
            Rig rig = CreateRig(build);
            Cast(rig, instance, AllPerfect());
            int health = rig.Enemy.CurrentHealth;
            StartEnemyTurn(rig);
            Assert.That(rig.EnemyLost(health), Is.EqualTo(30));
        }

        [Test]
        public void Cyclone_HitsSeparately_SoEachHitFeedsTheMarks()
        {
            AbilityDefinition cyclone = Ability("cyclone", AbilityRole.Damage, AbilityDelivery.Spell, 0, 60, ElementType.Wind,
                new MultiHitDamageEffect(1f, 6));
            var build = new RunBuildState();
            AbilityInstance instance = build.AddAbility(cyclone);
            Rig rig = CreateRig(build);
            rig.Runtime.Marks.Apply(ElementalMarks.Static, 1, 10, "setup");
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, instance, AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(60), "total unchanged");
            Assert.That(rig.Runtime.Marks.Stacks(ElementalMarks.Static), Is.EqualTo(5), "1 + 6 hits, capped at 5");
        }

        [Test]
        public void PuristAndAttunement_FollowTheEquippedElements()
        {
            AbilityDefinition strike = Ability("strike", AbilityRole.Damage, AbilityDelivery.Melee, 0, 100);
            AbilityDefinition undertow = Ability("undertow", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Water, new DealDamageEffect(1f));
            AbilityDefinition bolt = Ability("bolt", AbilityRole.Damage, AbilityDelivery.Spell, 0, 100, ElementType.Fire, new DealDamageEffect(1f));
            var build = new RunBuildState();
            build.AddAbility(strike);
            AbilityInstance water = build.AddAbility(undertow);
            PassiveInstance purist = build.AddPassive(Passive("purist", new PuristPassive(0.2f, 0f, 1)));
            Assert.IsTrue(build.IsPassiveActive(purist), "physical abilities don't break it");
            Rig rig = CreateRig(build);
            int health = rig.Enemy.CurrentHealth;
            Cast(rig, water, AllPerfect());
            Assert.That(rig.EnemyLost(health), Is.EqualTo(120));
            build.AddAbility(bolt);
            Assert.IsFalse(build.IsPassiveActive(purist), "a second element breaks it");
        }
    }
}
