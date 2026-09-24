using System;
using System.Collections.Generic;
using System.Globalization;

namespace RythmRPG.LevelComposer.Types
{
    /// <summary>How a parameter is edited and stored.</summary>
    public enum ParamKind
    {
        Int,
        Float,
        Bool,
        /// <summary>One of <see cref="ParamDef.Options"/> (stored as the option string).</summary>
        Choice,
        /// <summary>A length in beats (e.g. travel time). Edited in beats, shown with seconds.</summary>
        Beats,
        /// <summary>A length in seconds (e.g. hit windows).</summary>
        Seconds,
        Text
    }

    /// <summary>
    /// One editable property of a note type. The inspector, validator, simulator and importer all read these, so a new
    /// gimmick only has to declare its parameters once.
    /// </summary>
    public sealed class ParamDef
    {
        public string Key = "";
        public string Label = "";
        public ParamKind Kind = ParamKind.Float;
        /// <summary>Default value. Beats params may use <see cref="DefaultSeconds"/> instead (tempo dependent).</summary>
        public object Default;
        /// <summary>Beats params only: when &gt; 0 the default is this many seconds, converted with the level tempo.</summary>
        public double DefaultSeconds;
        public double Min = double.NegativeInfinity;
        public double Max = double.PositiveInfinity;
        public double Step;
        public string[] Options = new string[0];
        public string Tooltip = "";
        /// <summary>Inspector group ("" = main, "Advanced" is collapsed by default).</summary>
        public string Group = "";
        /// <summary>
        /// Which game field this parameter writes when the level is imported into Unity (see <see cref="ParamBindings"/>).
        /// Empty = written as note metadata (key = <see cref="Key"/>), which new note scripts can read at runtime.
        /// </summary>
        public string Bind = "";

        public ParamDef Clone()
        {
            var c = (ParamDef)MemberwiseClone();
            c.Options = (string[])Options.Clone();
            return c;
        }

        /// <summary>The default for this parameter at a tempo (beats per second).</summary>
        public object DefaultValue(double secondsPerBeat)
        {
            if (Kind == ParamKind.Beats && DefaultSeconds > 0d)
                return DefaultSeconds / Math.Max(1e-6, secondsPerBeat);
            return Default ?? ZeroOf(Kind);
        }

        public static object ZeroOf(ParamKind kind)
        {
            switch (kind)
            {
                case ParamKind.Bool: return false;
                case ParamKind.Choice:
                case ParamKind.Text: return "";
                default: return 0d;
            }
        }

        /// <summary>Converts any stored value to the canonical type for this kind (double / bool / string) and clamps it.</summary>
        public object Coerce(object value)
        {
            switch (Kind)
            {
                case ParamKind.Bool:
                    if (value is bool) return value;
                    if (value is double) return (double)value != 0d;
                    bool b;
                    return value is string && bool.TryParse((string)value, out b) ? b : (object)false;
                case ParamKind.Choice:
                {
                    string s = value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (Options.Length == 0) return s;
                    for (int i = 0; i < Options.Length; i++)
                        if (string.Equals(Options[i], s, StringComparison.OrdinalIgnoreCase)) return Options[i];
                    return Default is string ? Default : Options[0];
                }
                case ParamKind.Text:
                    return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
                default:
                {
                    double d = Json.JsonRead.ToDouble(value, Json.JsonRead.ToDouble(Default, 0d));
                    if (Kind == ParamKind.Int) d = Math.Round(d);
                    if (d < Min) d = Min;
                    if (d > Max) d = Max;
                    return d;
                }
            }
        }
    }

    /// <summary>Well-known values for <see cref="ParamDef.Bind"/>. The Unity importer maps them onto RhythmNoteData / SequenceActivationData.</summary>
    public static class ParamBindings
    {
        public const string TravelBeats = "travelBeats";
        public const string Damage = "damage";
        public const string Speed = "speed";
        public const string BadWindow = "badWindow";
        public const string GoodWindow = "goodWindow";
        public const string PerfectWindow = "perfectWindow";
        public const string MashPresses = "mashPresses";
        public const string HitEffect = "hitEffect";
        public const string PlayerState = "playerState";
        public const string Movement = "movement";

        public const string SeqVolleys = "seq.volleys";
        public const string SeqInitialTravel = "seq.initialTravel";
        public const string SeqSpeedUp = "seq.speedUp";
        public const string SeqMinTravel = "seq.minTravel";
        public const string SeqReturn = "seq.return";
        public const string SeqAlignToBeat = "seq.alignToBeat";
        public const string SeqDamage = "seq.damage";
        public const string SeqMaxAge = "seq.maxAge";
        /// <summary>Space/comma separated 1-based lane numbers cycled per volley ("" = the note's own lane).</summary>
        public const string SeqLanes = "seq.lanes";
    }

    /// <summary>Well-known simulator archetypes. Custom ones can be registered in the simulator's behaviour registry.</summary>
    public static class Archetypes
    {
        public const string Tap = "tap";
        public const string Hold = "hold";
        public const string Stationary = "stationary";
        public const string StationaryHold = "stationary_hold";
        public const string Mash = "mash";
        public const string PingPong = "pingpong";
    }

    public enum NoteOutput
    {
        /// <summary>Imported as a RhythmNoteData in the chart.</summary>
        Note,
        /// <summary>Imported as a SequenceActivationData (run-time attack that spawns its own notes).</summary>
        Sequence
    }

    /// <summary>Glyph used for the note on the timeline and in the simulator.</summary>
    public enum NoteShape
    {
        Circle,
        Diamond,
        Square,
        Hexagon,
        Triangle,
        Ring,
        Star
    }

    /// <summary>
    /// A note type (gimmick) as the composer sees it: how it looks, how it previews, which parameters it has and how it
    /// turns into game data. Built-ins are defined in code; more can be added as JSON files without rebuilding the app
    /// (see <see cref="NoteTypeRegistry"/> and the README in Assets/LevelComposer).
    /// </summary>
    public sealed class NoteTypeDef
    {
        public string Id = "";
        public string Name = "";
        public string Category = "Moving";
        public string Description = "";
        /// <summary>#RRGGBB.</summary>
        public string Color = "#4FA3FF";
        public NoteShape Shape = NoteShape.Circle;
        /// <summary>Simulator behaviour (see <see cref="Archetypes"/>).</summary>
        public string Archetype = Archetypes.Tap;
        /// <summary>The note has a length (hold end) edited on the timeline.</summary>
        public bool HasLength;
        public double DefaultLengthBeats = 1d;
        public NoteOutput Output = NoteOutput.Note;
        /// <summary>Output = Note: RhythmNoteType name (Normal, Hold, Laser, HoldLaser, Pong, Mash...).</summary>
        public string LegacyType = "Normal";
        /// <summary>Output = Sequence: SequenceKind name (PingPong...).</summary>
        public string SequenceKind = "";
        /// <summary>Optional prefab path in the Unity project; the importer sets it as the note's prefab override.</summary>
        public string Prefab = "";
        /// <summary>Stationary notes appear on their lane marker instead of travelling (drawn differently).</summary>
        public bool Stationary;
        public bool Hidden;
        /// <summary>Where the definition came from ("built-in" or a file path).</summary>
        public string Source = "built-in";
        public List<ParamDef> Params = new List<ParamDef>();

        public ParamDef FindParam(string key)
        {
            for (int i = 0; i < Params.Count; i++)
                if (Params[i].Key == key) return Params[i];
            return null;
        }

        /// <summary>The parameter bound to a well-known field, or null.</summary>
        public ParamDef FindBound(string bind)
        {
            for (int i = 0; i < Params.Count; i++)
                if (Params[i].Bind == bind) return Params[i];
            return null;
        }

        public NoteTypeDef Clone()
        {
            var c = (NoteTypeDef)MemberwiseClone();
            c.Params = new List<ParamDef>();
            for (int i = 0; i < Params.Count; i++) c.Params.Add(Params[i].Clone());
            return c;
        }

        /// <summary>Color as 0..1 floats; falls back to light blue on a bad string.</summary>
        public void GetRgb(out float r, out float g, out float b)
        {
            ParseHex(Color, out r, out g, out b);
        }

        public static bool ParseHex(string hex, out float r, out float g, out float b)
        {
            r = 0.31f; g = 0.64f; b = 1f;
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.TrimStart('#');
            if (h.Length != 6) return false;
            int v;
            if (!int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            r = ((v >> 16) & 0xff) / 255f;
            g = ((v >> 8) & 0xff) / 255f;
            b = (v & 0xff) / 255f;
            return true;
        }
    }
}
