using System;

namespace RythmRPG.Rhythm
{
    public enum SyncPolicy
    {
        Immediate,
        NextBeat,
        NextHalfBeat,
        NextMeasure
    }

    /// <summary>Pure helpers used by WaitForBeat-style scheduling.</summary>
    public static class BeatSync
    {
        private const double Epsilon = 1e-6;

        /// <summary>Returns the beat at which an action requested at <paramref name="currentBeat"/> should fire.</summary>
        public static double ResolveTargetBeat(double currentBeat, SyncPolicy policy, int beatsPerMeasure)
        {
            switch (policy)
            {
                case SyncPolicy.Immediate:
                    return currentBeat;
                case SyncPolicy.NextBeat:
                    return NextBoundary(currentBeat, 1d);
                case SyncPolicy.NextHalfBeat:
                    return NextBoundary(currentBeat, 0.5d);
                case SyncPolicy.NextMeasure:
                    return NextBoundary(currentBeat, Math.Max(1, beatsPerMeasure));
                default:
                    return currentBeat;
            }
        }

        private static double NextBoundary(double beat, double size)
        {
            double q = beat / size;
            double rounded = Math.Round(q);
            // Already (within epsilon) on a boundary: fire on it rather than skipping a full unit.
            if (Math.Abs(q - rounded) * size < Epsilon) return rounded * size;
            return Math.Ceiling(q) * size;
        }
    }
}
