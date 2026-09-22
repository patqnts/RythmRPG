using System;
using System.Collections;
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
        /// <summary>Applies the ability's damage (only the first call counts).</summary>
        public Action ApplyDamage;
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

        /// <summary>Makes sprite visuals of a spawned object face the camera like the notes do.</summary>
        public void FaceCamera(GameObject instance)
        {
            if (instance == null || Camera == null) return;
            RhythmNoteVisualLayer layer = instance.GetComponent<RhythmNoteVisualLayer>();
            if (layer == null) layer = instance.AddComponent<RhythmNoteVisualLayer>();
            layer.FaceSpritesToCamera(Camera);
        }
    }

    /// <summary>
    /// One thing that happens during a character attack. Subclass it for new kinds of attack presentation; the
    /// inspector's step picker finds subclasses automatically.
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
        /// <summary>True when this step applies the ability's damage.</summary>
        public virtual bool DealsDamage => false;

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
            Animator animator = context.CasterAnimator;
            if (animator == null) yield break;
            if (!string.IsNullOrWhiteSpace(trigger)) animator.SetTrigger(trigger);
            else if (!string.IsNullOrWhiteSpace(stateName) && animator.HasState(0, Animator.StringToHash(stateName)))
                animator.CrossFadeInFixedTime(stateName, crossFadeSeconds, 0, 0f);

            switch (wait)
            {
                case WaitMode.Seconds:
                    yield return new WaitForSeconds(seconds);
                    break;
                case WaitMode.StateLength:
                    yield return null; // let the transition start
                    yield return null;
                    AnimatorStateInfo info = animator.IsInTransition(0)
                        ? animator.GetNextAnimatorStateInfo(0)
                        : animator.GetCurrentAnimatorStateInfo(0);
                    yield return new WaitForSeconds(Mathf.Max(0f, info.length / Mathf.Max(0.01f, Mathf.Abs(animator.speed))));
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
            if (prefab != null)
            {
                Transform parent = attachToAnchor ? AnchorTransform(context) : context.EffectParent;
                GameObject instance = UnityEngine.Object.Instantiate(prefab, context.Resolve(at, offset), prefab.transform.rotation, parent);
                instance.transform.localScale = prefab.transform.localScale * scale;
                if (faceCamera) context.FaceCamera(instance);
                if (lifetime > 0f) UnityEngine.Object.Destroy(instance, lifetime);
            }

            if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);
        }

        private Transform AnchorTransform(AttackStepContext context) => at switch
        {
            AttackAnchor.Target => context.Target,
            AttackAnchor.Caster => context.Caster,
            _ => context.EffectParent
        };
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
        [Tooltip("Apply the ability's damage when the projectile arrives.")]
        [SerializeField] private bool damageOnHit = true;
        [Tooltip("Optional effect spawned where the projectile lands.")]
        [SerializeField] private GameObject impactEffect;
        [SerializeField, Min(0f)] private float impactEffectLifetime = 1.5f;

        public override bool DealsDamage => damageOnHit;

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
                    Vector3 position = Vector3.LerpUnclamped(start, end, t) + Vector3.up * (arcHeight * 4f * t * (1f - t));
                    projectile.transform.position = position;
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
            if (damageOnHit) context.ApplyDamage?.Invoke();
        }
    }

    /// <summary>The moment the hit lands: applies the damage (enemy flash, damage number) without any visual of its own.</summary>
    [Serializable]
    public sealed class ImpactStep : AttackStep
    {
        public override bool DealsDamage => true;

        public override IEnumerator Run(AttackStepContext context)
        {
            context.ApplyDamage?.Invoke();
            yield break;
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
            if (context.Camera == null) yield break;
            Tween.ShakeLocalPosition(context.Camera.transform, Vector3.one * strength, seconds);
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
