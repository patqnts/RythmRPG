using System.Collections.Generic;
using NUnit.Framework;
using RythmRPG.Rhythm.Sequences;

namespace RythmRPG.Rhythm.Tests
{
    public class SequenceAttackTests
    {
        private sealed class FakeHost : IPingPongHost
        {
            public readonly List<string> Spawned = new List<string>();
            public readonly List<double> Travels = new List<double>();
            public readonly List<string> Lanes = new List<string>();
            public readonly List<double> Hits = new List<double>();
            public int Returns;
            public bool Refuse;

            public bool SpawnIncoming(string noteId, string laneId, double hitTime, double travelSeconds)
            {
                if (Refuse) return false;
                Spawned.Add(noteId);
                Lanes.Add(laneId);
                Hits.Add(hitTime);
                Travels.Add(travelSeconds);
                return true;
            }

            public void ReturnShot(string laneId, double fromTime, double toTime) { Returns++; }
        }

        private static PingPongSettings Settings(int volleys = 3)
        {
            return new PingPongSettings { LaneIds = new[] { "a", "b" }, Volleys = volleys, InitialTravelSeconds = 2d, SpeedUpFactor = 0.5d, MinTravelSeconds = 0.6d, ReturnSeconds = 0.5d };
        }

        [Test]
        public void PingPong_SpawnsFirstShotOnActivate()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            pool.Start(new PingPongAttack("s", Settings(), host), 10d);
            Assert.AreEqual(1, host.Spawned.Count);
            Assert.AreEqual("s:0", host.Spawned[0]);
            Assert.AreEqual(12d, host.Hits[0], 1e-9);
            Assert.IsTrue(pool.HasBlocking);
        }

        [Test]
        public void PingPong_DeflectsSpeedUp_CycleLanes_AndComplete()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var attack = new PingPongAttack("s", Settings(3), host);
            pool.Start(attack, 0d);
            pool.NotifyNoteResolved("s:0", true, 2d);          // deflected at t=2
            Assert.AreEqual(1, host.Returns);
            pool.Tick(2.4d);                                    // still flying back
            Assert.AreEqual(1, host.Spawned.Count);
            pool.Tick(2.5d);                                    // enemy fires again
            Assert.AreEqual(2, host.Spawned.Count);
            Assert.AreEqual(1d, host.Travels[1], 1e-9);         // 2 * 0.5
            Assert.AreEqual("b", host.Lanes[1]);
            pool.NotifyNoteResolved("s:1", true, 3.5d);
            pool.Tick(4d);
            Assert.AreEqual(0.6d, host.Travels[2], 1e-9);       // clamped to the minimum
            pool.NotifyNoteResolved("s:2", true, 4.6d);
            Assert.AreEqual(SequenceState.Completed, attack.State);
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasBlocking);
        }

        [Test]
        public void PingPong_MissedDeflect_FailsAndLeavesPool()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var attack = new PingPongAttack("s", Settings(), host);
            ISequenceAttack finished = null;
            pool.AttackFinished += a => finished = a;
            pool.Start(attack, 0d);
            pool.NotifyNoteResolved("s:0", false, 2d);
            Assert.AreEqual(SequenceState.Failed, attack.State);
            Assert.IsTrue(finished == attack);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void PingPong_IgnoresOtherNotes()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var attack = new PingPongAttack("s", Settings(), host);
            pool.Start(attack, 0d);
            pool.NotifyNoteResolved("someone-else", false, 1d);
            Assert.AreEqual(SequenceState.Active, attack.State);
        }

        [Test]
        public void PingPong_HitTimesAlignToGrid()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            // grid of 0.5 s, rounded up
            pool.Start(new PingPongAttack("s", Settings(), host, null, t => System.Math.Ceiling(t / 0.5d) * 0.5d), 0.3d);
            Assert.AreEqual(2.5d, host.Hits[0], 1e-9);          // 0.3 + 2.0 = 2.3 -> 2.5
            Assert.AreEqual(2.2d, host.Travels[0], 1e-9);
        }

        [Test]
        public void PingPong_HostRefusesSpawn_FailsInsteadOfHanging()
        {
            var host = new FakeHost { Refuse = true };
            var pool = new SequenceAttackPool();
            var attack = new PingPongAttack("s", Settings(), host);
            pool.Start(attack, 0d);
            Assert.AreEqual(SequenceState.Failed, attack.State);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void PingPong_NoLanes_Fails()
        {
            var pool = new SequenceAttackPool();
            var attack = new PingPongAttack("s", new PingPongSettings(), new FakeHost());
            pool.Start(attack, 0d);
            Assert.AreEqual(SequenceState.Failed, attack.State);
        }

        [Test]
        public void Pool_MaxAge_CancelsStuckSequence()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var policy = new SequencePolicy { MaxAgeSeconds = 5d };
            var attack = new PingPongAttack("s", Settings(), host, policy);
            pool.Start(attack, 10d);
            pool.Tick(14.9d);
            Assert.AreEqual(SequenceState.Active, attack.State);
            pool.Tick(15d);
            Assert.AreEqual(SequenceState.Cancelled, attack.State);
            Assert.IsTrue(attack.CancelReason == SequenceCancelReason.MaxAge);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void Pool_CancelAll_IsIdempotentAndClearsEverything()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var a = new PingPongAttack("a", Settings(), host);
            var b = new PingPongAttack("b", Settings(), host);
            int finished = 0;
            pool.AttackFinished += x => finished++;
            pool.Start(a, 0d);
            pool.Start(b, 0d);
            pool.CancelAll(SequenceCancelReason.EncounterInterrupted);
            pool.CancelAll(SequenceCancelReason.EncounterInterrupted);
            Assert.AreEqual(0, pool.Count);
            Assert.AreEqual(2, finished);
            Assert.IsTrue(a.CancelReason == SequenceCancelReason.EncounterInterrupted);
            pool.Tick(1d);
            pool.NotifyNoteResolved("a:0", true, 1d);
            Assert.AreEqual(SequenceState.Cancelled, a.State);
        }

        [Test]
        public void Pool_ReplaceSameTag_CancelsPrevious()
        {
            var host = new FakeHost();
            var pool = new SequenceAttackPool();
            var first = new PingPongAttack("one", Settings(), host);
            var replace = new SequencePolicy { Concurrency = SequenceConcurrency.ReplaceSameTag };
            var second = new PingPongAttack("two", Settings(), host, replace);
            pool.Start(first, 0d);
            pool.Start(second, 1d);
            Assert.AreEqual(SequenceState.Cancelled, first.State);
            Assert.IsTrue(first.CancelReason == SequenceCancelReason.Superseded);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void Pool_NonBlockingSequence_DoesNotHoldTurn()
        {
            var pool = new SequenceAttackPool();
            var policy = new SequencePolicy { BlocksTurnEnd = false };
            pool.Start(new PingPongAttack("s", Settings(), new FakeHost(), policy), 0d);
            Assert.AreEqual(1, pool.Count);
            Assert.IsFalse(pool.HasBlocking);
        }
    }
}
