using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Makes the attacker (or defender) vanish and come back during a note: it disintegrates after throwing its weapon and
    /// reappears where it warps to, a ninja fading into smoke... Fade, a dissolve shader's float, or simply hiding, plus
    /// optional pixel flakes and effects. It always shows the combatant again when the note goes.
    /// For an animation instead (a "Disintegrate" state), use an Animator View on Source; for your own shader / script,
    /// use Material Float or a script implementing <see cref="INoteViewListener"/>.
    /// </summary>
    [Serializable]
    public sealed class ActorVanishView : NoteView
    {
        public enum Who { Source, Target }

        [SerializeField] private Who actor = Who.Source;
        [SerializeField] private VanishStyle style = VanishStyle.Fade;
        [Tooltip("Material Float: the shader property (e.g. _Dissolve).")]
        [SerializeField] private string property = "_Dissolve";
        [Tooltip("Material Float: the value when fully there.")]
        [SerializeField] private float visibleValue;
        [Tooltip("Material Float: the value when gone.")]
        [SerializeField] private float goneValue = 1f;

        [Header("Timing")]
        [SerializeField] private NoteCue vanishAt = new(NoteMoment.Spawned, 0.3f);
        [SerializeField, Min(0f)] private float vanishSeconds = 0.25f;
        [Tooltip("When it comes back. None = when the note goes.")]
        [SerializeField] private NoteCue appearAt = new(NoteMoment.ReachedBeat, -0.1f);
        [SerializeField, Min(0f)] private float appearSeconds;

        [Header("Flakes (built-in pixel bits)")]
        [SerializeField, Min(0)] private int vanishFlakes = 18;
        [SerializeField, Min(0)] private int appearFlakes = 8;
        [SerializeField] private Color flakeColor = new(0.7f, 0.9f, 1f, 1f);
        [Tooltip("Flake size in world units (1 / 32 = one pixel at 32 PPU).")]
        [SerializeField, Min(0.005f)] private float flakeSize = 2f / 32f;
        [Tooltip("How far flakes drift up (world units) over their life.")]
        [SerializeField] private float flakeRise = 0.8f;
        [SerializeField, Min(0.05f)] private float flakeLifetime = 0.6f;

        [Header("Effects")]
        [Tooltip("Spawned at the actor when it vanishes (smoke, sparks...).")]
        [SerializeField] private GameObject vanishEffect;
        [Tooltip("Spawned at the actor when it reappears.")]
        [SerializeField] private GameObject appearEffect;

        [NonSerialized] private Transform who;
        [NonSerialized] private float vanishStart = -1f;
        [NonSerialized] private float appearStart = -1f;
        [NonSerialized] private bool claimed;
        [NonSerialized] private bool done;
        [NonSerialized] private int appearBurstFrame = -1;

        public Who Actor { get => actor; set => actor = value; }
        public VanishStyle Style { get => style; set => style = value; }
        public NoteCue VanishAt { get => vanishAt ??= new NoteCue(NoteMoment.Spawned, 0.3f); set => vanishAt = value; }
        public float VanishSeconds { get => vanishSeconds; set => vanishSeconds = Mathf.Max(0f, value); }
        public NoteCue AppearAt { get => appearAt ??= new NoteCue(NoteMoment.None); set => appearAt = value; }
        public float AppearSeconds { get => appearSeconds; set => appearSeconds = Mathf.Max(0f, value); }
        public int VanishFlakes { get => vanishFlakes; set => vanishFlakes = Mathf.Max(0, value); }
        public int AppearFlakes { get => appearFlakes; set => appearFlakes = Mathf.Max(0, value); }
        public Color FlakeColor { get => flakeColor; set => flakeColor = value; }

        public override void Begin(NoteViewContext context)
        {
            who = actor == Who.Source ? context.Source : context.Target;
            vanishStart = -1f;
            appearStart = -1f;
            claimed = false;
            done = false;
            appearBurstFrame = -1;
            if (who == null) return;
            context.Cue(VanishAt, () => StartVanish(context));
            if (AppearAt.moment != NoteMoment.None) context.Cue(AppearAt, () => StartAppear(context));
        }

        public override void Tick(NoteViewContext context)
        {
            if (!claimed || done || who == null) return;
            // The reappear burst waits a frame: a teleport on the same cue (Actor Move View) moves the actor after this.
            if (appearBurstFrame >= 0 && Time.frameCount > appearBurstFrame)
            {
                appearBurstFrame = -1;
                Bounds bounds = ActorVisibility.BoundsOf(who);
                Burst(context, bounds, appearFlakes, true);
                SpawnEffect(context, appearEffect, bounds.center);
            }
            if (!ActorVisibility.Owns(who, this))
            {
                done = true; // a newer note took the actor over
                return;
            }
            float visibility = Visibility(Time.time);
            ActorVisibility.Apply(who, this, visibility, style, property, visibleValue, goneValue);
            if (appearStart >= 0f && visibility >= 1f && appearBurstFrame < 0) Finish();
        }

        public override void End(NoteViewContext context) => Finish();

        /// <summary>1 = there, 0 = gone, from when the vanish and the return started.</summary>
        private float Visibility(float now)
        {
            if (appearStart >= 0f) return appearSeconds <= 0f ? 1f : Mathf.Clamp01((now - appearStart) / appearSeconds);
            if (vanishStart < 0f) return 1f;
            return vanishSeconds <= 0f ? 0f : 1f - Mathf.Clamp01((now - vanishStart) / vanishSeconds);
        }

        private void StartVanish(NoteViewContext context)
        {
            if (who == null || done) return;
            ActorVisibility.Acquire(who, this);
            claimed = true;
            vanishStart = Time.time;
            Bounds bounds = ActorVisibility.BoundsOf(who);
            Burst(context, bounds, vanishFlakes, false);
            SpawnEffect(context, vanishEffect, bounds.center);
        }

        private void StartAppear(NoteViewContext context)
        {
            if (!claimed || done || who == null) return;
            // Starts from however far the vanish got (an early return fades in from there).
            appearStart = Time.time - Visibility(Time.time) * appearSeconds;
            appearBurstFrame = Time.frameCount;
        }

        private void Finish()
        {
            if (done) return;
            done = true;
            if (claimed && who != null) ActorVisibility.Release(who, this);
        }

        private void SpawnEffect(NoteViewContext context, GameObject prefab, Vector3 position)
        {
            if (prefab == null) return;
            GameObject instance = Object.Instantiate(prefab, position, prefab.transform.rotation, context.EffectParent);
            context.FaceCamera(instance);
            Object.Destroy(instance, 4f);
        }

        private void Burst(NoteViewContext context, Bounds bounds, int count, bool gather)
        {
            if (count <= 0) return;
            SpriteRenderer reference = who.GetComponentInChildren<SpriteRenderer>();
            NoteFlakes.Spawn(bounds, count, flakeColor, flakeSize, flakeRise, flakeLifetime, gather, context.Camera, reference, context.EffectParent);
        }

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (style == VanishStyle.MaterialFloat && string.IsNullOrWhiteSpace(property))
                yield return "Actor Vanish View: Material Float needs the shader property's name.";
            if (VanishAt.moment == NoteMoment.None) yield return "Actor Vanish View: Vanish At is None, so it never vanishes.";
        }

        public override string Title => "Vanish " + actor;

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            items.Add(NoteTimelineItem.Timed("vanish", VanishAt, vanishSeconds, value => VanishSeconds = value, NoteTimelineColors.Move));
            if (AppearAt.moment != NoteMoment.None)
                items.Add(NoteTimelineItem.Timed("appear", AppearAt, appearSeconds, value => AppearSeconds = value, NoteTimelineColors.Move));
        }
    }
}
