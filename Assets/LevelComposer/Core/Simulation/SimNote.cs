using System;
using System.Collections.Generic;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Types;

namespace RythmRPG.LevelComposer.Simulation
{
    public enum SimNoteState
    {
        /// <summary>Not spawned yet.</summary>
        Waiting,
        /// <summary>On screen (travelling / charging / sequence running).</summary>
        Active,
        /// <summary>Being held (hold types) after a good press.</summary>
        Holding,
        /// <summary>Resolved (or skipped when playback started after it).</summary>
        Done
    }

    /// <summary>A note (or sequence) during a preview run. Times are simulator seconds.</summary>
    public sealed class SimNote
    {
        public LevelNote Source;
        public NoteTypeDef Def;
        public ISimBehaviour Behaviour;
        public int StepIndex;
        /// <summary>Simulator time of the step's beat 0.</summary>
        public double StepZero;
        public int Lane;
        public double SpawnTime;
        public double HitTime;
        /// <summary>Hold end (= HitTime for notes without a length).</summary>
        public double EndTime;
        public double TravelSeconds;
        public int Damage = 1;

        public SimNoteState State;
        public Judgement Result;
        public double ResultTime;
        public double ErrorSeconds;
        /// <summary>Short text shown with the judgement ("Early", "Released", "Cleared"...).</summary>
        public string ResultText = "";
        public bool Skipped;

        // Behaviour scratch values (mash presses, stationary windows, sequence progress...).
        public int Presses;
        public int RequiredPresses;
        public double PerfectWindow, GoodWindow, BadWindow;
        public double HoldStartTime;
        public double AutoCursor;

        /// <summary>Sequence notes: the shots they spawned. Shots: the sequence that owns them.</summary>
        public SimNote Parent;
        public readonly List<SimNote> Children = new List<SimNote>();
        public object Custom;

        public string Id { get { return Source != null ? Source.Id : ""; } }
        public bool IsResolved { get { return State == SimNoteState.Done; } }
        public bool IsHoldType { get { return EndTime > HitTime + 1e-6; } }
        public bool IsGenerated { get { return Parent != null; } }

        /// <summary>0 at spawn, 1 at the hit line / charge complete (can exceed 1 past the line).</summary>
        public double Progress(double t)
        {
            return (t - SpawnTime) / Math.Max(1e-4, TravelSeconds);
        }

        /// <summary>Progress of the hold tail end (reaches 1 at <see cref="EndTime"/>).</summary>
        public double TailProgress(double t)
        {
            return (t - (EndTime - TravelSeconds)) / Math.Max(1e-4, TravelSeconds);
        }
    }

    /// <summary>A judged moment (for popups, sounds and the log).</summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public double Time;
        public int Lane;
        public int StepIndex;
        public Judgement Judgement;
        public string Text;
        public SimNote Note;
    }

    public enum SimEventKind
    {
        Judged,
        HoldStart,
        MashPress,
        SequenceStart,
        SequenceEnd,
        KeyPress,
        KeyRelease
    }
}
