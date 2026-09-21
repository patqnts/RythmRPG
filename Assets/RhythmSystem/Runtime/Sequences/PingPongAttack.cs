using System;

namespace RythmRPG.Rhythm.Sequences
{
    /// <summary>What a Ping-Pong attack needs from the game (spawning the incoming shot and optional return visuals).</summary>
    public interface IPingPongHost
    {
        /// <summary>Spawns a note travelling toward the hit line, landing at <paramref name="hitTime"/> (song seconds). Returns false when it cannot be spawned.</summary>
        bool SpawnIncoming(string noteId, string laneId, double hitTime, double travelSeconds);
        /// <summary>The deflected shot flies back to the enemy between two song times. Purely cosmetic; may do nothing.</summary>
        void ReturnShot(string laneId, double fromTime, double toTime);
    }

    public sealed class PingPongSettings
    {
        public string[] LaneIds = new string[0];
        /// <summary>Successful deflects needed to end the attack (the enemy gives up).</summary>
        public int Volleys = 3;
        public double InitialTravelSeconds = 2.0d;
        /// <summary>Each return the incoming shot's travel time is multiplied by this (below 1 = faster).</summary>
        public double SpeedUpFactor = 0.85d;
        public double MinTravelSeconds = 0.6d;
        /// <summary>Time the deflected shot spends flying back before the enemy fires again.</summary>
        public double ReturnSeconds = 0.5d;
    }

    /// <summary>
    /// Ping-Pong: a shot comes down a lane, the player deflects it back, the enemy returns it faster, and so on.
    /// Missing a deflect fails the attack (the missed note already damages the player); deflecting all volleys completes it.
    /// </summary>
    public sealed class PingPongAttack : SequenceAttackBase
    {
        private enum Phase { Incoming, Returning }

        private readonly PingPongSettings settings;
        private readonly IPingPongHost host;
        private readonly Func<double, double> alignHit;
        private Phase phase;
        private int volley;
        private string currentNoteId;
        private string currentLane;
        private double nextSpawnAt;

        /// <param name="alignHit">Optional: rounds a proposed hit time up to a musical grid position (must return a time at or after its input).</param>
        public PingPongAttack(string id, PingPongSettings settings, IPingPongHost host, SequencePolicy policy = null, Func<double, double> alignHit = null)
            : base(id, "pingpong", policy)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (host == null) throw new ArgumentNullException("host");
            this.settings = settings;
            this.host = host;
            this.alignHit = alignHit;
        }

        public int CompletedVolleys { get { return volley; } }
        public string CurrentNoteId { get { return currentNoteId; } }

        /// <summary>Travel time of the incoming shot for a given volley index (0-based).</summary>
        public static double TravelFor(PingPongSettings s, int volleyIndex)
        {
            double t = s.InitialTravelSeconds * Math.Pow(s.SpeedUpFactor, Math.Max(0, volleyIndex));
            return Math.Max(s.MinTravelSeconds, t);
        }

        protected override void OnActivate(double now)
        {
            if (settings.LaneIds == null || settings.LaneIds.Length == 0 || settings.Volleys < 1)
            {
                Finish(SequenceState.Failed);
                return;
            }

            SpawnNext(now);
        }

        protected override void OnTick(double now)
        {
            if (phase == Phase.Returning && now >= nextSpawnAt) SpawnNext(now);
        }

        protected override void HandleNoteResolved(string noteId, bool success, double now)
        {
            if (phase != Phase.Incoming || noteId != currentNoteId) return;
            if (!success)
            {
                Finish(SequenceState.Failed);
                return;
            }

            volley++;
            if (volley >= settings.Volleys)
            {
                Finish(SequenceState.Completed);
                return;
            }

            phase = Phase.Returning;
            nextSpawnAt = now + Math.Max(0d, settings.ReturnSeconds);
            host.ReturnShot(currentLane, now, nextSpawnAt);
        }

        private void SpawnNext(double now)
        {
            double travel = TravelFor(settings, volley);
            double hit = now + travel;
            if (alignHit != null)
            {
                double aligned = alignHit(hit);
                if (aligned > now + 0.05d) hit = aligned;
            }

            currentLane = settings.LaneIds[volley % settings.LaneIds.Length];
            currentNoteId = Id + ":" + volley;
            phase = Phase.Incoming;
            if (!host.SpawnIncoming(currentNoteId, currentLane, hit, hit - now)) Finish(SequenceState.Failed);
        }
    }
}
