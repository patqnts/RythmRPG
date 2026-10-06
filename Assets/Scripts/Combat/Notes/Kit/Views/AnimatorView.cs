using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Plays animator states (or sets triggers) on the note's moments. The animator can be the note's own sprite, or the
    /// attacker's / defender's: a melee enemy can be the note's whole visual (wind up while it approaches, strike on the
    /// beat, recoil when blocked). The approach can scrub a state so its end lands exactly on the beat.
    /// </summary>
    [Serializable]
    public sealed class AnimatorView : NoteView
    {
        public enum Who
        {
            /// <summary>An animator on the note (its sprite).</summary>
            Note,
            /// <summary>The attacker: the enemy for enemy attacks (melee enemies), the player for ability charts.</summary>
            Source,
            /// <summary>The one being attacked.</summary>
            Target
        }

        [Tooltip("Whose animator plays: the note's own, the attacker's (e.g. a melee enemy striking) or the defender's.")]
        [SerializeField] private Who animate = Who.Note;
        [Tooltip("Note only: this animator. Empty = the first Animator on the note.")]
        [SerializeField] private Animator noteAnimator;
        [Tooltip("What to play when (state, or a trigger when the state is empty). Each entry has its own cue: a moment plus seconds.")]
        [SerializeField] private List<MomentState> states = new()
        {
            new MomentState(NoteMoment.Spawned, "Approach"),
            new MomentState(NoteMoment.Hit, "Hit"),
            new MomentState(NoteMoment.Missed, "Miss")
        };

        [Header("Beat sync")]
        [Tooltip("This state is scrubbed by the approach (0 at spawn, 1 on the beat), so e.g. a wind-up ends exactly on the beat. Empty = off.")]
        [SerializeField] private string approachState = string.Empty;
        [Tooltip("Float parameter set to the approach (0-1) every frame. Empty = off.")]
        [SerializeField] private string approachParameter = string.Empty;
        [Tooltip("Float parameter set to the hold progress (0-1) every frame. Empty = off.")]
        [SerializeField] private string holdParameter = string.Empty;

        [Header("After (attacker / defender only)")]
        [Tooltip("State played after the note, to go back to idle. Empty = none (the animator's own transitions).")]
        [SerializeField] private string afterState = string.Empty;
        [SerializeField] private NoteCue afterAt = new(NoteMoment.Resolved, 0.3f);

        [NonSerialized] private Animator animator;
        [NonSerialized] private bool scrubbing;
        [NonSerialized] private int scrubHash;
        [NonSerialized] private bool afterPlayed;
        [NonSerialized] private HashSet<string> parameters;

        public Who Animate { get => animate; set => animate = value; }
        public Animator NoteAnimator { get => noteAnimator; set => noteAnimator = value; }
        public List<MomentState> States => states ??= new List<MomentState>();
        public string ApproachState { get => approachState; set => approachState = value ?? string.Empty; }
        public string AfterState { get => afterState; set => afterState = value ?? string.Empty; }

        public NoteCue AfterAt { get => afterAt ??= new NoteCue(NoteMoment.Resolved, 0.3f); set => afterAt = value; }
        private bool UsesAfter => animate != Who.Note && !string.IsNullOrWhiteSpace(afterState);

        public override void Begin(NoteViewContext context)
        {
            animator = animate switch
            {
                Who.Note => noteAnimator != null ? noteAnimator : context.Note.GetComponentInChildren<Animator>(true),
                Who.Source => context.AnimatorOf(NoteAnchor.Source),
                _ => context.AnimatorOf(NoteAnchor.Target)
            };
            parameters = null;
            afterPlayed = false;
            scrubbing = false;
            if (animator != null && !string.IsNullOrWhiteSpace(approachState))
            {
                scrubHash = Animator.StringToHash(approachState);
                scrubbing = animator.HasState(0, scrubHash);
            }
            if (animator == null) return;
            foreach (MomentState entry in States)
            {
                if (entry == null) continue;
                MomentState play = entry;
                context.Cue(play.at, () => Play(play));
            }
            if (UsesAfter) context.Cue(AfterAt, PlayAfter);
        }

        public override void Moment(NoteViewContext context, NoteMoment moment)
        {
            if (animator == null) return;
            if (moment == NoteMoment.Pressed || moment == NoteMoment.Resolved || moment == NoteMoment.HoldStarted) scrubbing = false;
        }

        public override void Tick(NoteViewContext context)
        {
            if (animator == null) return;
            if (scrubbing && !context.Resolved)
            {
                float approach = context.Approach;
                animator.Play(scrubHash, 0, Mathf.Min(approach, 0.999f));
                if (approach >= 1f) scrubbing = false;
            }
            SetFloat(approachParameter, context.Approach);
            SetFloat(holdParameter, context.HoldProgress);
        }

        // The attacker / defender must not be left mid-strike when the note goes away early.
        public override void End(NoteViewContext context)
        {
            if (UsesAfter && !afterPlayed) PlayAfter();
        }

        private void PlayAfter()
        {
            if (afterPlayed) return;
            afterPlayed = true;
            if (animator == null) return;
            int hash = Animator.StringToHash(afterState);
            if (animator.HasState(0, hash)) animator.CrossFadeInFixedTime(hash, 0.1f, 0, 0f);
        }

        private void Play(MomentState entry)
        {
            if (animator == null) return;
            if (!string.IsNullOrWhiteSpace(entry.state))
            {
                int hash = Animator.StringToHash(entry.state);
                if (animator.HasState(0, hash))
                {
                    if (entry.crossFade > 0f) animator.CrossFadeInFixedTime(hash, entry.crossFade, 0, 0f);
                    else animator.Play(hash, 0, 0f);
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(entry.trigger) && HasParameter(entry.trigger)) animator.SetTrigger(entry.trigger);
        }

        private void SetFloat(string parameter, float value)
        {
            if (string.IsNullOrWhiteSpace(parameter) || !HasParameter(parameter)) return;
            animator.SetFloat(parameter, value);
        }

        private bool HasParameter(string parameter)
        {
            if (parameters == null)
            {
                parameters = new HashSet<string>();
                foreach (AnimatorControllerParameter p in animator.parameters) parameters.Add(p.name);
            }
            return parameters.Contains(parameter);
        }

        public override string Title => animate == Who.Note ? "Animator" : $"Animator ({animate})";

        public override IEnumerable<string> Validate(CombatNote note)
        {
            if (animate == Who.Note && noteAnimator == null && note.GetComponentInChildren<Animator>(true) == null)
                yield return "Animator View: the note has no Animator.";
            foreach (MomentState entry in States)
                if (entry != null && string.IsNullOrWhiteSpace(entry.state) && string.IsNullOrWhiteSpace(entry.trigger))
                    yield return "Animator View: an entry has neither a state nor a trigger.";
        }

        public override void DescribeTimeline(List<NoteTimelineItem> items)
        {
            if (!string.IsNullOrWhiteSpace(approachState))
                items.Add(NoteTimelineItem.Info("scrub " + approachState, NoteMoment.Spawned, NoteMoment.ReachedBeat, NoteTimelineColors.Animation));
            foreach (MomentState entry in States)
            {
                if (entry == null) continue;
                string label = !string.IsNullOrWhiteSpace(entry.state) ? entry.state : "trigger " + entry.trigger;
                NoteTimelineItem item = NoteTimelineItem.Marker(label, entry.at, NoteTimelineColors.Animation);
                item.AnimatorState = entry.state;
                item.AnimatorOwner = animate;
                items.Add(item);
            }
            if (UsesAfter)
            {
                NoteTimelineItem after = NoteTimelineItem.Marker(afterState + " (after)", AfterAt, NoteTimelineColors.Animation);
                after.AnimatorState = afterState;
                after.AnimatorOwner = animate;
                items.Add(after);
            }
        }
    }
}
