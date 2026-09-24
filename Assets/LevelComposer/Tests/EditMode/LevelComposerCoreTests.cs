using System;
using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Json;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Simulation;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;

namespace RythmRPG.LevelComposer.Tests
{
    public class LevelComposerCoreTests
    {
        private static CombatLevel SampleLevel()
        {
            var level = CombatLevel.CreateNew();
            level.Id = "goblin";
            level.Name = "Goblin \"Basic\"";
            level.Music.Bpm = 120;
            level.Music.Loop = "Audio/loop.wav";
            LevelStep s = level.Steps[0];
            s.Notes.Add(new LevelNote { Type = "normal", Lane = 1, Beat = 0 });
            s.Notes.Add(new LevelNote { Type = "hold", Lane = 2, Beat = 1, Length = 2 });
            s.Notes.Add(new LevelNote { Type = "stationary", Lane = 3, Beat = 4 });
            s.Notes.Add(new LevelNote { Type = "stationary_hold", Lane = 4, Beat = 5, Length = 1 });
            s.Notes.Add(new LevelNote { Type = "mash", Lane = 1, Beat = 8 });
            s.Notes[0].Params["damage"] = 3d;
            s.Notes[0].Params["custom"] = "x";
            return level;
        }

        // ------------------------------------------------------------------ JSON

        [Test]
        public void MiniJson_RoundTripsNestedValuesAndEscapes()
        {
            var d = new Dictionary<string, object>();
            d["s"] = "a \"quote\" \\ \n tab\t é";
            d["n"] = 0.1d;
            d["i"] = 42d;
            d["neg"] = -1.5e-3d;
            d["b"] = true;
            d["null"] = null;
            d["arr"] = new List<object> { 1d, "two", new Dictionary<string, object> { { "k", false } } };
            string json = MiniJson.Serialize(d);
            var back = (Dictionary<string, object>)MiniJson.Parse(json);
            Assert.AreEqual(d["s"], back["s"]);
            Assert.AreEqual(0.1d, (double)back["n"], 1e-15);
            Assert.AreEqual(42d, (double)back["i"]);
            Assert.AreEqual(-1.5e-3d, (double)back["neg"], 1e-15);
            Assert.AreEqual(true, back["b"]);
            Assert.IsNull(back["null"]);
            var arr = (List<object>)back["arr"];
            Assert.AreEqual(3, arr.Count);
            Assert.AreEqual(false, ((Dictionary<string, object>)arr[2])["k"]);
        }

        [Test]
        public void MiniJson_AcceptsCommentsAndTrailingCommas()
        {
            var o = (Dictionary<string, object>)MiniJson.Parse("{ // note\n \"a\": [1, 2,], }");
            Assert.AreEqual(2, ((List<object>)o["a"]).Count);
        }

        [Test]
        public void MiniJson_ReportsLineOfError()
        {
            try
            {
                MiniJson.Parse("{\n\"a\": }");
                Assert.Fail("expected an error");
            }
            catch (FormatException e)
            {
                Assert.IsTrue(e.Message.Contains("line 2"), e.Message);
            }
        }

        [Test]
        public void Level_RoundTripsThroughJson()
        {
            CombatLevel level = SampleLevel();
            level.Music.OffsetSeconds = 0.125;
            level.Music.ChartSync = MusicSyncMode.NextBeat;
            level.Preview.PlayerTurnBars = 3;
            level.Steps[0].Animation = "Laser";
            level.Steps[0].LaneCount = 4;
            string json = LevelSerializer.ToJson(level);
            CombatLevel back = LevelSerializer.FromJson(json);
            Assert.AreEqual(json, LevelSerializer.ToJson(back));
            Assert.AreEqual("Goblin \"Basic\"", back.Name);
            Assert.AreEqual(MusicSyncMode.NextBeat, back.Music.ChartSync);
            Assert.AreEqual(5, back.Steps[0].Notes.Count);
            Assert.AreEqual(3d, (double)back.Steps[0].Notes[0].Params["damage"]);
            Assert.AreEqual("x", back.Steps[0].Notes[0].Params["custom"]);
        }

        [Test]
        public void Level_ClampsBadInput()
        {
            string json = "{\"format\":\"rythmrpg.combatlevel\",\"steps\":[{\"lanes\":2,\"notes\":[{\"type\":\"normal\",\"lane\":4,\"beat\":-3}]}]}";
            var warnings = new List<string>();
            CombatLevel level = LevelSerializer.FromJson(json, warnings);
            Assert.AreEqual(2, level.Steps[0].Notes[0].Lane);
            Assert.AreEqual(0d, level.Steps[0].Notes[0].Beat);
            Assert.AreEqual(1, warnings.Count);
        }

        [Test]
        public void Level_RejectsOtherFormats()
        {
            Assert.Throws<FormatException>(() => LevelSerializer.FromJson("{\"format\":\"something.else\"}"));
        }

        [Test]
        public void Paths_AreStoredRelativeToTheLevelFile()
        {
            string root = System.IO.Path.GetTempPath();
            string level = System.IO.Path.Combine(root, "Levels", "a.combatlevel.json");
            string audio = System.IO.Path.Combine(root, "Audio", "loop.wav");
            string rel = LevelPaths.MakeRelative(audio, level);
            Assert.AreEqual("../Audio/loop.wav", rel);
            Assert.AreEqual(System.IO.Path.GetFullPath(audio), LevelPaths.Resolve(rel, level));
        }

        // ------------------------------------------------------------------ note types

        [Test]
        public void Registry_HasTheGameTypes()
        {
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            foreach (string id in new[] { "normal", "hold", "stationary", "stationary_hold", "pong", "mash", "pingpong" })
                Assert.IsNotNull(r.Find(id), id);
            Assert.AreEqual("Laser", r.Find("stationary").LegacyType);
            Assert.AreEqual(NoteOutput.Sequence, r.Find("pingpong").Output);
        }

        [Test]
        public void Registry_LoadsNewGimmickAndOverrides()
        {
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            string json = @"{ ""noteTypes"": [
                { ""id"": ""bomb"", ""extends"": ""normal"", ""name"": ""Bomb"", ""color"": ""#FF3030"", ""shape"": ""square"",
                  ""prefab"": ""Assets/Prefab/Bomb.prefab"",
                  ""params"": [ { ""key"": ""fuse"", ""label"": ""Fuse"", ""kind"": ""seconds"", ""default"": 0.5, ""min"": 0, ""max"": 3 } ] },
                { ""id"": ""normal"", ""color"": ""#000000"", ""params"": [ { ""key"": ""damage"", ""default"": 2 } ] }
            ] }";
            Assert.AreEqual(2, r.LoadJson(json, "test.json"));
            NoteTypeDef bomb = r.Find("bomb");
            Assert.AreEqual("Bomb", bomb.Name);
            Assert.AreEqual(NoteShape.Square, bomb.Shape);
            Assert.IsNotNull(bomb.FindParam("travel"), "extends copies the parent's params");
            Assert.AreEqual(0.5d, (double)bomb.FindParam("fuse").Default);
            Assert.AreEqual("Normal", bomb.LegacyType);
            Assert.AreEqual("#000000", r.Find("normal").Color);
            Assert.AreEqual(2d, (double)r.Find("normal").FindParam("damage").Default);
            Assert.AreEqual(ParamBindings.Damage, r.Find("normal").FindParam("damage").Bind, "override keeps unspecified fields");
        }

        [Test]
        public void Registry_WritesJsonItCanReadBack()
        {
            NoteTypeRegistry a = NoteTypeRegistry.CreateDefault();
            NoteTypeDef mash = a.Find("mash").Clone();
            mash.Id = "mash2";
            string json = MiniJson.Serialize(NoteTypeRegistry.ToJson(mash));
            NoteTypeRegistry b = NoteTypeRegistry.CreateDefault();
            b.LoadJson(json, "x");
            NoteTypeDef back = b.Find("mash2");
            Assert.AreEqual(mash.Params.Count, back.Params.Count);
            Assert.AreEqual(mash.Archetype, back.Archetype);
            Assert.AreEqual(2.5d, back.FindParam("travel").DefaultSeconds, 1e-9);
        }

        [Test]
        public void NoteParams_UseDefaultsAndStaySparse()
        {
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            NoteTypeDef normal = r.Find("normal");
            var tempo = new LevelTempo(120, 4);
            var n = new LevelNote { Type = "normal" };
            Assert.AreEqual(5d, NoteParams.TravelBeats(n, normal, tempo), 1e-9, "2.5 s at 120 BPM");
            NoteParams.Set(n, normal, "damage", 1d, tempo);
            Assert.IsFalse(n.HasParam("damage"), "default values are not stored");
            NoteParams.Set(n, normal, "damage", 500d, tempo);
            Assert.AreEqual(99d, (double)n.Params["damage"], "clamped to max");
        }

        // ------------------------------------------------------------------ editing

        [Test]
        public void Session_AddMoveUndoRedo()
        {
            var session = new LevelEditSession(NoteTypeRegistry.CreateDefault());
            LevelNote n = session.AddNote("normal", 2, 1.1);
            Assert.AreEqual(1d, n.Beat, 1e-9, "snapped to 1/4");
            session.MoveSelected(1, 1);
            Assert.AreEqual(2d, session.Step.Notes[0].Beat, 1e-9);
            Assert.AreEqual(3, session.Step.Notes[0].Lane);
            session.MoveSelected(0, 10);
            Assert.AreEqual(4, session.Step.Notes[0].Lane, "clamped to the lanes");
            session.Undo();
            Assert.AreEqual(3, session.Step.Notes[0].Lane);
            session.Undo();
            session.Undo();
            Assert.AreEqual(0, session.Step.Notes.Count);
            session.Redo();
            Assert.AreEqual(1, session.Step.Notes.Count);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void Session_GestureIsOneUndoStep()
        {
            var session = new LevelEditSession(NoteTypeRegistry.CreateDefault());
            session.AddNote("normal", 1, 0);
            session.BeginGesture("Drag");
            for (int i = 0; i < 5; i++) session.MoveSelected(0.25, 0);
            session.EndGesture();
            Assert.AreEqual(1.25d, session.Step.Notes[0].Beat, 1e-9);
            session.Undo();
            Assert.AreEqual(0d, session.Step.Notes[0].Beat, 1e-9);
        }

        [Test]
        public void Session_CopyPasteDuplicate()
        {
            var session = new LevelEditSession(NoteTypeRegistry.CreateDefault());
            session.AddNote("normal", 1, 1);
            session.AddNote("normal", 2, 2);
            session.AddNote("normal", 3, 4);
            session.SelectAll();
            session.Copy();
            session.Paste(8);
            List<LevelNote> pasted = session.SelectedNotes();
            pasted.Sort(LevelSerializer.CompareNotes);
            Assert.AreEqual(3, pasted.Count);
            Assert.AreEqual(8d, pasted[0].Beat, 1e-9);
            Assert.AreEqual(9d, pasted[1].Beat, 1e-9);
            Assert.AreEqual(11d, pasted[2].Beat, 1e-9);
            session.Undo();
            Assert.AreEqual(3, session.Step.Notes.Count, "one undo removes the whole paste");
            session.SelectAll();
            session.DuplicateSelected();
            Assert.AreEqual(6, session.Step.Notes.Count);
        }

        [Test]
        public void Session_LaneCountMovesNotesAndTypeChangeDropsParams()
        {
            var session = new LevelEditSession(NoteTypeRegistry.CreateDefault());
            session.AddNote("stationary", 4, 0);
            session.SetParamOnSelected("perfectWindow", 0.2d);
            Assert.AreEqual(1, session.SetLaneCount(2));
            Assert.AreEqual(2, session.Step.Notes[0].Lane);
            session.SelectAll();
            session.SetTypeOfSelected("hold");
            Assert.IsFalse(session.Step.Notes[0].HasParam("perfectWindow"));
            Assert.AreEqual(1d, session.Step.Notes[0].Length, 1e-9, "hold gets its default length");
        }

        [Test]
        public void Session_Steps()
        {
            var session = new LevelEditSession(NoteTypeRegistry.CreateDefault());
            session.AddNote("normal", 1, 0);
            session.DuplicateStep();
            Assert.AreEqual(2, session.Level.Steps.Count);
            Assert.AreEqual(1, session.StepIndex);
            Assert.AreNotEqual(session.Level.Steps[0].Notes[0].Id, session.Level.Steps[1].Notes[0].Id);
            session.MoveStep(-1);
            Assert.AreEqual(0, session.StepIndex);
            session.RemoveStep();
            Assert.AreEqual(1, session.Level.Steps.Count);
            session.Undo();
            Assert.AreEqual(2, session.Level.Steps.Count);
        }

        [Test]
        public void PatternStamps_AreDeterministic()
        {
            var r = new StampRequest { PatternId = "stream", LengthBeats = 4, LaneCount = 4, Seed = 7, StartBeat = 8 };
            List<LevelNote> a = PatternStamps.Generate(r), b = PatternStamps.Generate(r);
            Assert.AreEqual(16, a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Lane, b[i].Lane);
                Assert.AreEqual(a[i].Beat, b[i].Beat);
            }

            Assert.AreEqual(8d, a[0].Beat, 1e-9);
            List<LevelNote> sweep = PatternStamps.Generate(new StampRequest { PatternId = "sweep", LengthBeats = 3, LaneCount = 3 });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 2, 1, 2 }, sweep.ConvertAll(n => n.Lane));
        }

        // ------------------------------------------------------------------ validation

        [Test]
        public void Validator_FlagsCommonProblems()
        {
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            CombatLevel level = CombatLevel.CreateNew();
            LevelStep s = level.Steps[0];
            s.Notes.Add(new LevelNote { Type = "hold", Lane = 1, Beat = 0, Length = 4 });
            s.Notes.Add(new LevelNote { Type = "normal", Lane = 1, Beat = 2 });
            var mash = new LevelNote { Type = "mash", Lane = 2, Beat = 4 };
            mash.Params["presses"] = 40d;
            mash.Params["travel"] = 2d; // 1 s at 120 BPM -> 40 presses/s
            s.Notes.Add(mash);
            s.Notes.Add(new LevelNote { Type = "mystery", Lane = 3, Beat = 1 });
            List<Issue> issues = LevelValidator.Validate(level, r);
            Assert.IsTrue(issues.Exists(i => i.Severity == Severity.Error && i.Message.Contains("hold")), "note inside hold");
            Assert.IsTrue(issues.Exists(i => i.Severity == Severity.Error && i.Message.Contains("presses")), "mash too fast");
            Assert.IsTrue(issues.Exists(i => i.Severity == Severity.Error && i.Message.Contains("mystery")), "unknown type");
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("main loop")), "no music");
            Assert.AreEqual(Severity.Error, issues[0].Severity, "sorted by severity");
        }

        // ------------------------------------------------------------------ simulation

        private static Simulator Run(CombatLevel level, bool auto, double until, Action<Simulator> during = null)
        {
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            FlowPlan plan = FlowPlan.ForStep(level, 0, r, new FlowAudio(), 0);
            var sim = new Simulator { AutoPlay = auto };
            sim.Build(r, LevelTempo.Of(level), plan.Placements);
            sim.ResetTo(plan.TimelineStart);
            for (double t = plan.TimelineStart; t <= until; t += 1d / 60d)
            {
                sim.Advance(t);
                if (during != null) during(sim);
            }

            sim.Advance(until);
            return sim;
        }

        [Test]
        public void Simulator_AutoHitIsAllPerfect()
        {
            CombatLevel level = SampleLevel();
            Simulator sim = Run(level, true, 12);
            Assert.AreEqual(5, sim.Stats.Perfect, "P" + sim.Stats.Perfect + " G" + sim.Stats.Good + " B" + sim.Stats.Bad + " M" + sim.Stats.Miss);
            Assert.AreEqual(0, sim.Stats.Miss);
            Assert.AreEqual(5, sim.Stats.MaxCombo);
            Assert.IsTrue(sim.AllResolved);
        }

        [Test]
        public void Simulator_NoInputIsAllMiss()
        {
            CombatLevel level = SampleLevel();
            Simulator sim = Run(level, false, 12);
            Assert.AreEqual(5, sim.Stats.Miss);
            Assert.AreEqual(3 + 1 + 1 + 1 + 1, sim.Stats.DamageTaken, "damage override on the first note");
        }

        [Test]
        public void Simulator_JudgesManualTiming()
        {
            CombatLevel level = CombatLevel.CreateNew();
            level.Steps[0].Notes.Add(new LevelNote { Type = "normal", Lane = 1, Beat = 2 }); // hit at 1.0 s
            level.Steps[0].Notes.Add(new LevelNote { Type = "normal", Lane = 2, Beat = 4 }); // hit at 2.0 s
            level.Steps[0].Notes.Add(new LevelNote { Type = "normal", Lane = 3, Beat = 6 }); // hit at 3.0 s
            Simulator sim = Run(level, false, 4, s =>
            {
                if (s.Now >= 0.9 && s.Now < 0.92) s.Press(1, 1.03);   // 30 ms late: Perfect
                if (s.Now >= 1.9 && s.Now < 1.92) s.Press(2, 2.1);    // 100 ms late: Bad
                if (s.Now >= 2.5 && s.Now < 2.52) s.Press(3, 2.6);    // 400 ms early: ignored -> Miss later
            });
            Assert.AreEqual(1, sim.Stats.Perfect);
            Assert.AreEqual(1, sim.Stats.Bad);
            Assert.AreEqual(1, sim.Stats.Miss);
        }

        [Test]
        public void Simulator_HoldReleasedEarlyIsMiss_StationaryHoldIsBad()
        {
            CombatLevel level = CombatLevel.CreateNew();
            level.Steps[0].Notes.Add(new LevelNote { Type = "hold", Lane = 1, Beat = 2, Length = 2 });            // 1.0 .. 2.0 s
            level.Steps[0].Notes.Add(new LevelNote { Type = "stationary_hold", Lane = 2, Beat = 2, Length = 2 }); // 1.0 .. 2.0 s
            Simulator sim = Run(level, false, 3, s =>
            {
                if (s.Now >= 0.98 && s.Now < 1.0) { s.Press(1, 1.0); s.Press(2, 0.99); }
                if (s.Now >= 1.5 && s.Now < 1.52) { s.Release(1, 1.5); s.Release(2, 1.5); }
            });
            Assert.AreEqual(1, sim.Stats.Miss);
            Assert.AreEqual(1, sim.Stats.Bad);
        }

        [Test]
        public void Simulator_MashNeedsAllPresses()
        {
            CombatLevel level = CombatLevel.CreateNew();
            var mash = new LevelNote { Type = "mash", Lane = 1, Beat = 8 }; // hit at 4 s, travel 2.5 s
            mash.Params["presses"] = 4d;
            level.Steps[0].Notes.Add(mash);
            int pressed = 0;
            Simulator sim = Run(level, false, 5, s =>
            {
                if (s.Now > 2.0 && pressed < 3) { s.Press(1, s.Now); s.Release(1, s.Now + 0.01); pressed++; }
            });
            Assert.AreEqual(1, sim.Stats.Miss, "3 of 4 presses is not a clear");
            Simulator auto = Run(level, true, 5);
            Assert.AreEqual(1, auto.Stats.Perfect);
        }

        [Test]
        public void Simulator_PingPongRunsItsVolleys()
        {
            CombatLevel level = CombatLevel.CreateNew();
            var pp = new LevelNote { Type = "pingpong", Lane = 2, Beat = 0 };
            pp.Params["volleys"] = 4d;
            pp.Params["lanes"] = "1 2";
            level.Steps[0].Notes.Add(pp);
            Simulator sim = Run(level, true, 20);
            Assert.AreEqual(4, sim.Stats.Perfect);
            SimNote seq = null;
            foreach (SimNote n in sim.Notes) if (n.Def.Id == "pingpong") seq = n;
            Assert.AreEqual(4, seq.Children.Count);
            Assert.AreEqual(1, seq.Children[0].Lane);
            Assert.AreEqual(2, seq.Children[1].Lane);
            Assert.AreEqual("Ping-Pong cleared", seq.ResultText);
            // Aligned to beats: every shot lands on a beat.
            foreach (SimNote c in seq.Children)
                Assert.AreEqual(0d, Math.Abs(c.HitTime / 0.5d - Math.Round(c.HitTime / 0.5d)), 1e-6);

            Simulator missed = Run(level, false, 20);
            Assert.AreEqual(1, missed.Stats.Miss, "a missed deflect ends the attack");
        }

        [Test]
        public void Simulator_ResetToSkipsPastNotes()
        {
            CombatLevel level = SampleLevel();
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            var sim = new Simulator { AutoPlay = true };
            sim.Build(r, LevelTempo.Of(level), FlowPlan.ForStep(level, 0, r, new FlowAudio(), 0).Placements);
            sim.ResetTo(1.8); // after the first two notes' hits (0 s and 0.5 s); the mash is already on its way
            for (double t = 1.8; t < 8; t += 0.01) sim.Advance(t);
            Assert.AreEqual(3, sim.Stats.Perfect);
            Assert.AreEqual(0, sim.Stats.Miss);
        }

        // ------------------------------------------------------------------ flow

        [Test]
        public void Flow_FirstChartLandsOnLoopBar_LikeTheGame()
        {
            // Documented case: 225 BPM, 2.5 s travel, bar sync -> first projectile 0.70 s into the loop.
            CombatLevel level = CombatLevel.CreateNew();
            level.Music.Bpm = 225;
            var n = new LevelNote { Type = "normal", Lane = 1, Beat = 0 };
            n.Params["travel"] = 2.5d / (60d / 225d);
            level.Steps[0].Notes.Add(n);
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            FlowPlan plan = FlowPlan.ForLevel(level, r, new FlowAudio { IntroSeconds = 4, LoopSeconds = 32 * (60d / 225d) * 4, EndSeconds = 3 });
            FlowSegment first = plan.Segments.Find(s => s.Kind == FlowSegmentKind.EnemyStep);
            Assert.AreEqual(4d + 3.2d, first.Zero, 1e-6, "beat 0 on the 3rd bar line of the loop");
            Assert.AreEqual(0.7d, first.Zero - 2.5d - 4d, 1e-6);
            Assert.AreEqual(FlowSegmentKind.Intro, plan.Segments[0].Kind);
            Assert.AreEqual(FlowSegmentKind.End, plan.Segments[plan.Segments.Count - 1].Kind);
            double bar = 4 * 60d / 225d;
            double k = (plan.EndStart - plan.LoopStart) / bar;
            Assert.AreEqual(Math.Round(k), k, 1e-6, "end starts on a bar line");
        }

        [Test]
        public void Flow_StepsAndPlayerTurnsAlternate()
        {
            CombatLevel level = SampleLevel();
            level.Steps.Add(level.Steps[0].CloneWithNewIds());
            level.Preview.PlayerTurnBars = 2;
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            FlowPlan plan = FlowPlan.ForLevel(level, r, new FlowAudio { LoopSeconds = 16 });
            var kinds = plan.Segments.ConvertAll(s => s.Kind);
            CollectionAssert.AreEqual(new[] { FlowSegmentKind.EnemyStep, FlowSegmentKind.PlayerTurn, FlowSegmentKind.EnemyStep, FlowSegmentKind.PlayerTurn, FlowSegmentKind.End }, kinds);
            for (int i = 1; i < plan.Segments.Count; i++)
                Assert.IsTrue(plan.Segments[i].Start >= plan.Segments[i - 1].Start - 1e-9);
            FlowSegment turn = plan.Segments[1];
            Assert.AreEqual(1d, plan.PlayerTurnWeight(turn.Start + 1d), 1e-9);
            Assert.AreEqual(0d, plan.PlayerTurnWeight(turn.Start - 0.1d), 1e-9);
            Assert.AreEqual(2, plan.Placements.Count);
            Assert.IsTrue(plan.Placements[1].Zero > plan.Placements[0].Zero);
        }

        [Test]
        public void Flow_StepPreviewPlaysLoopUnderTheLeadIn()
        {
            CombatLevel level = SampleLevel();
            level.Music.OffsetSeconds = 0.5;
            NoteTypeRegistry r = NoteTypeRegistry.CreateDefault();
            FlowPlan plan = FlowPlan.ForStep(level, 0, r, new FlowAudio { LoopSeconds = 8 }, 1);
            Assert.AreEqual(-2.5d, plan.TimelineStart, 1e-9);
            Assert.IsTrue(plan.LoopStart <= plan.TimelineStart);
            Assert.AreEqual(0.5d + 2d, plan.LoopClipTime(0d), 1e-9, "offset + one bar at 120 BPM");
            Assert.IsFalse(double.IsNaN(plan.LoopClipTime(-2.5d)));
        }

        [Test]
        public void LaneKeys_MatchTheGameLayout()
        {
            Assert.AreEqual("J", LaneKeys.KeyName(1, 1));
            Assert.AreEqual("S", LaneKeys.KeyName(2, 1));
            Assert.AreEqual("K", LaneKeys.KeyName(3, 3));
            Assert.AreEqual("A", LaneKeys.KeyName(4, 1));
            Assert.AreEqual(0, LaneKeys.LaneForSlot(3, 1), "A is unused with 3 lanes");
            Assert.AreEqual(3, LaneKeys.LaneForSlot(3, 4));
        }
    }
}
