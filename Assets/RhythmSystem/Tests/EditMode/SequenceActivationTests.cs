using NUnit.Framework;
using UnityEngine;

namespace RythmRPG.Rhythm.Tests
{
    public class SequenceActivationTests
    {
        [Test]
        public void Chart_SequenceStart_ExtendsEffectiveDuration()
        {
            RhythmChart chart = ScriptableObject.CreateInstance<RhythmChart>();
            try
            {
                chart.CompositionDuration = 5d;
                var s = new SequenceActivationData { StartTime = 12f };
                s.EnsureId();
                chart.Sequences.Add(s);
                Assert.IsTrue(chart.EffectiveDuration >= 12d);
                Assert.IsFalse(string.IsNullOrEmpty(s.Id));
            }
            finally
            {
                Object.DestroyImmediate(chart);
            }
        }

        [Test]
        public void Data_ClampsToSafeValues()
        {
            var s = new SequenceActivationData();
            s.Volleys = 0;
            s.MinTravelSeconds = -1f;
            s.MaxAgeSeconds = 0f;
            Assert.AreEqual(1, s.Volleys);
            Assert.IsTrue(s.MinTravelSeconds >= 0.1f);
            Assert.IsTrue(s.MaxAgeSeconds >= 1f);
        }
    }
}
