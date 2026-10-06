using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Lengths the timeline cannot read from the step itself: animator states. The Attack Sequence Editor fills
    /// <see cref="StateSeconds"/> from the character's Animator Controller; without it a clip with the state's name is
    /// looked up on <see cref="Animator"/>, then <see cref="FallbackStateSeconds"/> is used.
    /// </summary>
    public sealed class AttackTimingContext
    {
        /// <summary>Seconds of an animator state (layer 0), or 0 / negative when unknown.</summary>
        public Func<string, float> StateSeconds;
        /// <summary>The character's controller (its clips are matched by name when <see cref="StateSeconds"/> is not set).</summary>
        public RuntimeAnimatorController Animator;
        public float FallbackStateSeconds = 0.5f;
        /// <summary>Distance used to time projectiles that fly by Speed (character to enemy).</summary>
        public float ThrowDistance = 4f;

        public static readonly AttackTimingContext Default = new();

        /// <summary>Seconds of the state, and whether that length is known (false = the fallback was used).</summary>
        public float Resolve(string state, out bool known)
        {
            known = false;
            if (string.IsNullOrWhiteSpace(state)) return FallbackStateSeconds;
            float seconds = StateSeconds != null ? StateSeconds(state) : 0f;
            if (seconds <= 0f && Animator != null)
                foreach (AnimationClip clip in Animator.animationClips)
                    if (clip != null && string.Equals(clip.name, state, StringComparison.OrdinalIgnoreCase))
                    {
                        seconds = clip.length;
                        break;
                    }
            known = seconds > 0f;
            return known ? seconds : FallbackStateSeconds;
        }

        public float Resolve(string state) => Resolve(state, out _);
    }

    /// <summary>A hit a step lands, for the timeline: seconds from the step's start (after its delay) and its weight.</summary>
    public sealed class AttackHitMark
    {
        public float Time;
        public float Weight;
        /// <summary>Moves the hit to a new time from the step's start (Timed Hits). Null = it cannot be dragged.</summary>
        public Action<float> Move;
        public string Label = string.Empty;
    }

    /// <summary>One step placed on the timeline.</summary>
    public sealed class AttackTimelineEntry
    {
        public int Index;
        public AttackStep Step;
        /// <summary>When the step is reached (its delay starts here).</summary>
        public float DelayStart;
        /// <summary>When it actually starts (after its delay).</summary>
        public float Start;
        /// <summary>Seconds it runs (what Wait For Completion waits for).</summary>
        public float Duration;
        /// <summary>Seconds something of it is still visible (an animation or effect that outlives the step).</summary>
        public float VisualDuration;
        /// <summary>The step's length comes from an animator state the timeline could not read (a guess).</summary>
        public bool LengthIsGuess;
        public bool Background;
        public readonly List<AttackHitMark> Hits = new();

        public float End => Start + Duration;
        public float VisualEnd => Start + Mathf.Max(Duration, VisualDuration);
    }

    /// <summary>
    /// Where each step of a <see cref="CharacterAttackSequence"/> happens in time, exactly as
    /// <see cref="CharacterAttackPerformer"/> runs them: walk in, then each step after its delay (the next one waits for it
    /// unless Wait For Completion is off), then walk back once every step is done.
    /// </summary>
    public sealed class AttackTimelineLayout
    {
        public readonly List<AttackTimelineEntry> Entries = new();
        /// <summary>Walk to the stage (0 when the sequence does not move).</summary>
        public float MoveIn;
        /// <summary>When the last step is done (the walk back starts).</summary>
        public float StepsEnd;
        public float MoveBack;
        public float Total => StepsEnd + MoveBack;
        public float TotalHitWeight;

        public static AttackTimelineLayout Build(CharacterAttackSequence sequence, AttackTimingContext timing = null)
        {
            timing ??= AttackTimingContext.Default;
            var layout = new AttackTimelineLayout();
            if (sequence == null) return layout;
            layout.MoveIn = sequence.MoveToStage ? Mathf.Max(0f, sequence.MoveInSeconds) : 0f;
            layout.MoveBack = sequence.MoveToStage ? Mathf.Max(0f, sequence.MoveBackSeconds) : 0f;
            float cursor = layout.MoveIn;
            float lastEnd = cursor;
            IReadOnlyList<AttackStep> steps = sequence.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                AttackStep step = steps[i];
                if (step == null) continue;
                var entry = new AttackTimelineEntry
                {
                    Index = i,
                    Step = step,
                    DelayStart = cursor,
                    Start = cursor + step.Delay,
                    Background = !step.WaitForCompletion
                };
                entry.Duration = Mathf.Max(0f, step.EstimateSeconds(timing, out entry.LengthIsGuess));
                entry.VisualDuration = Mathf.Max(entry.Duration, step.VisualSeconds(timing));
                step.CollectHits(timing, entry.Hits);
                foreach (AttackHitMark hit in entry.Hits) layout.TotalHitWeight += Mathf.Max(0f, hit.Weight);
                layout.Entries.Add(entry);
                lastEnd = Mathf.Max(lastEnd, entry.End);
                if (step.WaitForCompletion) cursor = entry.End;
            }
            layout.StepsEnd = Mathf.Max(cursor, lastEnd);
            return layout;
        }

        /// <summary>Drag a step's bar: it starts at <paramref name="start"/> (its delay changes; nothing before it moves).</summary>
        public static void MoveStart(AttackTimelineEntry entry, float start)
        {
            if (entry?.Step == null) return;
            entry.Step.SetDelay(Mathf.Max(0f, start - entry.DelayStart));
        }

        /// <summary>Drag a step's end: it lasts until <paramref name="end"/> (when the step's length can be set).</summary>
        public static bool MoveEnd(AttackTimelineEntry entry, float end, AttackTimingContext timing = null)
        {
            if (entry?.Step == null || !entry.Step.CanSetLength) return false;
            entry.Step.SetLength(Mathf.Max(0f, end - entry.Start), timing ?? AttackTimingContext.Default);
            return true;
        }
    }
}
