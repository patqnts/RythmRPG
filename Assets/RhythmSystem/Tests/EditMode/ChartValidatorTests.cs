using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Editing;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class ChartValidatorTests
    {
        private static ChartSession NewSession()
        {
            var s = new ChartSession(new TempoMap(120d, 4), new List<string> { "L1", "L2" });
            s.SnapEnabled = false;
            return s;
        }

        private static bool Has(List<ValidationIssue> issues, string code)
        {
            for (int i = 0; i < issues.Count; i++) if (issues[i].Code == code) return true;
            return false;
        }

        [Test]
        public void EmptyChart_IsInfoOnly()
        {
            List<ValidationIssue> issues = ChartValidator.Validate(NewSession());
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(IssueSeverity.Info, issues[0].Severity);
        }

        [Test]
        public void CleanChart_HasNoIssues()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdNormal);
            s.AddNote("L1", 2d, NoteMigration.DefinitionIdNormal);
            s.AddNote("L2", 2d, NoteMigration.DefinitionIdNormal);
            Assert.AreEqual(0, ChartValidator.Validate(s).Count);
        }

        [Test]
        public void StackedNotes_AreFlagged()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdNormal);
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdStationary);
            Assert.IsTrue(Has(ChartValidator.Validate(s), "stacked"));
        }

        [Test]
        public void NotesTooCloseInLane_AreFlagged()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdNormal);
            s.AddNote("L1", 1.1d, NoteMigration.DefinitionIdNormal); // 0.05 s at 120 bpm
            Assert.IsTrue(Has(ChartValidator.Validate(s), "too-close"));
        }

        [Test]
        public void HoldOverlappingNextNote_IsWarning()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdHold, 2d);
            s.AddNote("L1", 2d, NoteMigration.DefinitionIdNormal);
            List<ValidationIssue> issues = ChartValidator.Validate(s);
            Assert.IsTrue(Has(issues, "hold-overlap"));
        }

        [Test]
        public void Mash_NeedsNoHold_AndDefaultTravelIsFine()
        {
            ChartSession s = NewSession();
            s.AddNote("L2", 4d, NoteMigration.DefinitionIdMash); // 8 presses over the default 2.5 s travel
            Assert.AreEqual(0, ChartValidator.Validate(s).Count);
        }

        [Test]
        public void FastMash_16PressesInOneSecondOfTravel_IsError()
        {
            ChartSession s = NewSession();
            NoteInstance n = s.AddNote("L1", 4d, NoteMigration.DefinitionIdMash);
            s.ModifySelected("mash", x =>
            {
                x.MashRequiredPresses = new Overridable<int>(16);
                x.TravelBeats = new Overridable<double>(2d); // 2 beats = 1 s at 120 bpm
            });
            Assert.IsTrue(Has(ChartValidator.Validate(s), "mash-too-fast"));
        }

        [Test]
        public void NoteHittingWhileMashTravels_InSameLane_IsWarned()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 8d, NoteMigration.DefinitionIdMash);          // travels beats 3..8 (2.5 s)
            s.AddNote("L1", 6d, NoteMigration.DefinitionIdNormal);        // inside the travel
            s.AddNote("L2", 6d, NoteMigration.DefinitionIdNormal);        // other lane: fine
            s.AddNote("L1", 12d, NoteMigration.DefinitionIdNormal);       // later: fine
            List<ValidationIssue> issues = ChartValidator.Validate(s);
            int count = 0;
            for (int i = 0; i < issues.Count; i++) if (issues[i].Code == "mash-lane-busy") count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void HoldWithoutLength_AndAfterAudio_AreWarnings()
        {
            ChartSession s = NewSession();
            s.AddNote("L1", 1d, NoteMigration.DefinitionIdHold, 0d);
            s.AddNote("L2", 100d, NoteMigration.DefinitionIdNormal); // 50 s
            List<ValidationIssue> issues = ChartValidator.Validate(s, 10d);
            Assert.IsTrue(Has(issues, "hold-no-length"));
            Assert.IsTrue(Has(issues, "after-audio"));
            Assert.IsFalse(Has(ChartValidator.Validate(s, 0d), "after-audio"));
        }

        [Test]
        public void WorstByNote_PicksHighestSeverity()
        {
            var issues = new List<ValidationIssue>
            {
                new ValidationIssue { Severity = IssueSeverity.Warning, NoteId = "a" },
                new ValidationIssue { Severity = IssueSeverity.Error, NoteId = "a" },
                new ValidationIssue { Severity = IssueSeverity.Info, NoteId = "b" }
            };
            Dictionary<string, IssueSeverity> map = ChartValidator.WorstByNote(issues);
            Assert.AreEqual(IssueSeverity.Error, map["a"]);
            Assert.AreEqual(IssueSeverity.Info, map["b"]);
        }
    }
}
