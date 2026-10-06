using NUnit.Framework;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat.Tests
{
    /// <summary>Note Kit rules that do not need a running combat: kinds, outcomes, actor claims, link and move maths.</summary>
    public sealed class NoteKitTests
    {
        [TearDown]
        public void TearDown() => ActorLock.Clear();

        [Test]
        public void Kinds_MapToChartTypes_AndClearability()
        {
            Assert.That(NoteKinds.ChartType(NoteKind.Tap), Is.EqualTo(RhythmNoteType.Normal));
            Assert.That(NoteKinds.ChartType(NoteKind.Hold), Is.EqualTo(RhythmNoteType.Hold));
            Assert.That(NoteKinds.ChartType(NoteKind.Stationary), Is.EqualTo(RhythmNoteType.Laser));
            Assert.That(NoteKinds.ChartType(NoteKind.StationaryHold), Is.EqualTo(RhythmNoteType.HoldLaser));
            Assert.That(NoteKinds.ChartType(NoteKind.Mash), Is.EqualTo(RhythmNoteType.Mash));
            Assert.That(NoteKinds.ChartType(NoteKind.Pong), Is.EqualTo(RhythmNoteType.Pong));

            Assert.That(NoteKinds.IsHold(NoteKind.StationaryHold), Is.True);
            Assert.That(NoteKinds.IsStationary(NoteKind.Hold), Is.False);
            Assert.That(NoteKinds.Moves(NoteKind.Mash), Is.True);
            Assert.That(NoteKinds.ClearableByEffects(NoteKind.Tap), Is.True);
            Assert.That(NoteKinds.ClearableByEffects(NoteKind.Stationary), Is.True);
            Assert.That(NoteKinds.ClearableByEffects(NoteKind.Hold), Is.False);
            Assert.That(NoteKinds.ClearableByEffects(NoteKind.Mash), Is.False);
            Assert.That(NoteKinds.ClearableByEffects(NoteKind.Pong), Is.False);
        }

        [Test]
        public void Outcomes_FollowJudgementAndSource()
        {
            RhythmJudgementResult Result(HitJudgement judgement, NoteResolutionSource source) =>
                new("n", 1, judgement, 0f, Vector3.zero, source);

            Assert.That(NoteKinds.Outcome(Result(HitJudgement.Bad, NoteResolutionSource.PlayerInput)), Is.EqualTo(NoteOutcome.Hit));
            Assert.That(NoteKinds.Outcome(Result(HitJudgement.Miss, NoteResolutionSource.Timeout)), Is.EqualTo(NoteOutcome.Miss));
            // A zap resolves the note as a Perfect, but it was not the player's hit.
            Assert.That(NoteKinds.Outcome(Result(HitJudgement.Perfect, NoteResolutionSource.Modifier)), Is.EqualTo(NoteOutcome.Cleared));
            Assert.That(NoteKinds.Outcome(Result(HitJudgement.Miss, NoteResolutionSource.SystemClear)), Is.EqualTo(NoteOutcome.Cleared));
            Assert.That(NoteKinds.OutcomeMoment(NoteOutcome.Hit), Is.EqualTo(NoteMoment.Hit));
            Assert.That(NoteKinds.OutcomeMoment(NoteOutcome.Miss), Is.EqualTo(NoteMoment.Missed));
            Assert.That(NoteKinds.OutcomeMoment(NoteOutcome.Cleared), Is.EqualTo(NoteMoment.Cleared));
        }

        [Test]
        public void ActorLock_NewestNoteTakesOver_AndOnlyTheLastOwnerSendsItHome()
        {
            Transform enemy = new GameObject("Enemy").transform;
            enemy.position = new Vector3(1f, 0f, 5f);
            object first = new object();
            object second = new object();

            Vector3 home = ActorLock.Acquire(enemy, first);
            Assert.That(home, Is.EqualTo(new Vector3(1f, 0f, 5f)));
            enemy.position = new Vector3(1f, 0f, 2f); // the first note moved it
            // The second note claims it mid-move: the home stays the original spot.
            Assert.That(ActorLock.Acquire(enemy, second), Is.EqualTo(home));
            Assert.That(ActorLock.Owns(enemy, first), Is.False);
            Assert.That(ActorLock.Owns(enemy, second), Is.True);

            Assert.That(ActorLock.Release(enemy, first), Is.False);
            Assert.That(ActorLock.Release(enemy, second), Is.True);
            Assert.That(ActorLock.TryGetHome(enemy, out _), Is.False);
        }

        [Test]
        public void ActorLock_RestoreAll_PutsActorsBackHome()
        {
            Transform enemy = new GameObject("Enemy").transform;
            enemy.position = new Vector3(0f, 0f, 4f);
            ActorLock.Acquire(enemy, new object());
            enemy.position = new Vector3(2f, 0f, 1f);
            ActorLock.RestoreAll();
            Assert.That(enemy.position, Is.EqualTo(new Vector3(0f, 0f, 4f)));
            Assert.That(ActorLock.TryGetHome(enemy, out _), Is.False);
        }

        [Test]
        public void ActorMove_ArrivesBeforeTheBeat()
        {
            // Beat at 10 s, arrive 0.1 s early, 0.4 s move: starts at 9.5, arrives at 9.9.
            Assert.That(ActorMath.MoveProgress(9.4d, 9.5d, 9.9d), Is.EqualTo(0f));
            Assert.That(ActorMath.MoveProgress(9.7d, 9.5d, 9.9d), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ActorMath.MoveProgress(10d, 9.5d, 9.9d), Is.EqualTo(1f));
            Assert.That(ActorMath.MoveProgress(9.9d, 9.9d, 9.9d), Is.EqualTo(1f));
        }

        [Test]
        public void Link_GrowsWithTheApproach_AndRetractsToNothing()
        {
            AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.That(LinkMath.Reach(LinkView.Reach.Full, 0.2f, 0f, 1f, linear), Is.EqualTo(1f));
            Assert.That(LinkMath.Reach(LinkView.Reach.GrowWithApproach, 0.25f, 0f, 1f, linear), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(LinkMath.Reach(LinkView.Reach.GrowOverSeconds, 0f, 0.1f, 0.4f, linear), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(LinkMath.Reach(LinkView.Reach.GrowOverSeconds, 0f, 2f, 0.4f, linear), Is.EqualTo(1f));

            Assert.That(LinkMath.Retract(0.8f, 0f, 0.2f), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(LinkMath.Retract(0.8f, 0.1f, 0.2f), Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(LinkMath.Retract(0.8f, 0.3f, 0.2f), Is.EqualTo(0f));
            Assert.That(LinkMath.Retract(0.8f, 0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Link_EndsStayPinned_WhileTheMiddleSagsAndWiggles()
        {
            Vector3 a = new(0f, 1f, 0f);
            Vector3 b = new(0f, 1f, 4f);
            Vector3 side = Vector3.right;
            Assert.That(LinkMath.Point(a, b, 0f, 1f, side, 0.5f, 2f, 0.3f), Is.EqualTo(a));
            Assert.That(LinkMath.Point(a, b, 1f, 1f, side, 0.5f, 2f, 0.3f), Is.EqualTo(b));
            Vector3 middle = LinkMath.Point(a, b, 0.5f, 1f, side, 0f, 2f, 0f);
            Assert.That(middle, Is.EqualTo(new Vector3(0f, 0f, 2f)));
        }

        [Test]
        public void Cues_PlannedOnesFollowTheChart_ReactiveOnesWaitForTheirMoment()
        {
            var scheduler = new NoteCueScheduler();
            var fired = new System.Collections.Generic.List<string>();
            scheduler.Add(new NoteCue(NoteMoment.Spawned), () => fired.Add("spawn"));
            scheduler.Add(new NoteCue(NoteMoment.ReachedBeat, -0.3f), () => fired.Add("windup"));
            scheduler.Add(new NoteCue(NoteMoment.HoldEnd, 0.1f), () => fired.Add("holdEnd"));
            scheduler.Add(new NoteCue(NoteMoment.Hit, 0.2f), () => fired.Add("hit"));
            scheduler.Add(new NoteCue(NoteMoment.Pressed), () => fired.Add("press"));
            // A Spawned cue cannot be anticipated: a negative offset counts as 0.
            scheduler.Add(new NoteCue(NoteMoment.Spawned, -1f), () => fired.Add("spawnClamped"));

            // Spawn at chart 1.0, beat at 3.0, hold end at 4.0.
            scheduler.Tick(1.0, 1.0, 3.0, 4.0, 0f);
            Assert.That(fired, Is.EqualTo(new[] { "spawn", "spawnClamped" }));
            scheduler.Tick(2.69, 1.0, 3.0, 4.0, 0f);
            Assert.That(fired.Count, Is.EqualTo(2));
            scheduler.Tick(2.7, 1.0, 3.0, 4.0, 0f);
            Assert.That(fired[2], Is.EqualTo("windup"));

            scheduler.OnMoment(NoteMoment.Pressed, 10f);
            scheduler.OnMoment(NoteMoment.Pressed, 10.1f); // repeating moments fire each time (Mash)
            scheduler.OnMoment(NoteMoment.Hit, 10f);
            Assert.That(fired.FindAll(f => f == "press").Count, Is.EqualTo(2));
            Assert.That(fired.Contains("hit"), Is.False);
            scheduler.Tick(3.2, 1.0, 3.0, 4.0, 10.19f);
            Assert.That(fired.Contains("hit"), Is.False);
            scheduler.Tick(3.2, 1.0, 3.0, 4.0, 10.2f);
            Assert.That(fired.Contains("hit"), Is.True);

            Assert.That(scheduler.SecondsUntilLastScheduled(3.2, 1.0, 3.0, 4.0), Is.EqualTo(0.9f).Within(0.0001f));
            scheduler.Tick(4.1, 1.0, 3.0, 4.0, 11f);
            Assert.That(fired[fired.Count - 1], Is.EqualTo("holdEnd"));
            Assert.That(scheduler.LongestDelayAfter(NoteMoment.Hit, NoteMoment.Missed), Is.EqualTo(0.2f).Within(0.0001f));
        }

        [Test]
        public void Moments_SitOnTheTimeline_AndDraggingKeepsTheMoment()
        {
            Assert.That(Moments.TimeOf(NoteMoment.Spawned, 2.5f, 1f), Is.EqualTo(-2.5f));
            Assert.That(Moments.TimeOf(NoteMoment.ReachedBeat, 2.5f, 1f), Is.EqualTo(0f));
            Assert.That(Moments.TimeOf(NoteMoment.HoldEnd, 2.5f, 1f), Is.EqualTo(1f));
            Assert.That(Moments.TimeOf(NoteMoment.Hit, 2.5f, 0f), Is.EqualTo(0f));
            Assert.That(Moments.TimeOf(NoteMoment.Hit, 2.5f, 1f), Is.EqualTo(1f));

            var cue = new NoteCue(NoteMoment.ReachedBeat);
            Moments.SetTime(cue, -0.4f, 2.5f, 0f);
            Assert.That(cue.moment, Is.EqualTo(NoteMoment.ReachedBeat));
            Assert.That(cue.offset, Is.EqualTo(-0.4f).Within(0.0001f));
            Assert.That(Moments.TimeOf(cue, 2.5f, 0f), Is.EqualTo(-0.4f).Within(0.0001f));

            // Reactive moments cannot be dragged before themselves.
            var hit = new NoteCue(NoteMoment.Hit);
            Moments.SetTime(hit, -0.5f, 2.5f, 0f);
            Assert.That(hit.offset, Is.EqualTo(0f));
            Assert.That(new NoteCue(NoteMoment.ReachedBeat, -0.25f).ToString(), Is.EqualTo("Beat -0.25s"));
        }

        [Test]
        public void AttackTimeline_PlacesStepsLikeThePerformer_AndDragsEditTheSteps()
        {
            CharacterAttackSequence sequence = ScriptableObject.CreateInstance<CharacterAttackSequence>();
            var wait = new WaitStep();
            wait.SetLength(0.2f, AttackTimingContext.Default);
            var shake = new CameraShakeStep(); // background: runs alongside the next step
            shake.SetLength(0.5f, AttackTimingContext.Default);
            shake.SetWaitForCompletion(false);
            var channel = new ChannelDamageStep();
            channel.SetLength(1f, AttackTimingContext.Default);
            channel.SetDelay(0.1f);
            sequence.EditorSetSteps(wait, shake, channel);
            sequence.EditorSetMove(true, 0.35f, 0.25f);

            AttackTimelineLayout layout = AttackTimelineLayout.Build(sequence);
            Assert.That(layout.MoveIn, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(layout.Entries[0].Start, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(layout.Entries[1].Start, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(layout.Entries[1].Background, Is.True);
            // The shake does not hold the next step back; the channel waits its own 0.1 s delay.
            Assert.That(layout.Entries[2].DelayStart, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(layout.Entries[2].Start, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(layout.StepsEnd, Is.EqualTo(1.65f).Within(0.0001f));
            Assert.That(layout.Total, Is.EqualTo(1.9f).Within(0.0001f));
            Assert.That(layout.Entries[2].Hits.Count, Is.EqualTo(5));
            Assert.That(layout.Entries[2].Hits[0].Time, Is.EqualTo(0.2f).Within(0.0001f));

            // Dragging the channel's bar later sets its delay; dragging its end sets its length.
            AttackTimelineLayout.MoveStart(layout.Entries[2], 0.95f);
            Assert.That(channel.Delay, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(AttackTimelineLayout.MoveEnd(AttackTimelineLayout.Build(sequence).Entries[2], 2.45f), Is.True);
            Assert.That(AttackTimelineLayout.Build(sequence).Entries[2].Duration, Is.EqualTo(1.5f).Within(0.0001f));
            // A bar cannot be dragged before the point its step is reached.
            AttackTimelineLayout.MoveStart(layout.Entries[2], 0f);
            Assert.That(channel.Delay, Is.EqualTo(0f));
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void AttackTimeline_TimedHitsFollowTheClip_AndCanBeDragged()
        {
            var timing = new AttackTimingContext { StateSeconds = state => state == "Attack" ? 0.8f : 0f };
            var hits = new TimedHitsStep(); // default hits at 0.25 / 0.5 / 0.8 of the clip
            var marks = new System.Collections.Generic.List<AttackHitMark>();
            hits.CollectHits(timing, marks);
            Assert.That(marks.Count, Is.EqualTo(3));
            Assert.That(marks[1].Time, Is.EqualTo(0.4f).Within(0.0001f));
            marks[1].Move(0.6f);
            marks.Clear();
            hits.CollectHits(timing, marks);
            Assert.That(marks[1].Time, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(hits.EstimateSeconds(timing, out bool guess), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(guess, Is.False);
            Assert.That(new PlayAnimationStep().EstimateSeconds(new AttackTimingContext(), out bool unknown), Is.EqualTo(0.5f));
            Assert.That(unknown, Is.True);
        }

        [Test]
        public void LaneFrame_SideUpForward_AreOrthonormal()
        {
            NoteSpawnOffset.LaneFrame(new Vector3(0f, 0f, -2f), out Vector3 side, out Vector3 up, out Vector3 forward);
            Assert.That(forward, Is.EqualTo(new Vector3(0f, 0f, -1f)));
            Assert.That(up, Is.EqualTo(Vector3.up));
            Assert.That(Vector3.Dot(side, forward), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Vector3.Dot(side, up), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(side.magnitude, Is.EqualTo(1f).Within(0.0001f));

            // Lanes running straight down the screen: up becomes "toward the camera".
            NoteSpawnOffset.LaneFrame(Vector3.down, out _, out Vector3 verticalUp, out _);
            Assert.That(Mathf.Abs(Vector3.Dot(verticalUp, Vector3.down)), Is.LessThan(0.001f));
        }

        [Test]
        public void WarpStrike_HopsArc_AndTheThrowSitsBetweenItsCues()
        {
            Assert.That(ActorMath.Hop(0f, 1f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ActorMath.Hop(0.5f, 0.8f), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(ActorMath.Hop(1f, 1f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ActorMath.Hop(0.5f, 0f), Is.EqualTo(0f));

            var travel = new TravelView
            {
                DepartAt = new NoteCue(NoteMoment.Spawned, 0.2f),
                ArriveAt = new NoteCue(NoteMoment.ReachedBeat),
                HideAt = new NoteCue(NoteMoment.ReachedBeat, -0.1f)
            };
            var items = new System.Collections.Generic.List<NoteTimelineItem>();
            travel.DescribeTimeline(items);
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(Moments.TimeOf(items[0].Cue, 2.5f, 0f), Is.EqualTo(-2.3f).Within(0.0001f));
            Assert.That(Moments.TimeOf(items[0].EndCue, 2.5f, 0f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Moments.TimeOf(items[1].Cue, 2.5f, 0f), Is.EqualTo(-0.1f).Within(0.0001f));

            var vanish = new ActorVanishView { AppearAt = new NoteCue(NoteMoment.ReachedBeat, -0.1f), AppearSeconds = 0f };
            items.Clear();
            vanish.DescribeTimeline(items);
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[1].IsMarker, Is.True);
        }
    }
}
