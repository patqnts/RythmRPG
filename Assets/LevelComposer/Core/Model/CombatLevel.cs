using System;
using System.Collections.Generic;
using System.Globalization;

namespace RythmRPG.LevelComposer.Model
{
    /// <summary>Mirrors RythmRPG.Rhythm.MusicSync: when a music hand-over happens.</summary>
    public enum MusicSyncMode
    {
        Immediate,
        NextBeat,
        NextBar
    }

    /// <summary>Mirrors RythmRPG.Combat.AttackStepEndPolicy.</summary>
    public enum StepEndPolicy
    {
        WaitForResolvedNotes,
        ClearAsMisses,
        ClearWithoutPenalty
    }

    /// <summary>The five music sections of a combat song (see CombatSong in the game).</summary>
    public enum MusicSection
    {
        Intro,
        Loop,
        PlayerTurn,
        End,
        DefeatEnd
    }

    /// <summary>The song an attack sequence plays over. Paths are relative to the level file when possible.</summary>
    public sealed class MusicSettings
    {
        public double Bpm = 120d;
        public int BeatsPerBar = 4;
        /// <summary>Seconds into the loop clip where bar 1 beat 1 falls.</summary>
        public double OffsetSeconds;
        public double Volume = 1d;
        public MusicSyncMode ChartSync = MusicSyncMode.NextBar;
        public MusicSyncMode EndSync = MusicSyncMode.NextBar;
        public bool ChartsWaitForLoop = true;
        public double PlayerTurnVolume = 1d;
        public double MainVolumeOnPlayerTurn;

        public string Intro = "";
        public string Loop = "";
        public string PlayerTurn = "";
        public string End = "";
        public string DefeatEnd = "";

        public double SecondsPerBeat { get { return 60d / Math.Max(1d, Bpm); } }
        public double BarSeconds { get { return SecondsPerBeat * Math.Max(1, BeatsPerBar); } }

        public string Get(MusicSection section)
        {
            switch (section)
            {
                case MusicSection.Intro: return Intro;
                case MusicSection.Loop: return Loop;
                case MusicSection.PlayerTurn: return PlayerTurn;
                case MusicSection.End: return End;
                default: return DefeatEnd;
            }
        }

        public void Set(MusicSection section, string path)
        {
            path = path ?? "";
            switch (section)
            {
                case MusicSection.Intro: Intro = path; break;
                case MusicSection.Loop: Loop = path; break;
                case MusicSection.PlayerTurn: PlayerTurn = path; break;
                case MusicSection.End: End = path; break;
                default: DefeatEnd = path; break;
            }
        }

        public MusicSettings Clone() { return (MusicSettings)MemberwiseClone(); }
    }

    /// <summary>
    /// One placed note (or sequence start). Parameters are sparse: a missing key means "use the note type's default",
    /// so changing a default in a note-type file updates every note that never overrode it.
    /// </summary>
    public sealed class LevelNote
    {
        public string Id = NewId();
        public string Type = "normal";
        /// <summary>1-based lane number (1..step lane count).</summary>
        public int Lane = 1;
        public double Beat;
        /// <summary>Hold length in beats (types with a length only).</summary>
        public double Length;
        public Dictionary<string, object> Params = new Dictionary<string, object>();

        public static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        public double EndBeat { get { return Beat + Math.Max(0d, Length); } }

        public bool HasParam(string key) { return Params.ContainsKey(key); }

        public LevelNote Clone()
        {
            var c = (LevelNote)MemberwiseClone();
            c.Params = new Dictionary<string, object>(Params);
            return c;
        }

        public LevelNote CloneWithNewId()
        {
            LevelNote c = Clone();
            c.Id = NewId();
            return c;
        }
    }

    /// <summary>One enemy attack step: a wind-up animation and a chart (EnemyAttackStepDefinition + RhythmChart).</summary>
    public sealed class LevelStep
    {
        public const int MaxLanes = 4;

        public string Id = LevelNote.NewId();
        public string Name = "Step";
        /// <summary>Enemy animator state played as the wind-up.</summary>
        public string Animation = "";
        /// <summary>Seconds the wind-up plays before the first projectile appears.</summary>
        public double Anticipation = 0.45d;
        public StepEndPolicy EndPolicy = StepEndPolicy.WaitForResolvedNotes;
        /// <summary>1..4 lanes (keys: 1 = J, 2 = S J, 3 = S J K, 4 = A S J K).</summary>
        public int LaneCount = 4;
        public List<LevelNote> Notes = new List<LevelNote>();

        public LevelStep Clone()
        {
            var c = (LevelStep)MemberwiseClone();
            c.Notes = new List<LevelNote>(Notes.Count);
            for (int i = 0; i < Notes.Count; i++) c.Notes.Add(Notes[i].Clone());
            return c;
        }

        public LevelStep CloneWithNewIds()
        {
            LevelStep c = Clone();
            c.Id = LevelNote.NewId();
            for (int i = 0; i < c.Notes.Count; i++) c.Notes[i].Id = LevelNote.NewId();
            return c;
        }

        public LevelNote Find(string id)
        {
            for (int i = 0; i < Notes.Count; i++)
                if (Notes[i].Id == id) return Notes[i];
            return null;
        }

        /// <summary>Last beat any note ends on (0 when empty).</summary>
        public double LastBeat
        {
            get
            {
                double end = 0d;
                for (int i = 0; i < Notes.Count; i++) end = Math.Max(end, Notes[i].EndBeat);
                return end;
            }
        }
    }

    /// <summary>Settings used only by the composer's preview.</summary>
    public sealed class PreviewSettings
    {
        /// <summary>Level preview: bars the player turn lasts between enemy steps.</summary>
        public int PlayerTurnBars = 2;
        /// <summary>Step preview: which bar of the loop a chart's beat 0 is heard against (the game picks the next bar line).</summary>
        public int LoopBar;
        public PreviewSettings Clone() { return (PreviewSettings)MemberwiseClone(); }
    }

    /// <summary>
    /// A combat level: one attack sequence of an enemy (EnemyAttackSequenceDefinition), i.e. its song and its ordered
    /// attack steps. This is what one .combatlevel.json file holds.
    /// </summary>
    public sealed class CombatLevel
    {
        public const string FormatId = "rythmrpg.combatlevel";
        public const int CurrentVersion = 1;
        public const string FileExtension = ".combatlevel.json";

        public string Id = "new-level";
        public string Name = "New Level";
        public string Author = "";
        public string Description = "";
        public double SelectionWeight = 1d;
        public MusicSettings Music = new MusicSettings();
        public List<LevelStep> Steps = new List<LevelStep>();
        public PreviewSettings Preview = new PreviewSettings();

        public static CombatLevel CreateNew()
        {
            var level = new CombatLevel();
            level.Steps.Add(new LevelStep { Name = "Step 1" });
            return level;
        }

        public CombatLevel Clone()
        {
            var c = (CombatLevel)MemberwiseClone();
            c.Music = Music.Clone();
            c.Preview = Preview.Clone();
            c.Steps = new List<LevelStep>(Steps.Count);
            for (int i = 0; i < Steps.Count; i++) c.Steps.Add(Steps[i].Clone());
            return c;
        }

        public int IndexOfStep(string stepId)
        {
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i].Id == stepId) return i;
            return -1;
        }

        public int TotalNotes
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Steps.Count; i++) n += Steps[i].Notes.Count;
                return n;
            }
        }

        /// <summary>A file-name / asset-name safe version of <paramref name="text"/>.</summary>
        public static string Slug(string text)
        {
            if (string.IsNullOrEmpty(text)) return "level";
            var sb = new System.Text.StringBuilder();
            foreach (char ch in text.Trim().ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else if (ch == ' ' || ch == '-' || ch == '_' || ch == '.') { if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-'); }
            }

            string s = sb.ToString().Trim('-');
            return s.Length == 0 ? "level" : s;
        }

        public static string FormatBeat(double beat, int beatsPerBar)
        {
            int bpb = Math.Max(1, beatsPerBar);
            double b = Math.Max(0d, beat);
            int bar = (int)Math.Floor(b / bpb + 1e-9);
            double inBar = b - bar * bpb;
            return (bar + 1).ToString(CultureInfo.InvariantCulture) + "." + (inBar + 1d).ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
