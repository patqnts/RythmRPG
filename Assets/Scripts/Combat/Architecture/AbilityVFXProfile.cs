using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    /// <summary>
    /// How an ability looks when it is cast: the charge around the player while its key is held, the wisp it turns
    /// into, the pop at centre stage (where its rhythm pattern bursts out), and the old impact projectile. Every prefab
    /// slot is optional: empty = the Combat VFX Theme's default, and if that is empty too, a built-in effect.
    /// Give each ability its own profile to give it its own look.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability VFX Profile", fileName = "AbilityVFXProfile")]
    public sealed class AbilityVFXProfile : ScriptableObject
    {
        [SerializeField] private Color accentColor = Color.white;

        [Header("Charge (while the key is held)")]
        [Tooltip("Spawned around the player while the ability's key is held. Particle systems in it emit more as the hold fills up. Empty = theme default / built-in gathering motes.")]
        [SerializeField] private GameObject chargePrefab;
        [Tooltip("Offset from the centre of the player's sprite.")]
        [SerializeField] private Vector3 chargeOffset = Vector3.zero;
        [Tooltip("Size of the charge effect (built-in: ring radius in world units; prefab: uniform scale).")]
        [SerializeField, Min(0.05f)] private float chargeSize = 1f;
        [Tooltip("Optional one-shot burst when the hold completes. Empty = theme default / built-in flash.")]
        [SerializeField] private GameObject chargeReleasePrefab;

        [Header("Wisp (flies to centre stage)")]
        [Tooltip("What the ability turns into and flies to centre stage (particles, trail, sprite...). Empty = theme default / built-in glowing wisp.")]
        [SerializeField] private GameObject wispPrefab;
        [Tooltip("Show the ability icon shrinking into the wisp as it forms.")]
        [SerializeField] private bool iconMorphsIntoWisp = true;
        [SerializeField, Min(0.05f)] private float wispSize = 0.6f;
        [FormerlySerializedAs("iconTravelDuration")]
        [SerializeField, Min(0.05f)] private float wispTravelSeconds = 0.45f;
        [Tooltip("How high (screen-up) the flight curves, in world units.")]
        [SerializeField] private float wispArcHeight = 0.8f;
        [Tooltip("Shortest time the wisp floats at centre stage before it pops. It may float longer so the pop lands on the song's beat, right as the first projectile appears.")]
        [SerializeField, Min(0f)] private float wispMinHoverSeconds = 0.1f;

        [Header("Pop (at centre stage; the pattern bursts out)")]
        [FormerlySerializedAs("burstPrefab")]
        [Tooltip("Spawned where the wisp pops. Empty = theme default / built-in spark burst.")]
        [SerializeField] private GameObject popPrefab;
        [FormerlySerializedAs("burstDuration")]
        [SerializeField, Min(0f)] private float popSeconds = 0.25f;
        [SerializeField, Min(0f)] private float popShake = 0.05f;
        [Tooltip("Small spark at centre stage each time one of the pattern's notes is thrown. Empty = theme default / built-in.")]
        [SerializeField] private GameObject noteSparkPrefab;
        [SerializeField] private bool sparkOnEveryNote = true;

        [Header("Impact (after the rhythm part, when there is no Attack Sequence)")]
        [SerializeField, Min(0f)] private float impactAnticipationDuration = 0.25f;
        [SerializeField] private GameObject impactProjectilePrefab;
        [SerializeField, Min(0.01f)] private float impactProjectileSpeed = 14f;
        [SerializeField, Min(0.01f)] private float impactProjectileScale = 0.55f;
        [SerializeField] private Vector3 impactOriginOffset = Vector3.up * 1.05f;
        [SerializeField] private Vector3 impactTargetOffset = Vector3.up * 0.35f;
        [SerializeField, Min(0f)] private float impactSettleDuration = 0.25f;

        public Color AccentColor => accentColor;
        public GameObject ChargePrefab => chargePrefab;
        public Vector3 ChargeOffset => chargeOffset;
        public float ChargeSize => chargeSize;
        public GameObject ChargeReleasePrefab => chargeReleasePrefab;
        public GameObject WispPrefab => wispPrefab;
        public bool IconMorphsIntoWisp => iconMorphsIntoWisp;
        public float WispSize => wispSize;
        public float WispTravelSeconds => wispTravelSeconds;
        public float WispArcHeight => wispArcHeight;
        public float WispMinHoverSeconds => wispMinHoverSeconds;
        public GameObject PopPrefab => popPrefab;
        public float PopSeconds => popSeconds;
        public float PopShake => popShake;
        public GameObject NoteSparkPrefab => noteSparkPrefab;
        public bool SparkOnEveryNote => sparkOnEveryNote;

        // Old names, kept for existing callers.
        public float IconTravelDuration => wispTravelSeconds;
        public float BurstDuration => popSeconds;
        public GameObject BurstPrefab => popPrefab;

        public float ImpactAnticipationDuration => impactAnticipationDuration;
        public GameObject ImpactProjectilePrefab => impactProjectilePrefab;
        public float ImpactProjectileSpeed => impactProjectileSpeed;
        public float ImpactProjectileScale => impactProjectileScale;
        public Vector3 ImpactOriginOffset => impactOriginOffset;
        public Vector3 ImpactTargetOffset => impactTargetOffset;
        public float ImpactSettleDuration => impactSettleDuration;
    }
}
