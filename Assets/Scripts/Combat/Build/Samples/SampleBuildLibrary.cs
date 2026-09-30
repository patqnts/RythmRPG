using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Code-defined sample content for testing the build system in play mode: abilities, dedicated upgrades,
    /// passives and five presets (Parry / Deflect, Glass Cannon, Tank, Adaptable, Blank Slate), plus enemy affinity
    /// profiles. Abilities reuse the presentation (icon, chart, VFX, attack sequence) of the existing ability assets and
    /// scale their power from the current Basic Attack, so they follow your tuning.
    ///
    /// Everything here is content data; Tools > Rythm RPG > Combat > Build > Export Sample Build Content saves it
    /// as editable assets (which then take priority over these code definitions).
    /// </summary>
    public static class SampleBuildLibrary
    {
        public const string StyleParry = "parry";
        public const string StyleGlass = "glass-cannon";
        public const string StyleTank = "tank";
        public const string StyleAdaptable = "adaptable";

        public sealed class Content
        {
            public readonly Dictionary<string, AbilityDefinition> Abilities = new();
            public readonly Dictionary<string, AbilityUpgradeDefinition> Upgrades = new();
            public readonly Dictionary<string, PassiveDefinition> Passives = new();
            public readonly List<BuildPreset> Presets = new();

            public bool IsAlive => Abilities.Values.All(a => a != null) && Passives.Values.All(p => p != null)
                                   && Upgrades.Values.All(u => u != null) && Presets.All(p => p != null);
        }

        private static Content cached;

        public static Content Get()
        {
            if (cached == null || !cached.IsAlive) cached = Create();
            return cached;
        }

        /// <summary>Forget the cached objects (they are destroyed when play mode ends).</summary>
        internal static void ClearCache() => cached = null;

        /// <summary>A new, uncached set of sample objects (used by the asset exporter).</summary>
        public static Content CreateFresh() => Create();

        public static void RegisterInto(BuildContentRegistry registry)
        {
            Content content = Get();
            foreach (AbilityDefinition ability in content.Abilities.Values) registry.Register(ability);
            foreach (AbilityUpgradeDefinition upgrade in content.Upgrades.Values) registry.Register(upgrade);
            foreach (PassiveDefinition passive in content.Passives.Values) registry.Register(passive);
            foreach (BuildPreset preset in content.Presets) registry.Register(preset);
        }

        /// <summary>Enemy affinity profiles to test element / status interactions against any enemy.</summary>
        public static IReadOnlyList<EnemyResponseProfile> EnemyProfiles { get; } = new List<EnemyResponseProfile>
        {
            new("Fire-resistant", new[] { new DamageAffinity(ElementType.Fire, 0.5f), new DamageAffinity(ElementType.Water, 1.5f) },
                new[] { new StatusResponse { statusId = "burn", durationScale = 0.5f } }),
            new("Armored", new[]
            {
                new DamageAffinity(ElementType.None, 0.6f), new DamageAffinity(ElementType.Lightning, 1.5f),
                new DamageAffinity(ElementType.Water, 1.25f)
            }),
            new("Burn-immune", new DamageAffinity[0], new[] { new StatusResponse { statusId = "burn", immune = true } }),
            new("Frail", new[]
            {
                new DamageAffinity(ElementType.None, 1.25f), new DamageAffinity(ElementType.Fire, 1.25f),
                new DamageAffinity(ElementType.Water, 1.25f), new DamageAffinity(ElementType.Lightning, 1.25f)
            })
        };

        private static StatusSpec Burn(float scalePerTick, int ticks) => new()
        {
            statusId = "burn", displayName = "Burn", element = ElementType.Fire, tickAt = TurnBoundary.EnemyTurnStart,
            powerScalePerTick = scalePerTick, ticks = ticks, stacking = StackPolicy.Refresh, maxStacks = 1
        };

        private static Content Create()
        {
            var content = new Content();
            AbilityDefinition basic = Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack");
            AbilityDefinition flame = Resources.Load<AbilityDefinition>("Combat/Abilities/Flamethrower") ?? basic;
            AbilityDefinition heal = Resources.Load<AbilityDefinition>("Combat/Abilities/Heal") ?? basic;
            AbilityDefinition guard = Resources.Load<AbilityDefinition>("Combat/Abilities/DeathFaith") ?? heal;
            // Power unit: the current Basic Attack, so samples follow the project's tuning.
            int unit = basic != null && basic.BasePower > 0 ? basic.BasePower : 100;
            int healPower = heal != null && heal.BasePower > 0 ? heal.BasePower : 150;
            int P(float factor) => Mathf.Max(1, Mathf.RoundToInt(unit * factor));

            AbilityDefinition Add(AbilityDefinition definition)
            {
                definition.hideFlags = HideFlags.DontUnloadUnusedAsset;
                content.Abilities[definition.Id] = definition;
                return definition;
            }

            // ---------------- Abilities ----------------
            AbilityDefinition strike = Add(new AbilityDefinition.Builder("sample-strike", "Strike", basic)
                .Type(AbilityType.BasicAttack).Tags(AbilityRole.Damage, AbilityDelivery.Melee).Cost(0).Power(P(1f))
                .Describe("Free physical melee attack. The affordable foundation of every build.")
                .Effect(new DealDamageEffect(1f)).Build());

            AbilityDefinition mend = Add(new AbilityDefinition.Builder("sample-mend", "Mend", heal)
                .Type(AbilityType.Healing).Tags(AbilityRole.Healing, AbilityDelivery.Spell).Element(ElementType.Water)
                .Cost(25, 2).Power(Mathf.RoundToInt(healPower * 0.9f))
                .Describe("Water-attributed healing spell.")
                .Effect(new HealEffect(1f, ElementType.Water)).Build());

            AbilityDefinition riposte = Add(new AbilityDefinition.Builder("sample-riposte", "Riposte", basic)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Melee | AbilityDelivery.Technique)
                .Cost(8).Power(P(1.2f))
                .Describe("Counter strike: spends every stored counter charge (up to 5) for +30% damage each.")
                .Effect(new DealDamageEffect(1f)).Effect(new SpendCountersEffect(5, 0.3f)).Build());

            AbilityDefinition mirror = Add(new AbilityDefinition.Builder("sample-mirror-stance", "Mirror Stance", guard)
                .Type(AbilityType.Defensive).Tags(AbilityRole.Buff | AbilityRole.Defense, AbilityDelivery.Technique)
                .Cost(15, 3).Power(0)
                .Describe("For 2 enemy turns, Perfect defense reflects 60% of the note's damage (Good 30%, max 60 per turn). Also stores a counter charge (2 on a strong chart).")
                .Effect(new ApplyBuffEffect(BuffKind.Reflect, 0.6f, 2, scaleByPerformance: true, reflectCap: 60))
                .Effect(new GainCountersEffect(1, 1, 0.85f)).Build());

            AbilityDefinition fireBolt = Add(new AbilityDefinition.Builder("sample-fire-bolt", "Fire Bolt", flame)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Spell | AbilityDelivery.Ranged)
                .Element(ElementType.Fire).Cost(20).Power(P(2.4f))
                .Describe("Fire spell. Its damage component inherits the Fire element.")
                .Effect(new DealDamageEffect(1f)).Build());

            AbilityDefinition inferno = Add(new AbilityDefinition.Builder("sample-inferno", "Inferno", flame)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Spell).Element(ElementType.Fire)
                .Cost(45, 2).Power(P(4.5f))
                .Describe("Huge fire spell that also applies Burn (fire damage at each enemy turn start, 3 ticks).")
                .Effect(new DealDamageEffect(1f)).Effect(new ApplyStatusEffect(Burn(0.15f, 3))).Build());

            AbilityDefinition focus = Add(new AbilityDefinition.Builder("sample-focus", "Focus", guard)
                .Type(AbilityType.Defensive).Tags(AbilityRole.Buff, AbilityDelivery.Technique).Cost(10, 2).Power(0)
                .Describe("Your next attack deals +60% damage (scaled by this chart). Lasts 2 player turns.")
                .Effect(new ApplyBuffEffect(BuffKind.NextAttackBonus, 0.6f, 2)).Build());

            AbilityDefinition shieldBash = Add(new AbilityDefinition.Builder("sample-shield-bash", "Shield Bash", basic)
                .Type(AbilityType.BasicAttack).Tags(AbilityRole.Damage | AbilityRole.Defense, AbilityDelivery.Melee)
                .Cost(6).Power(P(0.8f))
                .Describe("Physical hit that also gives a shield (x2 its power) for 2 enemy turns.")
                .Effect(new DealDamageEffect(1f)).Effect(new GainShieldEffect(2f, 2)).Build());

            AbilityDefinition bulwark = Add(new AbilityDefinition.Builder("sample-bulwark", "Bulwark", guard)
                .Type(AbilityType.Defensive).Tags(AbilityRole.Defense, AbilityDelivery.Technique).Cost(20, 3).Power(60)
                .Describe("Take 35% less note damage for 2 enemy turns and gain a 60 shield.")
                .Effect(new ApplyBuffEffect(BuffKind.DamageReduction, 0.35f, 2, scaleByPerformance: false))
                .Effect(new GainShieldEffect(1f, 2)).Build());

            AbilityDefinition spellblade = Add(new AbilityDefinition.Builder("sample-spellblade", "Spellblade", basic)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Melee | AbilityDelivery.Spell)
                .Element(ElementType.Fire).Cost(12).Power(P(1.6f))
                .Describe("Mixed hit: a physical component and a Fire component, resolved separately (melee and fire bonuses each apply to their own part).")
                .Effect(new DealDamageEffect(0.6f, ElementType.None)).Effect(new DealDamageEffect(0.6f, ElementType.Fire)).Build());

            AbilityDefinition rally = Add(new AbilityDefinition.Builder("sample-rally", "Rally", heal)
                .Type(AbilityType.Healing).Tags(AbilityRole.Healing | AbilityRole.Buff, AbilityDelivery.Spell).Cost(18, 2)
                .Power(Mathf.RoundToInt(healPower * 0.5f))
                .Describe("Small heal plus +30% on your next attack.")
                .Effect(new HealEffect(1f)).Effect(new ApplyBuffEffect(BuffKind.NextAttackBonus, 0.3f, 2)).Build());

            AbilityDefinition frostLance = Add(new AbilityDefinition.Builder("sample-frost-lance", "Frost Lance", flame)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Spell | AbilityDelivery.Ranged)
                .Element(ElementType.Water).Cost(16).Power(P(2f))
                .Describe("Water spell: the answer to fire-resistant enemies.")
                .Effect(new DealDamageEffect(1f)).Build());

            AbilityDefinition thunderClap = Add(new AbilityDefinition.Builder("sample-thunder-clap", "Thunder Clap", basic)
                .Type(AbilityType.SpecialAttack).Tags(AbilityRole.Damage, AbilityDelivery.Technique).Element(ElementType.Lightning)
                .Cost(14).Power(P(1.8f))
                .Describe("Lightning body technique (not a spell: spell passives do not apply).")
                .Effect(new DealDamageEffect(1f)).Build());

            AbilityDefinition siphon = Add(new AbilityDefinition.Builder("sample-siphon", "Siphon", basic)
                .Type(AbilityType.BasicAttack).Tags(AbilityRole.Damage | AbilityRole.Resource, AbilityDelivery.Melee).Cost(0)
                .Power(P(0.6f))
                .Describe("Weak free hit that restores 8 mana (scaled by performance). A conservation turn.")
                .Effect(new DealDamageEffect(1f)).Effect(new RestoreManaEffect(8)).Build());

            // ---------------- Dedicated upgrades ----------------
            AbilityUpgradeDefinition AddUpgrade(AbilityUpgradeDefinition upgrade)
            {
                upgrade.hideFlags = HideFlags.DontUnloadUnusedAsset;
                content.Upgrades[upgrade.Id] = upgrade;
                return upgrade;
            }

            AbilityUpgradeDefinition honedRiposte = AddUpgrade(new AbilityUpgradeDefinition.Builder("up-riposte-honed", "Honed Riposte")
                .For(riposte.Id).Power(0.25f).Style(StyleParry).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-mirror-lasting", "Lasting Mirror")
                .For(mirror.Id).CooldownDelta(-1).Style(StyleParry).Build());
            AbilityUpgradeDefinition quickcast = AddUpgrade(new AbilityUpgradeDefinition.Builder("up-fire-bolt-quickcast", "Quickcast")
                .For(fireBolt.Id).Cost(-5).Style(StyleGlass).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-inferno-ember-heart", "Ember Heart")
                .For(inferno.Id).CooldownDelta(-1).Cost(5).Describe("Cast Inferno every turn, at a higher price.").Style(StyleGlass).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-mend-overflow", "Overflowing Mend")
                .For(mend.Id).Power(0.2f).Style(StyleTank).Build());
            AbilityUpgradeDefinition searingEdge = AddUpgrade(new AbilityUpgradeDefinition.Builder("up-strike-searing", "Searing Edge")
                .For(strike.Id).Adds(new DealDamageEffect(0.4f, ElementType.Fire))
                .Describe("Strike gains a separate Fire damage component.").Style(StyleAdaptable).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-spellblade-ignite", "Ignite")
                .For(spellblade.Id).Adds(new ApplyStatusEffect(Burn(0.08f, 2))).Cost(3).Style(StyleAdaptable).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-shield-bash-heavy", "Heavy Shield")
                .For(shieldBash.Id).Power(0.3f).Cost(2).Style(StyleTank).Build());
            AddUpgrade(new AbilityUpgradeDefinition.Builder("up-honed-technique", "Honed Technique")
                .Requires(new AbilityRequirement { delivery = AbilityDelivery.Melee, role = AbilityRole.Damage }).Power(0.15f)
                .Describe("+15% power for any melee attack.").Build());

            // ---------------- Passives ----------------
            PassiveDefinition AddPassive(PassiveDefinition passive)
            {
                passive.hideFlags = HideFlags.DontUnloadUnusedAsset;
                content.Passives[passive.Id] = passive;
                return passive;
            }

            // Parry / Deflect
            PassiveDefinition counterPrep = AddPassive(new PassiveDefinition.Builder("ps-counter-preparation", "Counter Preparation", PassiveCategory.RhythmConditioned)
                .Describe("Turns accurate enemy-turn defense into stored counterpower.")
                .Effect(new CounterPreparationPassive(2, 1, 0.1f, 3)).Style(StyleParry).Build());
            PassiveDefinition deflection = AddPassive(new PassiveDefinition.Builder("ps-deflection", "Deflection", PassiveCategory.RhythmConditioned)
                .Describe("Conditional reflection without spending an action.")
                .Effect(new DeflectionPassive(0.3f, 0.1f, 40, 15)).Style(StyleParry).Build());
            AddPassive(new PassiveDefinition.Builder("ps-mirror-mastery", "Mirror Mastery", PassiveCategory.SupportEnhancement, 2)
                .Requires(new AbilityRequirement { requireEffectType = nameof(ApplyBuffEffect) })
                .Effect(new BuffMasteryPassive(BuffKind.Reflect, 1, 0, 0.15f, 0.15f)).Style(StyleParry).Build());

            // Glass cannon
            PassiveDefinition glassHeart = AddPassive(new PassiveDefinition.Builder("ps-glass-heart", "Glass Heart", PassiveCategory.DamageEnhancement, 2)
                .Describe("Big spell damage, paid for with max health.")
                .Requires(new AbilityRequirement { delivery = AbilityDelivery.Spell, role = AbilityRole.Damage })
                .Exclusive("heart")
                .Effect(new MaxHealthPassive(-0.35f, 0f))
                .Effect(new ActionModifierPassive(EffectKind.Damage, new EffectFilter(AbilityDelivery.Spell), 0.3f, 0.15f))
                .Style(StyleGlass).Build());
            PassiveDefinition manaSurge = AddPassive(new PassiveDefinition.Builder("ps-mana-surge", "Mana Surge", PassiveCategory.DamageEnhancement)
                .Effect(new ManaSurgePassive(30, 0.2f, 0.1f)).Style(StyleGlass).Build());
            PassiveDefinition pyromancy = AddPassive(new PassiveDefinition.Builder("ps-pyromancy", "Pyromancy", PassiveCategory.ElementalEnhancement)
                .Requires(new AbilityRequirement { requireElement = true, element = ElementType.Fire })
                .Stacking("element-fire", 0.6f)
                .Effect(new ActionModifierPassive(EffectKind.Damage, EffectFilter.ForElement(ElementType.Fire), 0.25f, 0.1f))
                .Style(StyleGlass, StyleAdaptable).Build());
            PassiveDefinition conservation = AddPassive(new PassiveDefinition.Builder("ps-conservation", "Conservation", PassiveCategory.MagicEfficiency)
                .Effect(new ConservationPassive(8, 0.6f, 5, 3)).Style(StyleGlass, StyleAdaptable).Build());
            AddPassive(new PassiveDefinition.Builder("ps-kindling", "Kindling", PassiveCategory.ElementalEnhancement, 2)
                .Requires(new AbilityRequirement { requireEffectType = nameof(ApplyStatusEffect) })
                .Effect(new StatusMasteryPassive("burn", 1, 0, 0.25f, 0.15f)).Style(StyleGlass).Build());

            // Tank
            PassiveDefinition fortitude = AddPassive(new PassiveDefinition.Builder("ps-fortitude", "Fortitude", PassiveCategory.Survivability)
                .Exclusive("heart").Effect(new MaxHealthPassive(0.3f, 0.1f)).Style(StyleTank).Build());
            PassiveDefinition ironSkin = AddPassive(new PassiveDefinition.Builder("ps-iron-skin", "Iron Skin", PassiveCategory.Survivability)
                .Effect(new MitigationPassive(0.25f, 0.05f, 40, 15)).Style(StyleTank).Build());
            PassiveDefinition secondWind = AddPassive(new PassiveDefinition.Builder("ps-second-wind", "Second Wind", PassiveCategory.Conversion)
                .Requires(new AbilityRequirement { role = AbilityRole.Healing })
                .Effect(new RecoveryConversionPassive(0.5f, 0.15f, 2)).Style(StyleTank).Build());
            PassiveDefinition tidal = AddPassive(new PassiveDefinition.Builder("ps-tidal-healing", "Tidal Healing", PassiveCategory.SupportEnhancement)
                .Requires(new AbilityRequirement { requireElement = true, element = ElementType.Water, role = AbilityRole.Healing })
                .Effect(new ActionModifierPassive(EffectKind.Healing, EffectFilter.ForElement(ElementType.Water), 0.25f, 0.1f))
                .Style(StyleTank).Build());
            AddPassive(new PassiveDefinition.Builder("ps-steady-guard", "Steady Guard", PassiveCategory.RhythmConditioned)
                .Effect(new SteadyGuardPassive(3, 30, 15, 1)).Style(StyleTank, StyleParry).Build());
            AddPassive(new PassiveDefinition.Builder("ps-bulwark-mastery", "Bastion", PassiveCategory.SupportEnhancement, 2)
                .Requires(new AbilityRequirement { requireEffectType = nameof(ApplyBuffEffect) })
                .Effect(new BuffMasteryPassive(BuffKind.DamageReduction, 1, 0, 0f, 0f)).Style(StyleTank).Build());

            // Adaptable
            PassiveDefinition versatility = AddPassive(new PassiveDefinition.Builder("ps-versatility", "Versatility", PassiveCategory.ActionSequence)
                .Effect(new VersatilityPassive(0.2f, 0.05f)).Style(StyleAdaptable).Build());
            PassiveDefinition followThrough = AddPassive(new PassiveDefinition.Builder("ps-follow-through", "Follow-Through", PassiveCategory.ActionSequence)
                .Effect(new FollowThroughPassive(0.5f, 0.25f, 0.1f, 2)).Style(StyleAdaptable, StyleTank).Build());
            PassiveDefinition measured = AddPassive(new PassiveDefinition.Builder("ps-measured-execution", "Measured Execution", PassiveCategory.RhythmConditioned)
                .Requires(new AbilityRequirement { role = AbilityRole.Damage })
                .Effect(new MeasuredExecutionPassive(0.85f, 0.15f, 0.05f)).Style(StyleAdaptable, StyleGlass).Build());
            PassiveDefinition thrift = AddPassive(new PassiveDefinition.Builder("ps-arcane-thrift", "Arcane Thrift", PassiveCategory.MagicEfficiency)
                .Requires(new AbilityRequirement { delivery = AbilityDelivery.Spell })
                .Effect(new ManaCostPassive(new EffectFilter(AbilityDelivery.Spell), 0.2f, 0.05f, 1)).Style(StyleAdaptable, StyleGlass).Build());
            AddPassive(new PassiveDefinition.Builder("ps-blade-discipline", "Blade Discipline", PassiveCategory.MeleeEnhancement)
                .Requires(new AbilityRequirement { delivery = AbilityDelivery.Melee, role = AbilityRole.Damage })
                .Stacking("melee-damage", 0.6f)
                .Effect(new ActionModifierPassive(EffectKind.Damage, new EffectFilter(AbilityDelivery.Melee), 0.2f, 0.1f))
                .Style(StyleParry, StyleAdaptable).Build());

            // Fallback (always eligible, levels up to 5)
            AddPassive(new PassiveDefinition.Builder("ps-vitality", "Vitality", PassiveCategory.Survivability, 5)
                .Fallback().Effect(new MaxHealthPassive(0.05f, 0.05f)).Build());

            // ---------------- Presets ----------------
            BuildPreset AddPreset(BuildPreset preset)
            {
                preset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                content.Presets.Add(preset);
                return preset;
            }

            AddPreset(new BuildPreset.Builder("preset-parry", "Mirror Guard (Parry / Deflect)", StyleParry)
                .Describe("Objective: turn accurate defense into offense. Perfect defense stores counter charges (Counter Preparation) and reflects damage (Deflection). " +
                          "Mirror Stance spends a turn for strong reflection + a charge; Riposte cashes charges in (+30% each). Strike is the free fallback, Mend the safety valve.\n" +
                          "Trade-off: needs Perfect defense; charges fade after 2 idle player turns.")
                .Slot(strike).Slot(riposte).Slot(mirror).Slot(mend)
                .Passive(counterPrep).Passive(deflection).Build());

            AddPreset(new BuildPreset.Builder("preset-glass", "Pyromancer (Glass Cannon)", StyleGlass)
                .Describe("Objective: spend mana on huge fire damage. Glass Heart: -35% max HP, +30% spell damage. Mana Surge: +20% on casts paying 30+ MP. " +
                          "Pyromancy: +25% Fire components. Conservation: a well-played Strike restores 5 MP. Focus primes the next attack by +60%.\n" +
                          "Trade-off: 650 HP; Inferno (45 MP) competes with survival; conservation turns are weak.")
                .Slot(strike).Slot(fireBolt).Slot(inferno).Slot(focus)
                .Passive(glassHeart).Passive(manaSurge).Passive(pyromancy).Passive(conservation).Build());

            AddPreset(new BuildPreset.Builder("preset-tank", "Bulwark (Tank)", StyleTank)
                .Describe("Objective: outlast the enemy with steady offense. Fortitude +30% HP, Iron Skin -25% note damage (max 40 prevented per enemy turn), " +
                          "Second Wind turns overheal into shield, Tidal Healing +25% Water healing. Shield Bash hits and shields; Bulwark cuts damage 35% for 2 turns.\n" +
                          "Trade-off: low damage, long fights; shields are capped (30% max HP) and expire.")
                .Slot(shieldBash).Slot(strike).Slot(bulwark).Slot(mend)
                .Passive(fortitude).Passive(ironSkin).Passive(secondWind).Passive(tidal).Build());

            AddPreset(new BuildPreset.Builder("preset-adaptable", "Wanderer (Adaptable)", StyleAdaptable)
                .Describe("Objective: value from switching tools. Versatility: +20% when your action's role differs from the last one. Follow-Through: a good Rally gives the next attack +25%. " +
                          "Measured Execution: +15% damage at 85%+ charts. Arcane Thrift: spells cost 20% less. Strike has Searing Edge (extra Fire component); Frost Lance answers fire resistance.\n" +
                          "Trade-off: lower peaks than the specialists; needs sequencing.")
                .Slot(strike, searingEdge).Slot(spellblade).Slot(rally).Slot(frostLance)
                .Passive(versatility).Passive(followThrough).Passive(measured).Passive(thrift).Build());

            AddPreset(new BuildPreset.Builder("preset-blank", "Blank Slate (reward progression)", string.Empty)
                .Describe("Strike + Mend and no passives. Win fights and claim rewards (Rewards tab) to grow a build from scratch.")
                .Slot(strike).Slot(mend).Build());

            // Hybrid example: parry + glass. Glass Heart's HP cost stays; counters replace Focus as the burst setup.
            AddPreset(new BuildPreset.Builder("preset-hybrid", "Riposte Mage (Parry + Glass hybrid)", StyleParry)
                .Describe("Hybrid: counter charges feed a melee payoff while Fire Bolt carries spell damage. Glass Heart's -35% HP still applies " +
                          "and counters only come from Perfect defense, so neither trade-off disappears.")
                .Slot(strike).Slot(riposte, honedRiposte).Slot(fireBolt, quickcast).Slot(siphon)
                .Passive(counterPrep).Passive(glassHeart).Build());

            // Keep unused locals referenced for readers: every ability above is in the reward pool.
            _ = thunderClap;
            return content;
        }
    }
}
