using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Rhythm
{
    public enum SequenceKind
    {
        PingPong
    }

    /// <summary>
    /// A point event on a chart that starts a run-time sequence attack (see RythmRPG.Rhythm.Sequences). It reserves no
    /// notes; the sequence spawns its own notes while it runs. Authored in the chart inspector until the composer gets a SEQ track.
    /// </summary>
    [Serializable]
    public sealed class SequenceActivationData
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private SequenceKind kind = SequenceKind.PingPong;
        [Tooltip("Chart seconds at which the sequence starts.")]
        [SerializeField, Min(0f)] private float startTime;
        [Tooltip("Chart lane ids the shots use, cycled per volley. Empty = the middle lane.")]
        [SerializeField] private List<string> laneIds = new List<string>();
        [Tooltip("Ping-Pong: successful deflects needed to end the attack.")]
        [SerializeField, Min(1)] private int volleys = 3;
        [SerializeField, Min(0.1f)] private float initialTravelSeconds = 2f;
        [Tooltip("Travel time is multiplied by this after every deflect (below 1 = faster).")]
        [SerializeField, Range(0.3f, 1.2f)] private float speedUpFactor = 0.85f;
        [SerializeField, Min(0.1f)] private float minTravelSeconds = 0.6f;
        [SerializeField, Min(0f)] private float returnSeconds = 0.5f;
        [Tooltip("Round each shot's arrival up to the next beat.")]
        [SerializeField] private bool alignToBeat = true;
        [SerializeField, Min(0)] private int damage = 1;
        [Tooltip("Failsafe: the sequence is cancelled after this long so it can never soft-lock a battle.")]
        [SerializeField, Min(1f)] private float maxAgeSeconds = 45f;

        public string Id { get { return id; } }
        public SequenceKind Kind { get { return kind; } set { kind = value; } }
        public float StartTime { get { return startTime; } set { startTime = Mathf.Max(0f, value); } }
        public List<string> LaneIds { get { return laneIds; } }
        public int Volleys { get { return volleys; } set { volleys = Mathf.Max(1, value); } }
        public float InitialTravelSeconds { get { return initialTravelSeconds; } set { initialTravelSeconds = Mathf.Max(0.1f, value); } }
        public float SpeedUpFactor { get { return speedUpFactor; } set { speedUpFactor = value; } }
        public float MinTravelSeconds { get { return minTravelSeconds; } set { minTravelSeconds = Mathf.Max(0.1f, value); } }
        public float ReturnSeconds { get { return returnSeconds; } set { returnSeconds = Mathf.Max(0f, value); } }
        public bool AlignToBeat { get { return alignToBeat; } set { alignToBeat = value; } }
        public int Damage { get { return damage; } set { damage = Mathf.Max(0, value); } }
        public float MaxAgeSeconds { get { return maxAgeSeconds; } set { maxAgeSeconds = Mathf.Max(1f, value); } }

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
            if (laneIds == null) laneIds = new List<string>();
        }
    }
}
