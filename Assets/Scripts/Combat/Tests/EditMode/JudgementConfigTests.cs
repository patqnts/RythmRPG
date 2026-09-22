using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    public sealed class JudgementConfigTests
    {
        [Test]
        public void Windows_AreMillisecondsFromTheBeat()
        {
            var config = ScriptableObject.CreateInstance<JudgementConfig>();
            try
            {
                Assert.That(config.Evaluate(0.030f), Is.EqualTo(HitJudgement.Perfect));
                Assert.That(config.Evaluate(-0.030f), Is.EqualTo(HitJudgement.Perfect), "early and late are the same");
                Assert.That(config.Evaluate(0.070f), Is.EqualTo(HitJudgement.Good));
                Assert.That(config.Evaluate(0.110f), Is.EqualTo(HitJudgement.Bad));
                Assert.That(config.Evaluate(0.150f), Is.EqualTo(HitJudgement.Miss));
                Assert.That(config.MissWindow, Is.EqualTo(0.18f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Ranges_StayOrdered()
        {
            var config = ScriptableObject.CreateInstance<JudgementConfig>();
            try
            {
                config.SetRangesMs(100f, 50f, 20f, 10f);
                Assert.That(config.GoodWindow, Is.GreaterThanOrEqualTo(config.PerfectWindow));
                Assert.That(config.BadWindow, Is.GreaterThanOrEqualTo(config.GoodWindow));
                Assert.That(config.MissWindow, Is.GreaterThanOrEqualTo(config.BadWindow));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
