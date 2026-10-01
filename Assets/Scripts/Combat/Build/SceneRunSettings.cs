using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Scenes/New Run Settings", fileName = "SceneRunSettings")]
    public sealed class SceneRunSettings : ScriptableObject
    {
        public const string ResourcePath = "Combat/Build/SceneRunSettings";
        [Tooltip("Optional starting preset. Empty copies DefaultLoadout into a fresh run so rewards and inventory work immediately.")]
        public BuildPreset startingPreset;
        public bool randomizeRunSeed = true;
        public int seed = 12345;

        public static RunBuildState CreateNewRun(bool randomize = true)
        {
            var settings = Resources.Load<SceneRunSettings>(ResourcePath);
            int seed = settings != null ? settings.seed : 12345;
            if (randomize && (settings == null || settings.randomizeRunSeed)) seed = System.Guid.NewGuid().GetHashCode() & int.MaxValue;
            if (settings != null && settings.startingPreset != null) return settings.startingPreset.CreateState(seed);
            var build = new RunBuildState { DisplayName = "New run", Seed = seed };
            var loadout = Resources.Load<AbilityLoadout>(AbilityLoadout.ResourcePath);
            if (loadout != null)
                foreach (var slot in loadout.Slots)
                    if (slot.Ability != null && slot.LaneId >= 1 && slot.LaneId <= RunBuildState.SlotCount)
                        build.AddAbility(slot.Ability, slot.LaneId - 1);
            return build;
        }
    }
}
