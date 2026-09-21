using UnityEngine;

namespace RythmRPG.Rhythm
{
    public enum NoteBehaviorKind
    {
        Moving,
        MovingHold,
        Stationary,
        StationaryHold,
        Pong,
        Mash
    }

    /// <summary>Data-driven note type. Lives as an asset so new note types need no enum edit.</summary>
    [CreateAssetMenu(menuName = "Rhythm/Note Definition", fileName = "NoteDefinition")]
    public sealed class NoteDefinition : ScriptableObject
    {
        [SerializeField] private string definitionId = "";
        [SerializeField] private string displayName = "";
        [SerializeField] private NoteBehaviorKind behavior = NoteBehaviorKind.Moving;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Color displayColor = Color.white;
        [SerializeField] private float defaultSpeed = 8f;
        [SerializeField] private int defaultDamage = 1;
        [SerializeField] private double defaultTravelSeconds = 2.5d;
        [SerializeField] private float stationaryBadWindow = 0.45f;
        [SerializeField] private float stationaryGoodWindow = 0.25f;
        [SerializeField] private float stationaryPerfectWindow = 0.1f;

        public string DefinitionId { get { return definitionId; } set { definitionId = value ?? ""; } }
        public string DisplayName { get { return displayName; } set { displayName = value ?? ""; } }
        public NoteBehaviorKind Behavior { get { return behavior; } set { behavior = value; } }
        public GameObject Prefab { get { return prefab; } set { prefab = value; } }
        public Color DisplayColor { get { return displayColor; } set { displayColor = value; } }
        public float DefaultSpeed { get { return defaultSpeed; } set { defaultSpeed = value; } }
        public int DefaultDamage { get { return defaultDamage; } set { defaultDamage = value; } }
        public double DefaultTravelSeconds { get { return defaultTravelSeconds; } set { defaultTravelSeconds = value; } }
        public float StationaryBadWindow { get { return stationaryBadWindow; } set { stationaryBadWindow = value; } }
        public float StationaryGoodWindow { get { return stationaryGoodWindow; } set { stationaryGoodWindow = value; } }
        public float StationaryPerfectWindow { get { return stationaryPerfectWindow; } set { stationaryPerfectWindow = value; } }
        public bool IsHold { get { return behavior == NoteBehaviorKind.MovingHold || behavior == NoteBehaviorKind.StationaryHold; } }
    }
}
