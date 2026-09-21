using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm.Editing
{
    public enum NoteHandleKind
    {
        Travel,
        HoldEnd,
        WindowBad,
        WindowGood,
        WindowPerfect
    }

    /// <summary>Per-definition fallbacks used when a note has no override for a handle-edited value.</summary>
    public sealed class NoteHandleDefaults
    {
        public double TravelSeconds = 2.5d;
        public float BadWindow = 0.45f;
        public float GoodWindow = 0.25f;
        public float PerfectWindow = 0.1f;
    }

    public struct NoteHandlePoint
    {
        public NoteHandleKind Kind;
        public double Beat;
    }

    /// <summary>
    /// Schema for the drag handles a note type exposes on the timeline (which lifecycle values can be
    /// edited directly), plus the pure math that maps handles to/from note values. No UI dependency.
    /// </summary>
    public static class NoteHandles
    {
        public const double MinTravelBeats = 0.1d;
        public const float MinWindowSeconds = 0.02f;

        private static readonly NoteHandleKind[] MovingSet = { NoteHandleKind.Travel };
        private static readonly NoteHandleKind[] MovingHoldSet = { NoteHandleKind.Travel, NoteHandleKind.HoldEnd };
        private static readonly NoteHandleKind[] StationarySet =
        {
            NoteHandleKind.Travel, NoteHandleKind.WindowBad, NoteHandleKind.WindowGood, NoteHandleKind.WindowPerfect
        };
        private static readonly NoteHandleKind[] StationaryHoldSet =
        {
            NoteHandleKind.Travel, NoteHandleKind.HoldEnd, NoteHandleKind.WindowBad, NoteHandleKind.WindowGood, NoteHandleKind.WindowPerfect
        };
        private static readonly NoteHandleKind[] MashSet = { NoteHandleKind.Travel }; // the travel time IS the mash window

        public static NoteHandleKind[] For(NoteBehaviorKind behavior)
        {
            switch (behavior)
            {
                case NoteBehaviorKind.MovingHold: return MovingHoldSet;
                case NoteBehaviorKind.Stationary: return StationarySet;
                case NoteBehaviorKind.StationaryHold: return StationaryHoldSet;
                case NoteBehaviorKind.Mash: return MashSet;
                default: return MovingSet;
            }
        }

        /// <summary>Lead-in length in beats: the override if present, otherwise the definition default converted at the hit.</summary>
        public static double TravelBeats(NoteInstance n, NoteHandleDefaults d, TempoMap tempo)
        {
            if (n.TravelBeats.HasValue) return n.TravelBeats.Value;
            double hitSeconds = tempo.BeatToSeconds(n.HitBeat);
            return n.HitBeat - tempo.SecondsToBeat(hitSeconds - d.TravelSeconds);
        }

        /// <summary>Lead-in length in seconds (spawn to hit line); the clearing time available to a Mash note.</summary>
        public static double TravelSeconds(NoteInstance n, NoteHandleDefaults d, TempoMap tempo)
        {
            double hitSeconds = tempo.BeatToSeconds(n.HitBeat);
            return hitSeconds - tempo.BeatToSeconds(n.HitBeat - TravelBeats(n, d, tempo));
        }

        public static float WindowSeconds(NoteHandleKind kind, NoteInstance n, NoteHandleDefaults d)
        {
            switch (kind)
            {
                case NoteHandleKind.WindowBad: return n.StationaryBadWindow.Resolve(d.BadWindow);
                case NoteHandleKind.WindowGood: return n.StationaryGoodWindow.Resolve(d.GoodWindow);
                default: return n.StationaryPerfectWindow.Resolve(d.PerfectWindow);
            }
        }

        public static double BeatOf(NoteHandleKind kind, NoteInstance n, NoteHandleDefaults d, TempoMap tempo)
        {
            switch (kind)
            {
                case NoteHandleKind.Travel:
                    return n.HitBeat - TravelBeats(n, d, tempo);
                case NoteHandleKind.HoldEnd:
                    return n.HitBeat + n.HoldBeats;
                default:
                    return tempo.SecondsToBeat(tempo.BeatToSeconds(n.HitBeat) + WindowSeconds(kind, n, d));
            }
        }

        /// <summary>Handles for a note; the hold end is skipped unless asked for (the timeline already draws it for hold notes).</summary>
        public static List<NoteHandlePoint> Points(NoteInstance n, NoteBehaviorKind behavior, NoteHandleDefaults d, TempoMap tempo, bool includeHoldEnd)
        {
            var list = new List<NoteHandlePoint>();
            NoteHandleKind[] kinds = For(behavior);
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == NoteHandleKind.HoldEnd && !includeHoldEnd) continue;
                list.Add(new NoteHandlePoint { Kind = kinds[i], Beat = BeatOf(kinds[i], n, d, tempo) });
            }

            return list;
        }

        /// <summary>Moves a handle to a beat by writing the matching value on the note (an override for everything but the hold length).</summary>
        public static void Apply(NoteHandleKind kind, NoteInstance n, double beat, NoteHandleDefaults d, TempoMap tempo)
        {
            switch (kind)
            {
                case NoteHandleKind.Travel:
                    n.TravelBeats = new Overridable<double>(Math.Max(MinTravelBeats, n.HitBeat - beat));
                    break;
                case NoteHandleKind.HoldEnd:
                    n.HoldBeats = Math.Max(0d, beat - n.HitBeat);
                    break;
                default:
                {
                    double hitSeconds = tempo.BeatToSeconds(n.HitBeat);
                    float sec = Math.Max(MinWindowSeconds, (float)(tempo.BeatToSeconds(beat) - hitSeconds));
                    float bad = WindowSeconds(NoteHandleKind.WindowBad, n, d);
                    float good = WindowSeconds(NoteHandleKind.WindowGood, n, d);
                    float perfect = WindowSeconds(NoteHandleKind.WindowPerfect, n, d);
                    // keep perfect <= good <= bad
                    if (kind == NoteHandleKind.WindowBad) n.StationaryBadWindow = new Overridable<float>(Math.Max(sec, good));
                    else if (kind == NoteHandleKind.WindowGood) n.StationaryGoodWindow = new Overridable<float>(Math.Min(bad, Math.Max(sec, perfect)));
                    else n.StationaryPerfectWindow = new Overridable<float>(Math.Min(sec, good));
                    break;
                }
            }
        }
    }
}
