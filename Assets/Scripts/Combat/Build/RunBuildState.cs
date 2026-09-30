using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>A run-owned ability: stable instance id + definition + its dedicated upgrades (which follow it between slots).</summary>
    public sealed class AbilityInstance
    {
        private readonly List<AbilityUpgradeDefinition> upgrades = new();

        public AbilityInstance(string instanceId, AbilityDefinition definition)
        {
            InstanceId = instanceId;
            Definition = definition;
        }

        public string InstanceId { get; }
        public AbilityDefinition Definition { get; }
        public IReadOnlyList<AbilityUpgradeDefinition> Upgrades => upgrades;

        public bool HasUpgrade(AbilityUpgradeDefinition upgrade) => upgrade != null && upgrades.Any(u => u == upgrade || u.Id == upgrade.Id);
        internal void AddUpgrade(AbilityUpgradeDefinition upgrade) => upgrades.Add(upgrade);
        public override string ToString() => Definition != null ? Definition.DisplayName : InstanceId;
    }

    public sealed class PassiveInstance
    {
        public PassiveInstance(string instanceId, PassiveDefinition definition, int level)
        {
            InstanceId = instanceId;
            Definition = definition;
            Level = Mathf.Clamp(level, 1, definition != null ? definition.MaxLevel : 1);
        }

        public string InstanceId { get; }
        public PassiveDefinition Definition { get; }
        public int Level { get; internal set; }
        public bool IsMaxLevel => Definition == null || Level >= Definition.MaxLevel;
    }

    public enum RewardKind
    {
        NewAbility,
        AbilityUpgrade,
        Passive,
        /// <summary>Permanent run growth: max health and / or max mana (the extra card on every victory).</summary>
        Growth
    }

    [Serializable]
    public sealed class RewardOptionData
    {
        public string optionId;
        public RewardKind kind;
        public string contentId;
        /// <summary>AbilityUpgrade: the ability instance it attaches to.</summary>
        public string targetInstanceId;
    }

    /// <summary>A generated reward offer. Saved with the build so reopening / retrying / reloading shows the same options.</summary>
    [Serializable]
    public sealed class RewardOfferData
    {
        public string offerId;
        public string sourceKey;
        public List<RewardOptionData> options = new();
        public bool claimed;
        public string claimedOptionId;
    }

    /// <summary>
    /// The run's build: ability instances (4 slots + reserve), dedicated upgrades, the passive collection, reward offers
    /// and claim ids. Lives outside encounters; encounters rebuild their runtime from it at start.
    /// </summary>
    public sealed class RunBuildState
    {
        public const int SlotCount = 4;

        private readonly List<AbilityInstance> abilities = new();
        private readonly AbilityInstance[] slots = new AbilityInstance[SlotCount];
        private readonly List<PassiveInstance> passives = new();
        private readonly List<RewardOfferData> offers = new();
        private readonly HashSet<string> claimIds = new();
        private int nextId = 1;

        public string PresetId = string.Empty;
        public string DisplayName = "Custom build";
        public string Playstyle = string.Empty;
        public int Seed = 12345;

        /// <summary>Max health added by growth rewards (before percentage passives).</summary>
        public int BonusMaxHealth { get; private set; }
        /// <summary>Max mana added by growth rewards.</summary>
        public int BonusMaxMana { get; private set; }
        /// <summary>Victories this run: how deep the run is (enemy scaling, depth-gated enemy attacks).</summary>
        public int Depth { get; private set; }

        /// <summary>Adds permanent max health / max mana for the rest of the run.</summary>
        public void AddGrowth(int maxHealth, int maxMana)
        {
            if (maxHealth == 0 && maxMana == 0) return;
            BonusMaxHealth = Mathf.Max(0, BonusMaxHealth + maxHealth);
            BonusMaxMana = Mathf.Max(0, BonusMaxMana + maxMana);
            MarkChanged();
        }

        /// <summary>One more encounter won: the run gets deeper.</summary>
        public void RecordVictory()
        {
            Depth++;
            MarkChanged();
        }

        /// <summary>Dev tool: set the run depth directly (F11 panel).</summary>
        public void SetDepth(int depth)
        {
            Depth = Mathf.Max(0, depth);
            MarkChanged();
        }

        public IReadOnlyList<AbilityInstance> Abilities => abilities;
        public IReadOnlyList<PassiveInstance> Passives => passives;
        public IReadOnlyList<RewardOfferData> Offers => offers;
        public IReadOnlyCollection<string> ClaimIds => claimIds;
        /// <summary>Bumped on every change; runtimes compare it to know when to refresh.</summary>
        public int Version { get; private set; }
        public event Action Changed;

        public AbilityInstance GetSlot(int slotIndex) => slotIndex >= 0 && slotIndex < SlotCount ? slots[slotIndex] : null;
        public int SlotOf(AbilityInstance instance) => Array.IndexOf(slots, instance);
        public IEnumerable<AbilityInstance> Equipped => slots.Where(slot => slot != null);
        public IEnumerable<AbilityInstance> Reserve => abilities.Where(ability => SlotOf(ability) < 0);
        public bool HasFreeSlot => slots.Any(slot => slot == null);
        public AbilityInstance FindInstance(string instanceId) => abilities.FirstOrDefault(a => a.InstanceId == instanceId);
        public bool OwnsAbility(string abilityId) => abilities.Any(a => a.Definition != null && a.Definition.Id == abilityId);
        public PassiveInstance FindPassive(string passiveId) => passives.FirstOrDefault(p => p.Definition != null && p.Definition.Id == passiveId);

        public void MarkChanged()
        {
            Version++;
            Changed?.Invoke();
        }

        // ---------- Abilities ----------

        /// <summary>Adds an ability instance. slotIndex -1 = first free slot (or reserve when full); -2 = reserve.</summary>
        public AbilityInstance AddAbility(AbilityDefinition definition, int slotIndex = -1)
        {
            if (definition == null) return null;
            var instance = new AbilityInstance("ab-" + nextId++, definition);
            abilities.Add(instance);
            if (slotIndex == -1) slotIndex = Array.FindIndex(slots, slot => slot == null);
            if (slotIndex >= 0 && slotIndex < SlotCount) slots[slotIndex] = instance;
            MarkChanged();
            return instance;
        }

        /// <summary>Moves an owned instance into a slot (the previous occupant goes to reserve). Upgrades move with it.</summary>
        public bool Equip(AbilityInstance instance, int slotIndex)
        {
            if (instance == null || !abilities.Contains(instance) || slotIndex < 0 || slotIndex >= SlotCount) return false;
            int current = SlotOf(instance);
            if (current == slotIndex) return true;
            AbilityInstance occupant = slots[slotIndex];
            if (current >= 0) slots[current] = occupant; // swap when moving between slots
            slots[slotIndex] = instance;
            MarkChanged();
            return true;
        }

        public bool Unequip(int slotIndex)
        {
            if (GetSlot(slotIndex) == null) return false;
            slots[slotIndex] = null;
            MarkChanged();
            return true;
        }

        public bool AddUpgrade(AbilityInstance instance, AbilityUpgradeDefinition upgrade)
        {
            if (instance == null || upgrade == null || !upgrade.IsCompatible(instance) || instance.HasUpgrade(upgrade)) return false;
            instance.AddUpgrade(upgrade);
            MarkChanged();
            return true;
        }

        // ---------- Passives ----------

        public bool IsExcluded(PassiveDefinition definition) =>
            definition != null && !string.IsNullOrEmpty(definition.ExclusivityGroup)
            && passives.Any(p => p.Definition != null && p.Definition != definition && p.Definition.Id != definition.Id
                                 && p.Definition.ExclusivityGroup == definition.ExclusivityGroup);

        public bool CanAddPassive(PassiveDefinition definition)
        {
            if (definition == null || IsExcluded(definition)) return false;
            PassiveInstance owned = FindPassive(definition.Id);
            return owned == null || !owned.IsMaxLevel;
        }

        /// <summary>Adds a passive, or levels it up when already owned (duplicates level under the definition's max).</summary>
        public PassiveInstance AddPassive(PassiveDefinition definition, int level = 1)
        {
            if (!CanAddPassive(definition)) return null;
            PassiveInstance owned = FindPassive(definition.Id);
            if (owned != null) owned.Level = Mathf.Min(definition.MaxLevel, owned.Level + Mathf.Max(1, level));
            else passives.Add(owned = new PassiveInstance("ps-" + nextId++, definition, level));
            MarkChanged();
            return owned;
        }

        // ---------- Views ----------

        public AbilityQuote Quote(AbilityInstance instance) => AbilityResolver.Resolve(instance?.Definition, instance, this);

        public List<AbilityQuote> EquippedQuotes() => Equipped.Select(Quote).ToList();

        /// <summary>Owned but doing nothing with the current loadout (shown as inactive, never removed).</summary>
        public bool IsPassiveActive(PassiveInstance passive) => IsPassiveActive(passive, EquippedQuotes());

        public static bool IsPassiveActive(PassiveInstance passive, IReadOnlyList<AbilityQuote> equipped)
        {
            if (passive?.Definition == null) return false;
            if (!passive.Definition.Requirement.SatisfiedBy(equipped)) return false;
            return passive.Definition.Effects.Where(effect => effect != null).All(effect => effect.IsActive(equipped));
        }

        /// <summary>At least one equipped ability costs no mana (the affordable basic action rule).</summary>
        public bool HasAffordableBasicAction() => EquippedQuotes().Any(quote => quote.ManaCost == 0);

        /// <summary>
        /// Why this loadout can't be taken into battle, or null when it can: at least one ability and one 0 MP action
        /// must stay equipped. The loadout panel refuses (and undoes) changes that break this.
        /// </summary>
        public string LoadoutProblem()
        {
            if (!Equipped.Any()) return "Keep at least one ability equipped.";
            if (!HasAffordableBasicAction()) return "Keep a 0 MP ability equipped: it is your basic action.";
            return null;
        }

        // ---------- Offers ----------

        public RewardOfferData FindOffer(string sourceKey) => offers.FirstOrDefault(offer => offer.sourceKey == sourceKey);
        internal void AddOffer(RewardOfferData offer) { offers.Add(offer); MarkChanged(); }
        internal bool TryClaim(string claimId) => claimIds.Add(claimId);
        public bool IsClaimed(string claimId) => claimIds.Contains(claimId);

        public string NewOfferId() => "offer-" + nextId++;

        // ---------- Save / load ----------

        [Serializable]
        private sealed class SaveData
        {
            public string presetId;
            public string displayName;
            public string playstyle;
            public int seed;
            public int nextId;
            public List<AbilitySave> abilities = new();
            public List<string> slots = new();
            public List<PassiveSave> passives = new();
            public List<RewardOfferData> offers = new();
            public List<string> claims = new();
            public int bonusMaxHealth;
            public int bonusMaxMana;
            public int depth;
        }

        [Serializable]
        private sealed class AbilitySave
        {
            public string instanceId;
            public string abilityId;
            public List<string> upgrades = new();
        }

        [Serializable]
        private sealed class PassiveSave
        {
            public string instanceId;
            public string passiveId;
            public int level;
        }

        public string ToJson()
        {
            var data = new SaveData
            {
                presetId = PresetId, displayName = DisplayName, playstyle = Playstyle, seed = Seed, nextId = nextId,
                // ReferenceEquals, not Unity's ==: definitions made in code are destroyed when play mode ends, but their
                // ids are still readable, which is what lets RunBuild rebuild the build for the next play session.
                abilities = abilities.Select(a => new AbilitySave
                {
                    instanceId = a.InstanceId, abilityId = !ReferenceEquals(a.Definition, null) ? a.Definition.Id : string.Empty,
                    upgrades = a.Upgrades.Where(u => !ReferenceEquals(u, null)).Select(u => u.Id).ToList()
                }).ToList(),
                slots = slots.Select(s => s != null ? s.InstanceId : string.Empty).ToList(),
                passives = passives.Select(p => new PassiveSave
                {
                    instanceId = p.InstanceId, passiveId = !ReferenceEquals(p.Definition, null) ? p.Definition.Id : string.Empty, level = p.Level
                }).ToList(),
                offers = offers.ToList(),
                claims = claimIds.ToList(),
                bonusMaxHealth = BonusMaxHealth,
                bonusMaxMana = BonusMaxMana,
                depth = Depth
            };
            return JsonUtility.ToJson(data);
        }

        /// <summary>Rebuilds a build from saved ids. Unknown ids are skipped with a warning.</summary>
        public static RunBuildState FromJson(string json, BuildContentRegistry registry)
        {
            if (string.IsNullOrEmpty(json) || registry == null) return null;
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null) return null;
            var state = new RunBuildState
            {
                PresetId = data.presetId ?? string.Empty,
                DisplayName = data.displayName ?? "Custom build",
                Playstyle = data.playstyle ?? string.Empty,
                Seed = data.seed,
                nextId = Mathf.Max(1, data.nextId),
                BonusMaxHealth = Mathf.Max(0, data.bonusMaxHealth),
                BonusMaxMana = Mathf.Max(0, data.bonusMaxMana),
                Depth = Mathf.Max(0, data.depth)
            };
            foreach (AbilitySave saved in data.abilities)
            {
                AbilityDefinition definition = registry.Ability(saved.abilityId);
                if (definition == null) { Debug.LogWarning("[Build] Unknown ability id in save: " + saved.abilityId); continue; }
                var instance = new AbilityInstance(saved.instanceId, definition);
                foreach (string upgradeId in saved.upgrades)
                {
                    AbilityUpgradeDefinition upgrade = registry.Upgrade(upgradeId);
                    if (upgrade != null) instance.AddUpgrade(upgrade);
                }
                state.abilities.Add(instance);
            }
            for (int i = 0; i < SlotCount && i < data.slots.Count; i++)
                state.slots[i] = state.FindInstance(data.slots[i]);
            foreach (PassiveSave saved in data.passives)
            {
                PassiveDefinition definition = registry.Passive(saved.passiveId);
                if (definition == null) { Debug.LogWarning("[Build] Unknown passive id in save: " + saved.passiveId); continue; }
                state.passives.Add(new PassiveInstance(saved.instanceId, definition, saved.level));
            }
            state.offers.AddRange(data.offers ?? new List<RewardOfferData>());
            foreach (string claim in data.claims ?? new List<string>()) state.claimIds.Add(claim);
            return state;
        }
    }

    /// <summary>The build of the current run. Null = legacy behaviour (the scene / DefaultLoadout abilities, no passives).</summary>
    public static class RunBuild
    {
        private static RunBuildState current;
        private static string pendingRestore;

        /// <summary>
        /// Play mode start. With domain reload off (Enter Play Mode Options) static state survives between play
        /// sessions, but every ScriptableObject created in code during the last session was destroyed when it ended,
        /// so cached samples, the registry and the build would all point at dead objects (the "empty build"). Drop the
        /// caches and rebuild the build from its ids on first use.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            SampleBuildLibrary.ClearCache();
            BuildContentRegistry.Reset();
            pendingRestore = current != null ? current.ToJson() : null;
            current = null;
        }

        public static RunBuildState Current
        {
            get
            {
                if (pendingRestore != null)
                {
                    string json = pendingRestore;
                    pendingRestore = null;
                    current = RunBuildState.FromJson(json, BuildContentRegistry.Instance);
                }
                return current;
            }
            set
            {
                pendingRestore = null;
                if (current == value) return;
                current = value;
                CurrentChanged?.Invoke(current);
            }
        }

        public static event Action<RunBuildState> CurrentChanged;

        public const string SaveKey = "RythmRPG.RunBuild";

        public static void Save()
        {
            RunBuildState build = Current;
            if (build == null) PlayerPrefs.DeleteKey(SaveKey);
            else PlayerPrefs.SetString(SaveKey, build.ToJson());
            PlayerPrefs.Save();
        }

        public static bool Load()
        {
            if (!PlayerPrefs.HasKey(SaveKey)) return false;
            RunBuildState loaded = RunBuildState.FromJson(PlayerPrefs.GetString(SaveKey), BuildContentRegistry.Instance);
            if (loaded == null) return false;
            Current = loaded;
            return true;
        }
    }
}
