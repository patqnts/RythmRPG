using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Model;

namespace RythmRPG.LevelComposer.Editing
{
    /// <summary>Settings for stamping a generated pattern into a step.</summary>
    public sealed class StampRequest
    {
        public string PatternId = "sweep";
        public string NoteType = "normal";
        public double StartBeat;
        public double LengthBeats = 4d;
        /// <summary>Beats between notes (0 = the pattern's own spacing).</summary>
        public double Spacing;
        public int LaneCount = 4;
        public int Seed = 1;
    }

    public sealed class PatternStamp
    {
        public string Id;
        public string Name;
        public string Description;
        public double DefaultSpacing = 0.5d;
        public Func<StampRequest, double, List<LevelNote>> Generate;
    }

    /// <summary>
    /// Pattern generators that write ordinary notes (fully editable afterwards). The first four follow the game's
    /// PatternLibrary (sweep, stairs, stream, barrage); the rest are composing helpers. Deterministic per seed.
    /// </summary>
    public static class PatternStamps
    {
        private static readonly List<PatternStamp> all = Build();

        public static IList<PatternStamp> All { get { return all.AsReadOnly(); } }

        public static PatternStamp Find(string id)
        {
            foreach (PatternStamp p in all) if (p.Id == id) return p;
            return null;
        }

        public static List<LevelNote> Generate(StampRequest r)
        {
            PatternStamp p = Find(r.PatternId);
            if (p == null || r.LengthBeats <= 0d) return new List<LevelNote>();
            double spacing = r.Spacing > 0d ? r.Spacing : p.DefaultSpacing;
            List<LevelNote> notes = p.Generate(r, spacing);
            foreach (LevelNote n in notes)
            {
                n.Type = r.NoteType;
                n.Beat += r.StartBeat;
            }

            return notes;
        }

        private static List<PatternStamp> Build()
        {
            var list = new List<PatternStamp>();
            list.Add(new PatternStamp { Id = "sweep", Name = "Sweep", Description = "Across the lanes and back.", DefaultSpacing = 0.5d, Generate = Sweep });
            list.Add(new PatternStamp { Id = "stairs", Name = "Stairs", Description = "Climbs across the lanes and restarts.", DefaultSpacing = 0.5d, Generate = Stairs });
            list.Add(new PatternStamp { Id = "stream", Name = "Stream", Description = "Fast random walk between neighbouring lanes.", DefaultSpacing = 0.25d, Generate = Stream });
            list.Add(new PatternStamp { Id = "barrage", Name = "Barrage", Description = "Two lanes at once on every step.", DefaultSpacing = 1d, Generate = Barrage });
            list.Add(new PatternStamp { Id = "trill", Name = "Trill", Description = "Alternates between two neighbouring lanes.", DefaultSpacing = 0.5d, Generate = Trill });
            list.Add(new PatternStamp { Id = "pulse", Name = "Pulse", Description = "Same lane on every step (random lane).", DefaultSpacing = 1d, Generate = Pulse });
            list.Add(new PatternStamp { Id = "random", Name = "Random", Description = "Random lane each step, never the same lane twice in a row.", DefaultSpacing = 0.5d, Generate = RandomLanes });
            return list;
        }

        private static int Steps(StampRequest r, double spacing)
        {
            return Math.Max(0, (int)Math.Floor(r.LengthBeats / Math.Max(1e-6, spacing) + 1e-9));
        }

        private static LevelNote Note(int lane, double beat) { return new LevelNote { Lane = lane, Beat = beat }; }

        private static List<LevelNote> Sweep(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s), period = Math.Max(1, 2 * k - 2);
            for (int i = 0; i < n; i++)
            {
                int p = i % period;
                int pos = p < k ? p : period - p;
                list.Add(Note(pos + 1, i * s));
            }

            return list;
        }

        private static List<LevelNote> Stairs(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s);
            for (int i = 0; i < n; i++) list.Add(Note(i % k + 1, i * s));
            return list;
        }

        private static List<LevelNote> Stream(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            var rng = new Random(r.Seed);
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s), pos = rng.Next(k);
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && k > 1)
                {
                    pos += rng.Next(2) == 0 ? -1 : 1;
                    if (pos < 0) pos = 1;
                    if (pos >= k) pos = k - 2;
                }

                list.Add(Note(pos + 1, i * s));
            }

            return list;
        }

        private static List<LevelNote> Barrage(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            var rng = new Random(r.Seed);
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s);
            for (int i = 0; i < n; i++)
            {
                int a = rng.Next(k);
                list.Add(Note(a + 1, i * s));
                if (k > 1)
                {
                    int b = rng.Next(k - 1);
                    if (b >= a) b++;
                    list.Add(Note(b + 1, i * s));
                }
            }

            return list;
        }

        private static List<LevelNote> Trill(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            var rng = new Random(r.Seed);
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s);
            int a = k > 1 ? rng.Next(k - 1) : 0;
            for (int i = 0; i < n; i++) list.Add(Note((k > 1 ? a + (i % 2) : 0) + 1, i * s));
            return list;
        }

        private static List<LevelNote> Pulse(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            int lane = new Random(r.Seed).Next(Math.Max(1, r.LaneCount)) + 1, n = Steps(r, s);
            for (int i = 0; i < n; i++) list.Add(Note(lane, i * s));
            return list;
        }

        private static List<LevelNote> RandomLanes(StampRequest r, double s)
        {
            var list = new List<LevelNote>();
            var rng = new Random(r.Seed);
            int k = Math.Max(1, r.LaneCount), n = Steps(r, s), last = -1;
            for (int i = 0; i < n; i++)
            {
                int lane = rng.Next(k);
                if (k > 1 && lane == last) lane = (lane + 1 + rng.Next(k - 1)) % k;
                last = lane;
                list.Add(Note(lane + 1, i * s));
            }

            return list;
        }
    }
}
