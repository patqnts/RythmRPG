using System;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class RhythmSynergyRules
    {
        [Range(0f, 1f)] public float phraseSuccess = .7f;
        [Min(0)] public int smallShieldPerTurn = 20;
        [Min(0)] public int passiveManaPerCast = 12;
        [Range(0f, 1f)] public float maximumCostDiscount = .6f;
        [Min(0)] public int extraPlayerEffectTurns = 2;
    }

    public sealed class CastChoice
    {
        public bool SpendShield;
        public bool ConsumeBurn;
        public string ExtendKey;
        public string Label;
        public string Description;
    }

    public sealed class PhraseOutcome
    {
        public string Id;
        public string Label;
        public int Expected;
        public int Played;
        public float Accuracy;
        public bool Final;
        public bool Completed;
        public bool Successful(float threshold) => Completed && Played > 0 && Accuracy + .0001f >= threshold;
    }

    /// <summary>Budgets use the original authored notes: automatic clears cannot redistribute their reward shares.</summary>
    public sealed class RhythmPhraseTracker
    {
        private sealed class Section
        {
            public PhraseOutcome Outcome;
            public readonly HashSet<string> Notes = new();
            public readonly HashSet<string> Settled = new();
            public float Weight;
        }
        private readonly List<Section> sections = new();
        private readonly AbilityOutcomeProfile profile;
        private readonly bool defense;
        public IReadOnlyList<PhraseOutcome> Outcomes => sections.Select(s => s.Outcome).ToList();
        public PhraseOutcome Pending => sections.FirstOrDefault(s => !s.Outcome.Completed)?.Outcome;

        public RhythmPhraseTracker(RhythmChart chart, bool defense, AbilityOutcomeProfile profile = null)
        {
            this.profile = profile;
            this.defense = defense;
            if (chart == null) return;
            double bar = 60d / Math.Max(.01f, chart.Bpm) * Math.Max(1, chart.BeatsPerMeasure);
            foreach (var group in chart.Notes.Where(n => n != null).OrderBy(n => n.EndTime)
                         .GroupBy(n => (int)Math.Floor(Math.Max(0d, n.EndTime - chart.AudioOffsetSeconds) / bar)))
            {
                var section = new Section { Outcome = new PhraseOutcome { Id = "bar-" + group.Key, Label = "Phrase " + (sections.Count + 1) } };
                foreach (RhythmNoteData note in group) section.Notes.Add(note.Id);
                section.Outcome.Expected = section.Notes.Count;
                sections.Add(section);
            }
            if (sections.Count > 0) sections[sections.Count - 1].Outcome.Final = true;
        }

        public PhraseOutcome Record(RhythmJudgementResult result)
        {
            Section section = sections.FirstOrDefault(s => s.Notes.Contains(result.NoteId));
            if (section == null || !section.Settled.Add(result.NoteId)) return null;
            if (result.Source == NoteResolutionSource.PlayerInput)
            {
                section.Outcome.Played++;
                section.Weight += defense ? CombatResourceRules.Load().AccuracyWeight(result.Judgement)
                    : RhythmPerformanceCalculator.Weight(result.Judgement, profile);
            }
            section.Outcome.Accuracy = section.Weight / Math.Max(1, section.Outcome.Expected);
            section.Outcome.Completed = section.Settled.Count == section.Outcome.Expected;
            return section.Outcome.Completed ? section.Outcome : null;
        }

        public void Cancel()
        {
            foreach (Section section in sections.Where(s => !s.Outcome.Completed)) section.Outcome.Accuracy = 0f;
        }
    }

    /// <summary>New connections share the existing combat resources; no separate build-specific meter.</summary>
    public sealed partial class CombatBuildRuntime
    {
        private RhythmPhraseTracker phrases;
        private PatternRunMode phraseMode;
        private int phraseRun;
        private int smallShieldUsed;
        private readonly Dictionary<string, int> passiveManaUsed = new();
        public PhraseOutcome CurrentPhrase => phrases?.Pending;
        public bool HasPassive<T>() where T : PassiveEffect => hooks.Any(h => h.Definition.Effects.Any(e => e is T));

        private void ResetSynergy()
        {
            phrases = null;
            phraseRun = smallShieldUsed = 0;
            passiveManaUsed.Clear();
        }

        public void BeginRhythmPattern(RhythmChart chart, PatternRunContext context)
        {
            phraseMode = context.Mode;
            phraseRun++;
            phrases = new RhythmPhraseTracker(chart, context.Mode == PatternRunMode.EnemyDefense, CurrentCast?.Definition?.OutcomeProfile);
            RaiseChanged();
        }

        public void EndRhythmPattern(PatternRunResult result)
        {
            if (result.WasCancelled) phrases?.Cancel();
            if (result.Mode == PatternRunMode.PlayerAbility && CurrentCast != null)
                CurrentCast.ClosingPhrase = result.WasCancelled ? null : phrases?.Outcomes.LastOrDefault();
            phrases = null;
            RaiseChanged();
        }

        public void RecordPhraseJudgement(RhythmJudgementResult result)
        {
            if (CombatOver || phrases == null) return;
            PhraseOutcome outcome = phrases.Record(result);
            if (outcome != null && phraseMode == PatternRunMode.EnemyDefense)
            {
                string root = $"e{EncounterId}-et{EnemyTurn}-phrase{phraseRun}-{outcome.Id}";
                if (Events.TryClaim(root))
                    foreach (PassiveHook hook in hooks) ForEachEffect(hook, e => e.OnDefensePhrase(hook, outcome, root));
            }
            if (outcome != null) ShowAtPlayer($"{outcome.Label.ToUpperInvariant()} {Mathf.RoundToInt(outcome.Accuracy * 100f)}%", new Color(.7f, .9f, 1f));
            RaiseChanged();
        }

        public void CompleteRhythmChallenge(PatternRunMode mode, string id)
        {
            if (CombatOver) return;
            string root = mode == PatternRunMode.PlayerAbility ? CurrentCast?.CastId : $"e{EncounterId}-et{EnemyTurn}";
            if (root == null || !Events.TryClaim($"challenge:{root}:{phraseRun}:{id}")) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, e => e.OnRhythmChallenge(hook, mode, root));
        }

        public int GrantSmallShield(int amount, PassiveHook hook, string root)
        {
            int allowed = Mathf.Min(amount, Mathf.Max(0, Rules.Synergy.smallShieldPerTurn - smallShieldUsed));
            int added = Damage.AddShield(allowed, 2, hook.SourceId, root, true, hook.Label, hook.Definition.Icon);
            smallShieldUsed += added;
            return added;
        }

        public int RestorePassiveMana(int amount, PassiveHook hook, string root)
        {
            if (amount <= 0 || root == null) return 0;
            passiveManaUsed.TryGetValue(root, out int used);
            int allowed = Mathf.Min(amount, Mathf.Max(0, Rules.Synergy.passiveManaPerCast - used));
            int gained = Damage.RestoreMana(allowed, hook.SourceId, root, true, hook.Label);
            passiveManaUsed[root] = used + gained;
            return gained;
        }

        public void NotifyDefenseManaOverflow(int overflow)
        {
            if (CombatOver || overflow <= 0) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, e => e.OnDefenseManaOverflow(hook, overflow));
        }

        internal void ReactionResolved(ReactionContext reaction)
        {
            if (CombatOver) return;
            // Delayed hits / ticks have no current cast and are not part of an enemy defense note.
            bool cast = CurrentCast != null && CurrentCast.CastId == reaction.RootId;
            bool defense = reaction.RootId != null && reaction.RootId.StartsWith($"e{EncounterId}-et{EnemyTurn}-", StringComparison.Ordinal);
            if (!cast && !defense) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, e => e.OnReactionResolved(hook, reaction));
        }

        public List<CastChoice> ChoicesFor(AbilityRuntimeInstance ability)
        {
            var result = new List<CastChoice>();
            if (ability == null) return result;
            AbilityQuote quote = ability.Quote();
            int shield = Modifiers?.Find<ShieldBuff>(ShieldBuff.Key)?.Capacity ?? 0;
            if (quote.Effects.Any(e => e is RepriseEffect))
            {
                foreach (ICombatModifierRuntime modifier in Modifiers?.ActiveModifiers ?? Array.Empty<ICombatModifierRuntime>())
                {
                    if (modifier is TimedBuff buff && !(buff is StatusInstance) && buff.CanReprise(Rules.Synergy.extraPlayerEffectTurns))
                        result.Add(new CastChoice { Label = buff.IconLabel, Description = $"Keep this effect for one more turn ({buff.TurnsRemaining} remaining).", ExtendKey = buff.StackKey });
                    else if (modifier is LaneWardModifier ward && ward.CanReprise(Rules.Synergy.extraPlayerEffectTurns))
                        result.Add(new CastChoice { Label = ward.Label, Description = "Keep this lane ward for one more enemy turn.", ExtendKey = "ward:" + ward.GetHashCode() });
                }
                return result;
            }
            result.Add(new CastChoice { Label = quote.Effects.Any(e => e is BreakwaterEffect) ? "Guard" : "Keep resources", Description = "Use the ability without spending shield or Burn." });
            if (quote.Effects.Any(e => e is CauterizeEffect) && Marks.Has(ElementalMarks.Burn))
                result.Add(new CastChoice { Label = "Consume one Burn", Description = "Remove one Burn stack for stronger healing. Remaining Burn stays.", ConsumeBurn = true });
            bool breakwater = quote.Effects.Any(e => e is BreakwaterEffect);
            if (shield > 0 && (breakwater || (quote.Roles & AbilityRole.Damage) != 0 && HasPassive<PressureCastPassive>()))
            {
                int amount = Mathf.Min(breakwater ? 30 : 20, Mathf.CeilToInt(shield * (breakwater ? 1f / 3f : .2f)));
                result.Add(new CastChoice { Label = breakwater ? "Break" : "Pressure Cast", Description = $"Spend {amount} shield for extra {BuildTagUtility.ElementName(quote.Element)} damage; {shield - amount} shield remains.", SpendShield = true });
            }
            return result;
        }

        private void CommitChoice(CastSnapshot cast, CastChoice choice)
        {
            cast.Choice = choice ?? new CastChoice();
            cast.SmallShieldBeforeCast = Modifiers?.Find<ShieldBuff>(ShieldBuff.Key)?.Capacity ?? 0;
            if (cast.Choice.SpendShield && cast.HasRole(AbilityRole.Damage))
            {
                bool strong = cast.Quote.Effects.Any(e => e is BreakwaterEffect);
                if (strong || HasPassive<PressureCastPassive>())
                {
                    int amount = Mathf.Min(strong ? 30 : 20, Mathf.CeilToInt(cast.SmallShieldBeforeCast * (strong ? 1f / 3f : .2f)));
                    cast.ShieldSpent = Damage.SpendShield(amount, cast.CastId);
                    if (cast.ShieldSpent > 0) cast.Quote.Effects.Add(new ShieldSpendDamageEffect());
                }
            }
            if (cast.Choice.ConsumeBurn && cast.Quote.Effects.Any(e => e is CauterizeEffect))
            {
                StatusInstance burn = Marks.Get(ElementalMarks.Burn);
                if (burn != null)
                {
                    if (burn.Stacks == 1) Marks.Remove(ElementalMarks.Burn);
                    else burn.SetStacks(burn.Stacks - 1);
                    cast.BurnConsumed = true;
                    Modifiers.NotifyChanged();
                }
            }
            cast.ShieldForBedrock = Modifiers?.Find<ShieldBuff>(ShieldBuff.Key)?.Capacity ?? 0;
        }

        public bool ExtendPlayerEffect(string key)
        {
            if (Modifiers == null || string.IsNullOrEmpty(key)) return false;
            bool extended = false;
            foreach (ICombatModifierRuntime modifier in Modifiers.ActiveModifiers)
            {
                if (modifier is TimedBuff buff && !(buff is StatusInstance) && buff.StackKey == key)
                    extended = buff.Reprise(Rules.Synergy.extraPlayerEffectTurns);
                else if (modifier is LaneWardModifier ward && "ward:" + ward.GetHashCode() == key)
                    extended = ward.Reprise(Rules.Synergy.extraPlayerEffectTurns);
                if (extended) break;
            }
            if (extended) Modifiers.NotifyChanged();
            return extended;
        }
    }
}
