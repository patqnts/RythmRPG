using System;
using System.Collections;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Points an attack step can refer to.</summary>
    public enum AttackAnchor
    {
        /// <summary>The character (where it stands during the attack).</summary>
        Caster,
        /// <summary>The enemy being attacked.</summary>
        Target,
        /// <summary>Halfway between character and enemy.</summary>
        Between,
        /// <summary>Where the character stood before moving to the stage.</summary>
        CasterHome
    }

    /// <summary>Everything a step may need while it runs. Built by <see cref="CharacterAttackPerformer"/>.</summary>
    public sealed class AttackStepContext
    {
        public MonoBehaviour Host;
        public Transform Caster;
        public Transform Target;
        public Animator CasterAnimator;
        public Camera Camera;
        public Vector3 CasterHome;
        public Transform EffectParent;
        /// <summary>
        /// One hit of the attack with a weight. The ability's damage/heal is divided by weight over all hits of the
        /// sequence (weights are relative: three hits of 1 = a third each; 1, 1, 2 = 25%, 25%, 50%).
        /// </summary>
        public Action<float> Hit;
        /// <summary>Raised by <see cref="AttackAnimationEventRelay"/> when the character's animation fires AttackHit().</summary>
        public AttackAnimationEventRelay AnimationEvents;
        /// <summary>Tint for effects that want it (the ability's accent colour).</summary>
        public Color Accent = Color.white;

        public Vector3 Resolve(AttackAnchor anchor, Vector3 offset)
        {
            Vector3 caster = Caster != null ? Caster.position : CasterHome;
            Vector3 target = Target != null ? Target.position : caster;
            Vector3 point = anchor switch
            {
                AttackAnchor.Target => target,
                AttackAnchor.Between => (caster + target) * 0.5f,
                AttackAnchor.CasterHome => CasterHome,
                _ => caster
            };
            return point + offset;
        }

        public Transform AnchorTransform(AttackAnchor anchor) => anchor switch
        {
            AttackAnchor.Target => Target,
            AttackAnchor.Caster => Caster,
            _ => EffectParent
        };

        /// <summary>Makes sprite visuals of a spawned object face the camera like the notes do.</summary>
        public void FaceCamera(GameObject instance)
        {
            if (instance == null || Camera == null) return;
            RhythmNoteVisualLayer layer = instance.GetComponent<RhythmNoteVisualLayer>();
            if (layer == null) layer = instance.AddComponent<RhythmNoteVisualLayer>();
            layer.FaceSpritesToCamera(Camera);
        }

        public GameObject Spawn(GameObject prefab, AttackAnchor anchor, Vector3 offset, bool attach, float lifetime, bool faceCamera = true)
        {
            if (prefab == null) return null;
            Transform parent = attach ? AnchorTransform(anchor) : EffectParent;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, Resolve(anchor, offset), prefab.transform.rotation, parent);
            if (faceCamera) FaceCamera(instance);
            if (lifetime > 0f) UnityEngine.Object.Destroy(instance, lifetime);
            return instance;
        }

        public void Shake(float strength, float seconds) => CombatCameraShaker.Shake(Camera, strength, seconds);

        /// <summary>Waits for the animator's current (or next, while blending) state length; 0 when unknown.</summary>
        public float CurrentStateSeconds()
        {
            if (CasterAnimator == null) return 0f;
            AnimatorStateInfo info = CasterAnimator.IsInTransition(0)
                ? CasterAnimator.GetNextAnimatorStateInfo(0)
                : CasterAnimator.GetCurrentAnimatorStateInfo(0);
            return Mathf.Max(0f, info.length / Mathf.Max(0.01f, Mathf.Abs(CasterAnimator.speed)));
        }

        /// <summary>Plays a state (or sets a trigger) on the character; false when there is nothing to play.</summary>
        public bool PlayState(string stateName, string trigger, float crossFade)
        {
            if (CasterAnimator == null) return false;
            if (!string.IsNullOrWhiteSpace(trigger))
            {
                CasterAnimator.SetTrigger(trigger);
                return true;
            }

            if (string.IsNullOrWhiteSpace(stateName) || !CasterAnimator.HasState(0, Animator.StringToHash(stateName))) return false;
            CasterAnimator.CrossFadeInFixedTime(stateName, crossFade, 0, 0f);
            return true;
        }
    }

    /// <summary>
    /// One thing that happens during a character attack. Subclass it for new kinds of attack presentation; the
    /// inspector's step picker finds subclasses automatically. Steps that land hits override <see cref="HitWeight"/>.
    /// </summary>
    [Serializable]
    public abstract class AttackStep
    {
        [Tooltip("On: the next step starts when this one finishes. Off: it runs in the background, so it combines with the following steps.")]
        [SerializeField] private bool waitForCompletion = true;
        [Tooltip("Delay before this step starts.")]
        [SerializeField, Min(0f)] private float delay;

        public bool WaitForCompletion => waitForCompletion;
        public float Delay => delay;
        /// <summary>Total hit weight this step delivers (0 = it lands no hits).</summary>
        public virtual float HitWeight => 0f;
        public bool DealsDamage => HitWeight > 0f;

        public abstract IEnumerator Run(AttackStepContext context);
    }

    [Serializable]
    public sealed class PlayAnimationStep : AttackStep
    {
        public enum WaitMode { DontWait, Seconds, StateLength }

        [SerializeField] private string stateName = "Attack";
        [Tooltip("Optional trigger to set instead of playing a state directly.")]
        [SerializeField] private string trigger = string.Empty;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.05f;
        [SerializeField] private WaitMode wait = WaitMode.StateLength;
        [SerializeField, Min(0f)] private float seconds = 0.4f;

        public override IEnumerator Run(AttackStepContext context)
        {
            if (!context.PlayState(stateName, trigger, crossFadeSeconds)) yield break;
            switch (wait)
            {
                case WaitMode.Seconds:
                    yield return new WaitForSeconds(seconds);
                    break;
                case WaitMode.StateLength:
                    yield return null; // let the transition start
                    yield return null;
                    yield return new WaitForSeconds(context.CurrentStateSeconds());
                    break;
            }
        }
    }

    [Serializable]
    public sealed class SpawnEffectStep : AttackStep
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private AttackAnchor at = AttackAnchor.Target;
        [SerializeField] private Vector3 offset = Vector3.up * 0.5f;
        [Tooltip("Follow the anchor while alive (e.g. an aura on the character).")]
        [SerializeField] private bool attachToAnchor;
        [SerializeField, Min(0.01f)] private float scale = 1f;
        [Tooltip("Destroyed after this many seconds (0 = let the prefab clean itself up).")]
        [SerializeField, Min(0f)] private float lifetime = 2f;
        [Tooltip("How long this step lasts when Wait For Completion is on.")]
        [SerializeField, Min(0f)] private float holdSeconds = 0.3f;
        [SerializeField] private bool faceCamera = true;

        public override IEnumerator Run(AttackStepContext context)
        {
            GameObject instance = context.Spawn(prefab, at, offset, attachToAnchor, lifetime, faceCamera);
            if (instance != null) instance.transform.localScale = prefab.transform.localScale * scale;
            if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);
        }
    }

    [Serializable]
    public sealed class ThrowProjectileStep : AttackStep
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private AttackAnchor from = AttackAnchor.Caster;
        [SerializeField] private Vector3 fromOffset = Vector3.up * 0.8f;
        [SerializeField] private AttackAnchor to = AttackAnchor.Target;
        [SerializeField] private Vector3 toOffset = Vector3.up * 0.5f;
        [Tooltip("Flight time. 0 = use Speed.")]
        [SerializeField, Min(0f)] private float duration = 0.35f;
        [SerializeField, Min(0.01f)] private float speed = 14f;
        [Tooltip("Height of the throw arc (0 = straight line).")]
        [SerializeField] private float arcHeight = 1f;
        [Tooltip("Spin around the camera's view axis, degrees per second.")]
        [SerializeField] private float spinDegreesPerSecond;
        [SerializeField, Min(0.01f)] private float scale = 1f;
        [Tooltip("Progress along the flight over time (0-1). Linear by default; bend it for ease-in / ease-out.")]
        [SerializeField] private AnimationCurve flightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Land a hit when the projectile arrives.")]
        [SerializeField] private bool damageOnHit = true;
        [Tooltip("Share of the ability's damage this projectile carries (relative to the sequence's other hits).")]
        [SerializeField, Min(0f)] private float hitWeight = 1f;
        [Tooltip("Optional effect spawned where the projectile lands.")]
        [SerializeField] private GameObject impactEffect;
        [SerializeField, Min(0f)] private float impactEffectLifetime = 1.5f;
        [SerializeField, Min(0f)] private float impactShake;

        public override float HitWeight => damageOnHit ? hitWeight : 0f;

        public override IEnumerator Run(AttackStepContext context)
        {
            Vector3 start = context.Resolve(from, fromOffset);
            Vector3 end = context.Resolve(to, toOffset);
            GameObject projectile = prefab != null
                ? UnityEngine.Object.Instantiate(prefab, start, prefab.transform.rotation, context.EffectParent)
                : null;
            if (projectile != null)
            {
                projectile.transform.localScale = prefab.transform.localScale * scale;
                context.FaceCamera(projectile);
            }

            float time = duration > 0f ? duration : Vector3.Distance(start, end) / speed;
            float elapsed = 0f;
            float angle = 0f;
            Quaternion baseRotation = projectile != null ? projectile.transform.rotation : Quaternion.identity;
            Vector3 spinAxis = context.Camera != null ? context.Camera.transform.forward : Vector3.forward;
            while (elapsed < time)
            {
                elapsed += Time.deltaTime;
                float linear = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, time));
                float t = flightCurve != null && flightCurve.length > 0 ? flightCurve.Evaluate(linear) : linear;
                if (projectile != null)
                {
                    projectile.transform.position = Vector3.LerpUnclamped(start, end, t) + Vector3.up * (arcHeight * 4f * t * (1f - t));
                    if (spinDegreesPerSecond != 0f)
                    {
                        angle += spinDegreesPerSecond * Time.deltaTime;
                        projectile.transform.rotation = Quaternion.AngleAxis(angle, spinAxis) * baseRotation;
                    }
                }
                yield return null;
            }

            if (projectile != null) UnityEngine.Object.Destroy(projectile);
            if (impactEffect != null)
            {
                GameObject fx = UnityEngine.Object.Instantiate(impactEffect, end, impactEffect.transform.rotation, context.EffectParent);
                context.FaceCamera(fx);
                if (impactEffectLifetime > 0f) UnityEngine.Object.Destroy(fx, impactEffectLifetime);
            }
            if (impactShake > 0f) context.Shake(impactShake, 0.2f);
            if (damageOnHit) context.Hit?.Invoke(hitWeight);
        }
    }

    /// <summary>The moment a hit lands: applies that hit's share of the ability (enemy flash, damage number) with no visual of its own.</summary>
    [Serializable]
    public sealed class ImpactStep : AttackStep
    {
        [Tooltip("Share of the ability's damage this hit carries (relative to the sequence's other hits).")]
        [SerializeField, Min(0f)] private float weight = 1f;
        [SerializeField] private GameObject effect;
        [SerializeField] private AttackAnchor effectAt = AttackAnchor.Target;
        [SerializeField] private Vector3 effectOffset = Vector3.up * 0.5f;
        [SerializeField, Min(0f)] private float shake;

        public override float HitWeight => weight;

        public override IEnumerator Run(AttackStepContext context)
        {
            context.Spawn(effect, effectAt, effectOffset, false, 1.5f);
            if (shake > 0f) context.Shake(shake, 0.2f);
            context.Hit?.Invoke(weight);
            yield break;
        }
    }

    /// <summary>
    /// Damage over time: channels for a while and lands a number of evenly spaced hits (ticks). Optional looping
    /// effect and animator state/bool while channelling, and an effect per tick.
    /// </summary>
    [Serializable]
    public sealed class ChannelDamageStep : AttackStep
    {
        [SerializeField, Min(0.05f)] private float channelSeconds = 1.5f;
        [SerializeField, Min(1)] private int ticks = 5;
        [Tooltip("Share of the ability's damage per tick (relative to the sequence's other hits).")]
        [SerializeField, Min(0f)] private float weightPerTick = 1f;
        [Tooltip("First tick right away instead of after one interval.")]
        [SerializeField] private bool tickAtStart;
        [Header("Presentation")]
        [SerializeField] private string channelState = string.Empty;
        [Tooltip("Animator bool held true while channelling (e.g. \"IsChanneling\").")]
        [SerializeField] private string channelBool = string.Empty;
        [SerializeField] private GameObject loopEffect;
        [SerializeField] private AttackAnchor loopEffectAt = AttackAnchor.Caster;
        [SerializeField] private Vector3 loopEffectOffset = Vector3.up * 0.6f;
        [SerializeField] private GameObject tickEffect;
        [SerializeField] private AttackAnchor tickEffectAt = AttackAnchor.Target;
        [SerializeField] private Vector3 tickEffectOffset = Vector3.up * 0.5f;
        [SerializeField, Min(0f)] private float tickShake = 0.03f;

        public override float HitWeight => ticks * weightPerTick;

        public override IEnumerator Run(AttackStepContext context)
        {
            context.PlayState(channelState, string.Empty, 0.05f);
            SetBool(context, true);
            GameObject loop = context.Spawn(loopEffect, loopEffectAt, loopEffectOffset, true, 0f);
            float interval = channelSeconds / Mathf.Max(1, ticks);
            try
            {
                for (int i = 0; i < ticks; i++)
                {
                    if (!(tickAtStart && i == 0)) yield return new WaitForSeconds(interval);
                    context.Spawn(tickEffect, tickEffectAt, tickEffectOffset, false, 1.2f);
                    if (tickShake > 0f) context.Shake(tickShake, Mathf.Min(0.15f, interval));
                    context.Hit?.Invoke(weightPerTick);
                }

                if (tickAtStart) yield return new WaitForSeconds(interval);
            }
            finally
            {
                if (loop != null) UnityEngine.Object.Destroy(loop);
                SetBool(context, false);
            }
        }

        private void SetBool(AttackStepContext context, bool value)
        {
            if (context.CasterAnimator == null || string.IsNullOrWhiteSpace(channelBool)) return;
            foreach (AnimatorControllerParameter p in context.CasterAnimator.parameters)
                if (p.type == AnimatorControllerParameterType.Bool && p.name == channelBool) context.CasterAnimator.SetBool(channelBool, value);
        }
    }

    /// <summary>
    /// Divides the damage over an animation: plays a state and lands hits at set moments of it (e.g. three punches).
    /// Times are either seconds from the start or 0-1 of the clip's length, so they follow the animation if it changes speed.
    /// </summary>
    [Serializable]
    public sealed class TimedHitsStep : AttackStep
    {
        public enum TimeMode { NormalizedClip, Seconds }

        [Serializable]
        public sealed class TimedHit
        {
            [Tooltip("NormalizedClip: 0-1 of the animation. Seconds: seconds from the step start.")]
            public float time = 0.5f;
            [Min(0f)] public float weight = 1f;
            [Tooltip("Impact prefab for this hit. Empty: uses the step's Hit Effect.")]
            public GameObject effect;
            [Min(0f)] public float shake = 0.04f;
        }

        [SerializeField] private string stateName = "Attack";
        [SerializeField] private string trigger = string.Empty;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.05f;
        [SerializeField] private TimeMode timeMode = TimeMode.NormalizedClip;
        [Tooltip("Clip length used for NormalizedClip when the character has no such state (seconds).")]
        [SerializeField, Min(0.05f)] private float fallbackClipSeconds = 0.6f;
        [SerializeField] private List<TimedHit> hits = new()
        {
            new TimedHit { time = 0.25f }, new TimedHit { time = 0.5f }, new TimedHit { time = 0.8f }
        };
        [Tooltip("Impact prefab spawned on every hit that has no effect of its own (like Channel Damage's Tick Effect).")]
        [SerializeField] private GameObject hitEffect;
        [SerializeField] private AttackAnchor effectAt = AttackAnchor.Target;
        [SerializeField] private Vector3 effectOffset = Vector3.up * 0.5f;
        [Tooltip("Wait for the rest of the animation after the last hit.")]
        [SerializeField] private bool waitForAnimationEnd = true;

        public override float HitWeight
        {
            get
            {
                float total = 0f;
                foreach (TimedHit hit in hits) if (hit != null) total += hit.weight;
                return total;
            }
        }

        public override IEnumerator Run(AttackStepContext context)
        {
            bool played = context.PlayState(stateName, trigger, crossFadeSeconds);
            float clip = fallbackClipSeconds;
            float elapsed = 0f;
            if (played)
            {
                yield return null;
                yield return null;
                elapsed = Time.deltaTime * 2f;
                float stateSeconds = context.CurrentStateSeconds();
                if (stateSeconds > 0.01f) clip = stateSeconds;
            }

            var ordered = new List<TimedHit>();
            foreach (TimedHit hit in hits) if (hit != null) ordered.Add(hit);
            ordered.Sort((a, b) => a.time.CompareTo(b.time));
            foreach (TimedHit hit in ordered)
            {
                float at = timeMode == TimeMode.NormalizedClip ? Mathf.Clamp01(hit.time) * clip : Mathf.Max(0f, hit.time);
                if (at > elapsed) yield return new WaitForSeconds(at - elapsed);
                elapsed = Mathf.Max(elapsed, at);
                context.Spawn(hit.effect != null ? hit.effect : hitEffect, effectAt, effectOffset, false, 1.2f);
                if (hit.shake > 0f) context.Shake(hit.shake, 0.15f);
                context.Hit?.Invoke(hit.weight);
            }

            float end = timeMode == TimeMode.NormalizedClip || played ? clip : elapsed;
            if (waitForAnimationEnd && end > elapsed) yield return new WaitForSeconds(end - elapsed);
        }
    }

    /// <summary>
    /// Hits driven by the animation itself: add an Animation Event calling <c>AttackHit</c> on the frames where the
    /// hits connect. Each event lands one hit. Missing events are delivered when the animation ends (or on timeout).
    /// </summary>
    [Serializable]
    public sealed class AnimationEventHitsStep : AttackStep
    {
        [SerializeField] private string stateName = "Attack";
        [SerializeField] private string trigger = string.Empty;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.05f;
        [Tooltip("How many AttackHit events the animation has.")]
        [SerializeField, Min(1)] private int expectedHits = 3;
        [SerializeField, Min(0f)] private float weightPerHit = 1f;
        [SerializeField] private GameObject effect;
        [SerializeField] private AttackAnchor effectAt = AttackAnchor.Target;
        [SerializeField] private Vector3 effectOffset = Vector3.up * 0.5f;
        [SerializeField, Min(0f)] private float shakePerHit = 0.04f;
        [SerializeField, Min(0.1f)] private float timeoutSeconds = 3f;

        public override float HitWeight => expectedHits * weightPerHit;

        public override IEnumerator Run(AttackStepContext context)
        {
            int landed = 0;
            void OnHit()
            {
                if (landed >= expectedHits) return;
                landed++;
                context.Spawn(effect, effectAt, effectOffset, false, 1.2f);
                if (shakePerHit > 0f) context.Shake(shakePerHit, 0.15f);
                context.Hit?.Invoke(weightPerHit);
            }

            if (context.AnimationEvents != null) context.AnimationEvents.Hit += OnHit;
            try
            {
                bool played = context.PlayState(stateName, trigger, crossFadeSeconds);
                float limit = timeoutSeconds;
                if (played)
                {
                    yield return null;
                    yield return null;
                    float stateSeconds = context.CurrentStateSeconds();
                    if (stateSeconds > 0.01f) limit = Mathf.Min(limit, stateSeconds + 0.1f);
                }

                float elapsed = 0f;
                while (landed < expectedHits && elapsed < limit)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            finally
            {
                if (context.AnimationEvents != null) context.AnimationEvents.Hit -= OnHit;
            }

            while (landed < expectedHits) OnHit(); // events missing: still deliver the full damage
        }
    }

    [Serializable]
    public sealed class WaitStep : AttackStep
    {
        [SerializeField, Min(0f)] private float seconds = 0.2f;

        public override IEnumerator Run(AttackStepContext context)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
        }
    }

    [Serializable]
    public sealed class PlaySoundStep : AttackStep
    {
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        public override IEnumerator Run(AttackStepContext context)
        {
            if (clip == null) yield break;
            Vector3 at = context.Camera != null ? context.Camera.transform.position : context.CasterHome;
            AudioSource.PlayClipAtPoint(clip, at, volume);
        }
    }

    [Serializable]
    public sealed class CameraShakeStep : AttackStep
    {
        [SerializeField, Min(0f)] private float strength = 0.08f;
        [SerializeField, Min(0.01f)] private float seconds = 0.2f;

        public override IEnumerator Run(AttackStepContext context)
        {
            context.Shake(strength, seconds);
            yield return new WaitForSeconds(seconds);
        }
    }

    [Serializable]
    public sealed class MoveCasterStep : AttackStep
    {
        [Tooltip("Dash to a point, e.g. Target with an offset for a melee hit. The character walks back home at the end of the sequence anyway.")]
        [SerializeField] private AttackAnchor to = AttackAnchor.Target;
        [SerializeField] private Vector3 offset = new(0f, 0f, -0.8f);
        [SerializeField, Min(0f)] private float seconds = 0.2f;
        [SerializeField] private Ease ease = Ease.OutQuad;

        public override IEnumerator Run(AttackStepContext context)
        {
            if (context.Caster == null) yield break;
            Vector3 destination = context.Resolve(to, offset);
            destination.y = context.Caster.position.y;
            if (seconds <= 0f)
            {
                context.Caster.position = destination;
                yield break;
            }

            yield return Tween.Position(context.Caster, destination, seconds, ease).ToYieldInstruction();
        }
    }
}
