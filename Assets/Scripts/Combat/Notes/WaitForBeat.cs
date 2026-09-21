using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Yield instruction that waits for the next beat (or bar) of the running chart, measured on the audio clock:
    /// <c>yield return new WaitForBeat(runner);</c> or <c>new WaitForBeat(runner, 4)</c> for the next bar in 4/4.
    /// Completes immediately when no chart is running, and when the run ends.
    /// </summary>
    public sealed class WaitForBeat : CustomYieldInstruction
    {
        private readonly RhythmPatternRunner runner;
        private readonly double targetBeat;

        public WaitForBeat(RhythmPatternRunner runner, double divisionBeats = 1d)
        {
            this.runner = runner;
            targetBeat = runner != null && runner.IsRunning
                ? BeatScheduler.NextBoundary(runner.ChartBeat, divisionBeats)
                : double.NegativeInfinity;
        }

        public override bool keepWaiting => runner != null && runner.IsRunning && runner.ChartBeat < targetBeat - 1e-9;
    }
}
