using System;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Receives Animation Events from the character's attack clips. Add an event calling <c>AttackHit</c> on each
    /// frame where a hit connects; the running <see cref="AnimationEventHitsStep"/> lands one hit per event. Added
    /// automatically next to the character's Animator during attacks.
    /// </summary>
    public sealed class AttackAnimationEventRelay : MonoBehaviour
    {
        public event Action Hit;

        /// <summary>Animation Event target.</summary>
        public void AttackHit() => Hit?.Invoke();
    }
}
