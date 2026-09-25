using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class PowerGaugeTests
    {
        [Test]
        public void Banked_EndsOnTheAbilityMultiplier()
        {
            var tally = new PowerTally();
            tally.Begin(null, 4);
            tally.Add(HitJudgement.Perfect);
            tally.Add(HitJudgement.Good);
            tally.Add(HitJudgement.Bad);
            tally.Add(HitJudgement.Miss);

            var judgements = new[]
            {
                Result(HitJudgement.Perfect), Result(HitJudgement.Good), Result(HitJudgement.Bad), Result(HitJudgement.Miss)
            };
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(4, judgements, null);
            Assert.That(tally.Banked, Is.EqualTo(performance.AverageWeight).Within(0.0001f));
            Assert.That(tally.Banked, Is.EqualTo((1f + 0.8f + 0.5f) / 4f).Within(0.0001f));
        }

        [Test]
        public void Potential_DropsOnMistakes_AndStartsFull()
        {
            var tally = new PowerTally();
            tally.Begin(null, 4);
            Assert.That(tally.Potential, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(tally.Banked, Is.EqualTo(0f));
            tally.Add(HitJudgement.Miss);
            Assert.That(tally.Potential, Is.EqualTo(0.75f).Within(0.0001f));
            tally.Add(HitJudgement.Perfect);
            Assert.That(tally.Potential, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(tally.Banked, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Running_IsAccuracySoFar()
        {
            var tally = new PowerTally();
            tally.Begin(null, 10);
            Assert.That(tally.Running, Is.EqualTo(1f).Within(0.0001f), "no notes yet = best multiplier");
            tally.Add(HitJudgement.Perfect);
            tally.Add(HitJudgement.Bad);
            Assert.That(tally.Running, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void ExpectedCount_OnlyGrows_AndFlawlessNeedsEveryNotePerfect()
        {
            var tally = new PowerTally();
            tally.Begin(null, 2);
            tally.SetExpected(3);
            tally.SetExpected(1);
            Assert.That(tally.Expected, Is.EqualTo(3));
            tally.Add(HitJudgement.Perfect);
            tally.Add(HitJudgement.Perfect);
            Assert.That(tally.Flawless, Is.False);
            tally.Add(HitJudgement.Perfect);
            Assert.That(tally.Flawless, Is.True);
        }

        [Test]
        public void Tier_IsTheHighestReached()
        {
            PowerGaugeStyle style = ScriptableObject.CreateInstance<PowerGaugeStyle>();
            try
            {
                Assert.That(style.TierFor(0.2f).label, Is.EqualTo("WEAK"));
                Assert.That(style.TierFor(0.5f).label, Is.EqualTo("GOOD"));
                Assert.That(style.TierFor(0.95f).label, Is.EqualTo("GREAT"));
                Assert.That(style.TierFor(1f).label, Is.EqualTo("FULL POWER"));
            }
            finally
            {
                Object.DestroyImmediate(style);
            }
        }

        private static RhythmJudgementResult Result(HitJudgement judgement) =>
            new("n", 0, judgement, 0f, Vector3.zero, default);
    }
}
