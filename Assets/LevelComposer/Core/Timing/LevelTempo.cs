using System;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Timing
{
    /// <summary>
    /// Constant-tempo beat/seconds math for one chart. Chart time 0 = beat 0 (the game starts each chart so that its
    /// beat 0 lands on a bar line of the loop). Negative times are the lead-in before beat 0.
    /// </summary>
    public struct LevelTempo
    {
        public readonly double Bpm;
        public readonly int BeatsPerBar;

        public LevelTempo(double bpm, int beatsPerBar)
        {
            Bpm = Math.Max(1d, bpm);
            BeatsPerBar = Math.Max(1, beatsPerBar);
        }

        public static LevelTempo Of(CombatLevel level) { return new LevelTempo(level.Music.Bpm, level.Music.BeatsPerBar); }

        public double SecondsPerBeat { get { return 60d / Bpm; } }
        public double BarSeconds { get { return SecondsPerBeat * BeatsPerBar; } }
        public double BeatToSeconds(double beat) { return beat * SecondsPerBeat; }
        public double SecondsToBeat(double seconds) { return seconds / SecondsPerBeat; }

        public static double Snap(double beat, int division)
        {
            double d = Math.Max(1, division);
            return Math.Round(beat * d, MidpointRounding.AwayFromZero) / d;
        }

        public static double SnapFloor(double beat, int division)
        {
            double d = Math.Max(1, division);
            return Math.Floor(beat * d + 1e-9) / d;
        }
    }

    /// <summary>Which keyboard key plays each lane, matching GameInput.LaneKeySlot (balanced per hand).</summary>
    public static class LaneKeys
    {
        // Key slots: 1 = A (left outer), 2 = S (left inner), 3 = J (right inner), 4 = K (right outer).
        private static readonly int[][] Layouts =
        {
            new[] { 3 },
            new[] { 2, 3 },
            new[] { 2, 3, 4 },
            new[] { 1, 2, 3, 4 },
        };

        private static readonly string[] SlotNames = { "A", "S", "J", "K" };

        /// <summary>Key slot (1..4) used by 1-based <paramref name="lane"/> when the step has <paramref name="laneCount"/> lanes.</summary>
        public static int Slot(int laneCount, int lane)
        {
            int count = Math.Max(1, Math.Min(4, laneCount));
            int[] layout = Layouts[count - 1];
            return lane >= 1 && lane <= layout.Length ? layout[lane - 1] : lane;
        }

        public static string KeyName(int laneCount, int lane)
        {
            int slot = Slot(laneCount, lane);
            return slot >= 1 && slot <= 4 ? SlotNames[slot - 1] : lane.ToString();
        }

        /// <summary>The lane (1-based) that a key slot plays, or 0 if that key is unused with this lane count.</summary>
        public static int LaneForSlot(int laneCount, int slot)
        {
            for (int lane = 1; lane <= Math.Max(1, Math.Min(4, laneCount)); lane++)
                if (Slot(laneCount, lane) == slot) return lane;
            return 0;
        }
    }

    /// <summary>Resolves a note's effective parameter values (its override, else the type default at the level tempo).</summary>
    public static class NoteParams
    {
        public static object Get(LevelNote note, NoteTypeDef def, string key, LevelTempo tempo)
        {
            ParamDef p = def != null ? def.FindParam(key) : null;
            object raw;
            bool has = note.Params.TryGetValue(key, out raw);
            if (p == null) return has ? raw : null;
            return p.Coerce(has ? raw : p.DefaultValue(tempo.SecondsPerBeat));
        }

        public static double GetNumber(LevelNote note, NoteTypeDef def, string key, LevelTempo tempo, double fallback)
        {
            return Json.JsonRead.ToDouble(Get(note, def, key, tempo), fallback);
        }

        public static bool GetBool(LevelNote note, NoteTypeDef def, string key, LevelTempo tempo, bool fallback)
        {
            object v = Get(note, def, key, tempo);
            return v is bool ? (bool)v : fallback;
        }

        public static string GetString(LevelNote note, NoteTypeDef def, string key, LevelTempo tempo)
        {
            object v = Get(note, def, key, tempo);
            return v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Value of the parameter bound to <paramref name="bind"/>, or <paramref name="fallback"/> when the type has none.</summary>
        public static double GetBound(LevelNote note, NoteTypeDef def, string bind, LevelTempo tempo, double fallback)
        {
            ParamDef p = def != null ? def.FindBound(bind) : null;
            return p == null ? fallback : GetNumber(note, def, p.Key, tempo, fallback);
        }

        /// <summary>Lead-in (spawn to hit) in beats. Types without a travel parameter use the default 2.5 s.</summary>
        public static double TravelBeats(LevelNote note, NoteTypeDef def, LevelTempo tempo)
        {
            double fallback = tempo.SecondsToBeat(BuiltInNoteTypes.DefaultTravelSeconds);
            return Math.Max(0.01d, GetBound(note, def, ParamBindings.TravelBeats, tempo, fallback));
        }

        public static double TravelSeconds(LevelNote note, NoteTypeDef def, LevelTempo tempo)
        {
            return tempo.BeatToSeconds(TravelBeats(note, def, tempo));
        }

        public static double HitSeconds(LevelNote note, LevelTempo tempo) { return tempo.BeatToSeconds(note.Beat); }

        public static double SpawnSeconds(LevelNote note, NoteTypeDef def, LevelTempo tempo)
        {
            if (def != null && def.Output == NoteOutput.Sequence) return tempo.BeatToSeconds(note.Beat);
            return tempo.BeatToSeconds(note.Beat) - TravelSeconds(note, def, tempo);
        }

        /// <summary>Sets a parameter override; removes it when it equals the default (keeps files sparse).</summary>
        public static void Set(LevelNote note, NoteTypeDef def, string key, object value, LevelTempo tempo)
        {
            ParamDef p = def != null ? def.FindParam(key) : null;
            if (p == null)
            {
                if (value == null) note.Params.Remove(key);
                else note.Params[key] = value;
                return;
            }

            object v = p.Coerce(value);
            object d = p.Coerce(p.DefaultValue(tempo.SecondsPerBeat));
            // Beats defaults depend on tempo; keep explicit values for them so a BPM change does not move notes' spawn.
            if (p.Kind != ParamKind.Beats && Equals(v, d)) note.Params.Remove(key);
            else note.Params[key] = v;
        }
    }
}
