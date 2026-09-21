using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm
{
    public struct GeneratedNote
    {
        public int LaneIndex;
        public double BeatOffset;
        public string DefinitionId;
        public double HoldBeats;
    }

    public struct PatternContext
    {
        public int Seed;
        public double DurationBeats;
        /// <summary>Lane indices the pattern may use, ascending. Never empty.</summary>
        public int[] Lanes;
    }

    /// <summary>A named generator. Must be deterministic for a given context (same seed = same notes).</summary>
    public sealed class PatternTemplate
    {
        public string Id;
        public string Name;
        public string Description;
        public double DefaultDurationBeats = 4d;
        public Func<PatternContext, List<GeneratedNote>> Generate;
    }

    /// <summary>Built-in programmed patterns. Pure C#; used by the composer preview, validator and save.</summary>
    public static class PatternLibrary
    {
        public const string IdPrefix = "pattern:";
        private static readonly List<PatternTemplate> all = Build();

        public static IList<PatternTemplate> All { get { return all.AsReadOnly(); } }

        public static PatternTemplate Find(string id)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id) return all[i];
            }

            return null;
        }

        private static List<PatternTemplate> Build()
        {
            var list = new List<PatternTemplate>();
            list.Add(new PatternTemplate
            {
                Id = "sweep",
                Name = "Sweep",
                Description = "One note every half beat, sweeping across the lanes and back.",
                Generate = Sweep
            });
            list.Add(new PatternTemplate
            {
                Id = "stairs",
                Name = "Stairs",
                Description = "One note every half beat, climbing across the lanes and restarting.",
                Generate = Stairs
            });
            list.Add(new PatternTemplate
            {
                Id = "stream",
                Name = "Stream",
                Description = "A fast random walk, one note every quarter beat.",
                Generate = Stream
            });
            list.Add(new PatternTemplate
            {
                Id = "barrage",
                Name = "Barrage",
                Description = "Two notes on different lanes every beat.",
                Generate = Barrage
            });
            return list;
        }

        private static int Steps(PatternContext c, double stepBeats)
        {
            return Math.Max(0, (int)Math.Floor(c.DurationBeats / stepBeats + 1e-9));
        }

        private static GeneratedNote Tap(int lane, double offset)
        {
            return new GeneratedNote { LaneIndex = lane, BeatOffset = offset, DefinitionId = NoteMigration.DefinitionIdNormal };
        }

        private static List<GeneratedNote> Sweep(PatternContext c)
        {
            var result = new List<GeneratedNote>();
            int k = c.Lanes.Length;
            int n = Steps(c, 0.5d);
            int period = Math.Max(1, 2 * k - 2);
            for (int i = 0; i < n; i++)
            {
                int p = i % period;
                int pos = p < k ? p : period - p;
                result.Add(Tap(c.Lanes[pos], i * 0.5d));
            }

            return result;
        }

        private static List<GeneratedNote> Stairs(PatternContext c)
        {
            var result = new List<GeneratedNote>();
            int n = Steps(c, 0.5d);
            for (int i = 0; i < n; i++) result.Add(Tap(c.Lanes[i % c.Lanes.Length], i * 0.5d));
            return result;
        }

        private static List<GeneratedNote> Stream(PatternContext c)
        {
            var result = new List<GeneratedNote>();
            var rng = new Random(c.Seed);
            int k = c.Lanes.Length;
            int n = Steps(c, 0.25d);
            int pos = rng.Next(k);
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && k > 1)
                {
                    int step = rng.Next(2) == 0 ? -1 : 1;
                    pos += step;
                    if (pos < 0) pos = 1;
                    if (pos >= k) pos = k - 2;
                }

                result.Add(Tap(c.Lanes[pos], i * 0.25d));
            }

            return result;
        }

        private static List<GeneratedNote> Barrage(PatternContext c)
        {
            var result = new List<GeneratedNote>();
            var rng = new Random(c.Seed);
            int k = c.Lanes.Length;
            int n = Steps(c, 1d);
            for (int i = 0; i < n; i++)
            {
                int a = rng.Next(k);
                result.Add(Tap(c.Lanes[a], i));
                if (k > 1)
                {
                    int b = rng.Next(k - 1);
                    if (b >= a) b++;
                    result.Add(Tap(c.Lanes[b], i));
                }
            }

            return result;
        }
    }

    /// <summary>Turns a <see cref="PatternInstance"/> into ordinary note instances.</summary>
    public static class PatternExpander
    {
        /// <summary>Metadata key written on every generated note so the save step can tell them from hand-placed notes.</summary>
        public const string MetadataKey = "pattern";

        public static int[] ActiveLanes(PatternInstance pattern, int laneCount)
        {
            var lanes = new List<int>();
            for (int i = 0; i < laneCount; i++)
            {
                if (pattern.UsesLane(i)) lanes.Add(i);
            }

            if (lanes.Count == 0)
            {
                for (int i = 0; i < laneCount; i++) lanes.Add(i);
            }

            return lanes.ToArray();
        }

        public static List<NoteInstance> Expand(PatternInstance pattern, IList<string> laneIds)
        {
            var result = new List<NoteInstance>();
            PatternTemplate template = PatternLibrary.Find(pattern.TemplateId);
            if (template == null || laneIds == null || laneIds.Count == 0 || pattern.DurationBeats <= 0d) return result;

            var context = new PatternContext
            {
                Seed = pattern.Seed,
                DurationBeats = pattern.DurationBeats,
                Lanes = ActiveLanes(pattern, laneIds.Count)
            };
            List<GeneratedNote> generated = template.Generate(context);
            for (int i = 0; i < generated.Count; i++)
            {
                GeneratedNote g = generated[i];
                if (g.LaneIndex < 0 || g.LaneIndex >= laneIds.Count) continue;
                var note = new NoteInstance
                {
                    Id = "pat_" + pattern.Id + "_" + i,
                    LaneId = laneIds[g.LaneIndex],
                    DefinitionId = g.DefinitionId,
                    HitBeat = pattern.StartBeat + g.BeatOffset,
                    HoldBeats = g.HoldBeats
                };
                var meta = new RhythmMetadataEntry();
                meta.Key = MetadataKey;
                meta.Value = pattern.Id;
                note.Metadata.Add(meta);
                result.Add(note);
            }

            return result;
        }

        public static bool IsGenerated(IList<RhythmMetadataEntry> metadata)
        {
            if (metadata == null) return false;
            for (int i = 0; i < metadata.Count; i++)
            {
                if (metadata[i].Key == MetadataKey) return true;
            }

            return false;
        }
    }
}
