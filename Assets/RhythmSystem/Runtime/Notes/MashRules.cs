using System;

namespace RythmRPG.Rhythm
{
    public enum MashGrade
    {
        Miss,
        Bad,
        Good,
        Perfect
    }

    public enum MashRateSeverity
    {
        Ok,
        Warning,
        Error
    }

    /// <summary>
    /// Pure scoring and authoring-limit rules for the Mash note (unit tested; no Unity dependency).
    /// A Mash note travels like a moving note; the player presses its lane key the required number of times to
    /// destroy it before it crosses the hit line. The faster the clear, the better the grade; a final press that
    /// lands while the note is on the hit line still counts as Perfect.
    /// </summary>
    public static class MashRules
    {
        public const int DefaultRequiredPresses = 8;
        /// <summary>Clearing within this share of the travel time (spawn to hit line) is Perfect.</summary>
        public const float DefaultPerfectProgress = 0.6f;
        /// <summary>Clearing within this share of the travel time is Good; later (still before the line) is Bad.</summary>
        public const float DefaultGoodProgress = 0.85f;
        /// <summary>How long the note stays on the hit line, still clearable as Perfect, before it counts as a miss.</summary>
        public const double LineGraceSeconds = 0.15d;
        public const double WarnPressesPerSecond = 7d;
        public const double ErrorPressesPerSecond = 10d;
        public const double ShortWindowSeconds = 0.3d;

        /// <param name="secondsBeforeHit">Hit time minus the time of the clearing press (negative once the note is past the line).</param>
        /// <param name="travelSeconds">Time the note takes from spawn to the hit line.</param>
        public static MashGrade GradeClear(double secondsBeforeHit, double travelSeconds, float perfectProgress, float goodProgress)
        {
            if (secondsBeforeHit < -LineGraceSeconds) return MashGrade.Miss;
            if (secondsBeforeHit <= 0d) return MashGrade.Perfect;
            double progress = 1d - secondsBeforeHit / Math.Max(0.0001d, travelSeconds);
            if (progress <= perfectProgress) return MashGrade.Perfect;
            if (progress <= goodProgress) return MashGrade.Good;
            return MashGrade.Bad;
        }

        public static MashGrade GradeClear(double secondsBeforeHit, double travelSeconds)
        {
            return GradeClear(secondsBeforeHit, travelSeconds, DefaultPerfectProgress, DefaultGoodProgress);
        }

        /// <summary>Latest moment (seconds after spawn) at which a clear is still Perfect by speed.</summary>
        public static double PerfectDeadlineSeconds(double travelSeconds)
        {
            return Math.Max(0d, travelSeconds) * DefaultPerfectProgress;
        }

        public static double PressesPerSecond(int required, double windowSeconds)
        {
            return Math.Max(1, required) / Math.Max(0.0001d, windowSeconds);
        }

        /// <summary>Rate check over the time the note is on its way (spawn to hit line).</summary>
        public static MashRateSeverity Severity(int required, double windowSeconds)
        {
            double rate = PressesPerSecond(required, windowSeconds);
            if (rate > ErrorPressesPerSecond) return MashRateSeverity.Error;
            if (rate > WarnPressesPerSecond || windowSeconds < ShortWindowSeconds) return MashRateSeverity.Warning;
            return MashRateSeverity.Ok;
        }
    }
}
