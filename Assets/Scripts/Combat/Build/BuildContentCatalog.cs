using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>A list of build content (abilities, upgrades, passives, presets) that can appear in a run.</summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Build Content Catalog", fileName = "BuildContentCatalog")]
    public sealed class BuildContentCatalog : ScriptableObject
    {
        public const string ResourceFolder = "Combat/Build";

        [SerializeField] private List<AbilityDefinition> abilities = new();
        [SerializeField] private List<AbilityUpgradeDefinition> upgrades = new();
        [SerializeField] private List<PassiveDefinition> passives = new();
        [SerializeField] private List<BuildPreset> presets = new();

        public IReadOnlyList<AbilityDefinition> Abilities => abilities;
        public IReadOnlyList<AbilityUpgradeDefinition> Upgrades => upgrades;
        public IReadOnlyList<PassiveDefinition> Passives => passives;
        public IReadOnlyList<BuildPreset> Presets => presets;

        public void Set(IEnumerable<AbilityDefinition> abilityList, IEnumerable<AbilityUpgradeDefinition> upgradeList,
            IEnumerable<PassiveDefinition> passiveList, IEnumerable<BuildPreset> presetList)
        {
            abilities = abilityList.ToList();
            upgrades = upgradeList.ToList();
            passives = passiveList.ToList();
            presets = presetList.ToList();
        }
    }

    /// <summary>
    /// Id lookup for everything a build can contain: catalogs found in Resources/Combat/Build plus the code-defined
    /// samples. Used by saves (ids -> definitions) and the reward director (the reward pool).
    /// </summary>
    public sealed class BuildContentRegistry
    {
        private static BuildContentRegistry instance;

        private readonly Dictionary<string, AbilityDefinition> abilities = new();
        private readonly Dictionary<string, AbilityUpgradeDefinition> upgrades = new();
        private readonly Dictionary<string, PassiveDefinition> passives = new();
        private readonly Dictionary<string, BuildPreset> presets = new();

        public static BuildContentRegistry Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = new BuildContentRegistry();
                foreach (BuildContentCatalog catalog in Resources.LoadAll<BuildContentCatalog>(BuildContentCatalog.ResourceFolder))
                    instance.Register(catalog);
                // Code-defined samples fill in anything no catalog provides (exported sample assets win).
                SampleBuildLibrary.RegisterInto(instance);
                return instance;
            }
        }

        /// <summary>Forget the cached registry (tests, or after exporting content).</summary>
        public static void Reset() => instance = null;

        public IEnumerable<AbilityDefinition> Abilities => abilities.Values;
        public IEnumerable<AbilityUpgradeDefinition> Upgrades => upgrades.Values;
        public IEnumerable<PassiveDefinition> Passives => passives.Values;
        public IEnumerable<BuildPreset> Presets => presets.Values;

        public void Register(BuildContentCatalog catalog)
        {
            if (catalog == null) return;
            foreach (AbilityDefinition ability in catalog.Abilities) Register(ability);
            foreach (AbilityUpgradeDefinition upgrade in catalog.Upgrades) Register(upgrade);
            foreach (PassiveDefinition passive in catalog.Passives) Register(passive);
            foreach (BuildPreset preset in catalog.Presets) Register(preset);
        }

        public void Register(AbilityDefinition ability) { if (ability != null && !abilities.ContainsKey(ability.Id)) abilities[ability.Id] = ability; }
        public void Register(AbilityUpgradeDefinition upgrade) { if (upgrade != null && !upgrades.ContainsKey(upgrade.Id)) upgrades[upgrade.Id] = upgrade; }
        public void Register(PassiveDefinition passive) { if (passive != null && !passives.ContainsKey(passive.Id)) passives[passive.Id] = passive; }
        public void Register(BuildPreset preset) { if (preset != null && !presets.ContainsKey(preset.Id)) presets[preset.Id] = preset; }

        public AbilityDefinition Ability(string id) => id != null && abilities.TryGetValue(id, out AbilityDefinition value) ? value : null;
        public AbilityUpgradeDefinition Upgrade(string id) => id != null && upgrades.TryGetValue(id, out AbilityUpgradeDefinition value) ? value : null;
        public PassiveDefinition Passive(string id) => id != null && passives.TryGetValue(id, out PassiveDefinition value) ? value : null;
        public BuildPreset Preset(string id) => id != null && presets.TryGetValue(id, out BuildPreset value) ? value : null;
    }
}
