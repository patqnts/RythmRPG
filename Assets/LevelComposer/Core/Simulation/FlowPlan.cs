using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Simulation
{
    public enum FlowSegmentKind
    {
        Intro,
        EnemyStep,
        PlayerTurn,
        End
    }

    public sealed class FlowSegment
    {
        public FlowSegmentKind Kind;
        public int StepIndex = -1;
        public double Start;
        public double End;
        /// <summary>EnemyStep: preview time of the chart's beat 0.</summary>
        public double Zero;
        /// <summary>EnemyStep: when the wind-up animation starts (anticipation before the first projectile).</summary>
        public double WindUpStart;
        public int LaneCount = 4;
        public string Label = "";
    }

    /// <summary>Lengths of the loaded music clips (0 = none). Exact seconds (samples / frequency).</summary>
    public struct FlowAudio
    {
        public double IntroSeconds;
        public double LoopSeconds;
        public double EndSeconds;
    }

    /// <summary>
    /// When every piece of music plays and when each step's chart starts, on one preview clock (seconds).
    /// Step preview: just the selected step over the loop. Level preview: intro, then enemy steps and player turns in
    /// order, then the end section, following the game's rules (CombatMusicDirector + RhythmPatternRunner.PlanStart):
    /// each chart's beat 0 lands on the loop's next grid line (bar or beat) after its lead-in, the first chart waits
    /// for the loop, the end starts on the next bar.
    /// </summary>
    public sealed class FlowPlan
    {
        public readonly List<FlowSegment> Segments = new List<FlowSegment>();
        public readonly List<StepPlacement> Placements = new List<StepPlacement>();
        public bool IsLevel;
        public bool HasIntro;
        public double IntroStart;
        public double IntroEnd;
        /// <summary>Preview time where the loop clip's sample 0 plays (may be negative in step mode).</summary>
        public double LoopStart;
        public double LoopLength;
        /// <summary>Time the end clip starts (+infinity in step mode); the loop stops on the same sample.</summary>
        public double EndStart = double.PositiveInfinity;
        public double EndLength;
        public double Duration;
        /// <summary>Crossfade length used for the player-turn stem.</summary>
        public double PlayerTurnFade = 0.5d;
        public double TimelineStart;

        /// <summary>Loop clip position at preview time <paramref name="t"/> (wrapped); NaN before the loop starts or after the end.</summary>
        public double LoopClipTime(double t)
        {
            if (t < LoopStart || t >= EndStart) return double.NaN;
            double local = t - LoopStart;
            if (LoopLength > 0d) local %= LoopLength;
            return local;
        }

        /// <summary>0..1 how much the player-turn stem is faded in at <paramref name="t"/>.</summary>
        public double PlayerTurnWeight(double t)
        {
            double fade = Math.Max(1e-3, PlayerTurnFade);
            double w = 0d;
            foreach (FlowSegment s in Segments)
            {
                if (s.Kind != FlowSegmentKind.PlayerTurn || t < s.Start) continue;
                double peak = Clamp01((Math.Min(t, s.End) - s.Start) / fade);
                if (t <= s.End) w = Math.Max(w, peak);
                else if (t < s.End + fade) w = Math.Max(w, peak * (1d - (t - s.End) / fade));
            }

            return Clamp01(w);
        }

        public FlowSegment SegmentAt(double t)
        {
            FlowSegment best = null;
            foreach (FlowSegment s in Segments)
            {
                if (t >= s.Start && t < s.End) best = s;
            }

            if (best == null && Segments.Count > 0)
            {
                if (t < Segments[0].Start) return Segments[0];
                return Segments[Segments.Count - 1];
            }

            return best;
        }

        /// <summary>Lane count to show / play with at time <paramref name="t"/>.</summary>
        public int LaneCountAt(double t, int fallback)
        {
            FlowSegment s = SegmentAt(t);
            if (s == null) return fallback;
            if (s.Kind == FlowSegmentKind.EnemyStep) return s.LaneCount;
            // Between steps keep the lanes of the last step so the view does not jump.
            int count = fallback;
            foreach (FlowSegment x in Segments)
            {
                if (x.Start > t) break;
                if (x.Kind == FlowSegmentKind.EnemyStep) count = x.LaneCount;
            }

            return count;
        }

        private static double Clamp01(double v) { return v < 0d ? 0d : v > 1d ? 1d : v; }

        // ------------------------------------------------------------------ planning

        /// <summary>Plan for previewing one step: beat 0 at time 0, heard against loop bar <paramref name="loopBar"/>.</summary>
        public static FlowPlan ForStep(CombatLevel level, int stepIndex, NoteTypeRegistry types, FlowAudio audio, int loopBar)
        {
            var plan = new FlowPlan();
            MusicSettings m = level.Music;
            LevelTempo tempo = LevelTempo.Of(level);
            LevelStep step = level.Steps[Math.Max(0, Math.Min(stepIndex, level.Steps.Count - 1))];
            plan.LoopLength = audio.LoopSeconds;
            double clipAtZero = m.OffsetSeconds + Math.Max(0, loopBar) * tempo.BarSeconds;
            if (plan.LoopLength > 0d) clipAtZero %= plan.LoopLength;
            plan.LoopStart = -clipAtZero;
            // Move the loop start back whole passes so the lead-in before beat 0 has music too.
            double lead = LeadSeconds(step, types, tempo);
            if (plan.LoopLength > 0d)
                while (plan.LoopStart > -lead - 0.5d) plan.LoopStart -= plan.LoopLength;
            else
                plan.LoopStart = Math.Min(plan.LoopStart, -lead - 1d);

            double end = ContentEndSeconds(step, types, tempo);
            plan.TimelineStart = -lead;
            plan.Duration = end + 1d;
            plan.Placements.Add(new StepPlacement(step, stepIndex, 0d));
            plan.Segments.Add(new FlowSegment
            {
                Kind = FlowSegmentKind.EnemyStep, StepIndex = stepIndex, Start = -lead, End = plan.Duration, Zero = 0d,
                WindUpStart = -lead - step.Anticipation, LaneCount = step.LaneCount, Label = step.Name
            });
            return plan;
        }

        /// <summary>Plan for previewing the whole level flow.</summary>
        public static FlowPlan ForLevel(CombatLevel level, NoteTypeRegistry types, FlowAudio audio)
        {
            var plan = new FlowPlan { IsLevel = true };
            MusicSettings m = level.Music;
            LevelTempo tempo = LevelTempo.Of(level);
            plan.HasIntro = audio.IntroSeconds > 0d;
            plan.IntroStart = 0d;
            plan.IntroEnd = plan.HasIntro ? audio.IntroSeconds : 0d;
            plan.LoopStart = plan.IntroEnd;
            plan.LoopLength = audio.LoopSeconds;
            plan.EndLength = audio.EndSeconds;
            double grid = GridSeconds(m.ChartSync, tempo);

            if (plan.HasIntro)
                plan.Segments.Add(new FlowSegment { Kind = FlowSegmentKind.Intro, Start = 0d, End = plan.IntroEnd, Label = "Intro" });

            // The first attack may not throw before the loop starts (CombatController.startCombatWithLoop).
            double earliestSpawn = plan.LoopStart;
            double lastEnd = plan.LoopStart;
            for (int i = 0; i < level.Steps.Count; i++)
            {
                LevelStep step = level.Steps[i];
                double lead = LeadSeconds(step, types, tempo);
                double earliestZero = earliestSpawn + lead;
                double zero = NextGridTime(earliestZero, plan.LoopStart, plan.LoopLength, m.OffsetSeconds, grid, m.ChartsWaitForLoop);
                double firstSpawn = zero - lead;
                double end = zero + ContentEndSeconds(step, types, tempo);
                double windUp = firstSpawn - step.Anticipation;
                double segStart = Math.Max(lastEnd, Math.Min(windUp, firstSpawn));
                plan.Placements.Add(new StepPlacement(step, i, zero));
                plan.Segments.Add(new FlowSegment
                {
                    Kind = FlowSegmentKind.EnemyStep, StepIndex = i, Start = segStart, End = end, Zero = zero,
                    WindUpStart = windUp, LaneCount = step.LaneCount, Label = step.Name
                });

                // Player turn: from the end of the enemy step, for the configured number of bars.
                double turnStart = end;
                double turnEnd = turnStart + Math.Max(0, level.Preview.PlayerTurnBars) * tempo.BarSeconds;
                if (level.Preview.PlayerTurnBars > 0)
                    plan.Segments.Add(new FlowSegment { Kind = FlowSegmentKind.PlayerTurn, StepIndex = i, Start = turnStart, End = turnEnd, Label = "Player turn" });
                lastEnd = turnEnd;
                // Next enemy step: wind-up after the player's turn, then its first projectile.
                double nextAnticipation = i + 1 < level.Steps.Count ? level.Steps[i + 1].Anticipation : 0d;
                earliestSpawn = turnEnd + nextAnticipation;
            }

            // End section on the next bar (or beat) after the last player turn.
            double endGrid = GridSeconds(m.EndSync, tempo);
            plan.EndStart = NextGridTime(lastEnd, plan.LoopStart, plan.LoopLength, m.OffsetSeconds, endGrid, false);
            double endLength = plan.EndLength > 0d ? plan.EndLength : 2d;
            plan.Segments.Add(new FlowSegment { Kind = FlowSegmentKind.End, Start = plan.EndStart, End = plan.EndStart + endLength, Label = "End" });
            plan.Duration = plan.EndStart + endLength;
            plan.TimelineStart = 0d;
            return plan;
        }

        public static double GridSeconds(MusicSyncMode sync, LevelTempo tempo)
        {
            switch (sync)
            {
                case MusicSyncMode.NextBar: return tempo.BarSeconds;
                case MusicSyncMode.NextBeat: return tempo.SecondsPerBeat;
                default: return 0d;
            }
        }

        /// <summary>Seconds from the first spawn to beat 0 (the longest lead-in of notes before the first hit).</summary>
        public static double LeadSeconds(LevelStep step, NoteTypeRegistry types, LevelTempo tempo)
        {
            double first = 0d;
            foreach (LevelNote n in step.Notes)
            {
                NoteTypeDef def = types.Find(n.Type);
                if (def == null) continue;
                first = Math.Min(first, NoteParams.SpawnSeconds(n, def, tempo));
            }

            return -first;
        }

        /// <summary>Chart seconds when the step's content is over (last hit / hold end / sequence estimate, plus the Bad window).</summary>
        public static double ContentEndSeconds(LevelStep step, NoteTypeRegistry types, LevelTempo tempo)
        {
            double end = 0d;
            foreach (LevelNote n in step.Notes)
            {
                NoteTypeDef def = types.Find(n.Type);
                if (def == null) continue;
                double e = tempo.BeatToSeconds(def.HasLength ? n.EndBeat : n.Beat);
                if (def.Output == NoteOutput.Sequence && def.Archetype == Archetypes.PingPong)
                {
                    int volleys = (int)Math.Round(NoteParams.GetBound(n, def, ParamBindings.SeqVolleys, tempo, 3d));
                    e += PingPongBehaviour.EstimateSeconds(volleys,
                        NoteParams.GetBound(n, def, ParamBindings.SeqInitialTravel, tempo, 2d),
                        NoteParams.GetBound(n, def, ParamBindings.SeqSpeedUp, tempo, 0.85d),
                        NoteParams.GetBound(n, def, ParamBindings.SeqMinTravel, tempo, 0.6d),
                        NoteParams.GetBound(n, def, ParamBindings.SeqReturn, tempo, 0.5d)) + volleys * tempo.SecondsPerBeat;
                }
                else if (def.Archetype == Archetypes.Mash)
                {
                    e += MashBehaviour.LineGraceSeconds;
                }

                end = Math.Max(end, e);
            }

            return end + 0.25d;
        }

        /// <summary>
        /// Same rule as CombatSong.NextGridTime: the first grid line at or after <paramref name="earliest"/>, where the
        /// loop first starts at <paramref name="loopStart"/> and restarts its grid from <paramref name="offset"/> every pass.
        /// </summary>
        public static double NextGridTime(double earliest, double loopStart, double loopLength, double offset, double gridSeconds, bool waitForLoop)
        {
            double firstDownbeat = loopStart + offset;
            if (gridSeconds <= 0d) return waitForLoop ? Math.Max(earliest, firstDownbeat) : earliest;
            if (earliest < loopStart)
            {
                if (waitForLoop) return firstDownbeat;
                double steps = Math.Floor((firstDownbeat - earliest) / gridSeconds + 1e-6d);
                return firstDownbeat - steps * gridSeconds;
            }

            double elapsed = earliest - loopStart;
            double loopsDone = loopLength > 0d ? Math.Floor(elapsed / loopLength) : 0d;
            double passStart = loopStart + loopsDone * loopLength;
            double inLoop = elapsed - loopsDone * loopLength;
            return passStart + NextBarInLoop(inLoop, offset, gridSeconds, loopLength);
        }

        private static double NextBarInLoop(double clipSeconds, double offset, double barSeconds, double loopLength)
        {
            if (barSeconds <= 0d) return clipSeconds;
            if (loopLength <= 0d) loopLength = double.MaxValue;
            double bars = Math.Ceiling((clipSeconds - offset) / barSeconds - 1e-6d);
            double candidate = offset + bars * barSeconds;
            if (candidate >= loopLength - 1e-6d) candidate = loopLength + offset;
            return candidate;
        }
    }
}
