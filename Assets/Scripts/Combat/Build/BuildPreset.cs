using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class PresetSlot
    {
        public AbilityDefinition ability;
        public List<AbilityUpgradeDefinition> upgrades = new();
    }

    [Serializable]
    public sealed class PresetPassive
    {
        public PassiveDefinition passive;
        [Min(1)] public int level = 1;
    }

    /// <summary>A ready-made starting build (four slots, their dedicated upgrades, passives) for testing playstyles.</summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Build Preset", fileName = "BuildPreset")]
    public sealed class BuildPreset : ScriptableObject
    {
        [SerializeField] private string id = "preset";
        [SerializeField] private string displayName = "Build";
        [SerializeField] private string playstyle = string.Empty;
        [SerializeField, TextArea(3, 8)] private string description = string.Empty;
        [SerializeField] private List<PresetSlot> slots = new();
        [SerializeField] private List<AbilityDefinition> reserve = new();
        [SerializeField] private List<PresetPassive> passives = new();

        public string Id => id;
        public string DisplayName => displayName;
        public string Playstyle => playstyle;
        public string Description => description;
        public IReadOnlyList<PresetSlot> Slots => slots;
        public IReadOnlyList<AbilityDefinition> ReserveAbilities => reserve;
        public IReadOnlyList<PresetPassive> Passives => passives;

        public RunBuildState CreateState(int seed = 12345)
        {
            var state = new RunBuildState { PresetId = id, DisplayName = displayName, Playstyle = playstyle, Seed = seed };
            for (int i = 0; i < slots.Count && i < RunBuildState.SlotCount; i++)
            {
                PresetSlot slot = slots[i];
                if (slot?.ability == null) continue;
                AbilityInstance instance = state.AddAbility(slot.ability, i);
                foreach (AbilityUpgradeDefinition upgrade in slot.upgrades.Where(u => u != null)) state.AddUpgrade(instance, upgrade);
            }
            foreach (AbilityDefinition ability in reserve.Where(a => a != null)) state.AddAbility(ability, -2);
            foreach (PresetPassive passive in passives.Where(p => p?.passive != null)) state.AddPassive(passive.passive, passive.level);
            return state;
        }

        public sealed class Builder
        {
            private readonly BuildPreset preset;

            public Builder(string id, string displayName, string playstyle)
            {
                preset = CreateInstance<BuildPreset>();
                preset.name = displayName;
                preset.id = id;
                preset.displayName = displayName;
                preset.playstyle = playstyle;
                preset.slots = new List<PresetSlot>();
                preset.reserve = new List<AbilityDefinition>();
                preset.passives = new List<PresetPassive>();
            }

            public Builder Describe(string text) { preset.description = text; return this; }
            public Builder Slot(AbilityDefinition ability, params AbilityUpgradeDefinition[] upgrades)
            {
                preset.slots.Add(new PresetSlot { ability = ability, upgrades = upgrades.Where(u => u != null).ToList() });
                return this;
            }
            public Builder Reserve(AbilityDefinition ability) { preset.reserve.Add(ability); return this; }
            public Builder Passive(PassiveDefinition passive, int level = 1)
            {
                preset.passives.Add(new PresetPassive { passive = passive, level = level });
                return this;
            }
            public BuildPreset Build() => preset;
        }
    }
}
