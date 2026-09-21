using System;
using System.Collections.Generic;

namespace RythmRPG.Rhythm
{
    /// <summary>Sparse override: only stored values differ from the definition default.</summary>
    [Serializable]
    public struct Overridable<T>
    {
        public bool HasValue;
        public T Value;

        public Overridable(T value)
        {
            HasValue = true;
            Value = value;
        }

        public T Resolve(T fallback)
        {
            return HasValue ? Value : fallback;
        }

        public void Clear()
        {
            HasValue = false;
            Value = default(T);
        }
    }

    /// <summary>A placed note. Times are in beats (authoritative); seconds are derived via TempoMap.</summary>
    [Serializable]
    public sealed class NoteInstance
    {
        public string Id;
        public string LaneId;
        public string DefinitionId;
        public double HitBeat;
        public double HoldBeats;
        public Overridable<double> TravelBeats;
        public Overridable<float> Speed;
        public Overridable<int> Damage;
        public Overridable<HitEffect> HitEffect;
        public Overridable<PlayerState> PlayerState;
        public Overridable<NoteInitializeMovementType> InitializeMovement;
        public Overridable<float> StationaryBadWindow;
        public Overridable<float> StationaryGoodWindow;
        public Overridable<float> StationaryPerfectWindow;
        public Overridable<int> MashRequiredPresses;
        public List<RhythmMetadataEntry> Metadata = new List<RhythmMetadataEntry>();

        /// <summary>Deep copy (same Id). Use <see cref="CloneWithNewId"/> for paste/duplicate.</summary>
        public NoteInstance Clone()
        {
            var copy = (NoteInstance)MemberwiseClone();
            copy.Metadata = new List<RhythmMetadataEntry>();
            for (int i = 0; i < Metadata.Count; i++)
            {
                var m = new RhythmMetadataEntry();
                m.Key = Metadata[i].Key;
                m.Value = Metadata[i].Value;
                copy.Metadata.Add(m);
            }

            return copy;
        }

        public NoteInstance CloneWithNewId()
        {
            NoteInstance copy = Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            return copy;
        }
    }
}
