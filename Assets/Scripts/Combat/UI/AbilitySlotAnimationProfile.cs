using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// How the ability icons above the lane buttons move: pop up one by one at the start of the player turn, float
    /// while waiting, shake harder the longer an ability is held (charging), punch when the hold completes, and drop
    /// away when hidden. Distances are in icon heights, so the same profile works for any icon size or canvas.
    /// Loaded from Resources/Combat/UI/AbilitySlotAnimation; missing = the defaults below.
    /// </summary>
    [CreateAssetMenu(menuName = "Rythm RPG/Combat/Ability Slot Animation", fileName = "AbilitySlotAnimation")]
    public sealed class AbilitySlotAnimationProfile : ScriptableObject
    {
        public const string ResourcePath = "Combat/UI/AbilitySlotAnimation";

        [Header("Pop up (player turn start)")]
        [Tooltip("Wait after the player turn starts before the first icon appears.")]
        [SerializeField, Min(0f)] private float appearDelay = 0.25f;
        [Tooltip("Extra wait for each following icon (left to right).")]
        [SerializeField, Min(0f)] private float appearStagger = 0.09f;
        [SerializeField, Min(0.01f)] private float appearSeconds = 0.38f;
        [Tooltip("Scale over the pop (0-1 time). Going above 1 gives the overshoot.")]
        [SerializeField] private AnimationCurve appearScale = new(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.6f, 1.18f), new Keyframe(0.82f, 0.95f), new Keyframe(1f, 1f));
        [Tooltip("Icons rise from this far below their spot (icon heights).")]
        [SerializeField] private float appearRise = 0.7f;

        [Header("Hide")]
        [SerializeField, Min(0.01f)] private float hideSeconds = 0.16f;
        [SerializeField, Min(0f)] private float hideStagger = 0.035f;
        [Tooltip("Icons sink this far while hiding (icon heights).")]
        [SerializeField] private float hideDrop = 0.35f;

        [Header("Float (waiting)")]
        [SerializeField] private float floatHeight = 0.12f;
        [SerializeField, Min(0.05f)] private float floatPeriod = 1.7f;
        [Tooltip("Each icon is this much of a period behind its left neighbour, so they bob in a wave.")]
        [SerializeField, Range(0f, 1f)] private float floatPhaseStep = 0.22f;
        [Tooltip("Unused: idle float is vertical-only (no rotation). Kept only so old assets keep this value; nothing reads it any more.")]
        [SerializeField] private float floatSwayDegrees = 3f;

        [Header("Charge (holding a selected ability)")]
        [Tooltip("Shake distance when the hold starts and when it is almost complete (icon heights).")]
        [SerializeField, Min(0f)] private float chargeShakeStart = 0.015f;
        [SerializeField, Min(0f)] private float chargeShakeEnd = 0.08f;
        [Tooltip("Shakes per second.")]
        [SerializeField, Min(1f)] private float chargeShakeFrequency = 32f;
        [SerializeField, Min(0f)] private float chargeTiltDegrees = 7f;
        [Tooltip("Icon scale when fully charged.")]
        [SerializeField, Min(0f)] private float chargeScale = 1.2f;
        [Tooltip("How quickly the charge look fades in/out (per second).")]
        [SerializeField, Min(0.1f)] private float chargeBlendSpeed = 14f;
        [Tooltip("How much of the float is kept while charging (0 = stands still).")]
        [SerializeField, Range(0f, 1f)] private float floatWhileCharging = 0.2f;

        [Header("Charged (hold complete)")]
        [SerializeField, Min(0f)] private float readyPunch = 0.3f;
        [SerializeField, Min(0.01f)] private float readyPunchSeconds = 0.16f;

        [Header("Unusable (not enough mana / cooling down)")]
        [SerializeField, Range(0f, 1f)] private float unusableAlpha = 0.45f;
        [SerializeField] private Color unusableTint = new(0.55f, 0.55f, 0.6f, 1f);

        public float AppearDelay => appearDelay;
        public float AppearStagger => appearStagger;
        public float AppearSeconds => appearSeconds;
        public float AppearRise => appearRise;
        public float HideSeconds => hideSeconds;
        public float HideStagger => hideStagger;
        public float HideDrop => hideDrop;
        public float FloatHeight => floatHeight;
        public float FloatPeriod => floatPeriod;
        public float FloatPhaseStep => floatPhaseStep;
        public float FloatSwayDegrees => floatSwayDegrees;
        public float ChargeShakeStart => chargeShakeStart;
        public float ChargeShakeEnd => chargeShakeEnd;
        public float ChargeShakeFrequency => chargeShakeFrequency;
        public float ChargeTiltDegrees => chargeTiltDegrees;
        public float ChargeScale => chargeScale;
        public float ChargeBlendSpeed => chargeBlendSpeed;
        public float FloatWhileCharging => floatWhileCharging;
        public float ReadyPunch => readyPunch;
        public float ReadyPunchSeconds => readyPunchSeconds;
        public float UnusableAlpha => unusableAlpha;
        public Color UnusableTint => unusableTint;

        public float EvaluateAppearScale(float t)
        {
            if (appearScale == null || appearScale.length == 0) return t;
            return appearScale.Evaluate(Mathf.Clamp01(t));
        }

        private static AbilitySlotAnimationProfile fallback;

        public static AbilitySlotAnimationProfile LoadOrDefault()
        {
            AbilitySlotAnimationProfile profile = Resources.Load<AbilitySlotAnimationProfile>(ResourcePath);
            if (profile != null) return profile;
            if (fallback == null)
            {
                fallback = CreateInstance<AbilitySlotAnimationProfile>();
                fallback.hideFlags = HideFlags.DontSave;
            }
            return fallback;
        }
    }
}
