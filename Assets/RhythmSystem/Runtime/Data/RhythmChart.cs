using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Rhythm
{
    public enum RhythmNoteType
    {
        Normal,
        Hold,
        Laser,
        HoldLaser,
        Pong,
        Arrow,
        Cluster
    }

    public enum RhythmSnapDivision
    {
        OneBeat = 1,
        HalfBeat = 2,
        QuarterBeat = 4,
        EighthBeat = 8,
        SixteenthBeat = 16
    }

    [Serializable]
    public sealed class RhythmMetadataEntry
    {
        [SerializeField] private string key = string.Empty;
        [SerializeField] private string value = string.Empty;

        public string Key { get => key; set => key = value ?? string.Empty; }
        public string Value { get => value; set => this.value = value ?? string.Empty; }
    }

    [Serializable]
    public sealed class RhythmLaneData
    {
        [SerializeField, HideInInspector] private string id = string.Empty;
        [SerializeField] private string displayName = "Lane";
        [SerializeField] private Color color = new Color(0.2f, 0.65f, 0.95f, 1f);
        [SerializeField] private int keyIdentity = 1;
        [SerializeField] private KeyType keyType = KeyType.DEFAULT;

        public string Id => id;
        public string DisplayName { get => displayName; set => displayName = value ?? string.Empty; }
        public Color Color { get => color; set => color = value; }
        public int KeyIdentity { get => keyIdentity; set => keyIdentity = value; }
        public KeyType KeyType { get => keyType; set => keyType = value; }

        public RhythmLaneData()
        {
            EnsureId();
        }

        public RhythmLaneData(string name, int identity, Color laneColor, KeyType type = KeyType.DEFAULT)
        {
            id = Guid.NewGuid().ToString("N");
            displayName = name;
            keyIdentity = identity;
            color = laneColor;
            keyType = type;
        }

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
            }
        }

        public void RegenerateId()
        {
            id = Guid.NewGuid().ToString("N");
        }
    }

    [Serializable]
    public sealed class RhythmNoteDefinition
    {
        [SerializeField] private RhythmNoteType noteType;
        [SerializeField] private GameObject defaultPrefab;
        [SerializeField] private Color displayColor = new Color(0.25f, 0.75f, 1f, 1f);
        [SerializeField, Min(0.01f)] private float defaultSpeed = 8f;
        [SerializeField, Min(0)] private int defaultDamage = 1;
        [SerializeField, Min(0f)] private float defaultTravelTime = 2.5f;

        public RhythmNoteType NoteType { get => noteType; set => noteType = value; }
        public GameObject DefaultPrefab { get => defaultPrefab; set => defaultPrefab = value; }
        public Color DisplayColor { get => displayColor; set => displayColor = value; }
        public float DefaultSpeed { get => defaultSpeed; set => defaultSpeed = value; }
        public int DefaultDamage { get => defaultDamage; set => defaultDamage = value; }
        public float DefaultTravelTime { get => defaultTravelTime; set => defaultTravelTime = value; }

        public RhythmNoteDefinition() { }

        public RhythmNoteDefinition(RhythmNoteType type, Color color, float speed = 8f, int damage = 1, float travelTime = 2.5f)
        {
            noteType = type;
            displayColor = color;
            defaultSpeed = speed;
            defaultDamage = damage;
            defaultTravelTime = travelTime;
        }
    }

    [Serializable]
    public sealed class RhythmNoteData
    {
        [SerializeField, HideInInspector] private string id = string.Empty;
        [SerializeField, HideInInspector] private string laneId = string.Empty;
        [SerializeField] private double hitTime;
        [SerializeField] private RhythmNoteType noteType;
        [SerializeField] private double holdDuration;
        [SerializeField] private NoteInitializeMovementType initializeMovementType;
        [SerializeField] private HitEffect hitEffect = HitEffect.Default;
        [SerializeField] private PlayerState playerState = PlayerState.Default;
        [SerializeField] private int damage = 1;
        [SerializeField] private float speed = 8f;
        [SerializeField] private double travelTime = 2.5d;
        [SerializeField] private float stationaryBadWindow = 0.9f;
        [SerializeField] private float stationaryGoodWindow = 0.45f;
        [SerializeField] private float stationaryPerfectWindow = 0.15f;
        [SerializeField] private GameObject prefabOverride;
        [SerializeField] private List<RhythmMetadataEntry> metadata = new List<RhythmMetadataEntry>();

        public string Id => id;
        public string LaneId { get => laneId; set => laneId = value ?? string.Empty; }
        public double HitTime { get => hitTime; set => hitTime = value; }
        public RhythmNoteType NoteType { get => noteType; set => noteType = value; }
        public double HoldDuration { get => holdDuration; set => holdDuration = value; }
        public NoteInitializeMovementType InitializeMovementType { get => initializeMovementType; set => initializeMovementType = value; }
        public HitEffect HitEffect { get => hitEffect; set => hitEffect = value; }
        public PlayerState PlayerState { get => playerState; set => playerState = value; }
        public int Damage { get => damage; set => damage = value; }
        public float Speed { get => speed; set => speed = value; }
        public double TravelTime { get => travelTime; set => travelTime = value; }
        public float StationaryBadWindow { get => stationaryBadWindow; set => stationaryBadWindow = value; }
        public float StationaryGoodWindow { get => stationaryGoodWindow; set => stationaryGoodWindow = value; }
        public float StationaryPerfectWindow { get => stationaryPerfectWindow; set => stationaryPerfectWindow = value; }
        public GameObject PrefabOverride { get => prefabOverride; set => prefabOverride = value; }
        public List<RhythmMetadataEntry> Metadata => metadata;
        public bool IsHold => RhythmTimingUtility.IsHoldType(noteType);
        public double EndTime => hitTime + (IsHold ? Math.Max(0d, holdDuration) : 0d);
        public double SpawnTime => RhythmTimingUtility.GetSpawnTime(this);
        public float LegacyHoldLength => RhythmTimingUtility.GetLegacyHoldLength(this);

        public RhythmNoteData()
        {
            EnsureId();
        }

        public RhythmNoteData(string targetLaneId, double time, RhythmNoteType type)
        {
            id = Guid.NewGuid().ToString("N");
            laneId = targetLaneId;
            hitTime = time;
            noteType = type;
        }

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
            }

            metadata ??= new List<RhythmMetadataEntry>();
        }

        public void RegenerateId()
        {
            id = Guid.NewGuid().ToString("N");
        }
    }

    public sealed class RhythmChart : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField, HideInInspector] private int schemaVersion = CurrentSchemaVersion;
        [SerializeField, Min(0.01f)] private float bpm = 120f;
        [SerializeField, Min(1)] private int beatsPerMeasure = 4;
        [SerializeField] private double compositionDuration = 30d;
        [SerializeField] private AudioClip audioClip;
        [SerializeField] private bool snapEnabled = true;
        [SerializeField] private RhythmSnapDivision snapDivision = RhythmSnapDivision.QuarterBeat;
        [SerializeField] private List<RhythmLaneData> lanes = new List<RhythmLaneData>();
        [SerializeField] private List<RhythmNoteDefinition> noteDefinitions = new List<RhythmNoteDefinition>();
        [SerializeField] private List<RhythmNoteData> notes = new List<RhythmNoteData>();

        public int SchemaVersion => schemaVersion;
        public float Bpm { get => bpm; set => bpm = value; }
        public int BeatsPerMeasure { get => beatsPerMeasure; set => beatsPerMeasure = value; }
        public double CompositionDuration { get => compositionDuration; set => compositionDuration = value; }
        public AudioClip AudioClip { get => audioClip; set => audioClip = value; }
        public bool SnapEnabled { get => snapEnabled; set => snapEnabled = value; }
        public RhythmSnapDivision SnapDivision { get => snapDivision; set => snapDivision = value; }
        public List<RhythmLaneData> Lanes => lanes;
        public List<RhythmNoteDefinition> NoteDefinitions => noteDefinitions;
        public List<RhythmNoteData> Notes => notes;
        public double SecondsPerBeat => RhythmTimingUtility.GetSecondsPerBeat(bpm);

        public double EffectiveDuration
        {
            get
            {
                double result = Math.Max(0.01d, compositionDuration);
                if (audioClip != null)
                {
                    result = Math.Max(result, audioClip.length);
                }

                foreach (RhythmNoteData note in notes)
                {
                    if (note != null)
                    {
                        result = Math.Max(result, note.EndTime);
                    }
                }

                return result;
            }
        }

        public RhythmLaneData FindLane(string laneId)
        {
            return lanes.FirstOrDefault(lane => lane != null && lane.Id == laneId);
        }

        public RhythmNoteDefinition FindDefinition(RhythmNoteType type)
        {
            return noteDefinitions.FirstOrDefault(definition => definition != null && definition.NoteType == type);
        }

        public GameObject ResolvePrefab(RhythmNoteData note)
        {
            if (note == null)
            {
                return null;
            }

            return note.PrefabOverride != null ? note.PrefabOverride : FindDefinition(note.NoteType)?.DefaultPrefab;
        }

        public IEnumerable<RhythmNoteData> GetNotesByHitTime()
        {
            return notes.Where(note => note != null).OrderBy(note => note.HitTime).ThenBy(note => note.Id);
        }

        public IEnumerable<RhythmNoteData> GetNotesBySpawnTime()
        {
            return notes.Where(note => note != null).OrderBy(note => note.SpawnTime).ThenBy(note => note.Id);
        }

        public void EnsureIdentifiers()
        {
            lanes ??= new List<RhythmLaneData>();
            noteDefinitions ??= new List<RhythmNoteDefinition>();
            notes ??= new List<RhythmNoteData>();

            HashSet<string> laneIds = new HashSet<string>();
            foreach (RhythmLaneData lane in lanes.Where(lane => lane != null))
            {
                lane.EnsureId();
                while (!laneIds.Add(lane.Id))
                {
                    lane.RegenerateId();
                }
            }

            HashSet<string> noteIds = new HashSet<string>();
            foreach (RhythmNoteData note in notes.Where(note => note != null))
            {
                note.EnsureId();
                while (!noteIds.Add(note.Id))
                {
                    note.RegenerateId();
                }
            }
        }

        private void OnValidate()
        {
            schemaVersion = CurrentSchemaVersion;
            bpm = Mathf.Max(0.01f, bpm);
            beatsPerMeasure = Mathf.Max(1, beatsPerMeasure);
            compositionDuration = Math.Max(0.01d, compositionDuration);
            EnsureIdentifiers();
        }
    }
}
