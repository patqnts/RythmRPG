using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Incoming-damage multiplier for one damage type (None = physical). 1 = neutral, 0.5 = resists, 1.5 = weak.</summary>
    [Serializable]
    public sealed class DamageAffinity
    {
        public ElementType element = ElementType.None;
        [Min(0f)] public float multiplier = 1f;

        public DamageAffinity() { }
        public DamageAffinity(ElementType element, float multiplier) { this.element = element; this.multiplier = multiplier; }
    }

    /// <summary>How an enemy responds to one status (by status id): immunity, duration, potency and stack limits.</summary>
    [Serializable]
    public sealed class StatusResponse
    {
        public string statusId = "burn";
        public bool immune;
        [Min(0f)] public float durationScale = 1f;
        [Min(0f)] public float potencyScale = 1f;
        [Tooltip("0 = the status's own stack limit.")]
        [Min(0)] public int maxStacks;
    }

    /// <summary>
    /// Enemy affinities and effect responses, authored independently of attack sequences. Elemental resistance only
    /// affects matching damage components; statuses and control limits are separate entries.
    /// </summary>
    [Serializable]
    public sealed class EnemyResponseProfile
    {
        [SerializeField] private string label = string.Empty;
        [SerializeField] private List<DamageAffinity> affinities = new();
        [SerializeField] private List<StatusResponse> statuses = new();

        public string Label => string.IsNullOrEmpty(label) ? "Neutral" : label;
        public IReadOnlyList<DamageAffinity> Affinities => affinities;
        public IReadOnlyList<StatusResponse> Statuses => statuses;

        public EnemyResponseProfile() { }

        public EnemyResponseProfile(string label, IEnumerable<DamageAffinity> affinities, IEnumerable<StatusResponse> statuses = null)
        {
            this.label = label;
            this.affinities = affinities?.ToList() ?? new List<DamageAffinity>();
            this.statuses = statuses?.ToList() ?? new List<StatusResponse>();
        }

        /// <summary>Raw authored multiplier (1 when not listed). The damage service clamps it with the balance rules.</summary>
        public float Affinity(ElementType element)
        {
            foreach (DamageAffinity entry in affinities)
                if (entry != null && entry.element == element) return Mathf.Max(0f, entry.multiplier);
            return 1f;
        }

        public StatusResponse Status(string statusId) =>
            statuses.FirstOrDefault(entry => entry != null && entry.statusId == statusId);

        public string Describe()
        {
            List<string> parts = affinities.Where(a => a != null && !Mathf.Approximately(a.multiplier, 1f))
                .Select(a => $"{BuildTagUtility.ElementName(a.element)} x{a.multiplier:0.##}").ToList();
            parts.AddRange(statuses.Where(s => s != null).Select(s => s.immune
                ? $"{s.statusId} immune"
                : $"{s.statusId} dur x{s.durationScale:0.##} pot x{s.potencyScale:0.##}"));
            return parts.Count == 0 ? "no affinities" : string.Join(", ", parts);
        }
    }
}
