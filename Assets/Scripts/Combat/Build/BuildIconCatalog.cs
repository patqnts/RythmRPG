using System;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Build/Icon Catalog")]
    public sealed class BuildIconCatalog : ScriptableObject
    {
        public const string ResourcePath = "Combat/Build/BuildIconCatalog";
        [Serializable] public sealed class Entry { public string id; public Sprite icon; }
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        public Entry[] Entries => entries;
        public void Set(Entry[] values) => entries = values;
        public static void Apply(SampleBuildLibrary.Content content)
        {
            BuildIconCatalog catalog = Resources.Load<BuildIconCatalog>(ResourcePath);
            if (catalog == null) return;
            foreach (Entry entry in catalog.entries.Where(e => e?.icon != null))
            {
                if (content.Abilities.TryGetValue(entry.id, out AbilityDefinition ability)) ability.SetCodeIcon(entry.icon);
                if (content.Passives.TryGetValue(entry.id, out PassiveDefinition passive)) passive.SetCodeIcon(entry.icon);
            }
        }
    }
}
