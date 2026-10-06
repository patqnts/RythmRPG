using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// When a view does something: a moment of the note plus an offset in seconds. Spawned, Reached Beat and Hold End
    /// are known in advance (chart times), so Reached Beat and Hold End can also be negative ("0.3 s before the beat").
    /// The other moments happen when the player acts, so their offset is a delay after them.
    /// </summary>
    [Serializable]
    public sealed class NoteCue
    {
        [Tooltip("The moment it waits for.")]
        public NoteMoment moment = NoteMoment.Spawned;
        [Tooltip("Seconds after the moment. Negative = before it (Reached Beat and Hold End only).")]
        public float offset;

        public NoteCue() { }

        public NoteCue(NoteMoment moment, float offset = 0f)
        {
            this.moment = moment;
            this.offset = offset;
        }

        public NoteCue Clone() => new(moment, offset);

        /// <summary>The moment's chart time is known when the note spawns (it can be planned ahead of it).</summary>
        public bool IsScheduled => IsScheduledMoment(moment);
        /// <summary>The offset actually used: clamped to 0 or more for moments that cannot be anticipated.</summary>
        public float EffectiveOffset => ClampOffset(moment, offset);

        public static bool IsScheduledMoment(NoteMoment moment) =>
            moment == NoteMoment.Spawned || moment == NoteMoment.ReachedBeat || moment == NoteMoment.HoldEnd;

        /// <summary>Only Reached Beat and Hold End may be anticipated (negative offset).</summary>
        public static float ClampOffset(NoteMoment moment, float offset) =>
            moment == NoteMoment.ReachedBeat || moment == NoteMoment.HoldEnd ? offset : Mathf.Max(0f, offset);

        public override string ToString()
        {
            string when = Moments.Label(moment);
            float o = EffectiveOffset;
            return Mathf.Abs(o) < 0.0005f ? when : $"{when} {(o > 0f ? "+" : "-")}{Mathf.Abs(o):0.###}s";
        }
    }

    /// <summary>Moment names and where they sit on a note's timeline.</summary>
    public static class Moments
    {
        public static string Label(NoteMoment moment) => moment switch
        {
            NoteMoment.ReachedBeat => "Beat",
            NoteMoment.HoldStarted => "Hold Start",
            NoteMoment.HoldCompleted => "Hold Done",
            NoteMoment.HoldReleased => "Let Go",
            NoteMoment.HoldEnd => "Hold End",
            _ => moment.ToString()
        };

        /// <summary>
        /// Where a moment happens on the timeline, in seconds from the beat, for a note that travels
        /// <paramref name="travel"/> seconds and holds <paramref name="hold"/> seconds (0 = no hold). Moments that
        /// depend on the player use their usual time: on the beat, or at the hold's end for held notes.
        /// </summary>
        public static float TimeOf(NoteMoment moment, float travel, float hold)
        {
            hold = Mathf.Max(0f, hold);
            return moment switch
            {
                NoteMoment.Spawned => -Mathf.Max(0f, travel),
                NoteMoment.HoldEnd => hold,
                NoteMoment.HoldCompleted => hold,
                NoteMoment.HoldReleased => hold * 0.5f,
                NoteMoment.Hit => hold,
                NoteMoment.Resolved => hold,
                _ => 0f
            };
        }

        /// <summary>The cue's time on the timeline (seconds from the beat).</summary>
        public static float TimeOf(NoteCue cue, float travel, float hold) =>
            cue == null ? 0f : TimeOf(cue.moment, travel, hold) + cue.EffectiveOffset;

        /// <summary>Moves a cue so it lands at <paramref name="time"/> (seconds from the beat), keeping its moment.</summary>
        public static void SetTime(NoteCue cue, float time, float travel, float hold)
        {
            if (cue == null) return;
            cue.offset = NoteCue.ClampOffset(cue.moment, time - TimeOf(cue.moment, travel, hold));
        }
    }

    /// <summary>
    /// Fires view actions on their cues. Scheduled cues (Spawned, Reached Beat, Hold End) fire when chart time passes
    /// their moment + offset; the others fire offset seconds (real time) after their moment happens. Repeating moments
    /// (Pressed on a Mash note) fire their cues each time.
    /// </summary>
    public sealed class NoteCueScheduler
    {
        private sealed class Entry
        {
            public NoteCue Cue;
            public Action Fire;
            public bool Done;
        }

        private readonly List<Entry> entries = new();
        private readonly List<(float at, Action fire)> pending = new();
        private readonly List<Action> firing = new();

        public int Count => entries.Count;

        public void Clear()
        {
            entries.Clear();
            pending.Clear();
        }

        public void Add(NoteCue cue, Action fire)
        {
            if (cue == null || fire == null || cue.moment == NoteMoment.None) return;
            entries.Add(new Entry { Cue = cue, Fire = fire });
        }

        /// <summary>
        /// Fires scheduled cues whose time has come and delayed cues that are due. <paramref name="endTime"/> is the
        /// hold's end (the hit time for notes without a hold).
        /// </summary>
        public void Tick(double chartNow, double spawnTime, double hitTime, double endTime, float realNow)
        {
            firing.Clear();
            foreach (Entry entry in entries)
            {
                if (entry.Done || !entry.Cue.IsScheduled) continue;
                double at = BaseTime(entry.Cue.moment, spawnTime, hitTime, endTime) + entry.Cue.EffectiveOffset;
                if (chartNow + 1e-6 < at) continue;
                entry.Done = true;
                firing.Add(entry.Fire);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (realNow + 1e-5f < pending[i].at) continue;
                firing.Add(pending[i].fire);
                pending.RemoveAt(i);
            }
            Invoke();
        }

        /// <summary>A reactive moment happened: its cues fire now (offset 0) or after their offset.</summary>
        public void OnMoment(NoteMoment moment, float realNow)
        {
            if (NoteCue.IsScheduledMoment(moment)) return;
            firing.Clear();
            foreach (Entry entry in entries)
            {
                if (entry.Cue.moment != moment) continue;
                float delay = entry.Cue.EffectiveOffset;
                if (delay <= 0f) firing.Add(entry.Fire);
                else pending.Add((realNow + delay, entry.Fire));
            }
            Invoke();
        }

        /// <summary>Chart seconds until the last scheduled cue that has not fired yet (0 when none is ahead).</summary>
        public float SecondsUntilLastScheduled(double chartNow, double spawnTime, double hitTime, double endTime)
        {
            double latest = chartNow;
            foreach (Entry entry in entries)
            {
                if (entry.Done || !entry.Cue.IsScheduled) continue;
                latest = Math.Max(latest, BaseTime(entry.Cue.moment, spawnTime, hitTime, endTime) + entry.Cue.EffectiveOffset);
            }
            return (float)(latest - chartNow);
        }

        /// <summary>Longest delay of a cue on any of <paramref name="moments"/> (how long the note must linger).</summary>
        public float LongestDelayAfter(params NoteMoment[] moments)
        {
            float longest = 0f;
            foreach (Entry entry in entries)
                if (Array.IndexOf(moments, entry.Cue.moment) >= 0) longest = Mathf.Max(longest, entry.Cue.EffectiveOffset);
            return longest;
        }

        public static double BaseTime(NoteMoment moment, double spawnTime, double hitTime, double endTime) => moment switch
        {
            NoteMoment.Spawned => spawnTime,
            NoteMoment.HoldEnd => Math.Max(hitTime, endTime),
            _ => hitTime
        };

        private void Invoke()
        {
            // Copied out first: an action may add cues or moments.
            if (firing.Count == 0) return;
            Action[] now = firing.ToArray();
            firing.Clear();
            foreach (Action fire in now)
            {
                try { fire(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>
    /// One timed action of a view on the Note Designer's timeline: a point (marker) or a span. Spans either run from
    /// <see cref="Cue"/> to <see cref="EndCue"/>, or last <see cref="Length"/> seconds after it. Cues are the view's own
    /// fields, so the timeline edits them in place.
    /// </summary>
    public sealed class NoteTimelineItem
    {
        public string Label = string.Empty;
        public NoteCue Cue;
        public NoteCue EndCue;
        /// <summary>Seconds after the cue (when there is no end cue). 0 = a marker.</summary>
        public float Length;
        /// <summary>Changes <see cref="Length"/> (drag the span's end). Null = fixed.</summary>
        public Action<float> SetLength;
        /// <summary>Span start relative to the cue (e.g. a move that starts before its arrival cue). Usually 0.</summary>
        public float LeadIn;
        public Color Color = new(0.35f, 0.7f, 1f, 1f);
        /// <summary>Animator state this item plays: the Note Designer shows the clip's length after the marker.</summary>
        public string AnimatorState = string.Empty;
        public AnimatorView.Who AnimatorOwner = AnimatorView.Who.Note;
        /// <summary>False for information only (follows the note, cannot be dragged).</summary>
        public bool Editable = true;
        public string Tooltip = string.Empty;

        public bool IsMarker => EndCue == null && Length <= 0f && LeadIn <= 0f;

        public static NoteTimelineItem Marker(string label, NoteCue cue, Color color) =>
            new() { Label = label, Cue = cue, Color = color };

        public static NoteTimelineItem Span(string label, NoteCue start, NoteCue end, Color color) =>
            new() { Label = label, Cue = start, EndCue = end, Color = color };

        public static NoteTimelineItem Timed(string label, NoteCue start, float length, Action<float> setLength, Color color) =>
            new() { Label = label, Cue = start, Length = Mathf.Max(0f, length), SetLength = setLength, Color = color };

        /// <summary>Information only: a span between two fixed moments.</summary>
        public static NoteTimelineItem Info(string label, NoteMoment from, NoteMoment to, Color color) =>
            new() { Label = label, Cue = new NoteCue(from), EndCue = new NoteCue(to), Color = color, Editable = false };
    }

    /// <summary>Timeline colours shared by views (the Note Designer draws with them).</summary>
    public static class NoteTimelineColors
    {
        public static readonly Color Animation = new(0.45f, 0.8f, 0.45f, 1f);
        public static readonly Color Effect = new(1f, 0.55f, 0.2f, 1f);
        public static readonly Color Link = new(0.75f, 0.5f, 1f, 1f);
        public static readonly Color Move = new(0.35f, 0.7f, 1f, 1f);
        public static readonly Color Feedback = new(1f, 0.85f, 0.3f, 1f);
        public static readonly Color Info = new(0.6f, 0.6f, 0.6f, 1f);
    }
}
