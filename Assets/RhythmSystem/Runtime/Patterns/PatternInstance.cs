using System;

namespace RythmRPG.Rhythm
{
    public enum ReservationMode
    {
        /// <summary>The pattern coexists with other notes.</summary>
        None,
        /// <summary>The pattern's lanes are reserved for its duration; manual notes there are reported as conflicts.</summary>
        Exclusive
    }

    /// <summary>
    /// A programmed pattern placed on the timeline. It stores only its parameters; the notes it produces are
    /// generated deterministically (see <see cref="PatternExpander"/>) and written into the chart on save.
    /// </summary>
    [Serializable]
    public sealed class PatternInstance
    {
        public string Id;
        public string TemplateId;
        public double StartBeat;
        public double DurationBeats = 4d;
        public int Seed = 1;
        /// <summary>Bit i = lane index i. 0 means every lane.</summary>
        public int LaneMask;
        public ReservationMode Reservation = ReservationMode.Exclusive;

        public PatternInstance Clone()
        {
            return (PatternInstance)MemberwiseClone();
        }

        public double EndBeat { get { return StartBeat + DurationBeats; } }

        public bool UsesLane(int laneIndex)
        {
            return LaneMask == 0 || (laneIndex >= 0 && laneIndex < 31 && (LaneMask & (1 << laneIndex)) != 0);
        }
    }
}
