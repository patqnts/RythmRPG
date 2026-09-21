using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm
{
    /// <summary>
    /// Fires callbacks when the song beat passes a point, or on a repeating beat interval. Feed it the current
    /// beat every frame via <see cref="Advance"/> (from a MusicClock). Pure C#.
    /// </summary>
    public sealed class BeatScheduler
    {
        private const double Epsilon = 1e-9;
        private const int MaxCatchUpTicks = 64;

        private struct OneShot
        {
            public double Beat;
            public long Order;
            public Action Action;
        }

        private sealed class Repeater
        {
            public int Id;
            public double Start;
            public double Interval;
            public long NextIndex;
            public Action<long> Action;
        }

        private readonly List<OneShot> oneShots = new List<OneShot>();
        private readonly List<Repeater> repeaters = new List<Repeater>();
        private long order;
        private int nextId = 1;

        public int PendingCount { get { return oneShots.Count; } }

        /// <summary>Runs <paramref name="action"/> once when the beat reaches <paramref name="beat"/>.</summary>
        public void At(double beat, Action action)
        {
            if (action == null) throw new ArgumentNullException("action");
            oneShots.Add(new OneShot { Beat = beat, Order = order++, Action = action });
            oneShots.Sort(delegate (OneShot a, OneShot b)
            {
                int c = a.Beat.CompareTo(b.Beat);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });
        }

        /// <summary>Calls <paramref name="onTick"/>(tickIndex) at startBeat + k * intervalBeats. Returns an id for <see cref="Cancel"/>.</summary>
        public int Every(double intervalBeats, Action<long> onTick, double startBeat = 0d)
        {
            if (onTick == null) throw new ArgumentNullException("onTick");
            if (intervalBeats <= 0d) throw new ArgumentOutOfRangeException("intervalBeats");
            var r = new Repeater { Id = nextId++, Start = startBeat, Interval = intervalBeats, Action = onTick };
            repeaters.Add(r);
            return r.Id;
        }

        public void Cancel(int repeaterId)
        {
            repeaters.RemoveAll(delegate (Repeater r) { return r.Id == repeaterId; });
        }

        public void Clear()
        {
            oneShots.Clear();
            repeaters.Clear();
        }

        /// <summary>Fires everything due at or before <paramref name="beat"/>. Repeaters that fell far behind skip ahead instead of flooding.</summary>
        public void Advance(double beat)
        {
            while (oneShots.Count > 0 && oneShots[0].Beat <= beat + Epsilon)
            {
                OneShot s = oneShots[0];
                oneShots.RemoveAt(0);
                s.Action();
            }

            for (int i = 0; i < repeaters.Count; i++)
            {
                Repeater r = repeaters[i];
                long due = (long)Math.Floor((beat + Epsilon - r.Start) / r.Interval);
                if (due < r.NextIndex) continue;
                if (due - r.NextIndex >= MaxCatchUpTicks) r.NextIndex = due - MaxCatchUpTicks + 1;
                while (r.NextIndex <= due)
                {
                    long index = r.NextIndex++;
                    r.Action(index);
                }
            }
        }

        /// <summary>
        /// Rewinds repeaters after a seek/restart so ticks at or after <paramref name="beat"/> fire again. One-shots are not restored.
        /// </summary>
        public void Reposition(double beat)
        {
            for (int i = 0; i < repeaters.Count; i++)
            {
                Repeater r = repeaters[i];
                r.NextIndex = Math.Max(0L, (long)Math.Ceiling((beat - Epsilon - r.Start) / r.Interval));
            }
        }

        /// <summary>The first grid line strictly after <paramref name="beat"/> (grid spacing = divisionBeats, e.g. 1 = next beat, 4 = next bar in 4/4).</summary>
        public static double NextBoundary(double beat, double divisionBeats)
        {
            if (divisionBeats <= 0d) throw new ArgumentOutOfRangeException("divisionBeats");
            return (Math.Floor((beat + Epsilon) / divisionBeats) + 1d) * divisionBeats;
        }
    }
}
