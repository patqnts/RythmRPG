using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;

namespace RythmRPG.LevelComposer.Simulation
{
    /// <summary>A scheduled auto-play input (perfect play).</summary>
    public struct AutoInput
    {
        public double Time;
        public bool Press;
        public int Lane;
        public SimNote Note;
    }

    /// <summary>What behaviours may do to the running simulation.</summary>
    public interface ISimContext
    {
        LevelTempo Tempo { get; }
        JudgementWindows Windows { get; }
        double Now { get; }
        /// <summary>Resolves a note: records the judgement, updates stats (unless it is a sequence) and emits an event.</summary>
        void Resolve(SimNote note, Judgement judgement, double time, string text, double error = 0d);
        void Emit(SimEventKind kind, SimNote note, double time, Judgement judgement, string text);
        /// <summary>Spawns a generated note (e.g. a Ping-Pong shot) that behaves like <paramref name="archetype"/>.</summary>
        SimNote SpawnChild(SimNote parent, int lane, double spawnTime, double hitTime, string archetype, int damage);
        bool IsKeyHeld(int lane);
    }

    /// <summary>
    /// How one archetype of note plays in the preview. Implement this (and register it in <see cref="SimBehaviours"/>)
    /// to preview a brand-new gimmick; note types choose a behaviour through their "archetype".
    /// </summary>
    public interface ISimBehaviour
    {
        /// <summary>Called once when the simulation is built (read parameters into the note).</summary>
        void Setup(SimNote note, ISimContext ctx);
        /// <summary>Called when the note appears (spawn time reached).</summary>
        void OnSpawn(SimNote note, ISimContext ctx);
        /// <summary>Can the note take a lane press at <paramref name="t"/>? Lower priority wins between notes in one lane.</summary>
        bool CanTakePress(SimNote note, double t, ISimContext ctx, out double priority);
        void OnPress(SimNote note, double t, ISimContext ctx);
        void OnRelease(SimNote note, double t, ISimContext ctx);
        /// <summary>Time-based updates (auto-miss, hold completion...). Called often; must be idempotent.</summary>
        void Tick(SimNote note, double t, ISimContext ctx);
        /// <summary>Perfect inputs for auto-hit, added when the note spawns.</summary>
        void BuildAutoInputs(SimNote note, ISimContext ctx, List<AutoInput> into);
        /// <summary>Sequences: a note this one spawned was resolved.</summary>
        void OnChildResolved(SimNote parent, SimNote child, double t, ISimContext ctx);
    }

    /// <summary>Archetype name to behaviour factory. Unknown archetypes play as taps.</summary>
    public static class SimBehaviours
    {
        private static readonly Dictionary<string, Func<ISimBehaviour>> factories = new Dictionary<string, Func<ISimBehaviour>>(StringComparer.OrdinalIgnoreCase)
        {
            { Archetypes.Tap, () => new TapBehaviour() },
            { Archetypes.Hold, () => new HoldBehaviour() },
            { Archetypes.Stationary, () => new StationaryBehaviour() },
            { Archetypes.StationaryHold, () => new StationaryHoldBehaviour() },
            { Archetypes.Mash, () => new MashBehaviour() },
            { Archetypes.PingPong, () => new PingPongBehaviour() },
        };

        public static void Register(string archetype, Func<ISimBehaviour> factory)
        {
            if (string.IsNullOrEmpty(archetype) || factory == null) throw new ArgumentException("archetype and factory are required");
            factories[archetype] = factory;
        }

        public static bool IsRegistered(string archetype) { return archetype != null && factories.ContainsKey(archetype); }

        public static ISimBehaviour Create(string archetype)
        {
            Func<ISimBehaviour> f;
            return archetype != null && factories.TryGetValue(archetype, out f) ? f() : new TapBehaviour();
        }
    }

    /// <summary>Base with no-op defaults.</summary>
    public abstract class SimBehaviourBase : ISimBehaviour
    {
        public virtual void Setup(SimNote note, ISimContext ctx) { }
        public virtual void OnSpawn(SimNote note, ISimContext ctx) { }
        public virtual bool CanTakePress(SimNote note, double t, ISimContext ctx, out double priority) { priority = 0d; return false; }
        public virtual void OnPress(SimNote note, double t, ISimContext ctx) { }
        public virtual void OnRelease(SimNote note, double t, ISimContext ctx) { }
        public virtual void Tick(SimNote note, double t, ISimContext ctx) { }
        public virtual void BuildAutoInputs(SimNote note, ISimContext ctx, List<AutoInput> into) { }
        public virtual void OnChildResolved(SimNote parent, SimNote child, double t, ISimContext ctx) { }

        protected static void AddAuto(List<AutoInput> into, SimNote n, double time, bool press)
        {
            into.Add(new AutoInput { Time = time, Press = press, Lane = n.Lane, Note = n });
        }
    }

    /// <summary>Moving note: judged by time error at the hit line (game: NoteObject + JudgementConfig).</summary>
    public class TapBehaviour : SimBehaviourBase
    {
        public override bool CanTakePress(SimNote n, double t, ISimContext ctx, out double priority)
        {
            priority = Math.Abs(t - n.HitTime);
            if (n.State != SimNoteState.Active || t < n.SpawnTime) return false;
            double limit = ctx.Windows.PressInMissRangeCountsAsMiss ? ctx.Windows.Miss : ctx.Windows.Bad;
            return priority <= limit;
        }

        public override void OnPress(SimNote n, double t, ISimContext ctx)
        {
            double err = t - n.HitTime;
            Judgement j = ctx.Windows.Evaluate(err);
            if (j == Judgement.Miss) ctx.Resolve(n, Judgement.Miss, t, err < 0 ? "Too early" : "Too late", err);
            else OnGoodPress(n, t, j, err, ctx);
        }

        protected virtual void OnGoodPress(SimNote n, double t, Judgement j, double err, ISimContext ctx)
        {
            ctx.Resolve(n, j, t, TimingText(err, j), err);
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Active && t > n.HitTime + ctx.Windows.Bad)
                ctx.Resolve(n, Judgement.Miss, n.HitTime + ctx.Windows.Bad, "Missed", ctx.Windows.Bad);
        }

        public override void BuildAutoInputs(SimNote n, ISimContext ctx, List<AutoInput> into)
        {
            AddAuto(into, n, n.HitTime, true);
            AddAuto(into, n, n.HitTime + 0.06d, false);
        }

        public static string TimingText(double err, Judgement j)
        {
            if (j == Judgement.Perfect) return "";
            return err < 0 ? "Early" : "Late";
        }
    }

    /// <summary>Moving hold: judged at the head, then the key must stay down until the tail ends (game: HoldNoteObject).</summary>
    public class HoldBehaviour : TapBehaviour
    {
        protected override void OnGoodPress(SimNote n, double t, Judgement j, double err, ISimContext ctx)
        {
            n.State = SimNoteState.Holding;
            n.Result = j;
            n.ErrorSeconds = err;
            n.HoldStartTime = t;
            ctx.Emit(SimEventKind.HoldStart, n, t, j, TimingText(err, j));
        }

        public override void OnRelease(SimNote n, double t, ISimContext ctx)
        {
            if (n.State != SimNoteState.Holding) return;
            if (t < n.EndTime - 1e-3) ctx.Resolve(n, Judgement.Miss, t, "Released early", n.ErrorSeconds);
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Holding && t >= n.EndTime)
                ctx.Resolve(n, n.Result, n.EndTime, "Held", n.ErrorSeconds);
            else base.Tick(n, t, ctx);
        }

        public override void BuildAutoInputs(SimNote n, ISimContext ctx, List<AutoInput> into)
        {
            AddAuto(into, n, n.HitTime, true);
            AddAuto(into, n, n.EndTime + 0.02d, false);
        }
    }

    /// <summary>Stationary: charges on the lane marker; press before the charge completes (game: StationaryNote).</summary>
    public class StationaryBehaviour : SimBehaviourBase
    {
        public override void Setup(SimNote n, ISimContext ctx)
        {
            LevelTempo tempo = ctx.Tempo;
            n.PerfectWindow = NoteParams.GetBound(n.Source, n.Def, ParamBindings.PerfectWindow, tempo, 0.1d);
            n.GoodWindow = NoteParams.GetBound(n.Source, n.Def, ParamBindings.GoodWindow, tempo, 0.25d);
            n.BadWindow = NoteParams.GetBound(n.Source, n.Def, ParamBindings.BadWindow, tempo, 0.45d);
        }

        public override bool CanTakePress(SimNote n, double t, ISimContext ctx, out double priority)
        {
            priority = Math.Abs(n.HitTime - t);
            return n.State == SimNoteState.Active && t >= n.SpawnTime && t <= n.HitTime + 1e-6;
        }

        public override void OnPress(SimNote n, double t, ISimContext ctx)
        {
            double e = Math.Max(0d, n.HitTime - t);
            Judgement j = e <= n.PerfectWindow ? Judgement.Perfect : e <= n.GoodWindow ? Judgement.Good : e <= n.BadWindow ? Judgement.Bad : Judgement.Miss;
            if (j == Judgement.Miss) ctx.Resolve(n, Judgement.Miss, t, "Too early", -e);
            else OnGoodPress(n, t, j, -e, ctx);
        }

        protected virtual void OnGoodPress(SimNote n, double t, Judgement j, double err, ISimContext ctx)
        {
            ctx.Resolve(n, j, t, j == Judgement.Perfect ? "" : "Early", err);
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Active && t > n.HitTime + 1e-6)
                ctx.Resolve(n, Judgement.Miss, n.HitTime, "Too late", 0d);
        }

        public override void BuildAutoInputs(SimNote n, ISimContext ctx, List<AutoInput> into)
        {
            AddAuto(into, n, n.HitTime - 0.005d, true);
            AddAuto(into, n, n.HitTime + 0.06d, false);
        }
    }

    /// <summary>Stationary hold: charge, press, keep holding to the end; releasing early is Bad (game: StationaryHoldNote).</summary>
    public class StationaryHoldBehaviour : StationaryBehaviour
    {
        protected override void OnGoodPress(SimNote n, double t, Judgement j, double err, ISimContext ctx)
        {
            n.State = SimNoteState.Holding;
            n.Result = j;
            n.ErrorSeconds = err;
            n.HoldStartTime = t;
            ctx.Emit(SimEventKind.HoldStart, n, t, j, j == Judgement.Perfect ? "" : "Early");
        }

        public override void OnRelease(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Holding && t < n.EndTime - 1e-3)
                ctx.Resolve(n, Judgement.Bad, t, "Released early", n.ErrorSeconds);
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Holding && t >= n.EndTime)
                ctx.Resolve(n, n.Result, n.EndTime, "Held", n.ErrorSeconds);
            else base.Tick(n, t, ctx);
        }

        public override void BuildAutoInputs(SimNote n, ISimContext ctx, List<AutoInput> into)
        {
            AddAuto(into, n, n.HitTime - 0.005d, true);
            AddAuto(into, n, n.EndTime + 0.02d, false);
        }
    }

    /// <summary>Mash: press the lane key N times before the note reaches the line; faster clears grade higher (game: MashNote + MashRules).</summary>
    public class MashBehaviour : SimBehaviourBase
    {
        // Same values as RythmRPG.Rhythm.MashRules.
        public const double PerfectProgress = 0.6d;
        public const double GoodProgress = 0.85d;
        public const double LineGraceSeconds = 0.15d;

        public override void Setup(SimNote n, ISimContext ctx)
        {
            n.RequiredPresses = Math.Max(1, (int)Math.Round(NoteParams.GetBound(n.Source, n.Def, ParamBindings.MashPresses, ctx.Tempo, 8d)));
        }

        public override bool CanTakePress(SimNote n, double t, ISimContext ctx, out double priority)
        {
            priority = Math.Abs(t - n.HitTime);
            return n.State == SimNoteState.Active && t >= n.SpawnTime && t <= n.HitTime + LineGraceSeconds;
        }

        public override void OnPress(SimNote n, double t, ISimContext ctx)
        {
            n.Presses++;
            ctx.Emit(SimEventKind.MashPress, n, t, Judgement.None, n.Presses + "/" + n.RequiredPresses);
            if (n.Presses < n.RequiredPresses) return;
            ctx.Resolve(n, Grade(n.HitTime - t, n.TravelSeconds), t, "Cleared", t - n.HitTime);
        }

        public static Judgement Grade(double secondsBeforeHit, double travelSeconds)
        {
            if (secondsBeforeHit < -LineGraceSeconds) return Judgement.Miss;
            if (secondsBeforeHit <= 0d) return Judgement.Perfect;
            double progress = 1d - secondsBeforeHit / Math.Max(0.0001d, travelSeconds);
            if (progress <= PerfectProgress) return Judgement.Perfect;
            if (progress <= GoodProgress) return Judgement.Good;
            return Judgement.Bad;
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State == SimNoteState.Active && t > n.HitTime + LineGraceSeconds)
                ctx.Resolve(n, Judgement.Miss, n.HitTime + LineGraceSeconds, "Not cleared", LineGraceSeconds);
        }

        public override void BuildAutoInputs(SimNote n, ISimContext ctx, List<AutoInput> into)
        {
            // Evenly spaced over the first 45% of the travel: a comfortable Perfect clear. When playback starts while
            // the note is already on its way, the presses start now instead.
            double start = Math.Max(n.SpawnTime, ctx.Now);
            double span = Math.Max(0.01d, (n.HitTime - start) * 0.45d);
            for (int i = 0; i < n.RequiredPresses; i++)
            {
                double t = start + span * (i + 1) / n.RequiredPresses;
                AddAuto(into, n, t, true);
                AddAuto(into, n, t + Math.Min(0.03d, span / n.RequiredPresses * 0.5d), false);
            }
        }
    }

    /// <summary>Ping-Pong sequence attack (game: PingPongAttack). The shots it spawns are ordinary taps.</summary>
    public class PingPongBehaviour : SimBehaviourBase
    {
        private sealed class State
        {
            public int Volleys = 3;
            public double InitialTravel = 2d, SpeedUp = 0.85d, MinTravel = 0.6d, Return = 0.5d, MaxAge = 45d;
            public bool Align = true;
            public int Damage = 1;
            public List<int> Lanes = new List<int>();
            public int Volley;
            public bool Returning;
            public double NextSpawnAt;
            public SimNote Current;
        }

        public override void Setup(SimNote n, ISimContext ctx)
        {
            LevelTempo tempo = ctx.Tempo;
            var s = new State
            {
                Volleys = Math.Max(1, (int)Math.Round(NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqVolleys, tempo, 3d))),
                InitialTravel = Math.Max(0.1d, NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqInitialTravel, tempo, 2d)),
                SpeedUp = NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqSpeedUp, tempo, 0.85d),
                MinTravel = Math.Max(0.1d, NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqMinTravel, tempo, 0.6d)),
                Return = Math.Max(0d, NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqReturn, tempo, 0.5d)),
                MaxAge = Math.Max(1d, NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqMaxAge, tempo, 45d)),
                Damage = Math.Max(0, (int)Math.Round(NoteParams.GetBound(n.Source, n.Def, ParamBindings.SeqDamage, tempo, 1d))),
            };
            ParamDef align = n.Def != null ? n.Def.FindBound(ParamBindings.SeqAlignToBeat) : null;
            s.Align = align == null || NoteParams.GetBool(n.Source, n.Def, align.Key, tempo, true);
            ParamDef lanes = n.Def != null ? n.Def.FindBound(ParamBindings.SeqLanes) : null;
            if (lanes != null) s.Lanes = LevelValidator.ParseLaneList(NoteParams.GetString(n.Source, n.Def, lanes.Key, tempo));
            if (s.Lanes.Count == 0) s.Lanes.Add(n.Lane);
            n.Custom = s;
            n.EndTime = n.HitTime + EstimateSeconds(s.Volleys, s.InitialTravel, s.SpeedUp, s.MinTravel, s.Return);
        }

        /// <summary>Duration if every volley is deflected (the planner uses it to estimate when the step ends).</summary>
        public static double EstimateSeconds(int volleys, double initialTravel, double speedUp, double minTravel, double returnSeconds)
        {
            double total = 0d;
            for (int v = 0; v < Math.Max(1, volleys); v++)
            {
                total += Math.Max(minTravel, initialTravel * Math.Pow(speedUp, v));
                if (v + 1 < volleys) total += returnSeconds;
            }

            return total;
        }

        public override void OnSpawn(SimNote n, ISimContext ctx)
        {
            State s = (State)n.Custom;
            s.Volley = 0;
            ctx.Emit(SimEventKind.SequenceStart, n, n.SpawnTime, Judgement.None, "Ping-Pong");
            SpawnShot(n, s, n.SpawnTime, ctx);
        }

        private void SpawnShot(SimNote n, State s, double now, ISimContext ctx)
        {
            double travel = Math.Max(s.MinTravel, s.InitialTravel * Math.Pow(s.SpeedUp, s.Volley));
            double hit = now + travel;
            if (s.Align)
            {
                double spb = ctx.Tempo.SecondsPerBeat;
                double aligned = n.StepZero + Math.Ceiling((hit - n.StepZero) / spb - 1e-9) * spb;
                if (aligned > now + 0.05d) hit = aligned;
            }

            int lane = s.Lanes[s.Volley % s.Lanes.Count];
            s.Returning = false;
            s.Current = ctx.SpawnChild(n, lane, now, hit, Archetypes.Tap, s.Damage);
        }

        public override void OnChildResolved(SimNote n, SimNote child, double t, ISimContext ctx)
        {
            State s = (State)n.Custom;
            if (n.State == SimNoteState.Done || child != s.Current) return;
            if (child.Result == Judgement.Miss)
            {
                Finish(n, t, "Ping-Pong failed", ctx);
                return;
            }

            s.Volley++;
            if (s.Volley >= s.Volleys)
            {
                Finish(n, t, "Ping-Pong cleared", ctx);
                return;
            }

            s.Returning = true;
            s.NextSpawnAt = t + s.Return;
        }

        public override void Tick(SimNote n, double t, ISimContext ctx)
        {
            if (n.State != SimNoteState.Active) return;
            State s = (State)n.Custom;
            if (t - n.SpawnTime > s.MaxAge)
            {
                Finish(n, n.SpawnTime + s.MaxAge, "Ping-Pong timed out", ctx);
                return;
            }

            if (s.Returning && t >= s.NextSpawnAt) SpawnShot(n, s, s.NextSpawnAt, ctx);
        }

        private static void Finish(SimNote n, double t, string text, ISimContext ctx)
        {
            n.State = SimNoteState.Done;
            n.ResultTime = t;
            n.ResultText = text;
            n.EndTime = t;
            ctx.Emit(SimEventKind.SequenceEnd, n, t, Judgement.None, text);
        }
    }
}
