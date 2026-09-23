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

        // Sections timeline: 120 BPM 4/4 (2 s bars), a 4 s intro starting at 6 -> loop starts at 10, loop 16 s.
        [TestCase(11.2d, 12d)]   // inside the loop -> next bar
        [TestCase(25.5d, 26d)]   // end of the first pass -> first bar of the second pass
        [TestCase(6.5d, 8d)]     // inside the intro -> bars counted back from the loop's first downbeat
        [TestCase(8d, 8d)]       // exactly on an intro bar line
        public void NextGridTime_FollowsIntroAndLoop(double earliest, double expected)
        {
            Assert.That(CombatSong.NextGridTime(earliest, 10d, 16d, 0d, 2d), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void NextGridTime_WaitForLoop_ReturnsLoopsFirstDownbeat()
        {
            Assert.That(CombatSong.NextGridTime(6.5d, 10d, 16d, 0.25d, 2d, waitForLoop: true), Is.EqualTo(10.25d).Within(1e-9));
        }

        [Test]
        public void NextGridTime_BeatGridAndImmediate()
        {
            Assert.That(CombatSong.NextGridTime(11.2d, 10d, 16d, 0d, 0.5d), Is.EqualTo(11.5d).Within(1e-9));
            Assert.That(CombatSong.NextGridTime(11.2d, 10d, 16d, 0d, 0d), Is.EqualTo(11.2d).Within(1e-9));
        }
    }
}
