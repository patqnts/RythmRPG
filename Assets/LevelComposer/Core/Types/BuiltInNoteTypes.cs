using System.Collections.Generic;

namespace RythmRPG.LevelComposer.Types
{
    /// <summary>
    /// The note types the game supports today. Ids match the game's NoteDefinition ids (NoteMigration.DefinitionId*),
    /// legacy types match RhythmNoteType, and bound parameters match RhythmNoteData / SequenceActivationData fields.
    /// JSON note-type files may override any of these by id (colour, defaults, limits...).
    /// </summary>
    public static class BuiltInNoteTypes
    {
        public const string Normal = "normal";
        public const string Hold = "hold";
        public const string Stationary = "stationary";
        public const string StationaryHold = "stationary_hold";
        public const string Pong = "pong";
        public const string Mash = "mash";
        public const string PingPong = "pingpong";

        public const double DefaultTravelSeconds = 2.5d;

        public static List<NoteTypeDef> Create()
        {
            var list = new List<NoteTypeDef>();

            list.Add(new NoteTypeDef
            {
                Id = Normal, Name = "Default", Category = "Moving", Color = "#4FA8FF", Shape = NoteShape.Circle,
                Archetype = Archetypes.Tap, LegacyType = "Normal",
                Description = "Travels down its lane from the enemy. Press its key when it reaches the hit line.",
                Params = Moving()
            });

            var hold = new NoteTypeDef
            {
                Id = Hold, Name = "Hold", Category = "Moving", Color = "#47D98E", Shape = NoteShape.Circle,
                Archetype = Archetypes.Hold, LegacyType = "Hold", HasLength = true, DefaultLengthBeats = 1d,
                Description = "Travels like Default; press at the line and keep the key down until the tail ends. Releasing early is a Miss.",
                Params = Moving()
            };
            list.Add(hold);

            list.Add(new NoteTypeDef
            {
                Id = Pong, Name = "Pong", Category = "Moving", Color = "#FFB547", Shape = NoteShape.Hexagon,
                Archetype = Archetypes.Tap, LegacyType = "Pong",
                Description = "Ball-style moving note. Currently plays like Default.",
                Params = Moving()
            });

            var mash = new NoteTypeDef
            {
                Id = Mash, Name = "Mash", Category = "Moving", Color = "#FFD84A", Shape = NoteShape.Star,
                Archetype = Archetypes.Mash, LegacyType = "Mash",
                Description = "Smash its key the required number of times to destroy it before it reaches the line. Faster clears grade higher."
            };
            mash.Params.Add(Travel("Clear window", "Time from spawn to the hit line: the player's whole window to clear it."));
            mash.Params.Add(new ParamDef
            {
                Key = "presses", Label = "Presses", Kind = ParamKind.Int, Default = 8d, Min = 1, Max = 60, Step = 1,
                Bind = ParamBindings.MashPresses, Tooltip = "Presses needed to destroy it. Above ~7 per second is demanding, above 10 not reliably possible."
            });
            mash.Params.Add(DamageParam());
            list.Add(mash);

            list.Add(new NoteTypeDef
            {
                Id = Stationary, Name = "Stationary", Category = "Stationary", Color = "#FF6B6B", Shape = NoteShape.Diamond,
                Archetype = Archetypes.Stationary, LegacyType = "Laser", Stationary = true,
                Description = "Charges on the lane marker (outline shrinks) and must be pressed as the charge completes. Late = Miss.",
                Params = StationaryParams()
            });

            list.Add(new NoteTypeDef
            {
                Id = StationaryHold, Name = "Stationary Hold", Category = "Stationary", Color = "#C86BFF", Shape = NoteShape.Diamond,
                Archetype = Archetypes.StationaryHold, LegacyType = "HoldLaser", Stationary = true, HasLength = true, DefaultLengthBeats = 2d,
                Description = "Charges on the lane marker (fill grows), press as it completes and hold until the end. Releasing early is Bad.",
                Params = StationaryParams()
            });

            var pingPong = new NoteTypeDef
            {
                Id = PingPong, Name = "Ping-Pong", Category = "Sequence", Color = "#35D6E8", Shape = NoteShape.Ring,
                Archetype = Archetypes.PingPong, Output = NoteOutput.Sequence, SequenceKind = "PingPong",
                Description = "Sequence attack: a shot comes down, the player deflects it, the enemy returns it faster. Miss one and the attack ends. Placed at its start beat."
            };
            pingPong.Params.Add(new ParamDef { Key = "volleys", Label = "Volleys", Kind = ParamKind.Int, Default = 3d, Min = 1, Max = 30, Step = 1, Bind = ParamBindings.SeqVolleys, Tooltip = "Successful deflects needed to end the attack." });
            pingPong.Params.Add(new ParamDef { Key = "initialTravel", Label = "First travel", Kind = ParamKind.Seconds, Default = 2d, Min = 0.1, Max = 10, Step = 0.05, Bind = ParamBindings.SeqInitialTravel });
            pingPong.Params.Add(new ParamDef { Key = "speedUp", Label = "Speed-up", Kind = ParamKind.Float, Default = 0.85d, Min = 0.3, Max = 1.2, Step = 0.01, Bind = ParamBindings.SeqSpeedUp, Tooltip = "Travel time is multiplied by this after every deflect (below 1 = faster)." });
            pingPong.Params.Add(new ParamDef { Key = "minTravel", Label = "Fastest travel", Kind = ParamKind.Seconds, Default = 0.6d, Min = 0.1, Max = 10, Step = 0.05, Bind = ParamBindings.SeqMinTravel });
            pingPong.Params.Add(new ParamDef { Key = "returnSeconds", Label = "Return time", Kind = ParamKind.Seconds, Default = 0.5d, Min = 0, Max = 5, Step = 0.05, Bind = ParamBindings.SeqReturn, Tooltip = "Time the deflected shot flies back before the enemy fires again." });
            pingPong.Params.Add(new ParamDef { Key = "alignToBeat", Label = "Align to beat", Kind = ParamKind.Bool, Default = true, Bind = ParamBindings.SeqAlignToBeat, Tooltip = "Round each shot's arrival up to the next beat." });
            pingPong.Params.Add(new ParamDef { Key = "lanes", Label = "Lane cycle", Kind = ParamKind.Text, Default = "", Bind = ParamBindings.SeqLanes, Tooltip = "Lane numbers used per volley, e.g. \"2 3 1\". Empty = the lane it is placed on." });
            pingPong.Params.Add(new ParamDef { Key = "damage", Label = "Damage", Kind = ParamKind.Int, Default = 1d, Min = 0, Max = 99, Step = 1, Bind = ParamBindings.SeqDamage });
            pingPong.Params.Add(new ParamDef { Key = "maxAge", Label = "Failsafe (s)", Kind = ParamKind.Seconds, Default = 45d, Min = 1, Max = 600, Step = 1, Bind = ParamBindings.SeqMaxAge, Group = "Advanced", Tooltip = "The attack is cancelled after this long so it can never soft-lock a battle." });
            list.Add(pingPong);

            return list;
        }

        private static ParamDef Travel(string label, string tooltip)
        {
            return new ParamDef
            {
                Key = "travel", Label = label, Kind = ParamKind.Beats, DefaultSeconds = DefaultTravelSeconds,
                Min = 0.25, Max = 64, Step = 0.25, Bind = ParamBindings.TravelBeats, Tooltip = tooltip
            };
        }

        private static ParamDef DamageParam()
        {
            return new ParamDef { Key = "damage", Label = "Damage", Kind = ParamKind.Int, Default = 1d, Min = 0, Max = 99, Step = 1, Bind = ParamBindings.Damage, Tooltip = "Damage to the player on a Miss." };
        }

        private static List<ParamDef> Moving()
        {
            var p = new List<ParamDef>();
            p.Add(Travel("Travel", "Time from spawn (at the enemy) to the hit line. Longer = slower note."));
            p.Add(DamageParam());
            p.Add(new ParamDef
            {
                Key = "movement", Label = "Intro movement", Kind = ParamKind.Choice, Default = "None",
                Options = new[] { "None", "SlowThenBurst", "MissileSCurve" }, Bind = ParamBindings.Movement, Group = "Advanced",
                Tooltip = "Visual path at the start of travel. Arrival time is unchanged."
            });
            AddAdvanced(p);
            return p;
        }

        private static List<ParamDef> StationaryParams()
        {
            var p = new List<ParamDef>();
            p.Add(Travel("Charge time", "Time the charge takes on the lane marker; press as it completes."));
            p.Add(new ParamDef { Key = "perfectWindow", Label = "Perfect window", Kind = ParamKind.Seconds, Default = 0.1d, Min = 0.02, Max = 2, Step = 0.01, Bind = ParamBindings.PerfectWindow, Tooltip = "Seconds before the charge completes that still count as Perfect." });
            p.Add(new ParamDef { Key = "goodWindow", Label = "Good window", Kind = ParamKind.Seconds, Default = 0.25d, Min = 0.02, Max = 2, Step = 0.01, Bind = ParamBindings.GoodWindow });
            p.Add(new ParamDef { Key = "badWindow", Label = "Bad window", Kind = ParamKind.Seconds, Default = 0.45d, Min = 0.02, Max = 3, Step = 0.01, Bind = ParamBindings.BadWindow, Tooltip = "Earlier presses than this are a Miss." });
            p.Add(DamageParam());
            AddAdvanced(p);
            return p;
        }

        private static void AddAdvanced(List<ParamDef> p)
        {
            p.Add(new ParamDef
            {
                Key = "hitEffect", Label = "Hit effect", Kind = ParamKind.Choice, Default = "Default",
                Options = new[] { "Default", "Ghost", "Cluster", "DoubleHit", "Pong" }, Bind = ParamBindings.HitEffect, Group = "Advanced"
            });
            p.Add(new ParamDef
            {
                Key = "playerState", Label = "Player state on hit", Kind = ParamKind.Choice, Default = "Default",
                Options = new[] { "Default", "Freeze", "Nausea", "None" }, Bind = ParamBindings.PlayerState, Group = "Advanced"
            });
        }
    }
}
