using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Which ability sits on which lane. Used by <see cref="AbilitySlotController"/> when it has no assignments of its
    /// own (loaded from Resources/Combat/Abilities/DefaultLoadout).
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability Loadout", fileName = "AbilityLoadout")]
    public sealed class AbilityLoadout : ScriptableObject
    {
        public const string ResourcePath = "Combat/Abilities/DefaultLoadout";

        [SerializeField] private List<AbilitySlotAssignment> slots = new();

        public IReadOnlyList<AbilitySlotAssignment> Slots => slots;
    }
}
