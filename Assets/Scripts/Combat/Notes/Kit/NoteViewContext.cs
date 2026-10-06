using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Everything a note view may need: the note, its lane, who attacks and who is attacked, its chart times and how far
    /// along it is. One per note, filled by <see cref="CombatNote"/> and passed to every view and listener.
    /// </summary>
    public sealed class NoteViewContext
    {
        public CombatNote Note { get; internal set; }
        internal NoteCueScheduler Scheduler { get; set; }

        /// <summary>Runs <paramref name="fire"/> on the cue (register in <see cref="NoteView.Begin"/>).</summary>
        public void Cue(NoteCue cue, System.Action fire) => Scheduler?.Add(cue, fire);
        public NoteKind Kind => Note != null ? Note.Kind : NoteKind.Tap;
        public PatternRunMode Mode { get; internal set; }
        /// <summary>The lane (key) this note is on, 1-4.</summary>
        public int LaneId => Note != null ? Note.GetNoteIdentity() : 0;
        /// <summary>The lane's point on the hit line (null before the lanes exist).</summary>
        public Transform HitPoint { get; internal set; }
        /// <summary>Who attacks with this note (the enemy for enemy attacks, the player for ability charts).</summary>
        public Transform Source { get; internal set; }
        /// <summary>Who is attacked.</summary>
        public Transform Target { get; internal set; }
        public Vector3 SpawnPoint { get; internal set; }
        /// <summary>What the note's Travel view flies (null = none; the Projectile anchor is then the note).</summary>
        public Transform Projectile { get; set; }
        /// <summary>Direction notes travel along this lane (toward the hit line).</summary>
        public Vector3 LaneForward { get; internal set; } = Vector3.forward;
        public Camera Camera { get; internal set; }
        /// <summary>Colour of the projectile (sprites / particles), for tinting effects.</summary>
        public Color Color { get; internal set; } = Color.white;

        /// <summary>Chart seconds now (the audio clock while a chart runs).</summary>
        public double Now => Note != null ? Note.NowSeconds : 0d;
        public double SpawnTime { get; internal set; }
        public double HitTime { get; internal set; }
        public double EndTime { get; internal set; }
        /// <summary>0 at spawn, 1 on the beat (clamped).</summary>
        public float Approach => HitTime <= SpawnTime ? 1f : Mathf.Clamp01((float)((Now - SpawnTime) / (HitTime - SpawnTime)));
        /// <summary>Seconds until the beat (negative after it).</summary>
        public float SecondsToBeat => (float)(HitTime - Now);
        /// <summary>Hold notes: 0 when the hold starts, 1 at its end.</summary>
        public float HoldProgress => EndTime <= HitTime ? 0f : Mathf.Clamp01((float)((Now - HitTime) / (EndTime - HitTime)));
        public bool IsHolding => Note != null && Note.IsHolding;
        /// <summary>Mash notes: presses so far and presses needed.</summary>
        public int Presses => Note != null ? Note.Presses : 0;
        public int RequiredPresses => Note != null ? Note.RequiredPresses : 1;
        public float MashProgress => Mathf.Clamp01(Presses / (float)Mathf.Max(1, RequiredPresses));

        public bool Resolved { get; internal set; }
        public NoteOutcome Outcome { get; internal set; }
        public HitJudgement Judgement { get; internal set; }
        /// <summary>Time.time when the note resolved (for fades after it).</summary>
        public float ResolvedAt { get; internal set; }
        public float SecondsSinceResolved => Resolved ? Time.time - ResolvedAt : 0f;

        /// <summary>Parent for things views spawn in the world (so they do not travel with the note).</summary>
        public Transform EffectParent => Note != null && Note.transform.parent != null ? Note.transform.parent : null;

        /// <summary>The anchor's transform (or a socket on it). Spawn Point has none.</summary>
        public Transform AnchorTransform(NoteAnchor anchor, string socket = null)
        {
            Transform root = anchor switch
            {
                NoteAnchor.Note => Note != null ? Note.transform : null,
                NoteAnchor.HitPoint => HitPoint,
                NoteAnchor.Source => Source,
                NoteAnchor.Target => Target,
                NoteAnchor.Projectile => Projectile != null ? Projectile : Note != null ? Note.transform : null,
                _ => null
            };
            if (root == null || string.IsNullOrWhiteSpace(socket)) return root;
            Transform found = CombatSocket.Find(root, socket);
            return found != null ? found : root;
        }

        /// <summary>World position of an anchor (or its socket) plus an offset in lane space (X side, Y up, Z forward).</summary>
        public Vector3 Position(NoteAnchor anchor, string socket, Vector3 laneOffset)
        {
            Transform point = AnchorTransform(anchor, socket);
            Vector3 position = point != null ? point.position
                : anchor == NoteAnchor.SpawnPoint ? SpawnPoint
                : Note != null ? Note.transform.position : SpawnPoint;
            return position + LaneOffset(laneOffset);
        }

        /// <summary>An offset in lane space (X side, Y up, Z forward toward the hit line) as a world vector.</summary>
        public Vector3 LaneOffset(Vector3 laneOffset)
        {
            if (laneOffset == Vector3.zero) return Vector3.zero;
            NoteSpawnOffset.LaneFrame(LaneForward, out Vector3 side, out Vector3 up, out Vector3 forward);
            return side * laneOffset.x + up * laneOffset.y + forward * laneOffset.z;
        }

        /// <summary>The animator of an anchor: the note's own (or a child's), or the combatant's.</summary>
        public Animator AnimatorOf(NoteAnchor anchor)
        {
            Transform root = AnchorTransform(anchor);
            return root != null ? root.GetComponentInChildren<Animator>(true) : null;
        }

        /// <summary>Makes camera-facing sprites of a spawned object face the combat camera, like notes do.</summary>
        public void FaceCamera(GameObject instance)
        {
            if (instance == null || Camera == null) return;
            RhythmNoteVisualLayer layer = instance.GetComponent<RhythmNoteVisualLayer>();
            if (layer == null) layer = instance.AddComponent<RhythmNoteVisualLayer>();
            layer.FaceSpritesToCamera(Camera);
        }
    }
}
