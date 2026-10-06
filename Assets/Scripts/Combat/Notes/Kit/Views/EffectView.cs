using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat
{
    /// <summary>A beam / line script that can be pointed from a start to a target (implement it on custom beam scripts).</summary>
    public interface INoteBeam
    {
        void SetBeam(Vector3 start, Transform target);
    }

    /// <summary>
    /// Points beams at a target: <see cref="INoteBeam"/> components, and third-party "MagicBeamStatic" scripts (Magic
    /// Arsenal: SetBeamPositionAndTarget / SetBeamTarget, found by name so the package is not a dependency).
    /// </summary>
    public static class NoteBeams
    {
        private static readonly Dictionary<Type, (MethodInfo both, MethodInfo target)> magicBeamMethods = new();

        /// <returns>How many beams were aimed.</returns>
        public static int Aim(GameObject root, Vector3 start, Transform target)
        {
            if (root == null || target == null) return 0;
            int count = 0;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour is INoteBeam beam)
                {
                    beam.SetBeam(start, target);
                    count++;
                    continue;
                }
                Type type = behaviour.GetType();
                if (type.Name != "MagicBeamStatic") continue;
                if (!magicBeamMethods.TryGetValue(type, out var methods))
                {
                    methods = (type.GetMethod("SetBeamPositionAndTarget", BindingFlags.Instance | BindingFlags.Public),
                        type.GetMethod("SetBeamTarget", BindingFlags.Instance | BindingFlags.Public));
                    magicBeamMethods[type] = methods;
                }
                if (methods.both != null) methods.both.Invoke(behaviour, new object[] { start, target });
                else
                {
                    behaviour.transform.position = start;
                    methods.target?.Invoke(behaviour, new object[] { target });
                }
                count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Spawns a prefab (particles, a beam, anything with a custom script) at an anchor on a moment, optionally following
    /// it or aiming at another anchor, and stops it on a later moment. Custom scripts on the prefab that implement
    /// <see cref="INoteViewListener"/> receive the note's moments and ticks too.
    /// </summary>
    [Serializable]
    public sealed class EffectView : NoteView
    {
        public enum Follow
        {
            /// <summary>Stays where it spawned.</summary>
            World,
            /// <summary>Moves with the anchor (or socket), e.g. a glow in the enemy's mouth.</summary>
            Anchor,
            /// <summary>A child of the note (travels with it).</summary>
            Note
        }

        public enum StopMode
        {
            /// <summary>Particles stop emitting and fade out, then it is removed.</summary>
            StopEmitting,
            /// <summary>Removed at once.</summary>
            Destroy,
            /// <summary>Left alone (use Lifetime).</summary>
            Keep
        }

        public enum AimAxis { Right, Up, Forward }

        [SerializeField] private GameObject prefab;
        [Tooltip("When it spawns: a moment plus seconds (e.g. Beat -0.3 s to start charging before the beat).")]
        [SerializeField] private NoteCue spawnAt = new(NoteMoment.Spawned);
        [SerializeField] private NoteAnchor at = NoteAnchor.Note;
        [Tooltip("Socket name on the Source / Target (Mouth, Hand...). Empty = the anchor itself.")]
        [SerializeField] private string socket = string.Empty;
        [Tooltip("Lane space: X side, Y up, Z forward toward the hit line.")]
        [SerializeField] private Vector3 offset;
        [SerializeField] private Follow follow = Follow.World;
        [SerializeField, Min(0.01f)] private float scale = 1f;
        [Tooltip("Turn camera-facing sprites in the effect toward the combat camera, like notes.")]
        [SerializeField] private bool faceCamera = true;

        [Header("Aim")]
        [Tooltip("Point the effect at another anchor (a beam from the mouth to the hit point...).")]
        [SerializeField] private bool aim;
        [SerializeField] private NoteAnchor aimAt = NoteAnchor.HitPoint;
        [SerializeField] private string aimSocket = string.Empty;
        [SerializeField] private AimAxis aimAxis = AimAxis.Right;
        [Tooltip("Beam scripts in the effect (MagicBeamStatic, INoteBeam) start at the effect and end at Aim At.")]
        [SerializeField] private bool driveBeams = true;
        [Tooltip("Keep following / aiming every frame (moving notes, moving enemies).")]
        [SerializeField] private bool keepUpdating = true;

        [Header("Stop")]
        [Tooltip("When it stops (see Stop). None = only Lifetime ends it.")]
        [SerializeField] private NoteCue stopAt = new(NoteMoment.Resolved);
        [SerializeField] private StopMode stop = StopMode.StopEmitting;
        [Tooltip("Seconds particles get to fade after they stop emitting.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 1f;
        [Tooltip("Removed after this many seconds anyway (0 = only when stopped).")]
        [SerializeField, Min(0f)] private float lifetime;

        [NonSerialized] private List<GameObject> instances;

        public GameObject Prefab { get => prefab; set => prefab = value; }
        public NoteCue SpawnAt { get => spawnAt ??= new NoteCue(NoteMoment.Spawned); set => spawnAt = value; }
        public NoteAnchor At { get => at; set => at = value; }
        public string Socket { get => socket; set => socket = value ?? string.Empty; }
        public Vector3 Offset { get => offset; set => offset = value; }
        public Follow FollowMode { get => follow; set => follow = value; }
        public bool AimEnabled { get => aim; set => aim = value; }
        public NoteAnchor AimAt { get => aimAt; set => aimAt = value; }
        public string AimSocket { get => aimSocket; set => aimSocket = value ?? string.Empty; }
        public NoteCue StopAt { get => stopAt ??= new NoteCue(NoteMoment.Resolved); set => stopAt = value; }
        public float Lifetime { get => lifetime; set => lifetime = Mathf.Max(0f, value); }
        public float FadeSeconds { get => fadeSeconds; set => fadeSeconds = Mathf.Max(0f, value); }
        public StopMode Stop { get => stop; set => stop = value; }

        public override float Linger => stop == StopMode.StopEmitting ? 0.05f : 0f;

        public override void Begin(NoteViewContext context)
        {
            instances ??= new List<GameObject>();
            instances.Clear();
            context.Cue(SpawnAt, () => Spawn(context, SpawnAt.moment));
            if (StopAt.moment != NoteMoment.None) context.Cue(StopAt, () => StopAll(context, stop, false));
        }

        public override void Tick(NoteViewContext context)
        {
            if (!keepUpdating || instances == null) return;
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                GameObject instance = instances[i];
                if (instance == null)
                {
                    instances.RemoveAt(i);
                    continue;
                }
                if (follow == Follow.Anchor) instance.transform.position = context.Position(at, socket, offset);
                Point(context, instance);
            }
        }

        // The note is being destroyed: effects parented to it go with it (they cannot be re-parented now).
        public override void End(NoteViewContext context) =>
            StopAll(context, stop == StopMode.Keep ? StopMode.Keep : StopMode.StopEmitting, true);

        private void Spawn(NoteViewContext context, NoteMoment moment)
        {
            if (prefab == null) return;
            Vector3 position = context.Position(at, socket, offset);
            Transform parent = follow switch
            {
                Follow.Anchor => context.AnchorTransform(at, socket),
                Follow.Note => context.Note.transform,
                _ => null
            };
            GameObject instance = Object.Instantiate(prefab, position, prefab.transform.rotation, parent);
            instance.transform.localScale = prefab.transform.localScale * scale;
            if (faceCamera) context.FaceCamera(instance);
            if (lifetime > 0f) Object.Destroy(instance, lifetime);
            instances.Add(instance);
            Point(context, instance);
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is not INoteViewListener listener) continue;
                context.Note.AddListener(listener);
                listener.OnNoteMoment(context, moment);
            }
        }

        private void Point(NoteViewContext context, GameObject instance)
        {
            if (!aim && !driveBeams) return;
            Transform target = context.AnchorTransform(aimAt, aimSocket);
            Vector3 targetPosition = target != null ? target.position : context.Position(aimAt, aimSocket, Vector3.zero);
            if (aim)
            {
                Vector3 direction = targetPosition - instance.transform.position;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    switch (aimAxis)
                    {
                        case AimAxis.Up: instance.transform.up = direction; break;
                        case AimAxis.Forward: instance.transform.forward = direction; break;
                        default: instance.transform.right = direction; break;
                    }
                }
            }
            if (driveBeams && target != null) NoteBeams.Aim(instance, instance.transform.position, target);
        }

        private void StopAll(NoteViewContext context, StopMode mode, bool noteDying)
        {
            if (instances == null) return;
            Transform note = context.Note != null ? context.Note.transform : null;
            foreach (GameObject instance in instances)
            {
                if (instance == null) continue;
                if (noteDying && note != null && instance.transform.IsChildOf(note)) continue;
                foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour is INoteViewListener listener) context.Note?.RemoveListener(listener);
                switch (mode)
                {
                    case StopMode.Destroy:
                        Object.Destroy(instance);
                        break;
                    case StopMode.StopEmitting:
                        foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
                            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                        foreach (TrailRenderer trail in instance.GetComponentsInChildren<TrailRenderer>(true)) trail.emitting = false;
                        instance.transform.SetParent(null, true);
                        Object.Destroy(instance, fadeSeconds);
                        break;
                }
            }
            instances.Clear();
        }

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (prefab == null) yield return "Effect View: no prefab.";
            if (StopAt.moment == NoteMoment.None && lifetime <= 0f && stop != StopMode.Keep)
                yield return "Effect View: it never stops (set Stop At or a Lifetime).";
        }

        public override string Title => prefab != null ? "Effect: " + prefab.name : "Effect";

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            string label = prefab != null ? prefab.name : "effect";
            if (StopAt.moment != NoteMoment.None && stop != StopMode.Keep)
                items.Add(NoteTimelineItem.Span(label, SpawnAt, StopAt, NoteTimelineColors.Effect));
            else if (lifetime > 0f)
                items.Add(NoteTimelineItem.Timed(label, SpawnAt, lifetime, value => lifetime = Mathf.Max(0f, value), NoteTimelineColors.Effect));
            else
                items.Add(NoteTimelineItem.Marker(label, SpawnAt, NoteTimelineColors.Effect));
        }
    }

    /// <summary>
    /// Points beams that are part of the note prefab (a laser child with MagicBeamStatic, or a script implementing
    /// <see cref="INoteBeam"/>) from one anchor to another, e.g. from the note to the lane's hit point.
    /// </summary>
    [Serializable]
    public sealed class BeamView : NoteView
    {
        [SerializeField] private NoteAnchor from = NoteAnchor.Note;
        [SerializeField] private string fromSocket = string.Empty;
        [Tooltip("Lane space: X side, Y up, Z forward toward the hit line.")]
        [SerializeField] private Vector3 fromOffset;
        [SerializeField] private NoteAnchor to = NoteAnchor.HitPoint;
        [SerializeField] private string toSocket = string.Empty;
        [Tooltip("Re-aim every frame (moving enemies or lanes).")]
        [SerializeField] private bool everyFrame;

        public NoteAnchor From { get => from; set => from = value; }
        public NoteAnchor To { get => to; set => to = value; }

        public override void Moment(NoteViewContext context, NoteMoment moment)
        {
            if (moment == NoteMoment.Spawned) Aim(context);
        }

        public override void DescribeTimeline(List<NoteTimelineItem> items) =>
            items.Add(NoteTimelineItem.Info("aim " + from + " > " + to, NoteMoment.Spawned, NoteMoment.Resolved, NoteTimelineColors.Info));

        public override void Tick(NoteViewContext context)
        {
            if (everyFrame && !context.Resolved) Aim(context);
        }

        private void Aim(NoteViewContext context)
        {
            Transform target = context.AnchorTransform(to, toSocket);
            if (target == null) return;
            NoteBeams.Aim(context.Note.gameObject, context.Position(from, fromSocket, fromOffset), target);
        }
    }
}
