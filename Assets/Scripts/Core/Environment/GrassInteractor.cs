using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Pushes <see cref="GrassField"/> grass aside. Put it on the player, enemies, projectiles, anything that should
    /// part the grass. It costs nothing per blade: all interactors are stamped into one small texture per frame
    /// (<see cref="GrassInteractionMap"/>), and the grass shader reads it. Trampled grass springs back over time.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Environment/Grass Interactor")]
    public sealed class GrassInteractor : MonoBehaviour
    {
        /// <summary>The most interactors the map takes per frame (nearest to the view first).</summary>
        public const int MaxActive = 16;

        [Tooltip("World radius of the push.")]
        [SerializeField, Min(0.05f)] private float radius = 0.6f;
        [Tooltip("How hard the grass is pushed (1 = fully bent at the centre).")]
        [SerializeField, Range(0f, 1f)] private float strength = 1f;
        [Tooltip("Offset of the push centre from this object's pivot (e.g. to the feet).")]
        [SerializeField] private Vector3 offset;
        [Tooltip("Above this height over the grass root the interactor no longer touches the grass (jumping, flying).")]
        [SerializeField, Min(0f)] private float maxHeightAboveGround = 1.5f;
        [Tooltip("Measure the height from the bottom of this object's collider (its feet) instead of its pivot. " +
                 "Characters usually have their pivot in the middle of the body.")]
        [SerializeField] private bool feetFromCollider = true;

        private static readonly List<GrassInteractor> active = new();
        private Vector3 lastPosition;
        private bool hasLast;
        private Collider body;

        public static IReadOnlyList<GrassInteractor> Active => active;
        public float Radius { get => radius; set => radius = Mathf.Max(0.05f, value); }
        public float Strength { get => strength; set => strength = Mathf.Clamp01(value); }
        public float MaxHeightAboveGround => maxHeightAboveGround;
        public Vector3 Position => transform.position + offset;

        /// <summary>World height of the interactor's lowest point (collider bottom, or its pivot + offset).</summary>
        public float FeetHeight => feetFromCollider && body != null && body.enabled ? body.bounds.min.y : Position.y;

        /// <summary>World velocity (units per second), measured over the last map update.</summary>
        public Vector3 Velocity { get; private set; }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
            hasLast = false;
            body = GetComponent<Collider>();
        }

        private void OnDisable() => active.Remove(this);

        internal void Sample(float deltaTime)
        {
            Vector3 position = Position;
            Velocity = hasLast && deltaTime > 0.0001f ? (position - lastPosition) / deltaTime : Vector3.zero;
            lastPosition = position;
            hasLast = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => active.Clear();

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(Position, radius);
        }
    }
}
