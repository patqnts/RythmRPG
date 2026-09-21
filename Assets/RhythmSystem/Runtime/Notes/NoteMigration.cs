using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm
{
    /// <summary>Converts legacy (seconds-based, enum-typed) notes to beat-based NoteInstances.</summary>
    public static class NoteMigration
    {
        public const string DefinitionIdNormal = "normal";
        public const string DefinitionIdHold = "hold";
        public const string DefinitionIdStationary = "stationary";
        public const string DefinitionIdStationaryHold = "stationary_hold";
        public const string DefinitionIdPong = "pong";
        public const string DefinitionIdMash = "mash";

        /// <summary>Returns null for unsupported types (Arrow, Cluster).</summary>
        public static string DefinitionIdFor(RhythmNoteType type)
        {
            switch (type)
            {
                case RhythmNoteType.Normal: return DefinitionIdNormal;
                case RhythmNoteType.Hold: return DefinitionIdHold;
                case RhythmNoteType.Laser: return DefinitionIdStationary;
                case RhythmNoteType.HoldLaser: return DefinitionIdStationaryHold;
                case RhythmNoteType.Pong: return DefinitionIdPong;
                case RhythmNoteType.Mash: return DefinitionIdMash;
                default: return null;
            }
        }

        public static RhythmNoteType LegacyTypeFor(string definitionId)
        {
            switch (definitionId)
            {
                case DefinitionIdHold: return RhythmNoteType.Hold;
                case DefinitionIdStationary: return RhythmNoteType.Laser;
                case DefinitionIdStationaryHold: return RhythmNoteType.HoldLaser;
                case DefinitionIdPong: return RhythmNoteType.Pong;
                case DefinitionIdMash: return RhythmNoteType.Mash;
                default: return RhythmNoteType.Normal;
            }
        }

        /// <summary>Converts one note. Overrides are stored for every legacy per-note value so behavior is unchanged.</summary>
        public static NoteInstance ToInstance(RhythmNoteData note, TempoMap tempo, Action<string> warn = null)
        {
            string defId = DefinitionIdFor(note.NoteType);
            if (defId == null)
            {
                if (warn != null) warn("Skipped unsupported note type " + note.NoteType + " (id " + note.Id + ")");
                return null;
            }

            double hitBeat = tempo.SecondsToBeat(note.HitTime);
            var inst = new NoteInstance
            {
                Id = note.Id,
                LaneId = note.LaneId,
                DefinitionId = defId,
                HitBeat = hitBeat,
                HoldBeats = note.IsHold ? tempo.SecondsToBeat(note.HitTime + Math.Max(0d, note.HoldDuration)) - hitBeat : 0d,
                TravelBeats = new Overridable<double>(tempo.SecondsToBeat(note.HitTime) - tempo.SecondsToBeat(note.HitTime - note.TravelTime)),
                Speed = new Overridable<float>(note.Speed),
                Damage = new Overridable<int>(note.Damage),
                HitEffect = new Overridable<HitEffect>(note.HitEffect),
                PlayerState = new Overridable<PlayerState>(note.PlayerState),
                InitializeMovement = new Overridable<NoteInitializeMovementType>(note.InitializeMovementType),
                StationaryBadWindow = new Overridable<float>(note.StationaryBadWindow),
                StationaryGoodWindow = new Overridable<float>(note.StationaryGoodWindow),
                StationaryPerfectWindow = new Overridable<float>(note.StationaryPerfectWindow),
                MashRequiredPresses = new Overridable<int>(note.MashRequiredPresses)
            };
            for (int i = 0; i < note.Metadata.Count; i++)
            {
                inst.Metadata.Add(note.Metadata[i]);
            }

            return inst;
        }

        public static List<NoteInstance> ToInstances(IEnumerable<RhythmNoteData> notes, TempoMap tempo, Action<string> warn = null)
        {
            var result = new List<NoteInstance>();
            foreach (RhythmNoteData n in notes)
            {
                NoteInstance i = ToInstance(n, tempo, warn);
                if (i != null) result.Add(i);
            }

            return result;
        }

        /// <summary>Round-trip back to legacy seconds (used to prove the migration is lossless for timing).</summary>
        public static double HitSeconds(NoteInstance inst, TempoMap tempo)
        {
            return tempo.BeatToSeconds(inst.HitBeat);
        }

        public static double TravelSeconds(NoteInstance inst, TempoMap tempo)
        {
            double hit = tempo.BeatToSeconds(inst.HitBeat);
            return hit - tempo.BeatToSeconds(inst.HitBeat - inst.TravelBeats.Value);
        }

        /// <summary>
        /// Writes an instance back into a legacy note (inverse of <see cref="ToInstance"/>). Values without an
        /// override fall back to the chart's definition defaults when <paramref name="defaults"/> is given.
        /// Fields the instance does not model (e.g. PrefabOverride) are left as they are on <paramref name="target"/>.
        /// </summary>
        public static void ApplyToLegacy(NoteInstance inst, TempoMap tempo, RhythmNoteData target, RhythmNoteDefinition defaults)
        {
            double hit = tempo.BeatToSeconds(inst.HitBeat);
            target.LaneId = inst.LaneId;
            target.NoteType = LegacyTypeFor(inst.DefinitionId);
            target.HitTime = hit;
            target.HoldDuration = target.IsHold
                ? tempo.BeatToSeconds(inst.HitBeat + Math.Max(0d, inst.HoldBeats)) - hit
                : 0d;

            if (inst.TravelBeats.HasValue)
            {
                target.TravelTime = hit - tempo.BeatToSeconds(inst.HitBeat - inst.TravelBeats.Value);
            }
            else if (defaults != null)
            {
                target.TravelTime = defaults.DefaultTravelTime;
            }

            if (inst.Speed.HasValue) target.Speed = inst.Speed.Value;
            else if (defaults != null) target.Speed = defaults.DefaultSpeed;

            if (inst.Damage.HasValue) target.Damage = inst.Damage.Value;
            else if (defaults != null) target.Damage = defaults.DefaultDamage;

            if (inst.HitEffect.HasValue) target.HitEffect = inst.HitEffect.Value;
            if (inst.PlayerState.HasValue) target.PlayerState = inst.PlayerState.Value;
            if (inst.InitializeMovement.HasValue) target.InitializeMovementType = inst.InitializeMovement.Value;
            if (inst.StationaryBadWindow.HasValue) target.StationaryBadWindow = inst.StationaryBadWindow.Value;
            if (inst.StationaryGoodWindow.HasValue) target.StationaryGoodWindow = inst.StationaryGoodWindow.Value;
            if (inst.StationaryPerfectWindow.HasValue) target.StationaryPerfectWindow = inst.StationaryPerfectWindow.Value;
            if (inst.MashRequiredPresses.HasValue) target.MashRequiredPresses = inst.MashRequiredPresses.Value;

            target.Metadata.Clear();
            for (int i = 0; i < inst.Metadata.Count; i++)
            {
                var m = new RhythmMetadataEntry();
                m.Key = inst.Metadata[i].Key;
                m.Value = inst.Metadata[i].Value;
                target.Metadata.Add(m);
            }
        }
    }
}
