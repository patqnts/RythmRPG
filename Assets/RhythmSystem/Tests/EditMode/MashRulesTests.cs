using NUnit.Framework;

namespace RythmRPG.Rhythm.Tests
{
    public class MashRulesTests
    {
        // travel 2.5 s: Perfect until 1.5 s after spawn (1.0 s before the line), Good until 2.125 s, then Bad.
        [TestCase(2.0, MashGrade.Perfect)]   // cleared with 2 s left
        [TestCase(1.0, MashGrade.Perfect)]   // exactly at the Perfect boundary
        [TestCase(0.9, MashGrade.Good)]
        [TestCase(0.375, MashGrade.Good)]    // exactly at the Good boundary
        [TestCase(0.3, MashGrade.Bad)]
        [TestCase(0.01, MashGrade.Bad)]      // just before the line
        [TestCase(0.0, MashGrade.Perfect)]   // final press right on the hit line
        [TestCase(-0.1, MashGrade.Perfect)]  // still on the line within the grace
        [TestCase(-0.2, MashGrade.Miss)]     // grace over
        public void GradeClear_FollowsClearSpeed(double secondsBeforeHit, MashGrade expected)
        {
            Assert.AreEqual(expected, MashRules.GradeClear(secondsBeforeHit, 2.5d));
        }

        [Test]
        public void GradeClear_ScalesWithTravelTime()
        {
            // 25% of the way through the travel is Perfect whatever the travel time is.
            Assert.AreEqual(MashGrade.Perfect, MashRules.GradeClear(0.75d * 1d, 1d));
            Assert.AreEqual(MashGrade.Perfect, MashRules.GradeClear(0.75d * 4d, 4d));
            Assert.AreEqual(MashGrade.Bad, MashRules.GradeClear(0.05d * 4d, 4d));
        }

        [Test]
        public void PerfectDeadline_IsShareOfTravel()
        {
            Assert.AreEqual(1.5d, MashRules.PerfectDeadlineSeconds(2.5d), 1e-5);
        }

        [Test]
        public void Severity_MatchesDesignThresholds()
        {
            Assert.AreEqual(MashRateSeverity.Error, MashRules.Severity(16, 1d));
            Assert.AreEqual(MashRateSeverity.Ok, MashRules.Severity(16, 2.5d));
            Assert.AreEqual(MashRateSeverity.Warning, MashRules.Severity(8, 1d));
            Assert.AreEqual(MashRateSeverity.Warning, MashRules.Severity(1, 0.2d));
        }

        [Test]
        public void MashNote_IsNotAHold_AndRoundTripsThroughMigration()
        {
            var t = new TempoMap(120d);
            var n = new RhythmNoteData("L", 3d, RhythmNoteType.Mash) { HoldDuration = 2d, MashRequiredPresses = 12 };
            Assert.IsFalse(n.IsHold);
            Assert.AreEqual(3d, n.EndTime, 1e-9);
            NoteInstance i = NoteMigration.ToInstance(n, t);
            Assert.AreEqual(NoteMigration.DefinitionIdMash, i.DefinitionId);
            Assert.AreEqual(0d, i.HoldBeats, 1e-9);
            var back = new RhythmNoteData("L", 0d, RhythmNoteType.Normal);
            NoteMigration.ApplyToLegacy(i, t, back, null);
            Assert.AreEqual(RhythmNoteType.Mash, back.NoteType);
            Assert.AreEqual(12, back.MashRequiredPresses);
            Assert.AreEqual(0d, back.HoldDuration, 1e-9);
        }
    }
}
