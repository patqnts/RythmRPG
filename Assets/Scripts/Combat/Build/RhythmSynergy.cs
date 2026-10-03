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
        [Tooltip("Pressure Cast bonus damage per point of shield spent.")]
        [Min(0f)] public float pressureCastDamagePerShield = 2f;
        [Tooltip("Breakwater's stronger Break bonus damage per point of shield spent.")]
        [Min(0f)] public float breakwaterDamagePerShield = 3f;
    }

    public sealed class CastChoice
    {
        public bool SpendShield;
        public bool ConsumeBurn;
        public string ExtendKey;
        public string MarkId;
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
        private readonly Dictionary<string, ComboJudgementOutcome> pendingCombo = new();
        private bool comboSaveUsedThisEnemyTurn;
        private float pendingReactionBonus;
        private string pendingReactionBonusLabel;
        public AbilitySlotController AbilitySlots { get; set; }
        public PhraseOutcome CurrentPhrase => phrases?.Pending;
        public bool HasPassive<T>() where T : PassiveEffect => hooks.Any(h => h.Definition.Effects.Any(e => e is T));

        private void ResetSynergy()
        {
            phrases = null;
            phraseRun = smallShieldUsed = 0;
            passiveManaUsed.Clear();
            pendingCombo.Clear();
            comboSaveUsedThisEnemyTurn = false;
            pendingReactionBonus = 0f;
            pendingReactionBonusLabel = null;
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
            if (mode == PatternRunMode.PlayerAbility && CurrentCast != null && !string.IsNullOrEmpty(id))
                CurrentCast.CompletedChallenges.Add(id);
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, e => e.OnRhythmChallenge(hook, mode, root));
        }

        /// <summary>Consumes the one shared Combo-save allowance for this enemy turn.</summary>
        public bool TryPreventComboBreak(RhythmJudgementResult result, PatternRunMode mode, int combo)
        {
            if (mode != PatternRunMode.EnemyDefense || combo <= 0 || comboSaveUsedThisEnemyTurn || CombatOver) return false;
            GraceNoteBuff grace = Modifiers?.OfType<GraceNoteBuff>().FirstOrDefault(buff => !buff.IsExpired);
            string label = null;
            if (grace != null && grace.TryUse()) label = grace.Label;
            if (label == null)
            {
                foreach (PassiveHook hook in hooks)
                {
                    bool saved = false;
                    ForEachEffect(hook, effect => saved |= effect.TryPreventComboBreak(hook, result, combo));
                    if (!saved) continue;
                    label = hook.Label;
                    break;
                }
            }
            if (label == null) return false;
            comboSaveUsedThisEnemyTurn = true;
            Record(new CombatEvent { Kind = CombatEventKind.PassiveTriggered, SourceId = label, RootCauseId = result.NoteId,
                RawJudgement = result.Judgement, Note = $"{label}: preserved {combo} Combo" });
            ShowAtPlayer("COMBO SAVED", new Color(.7f, 1f, .85f));
            return true;
        }

        /// <summary>Queues Combo reactions until the defense note has finished dealing damage.</summary>
        public void RecordComboJudgement(RhythmJudgementResult result, PatternRunMode mode, int before, int after,
            bool brokeCombo, bool breakPrevented)
        {
            var outcome = new ComboJudgementOutcome
            {
                Result = result, Mode = mode, ComboBefore = before, ComboAfter = after,
                BrokeCombo = brokeCombo, BreakPrevented = breakPrevented
            };
            if (mode == PatternRunMode.EnemyDefense) pendingCombo[result.NoteId ?? string.Empty] = outcome;
            else DispatchComboJudgement(outcome);
        }

        private void DispatchComboJudgement(ComboJudgementOutcome outcome)
        {
            if (outcome == null || CombatOver) return;
            foreach (PassiveHook hook in hooks) ForEachEffect(hook, effect => effect.OnComboJudgement(hook, outcome));
            if (Modifiers != null)
                foreach (IComboJudgementModifier modifier in Modifiers.OfType<IComboJudgementModifier>().ToList())
                    modifier.OnComboJudgement(this, outcome);
            RaiseChanged();
        }

        internal void FinishComboJudgement(string noteId)
        {
            string key = noteId ?? string.Empty;
            if (!pendingCombo.TryGetValue(key, out ComboJudgementOutcome outcome)) return;
            pendingCombo.Remove(key);
            DispatchComboJudgement(outcome);
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
            if (quote.Effects.Any(e => e is CatalyzeEffect))
            {
                foreach (string markId in new[] { ElementalMarks.Burn, ElementalMarks.Soaked, ElementalMarks.Static, ElementalMarks.Cracked })
                {
                    StatusInstance mark = Marks.Get(markId);
                    if (mark == null) continue;
                    result.Add(new CastChoice
                    {
                        MarkId = markId,
                        Label = ElementalMarks.DisplayName(markId),
                        Description = $"Keep this mark for one more turn ({mark.TurnsRemaining} remaining) and empower the next reaction."
                    });
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
                float scale = breakwater ? Rules.Synergy.breakwaterDamagePerShield : Rules.Synergy.pressureCastDamagePerShield;
                int perfectDamage = Mathf.RoundToInt(amount * scale);
                result.Add(new CastChoice
                {
                    Label = breakwater ? "Break" : "Pressure Cast",
                    Description = $"Spend {amount} shield for up to {perfectDamage} extra {BuildTagUtility.ElementName(quote.Element)} damage before bonuses; {shield - amount} shield remains.",
                    SpendShield = true
                });
            }
            return result;
        }

        private void CommitChoice(CastSnapshot cast, CastChoice choice)
        {
            cast.Choice = choice ?? new CastChoice();
            cast.SmallShieldBeforeCast = Modifiers?.Find<ShieldBuff>(ShieldBuff.Key)?.Capacity ?? 0;
            // Bedrock keeps the pre-spend value, while its passive may still use a larger shield generated by this cast.
            cast.ShieldForBedrock = cast.SmallShieldBeforeCast;
            if (cast.Choice.SpendShield && cast.HasRole(AbilityRole.Damage))
            {
                bool strong = cast.Quote.Effects.Any(e => e is BreakwaterEffect);
                if (strong || HasPassive<PressureCastPassive>())
                {
                    int amount = Mathf.Min(strong ? 30 : 20, Mathf.CeilToInt(cast.SmallShieldBeforeCast * (strong ? 1f / 3f : .2f)));
                    cast.ShieldSpent = Damage.SpendShield(amount, cast.CastId);
                    float scale = strong ? Rules.Synergy.breakwaterDamagePerShield : Rules.Synergy.pressureCastDamagePerShield;
                    if (cast.ShieldSpent > 0) cast.Quote.Effects.Add(new ShieldSpendDamageEffect(scale));
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

        public bool CatalyzeMark(string markId, int turns, float reactionBonus, string label)
        {
            StatusInstance mark = Marks.Get(markId);
            if (mark == null) return false;
            mark.Extend(turns);
            pendingReactionBonus = Mathf.Max(pendingReactionBonus, Mathf.Max(0f, reactionBonus));
            pendingReactionBonusLabel = label;
            Modifiers?.NotifyChanged();
            return true;
        }

        internal void ApplyPendingReactionBonus(ReactionContext context)
        {
            if (context == null || pendingReactionBonus <= 0f) return;
            context.Multiplier *= 1f + pendingReactionBonus;
            Record(new CombatEvent { Kind = CombatEventKind.PassiveTriggered, SourceId = pendingReactionBonusLabel,
                RootCauseId = context.RootId, Note = $"Next reaction +{Mathf.RoundToInt(pendingReactionBonus * 100f)}%" });
            pendingReactionBonus = 0f;
            pendingReactionBonusLabel = null;
        }

        public int ReduceLongestCooldown(string label)
        {
            int reduced = AbilitySlots?.ReduceLongestCooldown(1) ?? 0;
            if (reduced > 0) Toast((label ?? "Long Measure").ToUpperInvariant() + " -1 COOLDOWN", new Color(.75f, .85f, 1f));
            return reduced;
        }
    }
}
