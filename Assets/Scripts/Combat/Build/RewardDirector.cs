using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>What claiming a reward option would do, shown before the player picks it.</summary>
    public sealed class RewardPreview
    {
        public RewardOptionData Option;
        public string Title;
        public string Kind;
        public string Summary;
        public readonly List<string> Details = new();
        /// <summary>New ability with every slot full: the player picks a slot to replace (or keeps it in reserve).</summary>
        public bool NeedsReplacement;
        /// <summary>Slots that may be replaced (the last affordable basic action cannot be).</summary>
        public readonly List<int> ReplaceableSlots = new();
        /// <summary>Per slot: consequences of replacing it (upgrades stay with the old instance, passives that go inactive).</summary>
        public readonly Dictionary<int, List<string>> ReplacementConsequences = new();
    }

    public enum ClaimStatus
    {
        Claimed,
        AlreadyClaimed,
        UnknownOption,
        Invalid
    }

    /// <summary>
    /// Builds eligible reward offers from the current build (new abilities, dedicated upgrades, passives), saves them
    /// with the build under a source key, and claims them exactly once. Reopening, retrying the encounter or reloading
    /// returns the saved offer; there are no free rerolls.
    /// </summary>
    public static class RewardDirector
    {
        private sealed class Candidate
        {
            public RewardOptionData Option;
            public float Weight;
        }

        /// <summary>Returns the saved offer for this source, or generates and saves a new one.</summary>
        public static RewardOfferData GetOrCreateOffer(RunBuildState build, string sourceKey, BuildContentRegistry registry, int optionCount = 0)
        {
            if (build == null || registry == null || string.IsNullOrEmpty(sourceKey)) return null;
            RewardOfferData existing = build.FindOffer(sourceKey);
            if (existing != null) return existing;

            int count = optionCount > 0 ? optionCount : BuildBalanceRules.Load().OptionsPerOffer;
            var random = new System.Random(build.Seed ^ StableHash(sourceKey));
            List<Candidate> abilities = AbilityCandidates(build, registry);
            List<Candidate> upgrades = UpgradeCandidates(build, registry);
            List<Candidate> passives = PassiveCandidates(build, registry, fallbacks: false);

            // A mix: one of each available kind first (reinforce + alternatives), then the best of what is left.
            var chosen = new List<RewardOptionData>();
            foreach (List<Candidate> pool in new[] { passives, upgrades, abilities }.OrderBy(_ => random.Next()))
            {
                if (chosen.Count >= count) break;
                Candidate pick = PickWeighted(pool, random);
                if (pick != null) chosen.Add(pick.Option);
            }
            List<Candidate> rest = abilities.Concat(upgrades).Concat(passives).ToList();
            while (chosen.Count < count)
            {
                Candidate pick = PickWeighted(rest, random);
                if (pick == null) break;
                chosen.Add(pick.Option);
            }
            // Too few eligible options: broadly useful fallbacks instead of unusable rewards.
            if (chosen.Count < count)
            {
                List<Candidate> fallbacks = PassiveCandidates(build, registry, fallbacks: true)
                    .Where(c => chosen.All(o => o.contentId != c.Option.contentId)).ToList();
                while (chosen.Count < count)
                {
                    Candidate pick = PickWeighted(fallbacks, random);
                    if (pick == null) break;
                    chosen.Add(pick.Option);
                }
            }

            // Growth: one extra card on every offer (max HP / max MP), next to the usual options.
            ProgressionRules progression = BuildBalanceRules.Load().Progression;
            if (progression.growthCardEveryOffer)
            {
                var growth = new List<Candidate>
                {
                    new() { Option = new RewardOptionData { kind = RewardKind.Growth, contentId = GrowthRewards.Health }, Weight = progression.healthCardWeight },
                    new() { Option = new RewardOptionData { kind = RewardKind.Growth, contentId = GrowthRewards.Mana }, Weight = progression.manaCardWeight },
                    new() { Option = new RewardOptionData { kind = RewardKind.Growth, contentId = GrowthRewards.Balanced }, Weight = progression.balancedCardWeight }
                };
                growth.RemoveAll(candidate => candidate.Weight <= 0f);
                Candidate pick = PickWeighted(growth, random);
                if (pick != null) chosen.Add(pick.Option);
            }

            var offer = new RewardOfferData { offerId = build.NewOfferId(), sourceKey = sourceKey };
            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i].optionId = offer.offerId + "-o" + (i + 1);
                offer.options.Add(chosen[i]);
            }
            build.AddOffer(offer);
            return offer;
        }

        public static RewardPreview Preview(RunBuildState build, RewardOptionData option, BuildContentRegistry registry)
        {
            var preview = new RewardPreview { Option = option };
            if (build == null || option == null || registry == null) return preview;
            switch (option.kind)
            {
                case RewardKind.NewAbility:
                {
                    AbilityDefinition ability = registry.Ability(option.contentId);
                    preview.Kind = "New ability";
                    preview.Title = ability != null ? ability.DisplayName : option.contentId;
                    if (ability == null) break;
                    AbilityQuote quote = AbilityResolver.Resolve(ability, null, build);
                    preview.Summary = DescribeAbility(quote);
                    preview.Details.AddRange(quote.Effects.Select(effect => "• " + effect.Describe(ability)));
                    preview.Details.AddRange(quote.Changes.Select(change => "• " + change));
                    preview.NeedsReplacement = !build.HasFreeSlot;
                    if (!preview.NeedsReplacement)
                    {
                        preview.Details.Add("Goes into an empty slot.");
                        break;
                    }
                    for (int slot = 0; slot < RunBuildState.SlotCount; slot++)
                    {
                        List<string> consequences = ReplacementConsequences(build, slot, ability, out bool allowed);
                        preview.ReplacementConsequences[slot] = consequences;
                        if (allowed) preview.ReplaceableSlots.Add(slot);
                    }
                    break;
                }
                case RewardKind.AbilityUpgrade:
                {
                    AbilityUpgradeDefinition upgrade = registry.Upgrade(option.contentId);
                    AbilityInstance target = build.FindInstance(option.targetInstanceId);
                    preview.Kind = "Ability upgrade";
                    preview.Title = (upgrade != null ? upgrade.DisplayName : option.contentId) + (target != null ? " → " + target : string.Empty);
                    if (upgrade == null || target == null) break;
                    AbilityQuote before = build.Quote(target);
                    preview.Summary = upgrade.Summary();
                    preview.Details.Add($"Before: {DescribeAbility(before)}");
                    preview.Details.Add($"After: {PreviewUpgradedQuote(build, target, upgrade)}");
                    preview.Details.Add("Stays with this ability if it moves to another slot.");
                    break;
                }
                case RewardKind.Passive:
                {
                    PassiveDefinition passive = registry.Passive(option.contentId);
                    preview.Kind = "Passive";
                    preview.Title = passive != null ? passive.DisplayName : option.contentId;
                    if (passive == null) break;
                    PassiveInstance owned = build.FindPassive(passive.Id);
                    int level = owned != null ? owned.Level + 1 : 1;
                    preview.Summary = passive.DescribeLevel(level);
                    if (owned != null) preview.Details.Add($"Level {owned.Level} → {level} (max {passive.MaxLevel})");
                    if (!string.IsNullOrEmpty(passive.Description)) preview.Details.Add(passive.Description);
                    List<AbilityQuote> equipped = build.EquippedQuotes();
                    List<string> affected = equipped.Where(q => passive.Requirement.IsEmpty || passive.Requirement.SatisfiedBy(q))
                        .Select(q => q.Definition.DisplayName).ToList();
                    preview.Details.Add(affected.Count > 0 ? "Affects: " + string.Join(", ", affected) : "Affects: the whole build");
                    AbilityQuote[] cheaper = equipped.Select(q => (before: q, after: PreviewPassiveQuote(build, q, passive)))
                        .Where(pair => pair.after.ManaCost != pair.before.ManaCost).Select(pair => pair.after).ToArray();
                    foreach (AbilityQuote quote in cheaper) preview.Details.Add($"{quote.Definition.DisplayName}: now {quote.CostText}");
                    break;
                }
                case RewardKind.Growth:
                {
                    ProgressionRules rules = BuildBalanceRules.Load().Progression;
                    GrowthRewards.Amounts(option.contentId, rules, out int health, out int mana);
                    preview.Kind = "Growth";
                    preview.Title = GrowthRewards.Title(option.contentId);
                    preview.Summary = GrowthRewards.Summary(option.contentId, rules);
                    if (health > 0) preview.Details.Add($"Max HP bonus: +{build.BonusMaxHealth} -> +{build.BonusMaxHealth + health} (before passives)");
                    if (mana > 0) preview.Details.Add($"Max MP bonus: +{build.BonusMaxMana} -> +{build.BonusMaxMana + mana}");
                    preview.Details.Add("Permanent for this run. Applies from the next battle.");
                    break;
                }
            }
            return preview;
        }

        /// <summary>
        /// Claims one option of an offer exactly once. <paramref name="replaceSlot"/>: for a new ability with full slots,
        /// the slot to replace (-1 = keep the new ability in reserve).
        /// </summary>
        public static ClaimStatus Claim(RunBuildState build, RewardOfferData offer, string optionId, BuildContentRegistry registry,
            int replaceSlot = -1)
        {
            if (build == null || offer == null || registry == null) return ClaimStatus.Invalid;
            if (offer.claimed || build.IsClaimed(offer.offerId)) return ClaimStatus.AlreadyClaimed;
            RewardOptionData option = offer.options.FirstOrDefault(o => o.optionId == optionId);
            if (option == null) return ClaimStatus.UnknownOption;

            switch (option.kind)
            {
                case RewardKind.NewAbility:
                {
                    AbilityDefinition ability = registry.Ability(option.contentId);
                    if (ability == null) return ClaimStatus.Invalid;
                    if (build.HasFreeSlot) build.AddAbility(ability);
                    else if (replaceSlot >= 0)
                    {
                        ReplacementConsequences(build, replaceSlot, ability, out bool allowed);
                        if (!allowed) return ClaimStatus.Invalid;
                        // The replaced instance keeps its dedicated upgrades in reserve; nothing transfers implicitly.
                        build.AddAbility(ability, replaceSlot);
                    }
                    else build.AddAbility(ability, -2);
                    break;
                }
                case RewardKind.AbilityUpgrade:
                {
                    AbilityUpgradeDefinition upgrade = registry.Upgrade(option.contentId);
                    AbilityInstance target = build.FindInstance(option.targetInstanceId);
                    if (!build.AddUpgrade(target, upgrade)) return ClaimStatus.Invalid;
                    break;
                }
                case RewardKind.Passive:
                {
                    if (build.AddPassive(registry.Passive(option.contentId)) == null) return ClaimStatus.Invalid;
                    break;
                }
                case RewardKind.Growth:
                {
                    GrowthRewards.Amounts(option.contentId, BuildBalanceRules.Load().Progression, out int health, out int mana);
                    if (health <= 0 && mana <= 0) return ClaimStatus.Invalid;
                    build.AddGrowth(health, mana);
                    break;
                }
            }
            build.TryClaim(offer.offerId);
            offer.claimed = true;
            offer.claimedOptionId = optionId;
            build.MarkChanged();
            return ClaimStatus.Claimed;
        }

        // ---------- Eligibility ----------

        private static List<Candidate> AbilityCandidates(RunBuildState build, BuildContentRegistry registry) =>
            registry.Abilities.Where(ability => ability != null && ability.RhythmPattern != null && !build.OwnsAbility(ability.Id))
                .Select(ability => new Candidate
                {
                    Option = new RewardOptionData { kind = RewardKind.NewAbility, contentId = ability.Id },
                    Weight = 1f
                }).ToList();

        private static List<Candidate> UpgradeCandidates(RunBuildState build, BuildContentRegistry registry)
        {
            var list = new List<Candidate>();
            foreach (AbilityInstance instance in build.Equipped)
            foreach (AbilityUpgradeDefinition upgrade in registry.Upgrades)
            {
                if (upgrade == null || !upgrade.IsCompatible(instance) || instance.HasUpgrade(upgrade)) continue;
                list.Add(new Candidate
                {
                    Option = new RewardOptionData { kind = RewardKind.AbilityUpgrade, contentId = upgrade.Id, targetInstanceId = instance.InstanceId },
                    // Upgrades that name the ability directly reinforce the current direction.
                    Weight = upgrade.AbilityIds.Count > 0 ? 2f : 1f
                });
            }
            return list;
        }

        private static List<Candidate> PassiveCandidates(RunBuildState build, BuildContentRegistry registry, bool fallbacks)
        {
            List<AbilityQuote> equipped = build.EquippedQuotes();
            var list = new List<Candidate>();
            foreach (PassiveDefinition passive in registry.Passives)
            {
                if (passive == null || passive.IsFallback != fallbacks || !build.CanAddPassive(passive)) continue;
                // A status / melee / spell upgrade needs a usable source of it in the build.
                if (!passive.Requirement.SatisfiedBy(equipped)) continue;
                if (!passive.Effects.Any(e => e != null && e.IsActive(equipped))) continue;
                PassiveInstance owned = build.FindPassive(passive.Id);
                if (owned != null && passive.Effects.All(e => e is ManaCostPassive)
                    && !CostLevelBenefits(build, passive, owned.Level)) continue;
                float weight = 1f;
                if (!string.IsNullOrEmpty(build.Playstyle) && passive.StyleTags.Contains(build.Playstyle)) weight *= 2f;
                if (build.FindPassive(passive.Id) != null) weight *= 1.5f; // levelling what you have reinforces
                list.Add(new Candidate { Option = new RewardOptionData { kind = RewardKind.Passive, contentId = passive.Id }, Weight = weight });
            }
            return list;
        }

        private static Candidate PickWeighted(List<Candidate> pool, System.Random random)
        {
            if (pool == null || pool.Count == 0) return null;
            float total = pool.Sum(c => c.Weight);
            double roll = random.NextDouble() * total;
            Candidate pick = pool[pool.Count - 1];
            foreach (Candidate candidate in pool)
            {
                roll -= candidate.Weight;
                if (roll <= 0d)
                {
                    pick = candidate;
                    break;
                }
            }
            pool.Remove(pick);
            return pick;
        }

        private static bool CostLevelBenefits(RunBuildState build, PassiveDefinition passive, int level)
        {
            foreach (AbilityInstance instance in build.Equipped)
            {
                AbilityQuote before = AbilityResolver.Resolve(instance.Definition, instance, build);
                AbilityQuote after = AbilityResolver.Resolve(instance.Definition, instance, null);
                foreach (PassiveInstance owned in build.Passives)
                foreach (PassiveEffect effect in owned.Definition.Effects)
                    effect?.ModifyQuote(after, owned.Definition.Id == passive.Id ? level + 1 : owned.Level, owned.Definition.DisplayName);
                if (CombatBuildRuntime.Active?.Build == build) CombatBuildRuntime.Active.AdjustQuote(after);
                AbilityResolver.ClampCost(after);
                if (after.ManaCost < before.ManaCost) return true;
            }
            return false;
        }

        // ---------- Previews ----------

        private static List<string> ReplacementConsequences(RunBuildState build, int slot, AbilityDefinition incoming, out bool allowed)
        {
            var lines = new List<string>();
            AbilityInstance current = build.GetSlot(slot);
            allowed = true;
            if (current == null)
            {
                lines.Add("Empty slot.");
                return lines;
            }
            lines.Add($"{current} goes to reserve" + (current.Upgrades.Count > 0
                ? $" and keeps its upgrades ({string.Join(", ", current.Upgrades.Select(u => u.DisplayName))})." : "."));

            // Simulate the loadout after the swap for the free-action rule and passive activity.
            List<AbilityQuote> after = Enumerable.Range(0, RunBuildState.SlotCount)
                .Select(i => i == slot ? AbilityResolver.Resolve(incoming, null, build) : build.GetSlot(i) != null ? build.Quote(build.GetSlot(i)) : null)
                .Where(q => q?.Definition != null).ToList();
            if (after.All(q => q.ManaCost > 0))
            {
                allowed = false;
                lines.Add("Blocked: this would remove your last affordable (0 MP) action.");
            }
            List<AbilityQuote> before = build.EquippedQuotes();
            foreach (PassiveInstance passive in build.Passives)
            {
                bool wasActive = RunBuildState.IsPassiveActive(passive, before);
                bool isActive = RunBuildState.IsPassiveActive(passive, after);
                if (wasActive && !isActive) lines.Add($"{passive.Definition.DisplayName} becomes inactive (kept, no compatible ability).");
                if (!wasActive && isActive) lines.Add($"{passive.Definition.DisplayName} becomes active.");
            }
            return lines;
        }

        private static string PreviewUpgradedQuote(RunBuildState build, AbilityInstance target, AbilityUpgradeDefinition upgrade)
        {
            var copy = new AbilityInstance(target.InstanceId, target.Definition);
            foreach (AbilityUpgradeDefinition existing in target.Upgrades) copy.AddUpgrade(existing);
            copy.AddUpgrade(upgrade);
            return DescribeAbility(AbilityResolver.Resolve(copy.Definition, copy, build));
        }

        /// <summary>The ability's quote as if this passive were acquired (new passives only; level-ups keep the current quote).</summary>
        private static AbilityQuote PreviewPassiveQuote(RunBuildState build, AbilityQuote quote, PassiveDefinition passive)
        {
            AbilityQuote result = AbilityResolver.Resolve(quote.Definition, quote.Instance, build);
            if (build.FindPassive(passive.Id) != null) return result;
            foreach (PassiveEffect effect in passive.Effects) effect?.ModifyQuote(result, 1, passive.DisplayName);
            AbilityResolver.ClampCost(result);
            return result;
        }

        public static string DescribeAbility(AbilityQuote quote)
        {
            if (quote?.Definition == null) return "-";
            string power = Mathf.Approximately(quote.PowerScale, 1f)
                ? quote.BasePower.ToString() : $"{Mathf.RoundToInt(quote.BasePower * quote.PowerScale)} ({quote.BasePower} base)";
            return $"{quote.Definition.DisplayName}: power {power}, {quote.CostText}, cooldown {quote.Cooldown}, " +
                   $"{quote.Roles.ToString().Replace(", ", "/")} · {quote.Delivery.ToString().Replace(", ", "/")}" +
                   (quote.Element != ElementType.None ? " · " + quote.Element : string.Empty);
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text) hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
