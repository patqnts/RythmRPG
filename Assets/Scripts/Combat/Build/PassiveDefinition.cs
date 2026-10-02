using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    public enum PassiveCategory
    {
        DamageEnhancement,
        MeleeEnhancement,
        MagicEfficiency,
        ElementalEnhancement,
        SupportEnhancement,
        RhythmConditioned,
        ActionSequence,
        Conversion,
        Survivability
    }

    /// <summary>
    /// What the build must contain for something to be offered / active. Every set condition must hold for at least
    /// one equipped ability (None / unchecked = no condition). Empty requirement = always satisfied.
    /// </summary>
    [Serializable]
    public sealed class AbilityRequirement
    {
        public AbilityDelivery delivery = AbilityDelivery.None;
        public AbilityRole role = AbilityRole.None;
        [Tooltip("An equipped ability must have a damage / status / heal component of this element.")]
        public bool requireElement;
        public ElementType element = ElementType.None;
        [Tooltip("An equipped ability must carry an effect of this type name (e.g. ApplyStatusEffect).")]
        public string requireEffectType = string.Empty;

        public bool IsEmpty => delivery == AbilityDelivery.None && role == AbilityRole.None && !requireElement
                               && string.IsNullOrEmpty(requireEffectType);

        public bool SatisfiedBy(AbilityQuote quote)
        {
            if (quote?.Definition == null) return false;
            if (delivery != AbilityDelivery.None && (quote.Delivery & delivery) == 0) return false;
            if (role != AbilityRole.None && (quote.Roles & role) == 0) return false;
            if (requireElement && !quote.DamageElements().Concat(quote.HealingElements()).Contains(element)) return false;
            if (!string.IsNullOrEmpty(requireEffectType)
                && !(requireEffectType == nameof(ApplyStatusEffect) && quote.AppliedStatuses().Any())
                && quote.Effects.All(effect => effect.GetType().Name != requireEffectType)) return false;
            return true;
        }

        public bool SatisfiedBy(IEnumerable<AbilityQuote> quotes) => IsEmpty || quotes.Any(SatisfiedBy);

        public string Describe()
        {
            var parts = new List<string>();
            if (delivery != AbilityDelivery.None) parts.Add(delivery.ToString().Replace(", ", "/"));
            if (role != AbilityRole.None) parts.Add(role.ToString().Replace(", ", "/"));
            if (requireElement) parts.Add(BuildTagUtility.ElementName(element));
            if (!string.IsNullOrEmpty(requireEffectType)) parts.Add(requireEffectType.Replace("Effect", string.Empty));
            return parts.Count == 0 ? "any build" : "a " + string.Join(" ", parts) + " ability";
        }
    }

    /// <summary>
    /// A passive upgrade: acquired as a reward, lives in the run's passive collection (never in an ability slot) and
    /// applies automatically. Its behaviour is a list of <see cref="PassiveEffect"/>s; this asset holds identity,
    /// levels, acquisition prerequisites, stacking and exclusivity.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Passive", fileName = "Passive")]
    public sealed class PassiveDefinition : ScriptableObject
    {
        [SerializeField] private string id = "passive";
        [SerializeField] private string displayName = "Passive";
        [Tooltip("Shown in the passive column, the loadout panel and reward cards. Empty = the name's first letter on a category-coloured tile.")]
        [SerializeField] private Sprite icon;
        [SerializeField, TextArea(2, 4)] private string description = string.Empty;
        [SerializeField] private PassiveCategory category = PassiveCategory.DamageEnhancement;
        [SerializeField, Min(1)] private int maxLevel = 3;
        [Tooltip("Acquisition eligibility: the build must contain a matching ability for this to be offered. It is also " +
                 "what makes an owned passive active; with no compatible ability equipped it stays owned but inactive.")]
        [SerializeField] private AbilityRequirement requirement = new();
        [Tooltip("Owning another passive with the same exclusivity group blocks this one (empty = none).")]
        [SerializeField] private string exclusivityGroup = string.Empty;
        [Tooltip("Modifiers from passives sharing a stacking group add up to Stacking Cap (empty = own group).")]
        [SerializeField] private string stackingGroup = string.Empty;
        [SerializeField, Min(0f)] private float stackingCap;
        [Tooltip("Playstyle tags used to weight reward offers (parry, glass-cannon, tank, adaptable...).")]
        [SerializeField] private List<string> styleTags = new();
        [Tooltip("Fallback reward: offered when too few other options are eligible.")]
        [SerializeField] private bool isFallback;
        [SerializeReference, SubclassSelector] private List<PassiveEffect> effects = new();

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        internal void SetCodeIcon(Sprite value) => icon = value;
        public string Description => description;
        public PassiveCategory Category => category;
        public int MaxLevel => Mathf.Max(1, maxLevel);
        public AbilityRequirement Requirement => requirement ??= new AbilityRequirement();
        public string ExclusivityGroup => exclusivityGroup;
        public string StackingGroup => string.IsNullOrEmpty(stackingGroup) ? id : stackingGroup;
        public float StackingCap => stackingCap;
        public IReadOnlyList<string> StyleTags => styleTags;
        public bool IsFallback => isFallback;
        public IReadOnlyList<PassiveEffect> Effects => effects;

        public string DescribeLevel(int level)
        {
            IEnumerable<string> lines = effects.Where(effect => effect != null).Select(effect => effect.Describe(level));
            return string.Join("; ", lines);
        }

        public sealed class Builder
        {
            private readonly PassiveDefinition definition;

            public Builder(string id, string displayName, PassiveCategory category, int maxLevel = 3)
            {
                definition = CreateInstance<PassiveDefinition>();
                definition.name = displayName;
                definition.id = id;
                definition.displayName = displayName;
                definition.category = category;
                definition.maxLevel = maxLevel;
                definition.effects = new List<PassiveEffect>();
                definition.styleTags = new List<string>();
            }

            public Builder Describe(string text) { definition.description = text; return this; }
            public Builder Icon(Sprite sprite) { definition.icon = sprite; return this; }
            public Builder Requires(AbilityRequirement requirement) { definition.requirement = requirement; return this; }
            public Builder Exclusive(string group) { definition.exclusivityGroup = group; return this; }
            public Builder Stacking(string group, float cap) { definition.stackingGroup = group; definition.stackingCap = cap; return this; }
            public Builder Style(params string[] tags) { definition.styleTags.AddRange(tags); return this; }
            public Builder Fallback() { definition.isFallback = true; return this; }
            public Builder Effect(PassiveEffect effect) { if (effect != null) definition.effects.Add(effect); return this; }
            public PassiveDefinition Build() => definition;
        }
    }

    /// <summary>Everything a passive hook can reach. State is per passive instance per encounter.</summary>
    public sealed class PassiveHook
    {
        public CombatBuildRuntime Runtime;
        public PassiveInstance Instance;
        public Dictionary<string, int> State;

        public int Level => Instance != null ? Instance.Level : 1;
        public PassiveDefinition Definition => Instance?.Definition;
        public string SourceId => Instance?.Definition != null ? "passive:" + Instance.Definition.Id : "passive";
        public string Label => Instance?.Definition != null ? Instance.Definition.DisplayName : "Passive";
        public string StackKey => Instance?.Definition != null ? Instance.Definition.StackingGroup : null;
        public float StackCap => Instance?.Definition != null ? Instance.Definition.StackingCap : 0f;

        public int Get(string key) => State != null && State.TryGetValue(key, out int value) ? value : 0;
        public void Set(string key, int value) { if (State != null) State[key] = value; }
        public int Add(string key, int delta) { int value = Get(key) + delta; Set(key, value); return value; }
    }

    /// <summary>A defense note after its damage transaction (raw judgement kept separate from the outcome).</summary>
    public sealed class DefenseNoteOutcome
    {
        public RhythmJudgementResult Result;
        public string RootCauseId;
        public int NoteDamage;
        public int Attempted;
        public int Prevented;
        public int Absorbed;
        public int Actual;
        public bool IsPlayerExecution => Result.Source == NoteResolutionSource.PlayerInput;
    }

    /// <summary>Summary of one whole enemy turn (all attack steps), for whole-turn triggers that must fire once.</summary>
    public sealed class EnemyTurnSummary
    {
        public int EnemyTurn;
        public int Opportunities;
        public readonly int[] PlayerJudgements = new int[4];
        public int NonPlayerResolutions;
        /// <summary>Notes cleared by board effects (zaps, walls): not opportunities and not misses.</summary>
        public int Cleared;
        public int DamageTaken;
        public bool Interrupted;

        /// <summary>Had opportunities and was not interrupted: may qualify for completion rewards.</summary>
        public bool Eligible => Opportunities > 0 && !Interrupted;
        public int Misses => PlayerJudgements[(int)HitJudgement.Miss] + NonPlayerResolutions;
        public int Successes => PlayerJudgements[(int)HitJudgement.Good] + PlayerJudgements[(int)HitJudgement.Perfect];
    }

    public readonly struct HealOutcome
    {
        public readonly int Attempted;
        public readonly int Actual;
        public readonly int Overheal;
        public readonly ElementType Element;
        public readonly string SourceId;
        public readonly string RootCauseId;
        public readonly bool Secondary;

        public HealOutcome(int attempted, int actual, ElementType element, string sourceId, string rootCauseId, bool secondary)
        {
            Attempted = attempted;
            Actual = actual;
            Overheal = Mathf.Max(0, attempted - actual);
            Element = element;
            SourceId = sourceId;
            RootCauseId = rootCauseId;
            Secondary = secondary;
        }
    }

    /// <summary>
    /// One behaviour of a passive. Override only the hooks it needs. Hooks run in passive acquisition order, then effect
    /// order, so results are deterministic. Generated effects must go through the runtime's services (never apply
    /// damage/heal directly) so they are attributed and cannot re-trigger themselves.
    /// </summary>
    [Serializable]
    public abstract class PassiveEffect
    {
        protected static float LevelValue(float first, float perLevel, int level) => first + perLevel * (Mathf.Max(1, level) - 1);
        protected static int LevelValue(int first, int perLevel, int level) => first + perLevel * (Mathf.Max(1, level) - 1);

        public abstract string Describe(int level);

        /// <summary>
        /// Whether the passive does anything with the current loadout (in addition to the definition's requirement).
        /// Inactive passives stay owned and are shown as inactive.
        /// </summary>
        public virtual bool IsActive(IReadOnlyList<AbilityQuote> equipped) => true;

        /// <summary>Changes max health at encounter start (glass-cannon trade-offs, fortitude...).</summary>
        public virtual int ModifyMaxHealth(int maxHealth, int level) => maxHealth;
        /// <summary>Cost / cooldown changes shown in previews and paid on commit.</summary>
        public virtual void ModifyQuote(AbilityQuote quote, int level, string label) { }
        public virtual void OnEncounterStart(PassiveHook hook) { }
        /// <summary>Before the cast's totals freeze: add cast modifiers (performance is final here).</summary>
        public virtual void OnPreOutcome(PassiveHook hook, CastSnapshot cast) { }
        /// <summary>After all of a cast's effects landed.</summary>
        public virtual void OnCastResolved(PassiveHook hook, CastSnapshot cast) { }
        /// <summary>Changes the damage an enemy note is about to deal (after judgement rules and wards).</summary>
        public virtual int ModifyIncomingDamage(PassiveHook hook, RhythmJudgementResult result, int amount) => amount;
        /// <summary>After a defense note's damage transaction (also called for notes that dealt no damage).</summary>
        public virtual void OnDefenseNoteSettled(PassiveHook hook, DefenseNoteOutcome outcome) { }
        /// <summary>Once per enemy turn, spanning all attack steps.</summary>
        public virtual void OnEnemyTurnEnded(PassiveHook hook, EnemyTurnSummary summary) { }
        public virtual void OnHealed(PassiveHook hook, HealOutcome heal) { }
        public virtual void ModifyBuff(PassiveHook hook, BuffSpec spec) { }
        public virtual void ModifyStatus(PassiveHook hook, StatusSpec spec, ref int damagePerTick, ref int ticks) { }
        public virtual void OnTurnBoundary(PassiveHook hook, TurnBoundary boundary) { }
        public virtual void OnDefensePhrase(PassiveHook hook, PhraseOutcome phrase, string rootId) { }
        public virtual void OnRhythmChallenge(PassiveHook hook, PatternRunMode mode, string rootId) { }
        public virtual void OnReactionResolved(PassiveHook hook, ReactionContext reaction) { }
        public virtual void OnDefenseManaOverflow(PassiveHook hook, int overflow) { }
        /// <summary>Extra stack cap for a stacking status / mark (Pyre Keeper: Burn +2).</summary>
        public virtual int StatusStackBonus(PassiveHook hook, string statusId) => 0;
        /// <summary>A reaction is about to resolve: change its strength or keep the marks (Catalyst).</summary>
        public virtual void ModifyReaction(PassiveHook hook, ReactionContext context) { }
        /// <summary>A note would kill the player: return true to leave them at 1 HP instead (once-per-battle effects).</summary>
        public virtual bool PreventLethal(PassiveHook hook) => false;
    }
}
