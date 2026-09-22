using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class CombatResultTests
    {
        private static CombatResultGrading Grading() => ScriptableObject.CreateInstance<CombatResultGrading>();

        [Test]
        public void Combo_BreaksOnMiss_AndTracksMax()
        {
            var stats = new CombatStatsTracker(Grading());
            stats.RecordJudgement(HitJudgement.Perfect, PatternRunMode.EnemyDefense);
            stats.RecordJudgement(HitJudgement.Good, PatternRunMode.EnemyDefense);
            stats.RecordJudgement(HitJudgement.Bad, PatternRunMode.EnemyDefense);
            stats.RecordJudgement(HitJudgement.Miss, PatternRunMode.EnemyDefense);
            stats.RecordJudgement(HitJudgement.Perfect, PatternRunMode.PlayerAbility);
            CombatReport report = stats.Finish(true, "Test");

            Assert.That(report.MaxCombo, Is.EqualTo(3), "Bad does not break the combo by default");
            Assert.That(stats.Combo, Is.EqualTo(1));
            Assert.That(report.Count(HitJudgement.Perfect), Is.EqualTo(2));
            Assert.That(report.AbilityJudgements[(int)HitJudgement.Perfect], Is.EqualTo(1));
            Assert.That(report.FullCombo, Is.False);
            Assert.That(report.TotalNotes, Is.EqualTo(5));
        }

        [Test]
        public void AllPerfect_IsFullCombo_WithFullAccuracy()
        {
            var stats = new CombatStatsTracker(Grading());
            for (int i = 0; i < 10; i++) stats.RecordJudgement(HitJudgement.Perfect, PatternRunMode.EnemyDefense);
            CombatReport report = stats.Finish(true, "Test");

            Assert.That(report.AllPerfect, Is.True);
            Assert.That(report.FullCombo, Is.True);
            Assert.That(report.Accuracy, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(report.Score, Is.GreaterThan(3000), "combo bonus and victory bonus are added");
        }

        [Test]
        public void Grades_FollowThresholds_AndDefeatIsCapped()
        {
            CombatResultGrading grading = Grading();
            Assert.That(grading.GradeFor(1f, true, true).label, Is.EqualTo("SS"));
            Assert.That(grading.GradeFor(1f, false, true).label, Is.EqualTo("S"), "SS needs a full combo");
            Assert.That(grading.GradeFor(0.86f, false, true).label, Is.EqualTo("A"));
            Assert.That(grading.GradeFor(0.5f, false, true).label, Is.EqualTo("D"));
            Assert.That(grading.GradeFor(1f, true, false).label, Is.EqualTo("C"), "a defeat is capped at C");
        }

        [Test]
        public void Accuracy_IsWeightedByJudgement()
        {
            CombatResultGrading grading = Grading();
            var counts = new int[4];
            counts[(int)HitJudgement.Perfect] = 1;
            counts[(int)HitJudgement.Miss] = 1;
            Assert.That(grading.Accuracy(counts), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(grading.Accuracy(new int[4]), Is.EqualTo(1f), "no notes counts as perfect accuracy");
        }

        [Test]
        public void Report_FormatsValues()
        {
            var report = new CombatReport { Accuracy = 0.9523f, BattleSeconds = 83f, MaxCombo = 12 };
            Assert.That(report.Format(CombatStat.Accuracy), Is.EqualTo("95.23%"));
            Assert.That(report.Format(CombatStat.BattleTime), Is.EqualTo("1:23"));
            Assert.That(report.Format(CombatStat.MaxCombo), Is.EqualTo("12x"));
            Assert.That(report.Format(CombatStat.MaxCombo, 0f), Is.EqualTo("0x"));
        }
    }
}
