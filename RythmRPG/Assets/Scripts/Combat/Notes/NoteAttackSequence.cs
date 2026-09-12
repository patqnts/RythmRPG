using System;
using System.Collections.Generic;
using RythmRPG.Rhythm;
using UnityEngine;

public enum NoteAttackPatternType
{
    RhythmChart,
    NormalNotes,
    RandomLaser,
    SimultaneousNotes,
    WaveNotes,
    HoldNotes,
    PongNote,
    HoldLaser
}

public enum NoteAttackSequenceMode
{
    Ordered,
    ShuffleBag,
    WeightedRandom
}

[Serializable]
public sealed class NoteAttackPattern
{
    [SerializeField] private string label = string.Empty;
    [SerializeField] private bool enabled = true;
    [SerializeField] private NoteAttackPatternType patternType;
    [SerializeField, Min(0f)] private float durationOverride;
    [SerializeField, Min(0f)] private float cooldownAfter = 0.25f;
    [SerializeField, Min(0.01f)] private float selectionWeight = 1f;
    [SerializeField] private RhythmChart chartOverride;
    [SerializeField, Min(1)] private int simultaneousMaxCount = 4;
    [SerializeField] private bool simultaneousRandomAmount = true;

    public string Label { get => label; set => label = value ?? string.Empty; }
    public bool Enabled { get => enabled; set => enabled = value; }
    public NoteAttackPatternType PatternType { get => patternType; set => patternType = value; }
    public float DurationOverride { get => durationOverride; set => durationOverride = Mathf.Max(0f, value); }
    public float CooldownAfter { get => cooldownAfter; set => cooldownAfter = Mathf.Max(0f, value); }
    public float SelectionWeight { get => selectionWeight; set => selectionWeight = Mathf.Max(0.01f, value); }
    public RhythmChart ChartOverride { get => chartOverride; set => chartOverride = value; }
    public int SimultaneousMaxCount { get => simultaneousMaxCount; set => simultaneousMaxCount = Mathf.Max(1, value); }
    public bool SimultaneousRandomAmount { get => simultaneousRandomAmount; set => simultaneousRandomAmount = value; }

    public NoteAttackPattern() { }

    public NoteAttackPattern(NoteAttackPatternType type, string displayLabel, float cooldown = 0.25f)
    {
        patternType = type;
        label = displayLabel;
        cooldownAfter = cooldown;
    }

    public void Validate()
    {
        label ??= string.Empty;
        durationOverride = Mathf.Max(0f, durationOverride);
        cooldownAfter = Mathf.Max(0f, cooldownAfter);
        selectionWeight = Mathf.Max(0.01f, selectionWeight);
        simultaneousMaxCount = Mathf.Max(1, simultaneousMaxCount);
    }

    public static List<NoteAttackPattern> CreateDefaultSequence()
    {
        return new List<NoteAttackPattern>
        {
            new NoteAttackPattern(NoteAttackPatternType.RhythmChart, "Assigned Rhythm Chart"),
            new NoteAttackPattern(NoteAttackPatternType.NormalNotes, "Normal Notes"),
            new NoteAttackPattern(NoteAttackPatternType.RandomLaser, "Random Laser"),
            new NoteAttackPattern(NoteAttackPatternType.SimultaneousNotes, "Simultaneous Notes"),
            new NoteAttackPattern(NoteAttackPatternType.WaveNotes, "Wave Notes"),
            new NoteAttackPattern(NoteAttackPatternType.HoldNotes, "Hold Notes"),
            new NoteAttackPattern(NoteAttackPatternType.PongNote, "Pong Note"),
            new NoteAttackPattern(NoteAttackPatternType.HoldLaser, "Hold Laser")
        };
    }
}
