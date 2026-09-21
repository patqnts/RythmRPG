using NUnit.Framework;

namespace RythmRPG.Rhythm.Tests
{
    public sealed class CombatSongTests
    {
        [TestCase(0d, 0.5d)]      // before the first downbeat -> the first downbeat
        [TestCase(0.5d, 0.5d)]    // exactly on a bar line -> that bar line
        [TestCase(0.6d, 2.5d)]    // just after -> next bar
        [TestCase(4.4d, 4.5d)]
        public void NextBarInLoop_FindsNextBarLine(double t, double expected)
        {
            // 120 BPM, 4/4 -> 2 s bars, first downbeat at 0.5 s, long loop
            Assert.That(CombatSong.NextBarInLoop(t, 0.5d, 2d, 100d), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void NextBarInLoop_WrapsToNextLoopsFirstBar()
        {
            // loop is 9 s: bar lines at 0.5, 2.5, 4.5, 6.5, 8.5 -> after 8.5 comes the next loop's 0.5 (= 9.5)
            Assert.That(CombatSong.NextBarInLoop(8.6d, 0.5d, 2d, 9d), Is.EqualTo(9.5d).Within(1e-9));
        }
    }
}
