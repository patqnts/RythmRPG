using System;
using System.Collections.Generic;
using PrimeTween;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>What a <see cref="CombatNote"/> does: how it moves, is judged and resolves. Its looks are its views.</summary>
    public enum NoteKind
    {
        /// <summary>Travels down its lane; press it on the hit line.</summary>
        Tap,
        /// <summary>Travels down its lane; press on the line and hold until its end.</summary>
        Hold,
        /// <summary>Appears at the lane and charges up; press on the beat (early and late presses are graded).</summary>
        Stationary,
        /// <summary>Stationary, then hold until its end.</summary>
        StationaryHold,
        /// <summary>Travels down its lane; press its key repeatedly to destroy it before it lands.</summary>
        Mash,
        /// <summary>Travels down its lane like Tap; in a Ping-Pong sequence a hit sends it back to the enemy.</summary>
        Pong
    }

    /// <summary>Moments of a note's life that views react to.</summary>
    public enum NoteMoment
    {
        None,
        /// <summary>The note was spawned and placed (its travel or charge starts).</summary>
        Spawned,
        /// <summary>Chart time reached the note's hit time (the beat), whether or not it was pressed.</summary>
        ReachedBeat,
        /// <summary>A press was accepted (every press of a Mash note).</summary>
        Pressed,
        /// <summary>A hold note is now being held.</summary>
        HoldStarted,
        /// <summary>A hold was held to its end.</summary>
        HoldCompleted,
        /// <summary>A hold was let go too early.</summary>
        HoldReleased,
        /// <summary>Resolved by the player: Perfect, Good or Bad.</summary>
        Hit,
        /// <summary>Resolved as a Miss.</summary>
        Missed,
        /// <summary>Removed by an effect (zap, wall) or when the pattern was cleared.</summary>
        Cleared,
        /// <summary>Any end (after Hit, Missed or Cleared).</summary>
        Resolved,
        /// <summary>Chart time reached the end of a hold (the beat for notes without a hold), held or not.</summary>
        HoldEnd
    }

    public enum NoteOutcome
    {
        None,
        Hit,
        Miss,
        Cleared
    }

    /// <summary>Points views attach to, aim at, move to or draw between.</summary>
    public enum NoteAnchor
    {
        /// <summary>The note itself (follows it while it travels).</summary>
        Note,
        /// <summary>The lane's point on the hit line (its key marker).</summary>
        HitPoint,
        /// <summary>Who attacks with this note: the enemy for enemy attacks, the player for ability charts.</summary>
        Source,
        /// <summary>Who is attacked: the player for enemy attacks, the enemy for ability charts.</summary>
        Target,
        /// <summary>Where the note spawned.</summary>
        SpawnPoint,
        /// <summary>What a Travel view flies (a thrown sword...); the note itself when there is none.</summary>
        Projectile
    }

    /// <summary>
    /// A named point on a combatant (Mouth, Hand, Weapon...) that note views can use: put it on a child of the enemy or
    /// player at the right spot. Views name the socket; when it is missing they fall back to a child with that name, then
    /// to the combatant itself.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Combat/Combat Socket")]
    public sealed class CombatSocket : MonoBehaviour
    {
        [SerializeField] private string id = "Mouth";

        public string Id { get => id; set => id = value ?? string.Empty; }

        /// <summary>The socket <paramref name="socketId"/> under <paramref name="root"/>, a child with that name, or null.</summary>
        public static Transform Find(Transform root, string socketId)
        {
            if (root == null || string.IsNullOrWhiteSpace(socketId)) return null;
            foreach (CombatSocket socket in root.GetComponentsInChildren<CombatSocket>(true))
                if (socket != null && string.Equals(socket.id, socketId, StringComparison.OrdinalIgnoreCase)) return socket.transform;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child != null && child != root && string.Equals(child.name, socketId, StringComparison.OrdinalIgnoreCase)) return child;
            return null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.08f);
        }
    }

    /// <summary>
    /// Implement on any MonoBehaviour on a note prefab (or on an effect an Effect View spawns) to get the note's moments
    /// and a per-frame tick, with every anchor. This is the way to plug in custom visuals (a chain script, a beam asset...).
    /// </summary>
    public interface INoteViewListener
    {
        void OnNoteMoment(NoteViewContext context, NoteMoment moment);
        void OnNoteTick(NoteViewContext context);
    }

    /// <summary>
    /// One piece of a note's look. Add views to a <see cref="CombatNote"/> (they stack): each reacts to the note's moments
    /// and ticks every frame. Views never change timing or judgement.
    /// </summary>
    [Serializable]
    public abstract class NoteView
    {
        [Tooltip("Off = this view does nothing (handy while trying looks).")]
        [SerializeField] private bool enabled = true;

        public bool Enabled { get => enabled; set => enabled = value; }

        /// <summary>Seconds the note must stay after it resolves so this view can finish (retract, fade...).</summary>
        public virtual float Linger => 0f;

        /// <summary>The note was spawned (before the Spawned moment).</summary>
        public virtual void Begin(NoteViewContext context) { }
        public virtual void Moment(NoteViewContext context, NoteMoment moment) { }
        public virtual void Tick(NoteViewContext context) { }
        /// <summary>The note is being destroyed: clean up anything left in the scene.</summary>
        public virtual void End(NoteViewContext context) { }

        /// <summary>Editor checks (Note Designer): problems with this view's setup, or none.</summary>
        public virtual IEnumerable<string> Validate(CombatNote note) { yield break; }

        /// <summary>Name shown in the Note Designer (the class name without "View").</summary>
        public virtual string Title
        {
            get
            {
                string name = GetType().Name;
                return name.EndsWith("View", StringComparison.Ordinal) ? name.Substring(0, name.Length - 4) : name;
            }
        }

        /// <summary>
        /// What this view does when, for the Note Designer's timeline: one item per timed action. Items whose cues are
        /// editable can be dragged there (the cue objects are this view's own fields).
        /// </summary>
        public virtual void DescribeTimeline(List<NoteTimelineItem> items) { }
    }

    /// <summary>A cue and what to play on it.</summary>
    [Serializable]
    public sealed class MomentState
    {
        [Tooltip("When: a moment of the note plus seconds (negative = before it, for Reached Beat and Hold End).")]
        public NoteCue at = new(NoteMoment.Hit);
        [Tooltip("Animator state to play (layer 0).")]
        public string state = string.Empty;
        [Tooltip("Or a trigger to set (used when State is empty).")]
        public string trigger = string.Empty;
        [Min(0f)] public float crossFade = 0.05f;

        public MomentState() { }

        public MomentState(NoteMoment moment, string state, string trigger = "", float offset = 0f)
        {
            at = new NoteCue(moment, offset);
            this.state = state ?? string.Empty;
            this.trigger = trigger ?? string.Empty;
        }
    }

    /// <summary>Kind helpers shared by the runtime and the editor tools.</summary>
    public static class NoteKinds
    {
        /// <summary>The chart note type a kind is authored as (hold length, mash presses and windows come from it).</summary>
        public static RhythmNoteType ChartType(NoteKind kind) => kind switch
        {
            NoteKind.Hold => RhythmNoteType.Hold,
            NoteKind.Stationary => RhythmNoteType.Laser,
            NoteKind.StationaryHold => RhythmNoteType.HoldLaser,
            NoteKind.Mash => RhythmNoteType.Mash,
            NoteKind.Pong => RhythmNoteType.Pong,
            _ => RhythmNoteType.Normal
        };

        public static bool IsHold(NoteKind kind) => kind == NoteKind.Hold || kind == NoteKind.StationaryHold;
        public static bool IsStationary(NoteKind kind) => kind == NoteKind.Stationary || kind == NoteKind.StationaryHold;
        public static bool Moves(NoteKind kind) => !IsStationary(kind);
        /// <summary>Board effects (zaps, walls) may clear it: plain taps and stationary notes only.</summary>
        public static bool ClearableByEffects(NoteKind kind) => kind == NoteKind.Tap || kind == NoteKind.Stationary;

        /// <summary>Hit / Miss / Cleared for a resolution.</summary>
        public static NoteOutcome Outcome(RhythmJudgementResult result)
        {
            if (result.Source == NoteResolutionSource.Modifier || result.Source == NoteResolutionSource.SystemClear) return NoteOutcome.Cleared;
            return result.Judgement == HitJudgement.Miss ? NoteOutcome.Miss : NoteOutcome.Hit;
        }

        public static NoteMoment OutcomeMoment(NoteOutcome outcome) => outcome switch
        {
            NoteOutcome.Hit => NoteMoment.Hit,
            NoteOutcome.Miss => NoteMoment.Missed,
            NoteOutcome.Cleared => NoteMoment.Cleared,
            _ => NoteMoment.None
        };
    }

    /// <summary>
    /// A deflectable Ping-Pong shot (Ping-Pong sequence attack): hit, it flies back to the enemy and can be fired
    /// again as the next volley. <see cref="PongNote"/> and Pong-kind <see cref="CombatNote"/>s are rally shots.
    /// </summary>
    public interface IRallyShot
    {
        Note Note { get; }
        bool IsSequenceShot { get; set; }
        float ReturnSeconds { get; set; }
        Vector3 ReturnDestination { get; set; }
        bool IsWaitingForNextVolley { get; }
        void BeginDeflect();
        void KeepForNextVolley();
        void Rearm(RhythmNoteSpawnContext context, Vector3 spawnPosition);
    }

    /// <summary>
    /// Who moves a combatant right now (Actor Move View). The newest note takes over; the combatant's home is kept from the
    /// first claim and restored when the last owner lets go.
    /// </summary>
    public static class ActorLock
    {
        private sealed class Claim
        {
            public object Owner;
            public Vector3 Home;
        }

        private static readonly Dictionary<Transform, Claim> claims = new();

        /// <summary>Claims <paramref name="actor"/> for <paramref name="owner"/>; returns its home position.</summary>
        public static Vector3 Acquire(Transform actor, object owner)
        {
            if (actor == null) return default;
            if (!claims.TryGetValue(actor, out Claim claim))
            {
                claim = new Claim { Home = actor.position };
                claims[actor] = claim;
            }
            claim.Owner = owner;
            return claim.Home;
        }

        public static bool Owns(Transform actor, object owner) =>
            actor != null && claims.TryGetValue(actor, out Claim claim) && ReferenceEquals(claim.Owner, owner);

        public static bool TryGetHome(Transform actor, out Vector3 home)
        {
            home = default;
            if (actor == null || !claims.TryGetValue(actor, out Claim claim)) return false;
            home = claim.Home;
            return true;
        }

        /// <summary>Lets go; true when <paramref name="owner"/> was the current owner (so it should send the actor home).</summary>
        public static bool Release(Transform actor, object owner)
        {
            if (actor == null || !claims.TryGetValue(actor, out Claim claim) || !ReferenceEquals(claim.Owner, owner)) return false;
            claims.Remove(actor);
            return true;
        }

        public static void Clear()
        {
            claims.Clear();
            ActorVisibility.Clear();
        }

        /// <summary>Puts every claimed combatant back home at once (the battle ended or was cancelled mid-move).</summary>
        public static void RestoreAll()
        {
            foreach (KeyValuePair<Transform, Claim> pair in claims)
            {
                if (pair.Key == null) continue;
                Tween.StopAll(pair.Key);
                pair.Key.position = pair.Value.Home;
            }
            claims.Clear();
            ActorVisibility.RestoreAll(); // and show anyone a note made vanish
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => claims.Clear();
    }
}
