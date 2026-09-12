using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using UnityEngine;

public class NoteGenerator : MonoBehaviour
{
    [Header("Rhythm Chart")]
    [SerializeField] private RhythmChart rhythmChart;
    [SerializeField] private bool playChartAudio;
    [SerializeField] private AudioSource chartAudioSource;

    [Header("Attack Sequence")]
    [SerializeField] private NoteAttackSequenceMode sequenceMode = NoteAttackSequenceMode.ShuffleBag;
    [SerializeField] private bool loopSequence = true;
    [SerializeField] private List<NoteAttackPattern> attackSequence = NoteAttackPattern.CreateDefaultSequence();

    [Header("Legacy Pattern Prefabs")]
    public GameObject noteObjectPrefab;
    public GameObject lasePrefab;
    public GameObject holdLaserPrefab;
    public GameObject longNoteObjectPrefab;
    public GameObject pongNoteObjectPrefab;
    public Transform projectileObjectHolder;

    [Header("Legacy Pattern Timing")]
    [Min(0.01f)] public float attackDuration = 10f;
    [Min(0.01f)] public float generationSpeed = 1f;
    [Min(0.01f)] public float normalNoteGenerationSpeed = 0.3f;
    [Min(0.01f)] public float laserGenerationSpeed = 1.75f;
    [Min(0.01f)] public float holdLaserGenerationSpeed = 1.75f;

    [Header("Hold Notes")]
    [Min(0.01f)] public float holdNoteGenerationSpeed = 0.75f;
    [Min(0.01f)] public float holdNoteSpeed = 8f;
    public Vector2 holdNoteDurationRange = new Vector2(0.75f, 1.25f);

    [Header("Initialize Movement")]
    public bool enableMissileInitializeMovement = true;
    [Range(0f, 1f)] public float missileInitializeChance = 0.25f;
    public KeyCode[] keyCodesAsign;

    private bool isAttacking;
    private Coroutine attackCycleCoroutine;
    private NoteAttackPattern lastRandomPattern;

    public RhythmChart Chart => rhythmChart;
    public bool IsAttacking => isAttacking;
    public IReadOnlyList<NoteAttackPattern> AttackSequence => attackSequence;

    public event Action AttackAnimate;
    public event Action IdleAnimate;

    private void Start()
    {
        CombatManager combatManager = FindFirstObjectByType<CombatManager>();
        if (combatManager != null)
        {
            keyCodesAsign = combatManager.keyCodes;
        }
    }

    private void OnEnable()
    {
        if (CombatManager.instance == null)
        {
            return;
        }

        CombatManager.instance.AttackEvent += Attack;
        CombatManager.instance.StopAttackEvent += StopAttack;
    }

    private void OnDisable()
    {
        if (CombatManager.instance != null)
        {
            CombatManager.instance.AttackEvent -= Attack;
            CombatManager.instance.StopAttackEvent -= StopAttack;
        }

        StopAttack();
    }

    public void SetChart(RhythmChart chart)
    {
        rhythmChart = chart;
    }

    public void Attack()
    {
        if (attackCycleCoroutine != null)
        {
            return;
        }

        isAttacking = true;
        attackCycleCoroutine = StartCoroutine(AttackCycle());
    }

    public void StopAttack()
    {
        bool wasAttacking = isAttacking || attackCycleCoroutine != null;
        isAttacking = false;
        attackCycleCoroutine = null;
        StopAllCoroutines();
        StopChartAudio();
        if (wasAttacking)
        {
            IdleAnimate?.Invoke();
        }
    }

    private IEnumerator AttackCycle()
    {
        do
        {
            List<NoteAttackPattern> runnablePatterns = GetRunnablePatterns();
            if (runnablePatterns.Count == 0)
            {
                Debug.LogWarning($"{name} has no runnable note attack patterns.", this);
                break;
            }

            if (sequenceMode == NoteAttackSequenceMode.WeightedRandom)
            {
                yield return RunPatternAndCooldown(SelectWeightedPattern(runnablePatterns));
            }
            else
            {
                if (sequenceMode == NoteAttackSequenceMode.ShuffleBag)
                {
                    Shuffle(runnablePatterns);
                }

                foreach (NoteAttackPattern pattern in runnablePatterns)
                {
                    if (!isAttacking)
                    {
                        break;
                    }

                    yield return RunPatternAndCooldown(pattern);
                }
            }
        }
        while (isAttacking && loopSequence);

        isAttacking = false;
        attackCycleCoroutine = null;
        StopChartAudio();
        IdleAnimate?.Invoke();
    }

    private IEnumerator RunPatternAndCooldown(NoteAttackPattern pattern)
    {
        yield return RunPattern(pattern);

        if (isAttacking && pattern.CooldownAfter > 0f)
        {
            yield return new WaitForSeconds(pattern.CooldownAfter);
        }
    }

    private IEnumerator RunPattern(NoteAttackPattern pattern)
    {
        float duration = pattern.DurationOverride > 0f ? pattern.DurationOverride : attackDuration;

        switch (pattern.PatternType)
        {
            case NoteAttackPatternType.RhythmChart:
                yield return PlayRhythmChart(pattern.ChartOverride != null ? pattern.ChartOverride : rhythmChart);
                break;
            case NoteAttackPatternType.NormalNotes:
                yield return GenerateNormalNotesForDuration(duration);
                break;
            case NoteAttackPatternType.RandomLaser:
                yield return GenerateRandomLaser(duration);
                break;
            case NoteAttackPatternType.SimultaneousNotes:
                yield return GenerateSimultaneousNotes(
                    duration,
                    Mathf.Max(1, pattern.SimultaneousMaxCount),
                    pattern.SimultaneousRandomAmount);
                break;
            case NoteAttackPatternType.WaveNotes:
                yield return GenerateWaveNotesForDuration(duration);
                break;
            case NoteAttackPatternType.HoldNotes:
                yield return GenerateHoldNotesForDuration(duration);
                break;
            case NoteAttackPatternType.PongNote:
                yield return GeneratePongNote();
                break;
            case NoteAttackPatternType.HoldLaser:
                yield return GenerateRandomHoldLaser(duration);
                break;
        }
    }

    private List<NoteAttackPattern> GetRunnablePatterns()
    {
        if (attackSequence == null)
        {
            return new List<NoteAttackPattern>();
        }

        return attackSequence
            .Where(pattern => pattern != null && pattern.Enabled)
            .Where(pattern => pattern.PatternType != NoteAttackPatternType.RhythmChart
                || pattern.ChartOverride != null
                || rhythmChart != null)
            .ToList();
    }

    private NoteAttackPattern SelectWeightedPattern(List<NoteAttackPattern> patterns)
    {
        List<NoteAttackPattern> candidates = patterns.Count > 1
            ? patterns.Where(pattern => pattern != lastRandomPattern).ToList()
            : patterns;

        float totalWeight = candidates.Sum(pattern => Mathf.Max(0.01f, pattern.SelectionWeight));
        float choice = UnityEngine.Random.value * totalWeight;

        foreach (NoteAttackPattern candidate in candidates)
        {
            choice -= Mathf.Max(0.01f, candidate.SelectionWeight);
            if (choice <= 0f)
            {
                lastRandomPattern = candidate;
                return candidate;
            }
        }

        lastRandomPattern = candidates[candidates.Count - 1];
        return lastRandomPattern;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }

    private IEnumerator PlayRhythmChart(RhythmChart chart)
    {
        if (chart == null)
        {
            yield break;
        }

        List<RhythmNoteData> notes = chart.GetNotesBySpawnTime().ToList();
        double chartStartTime = RhythmTimingUtility.GetPlaybackStartTime(chart);
        double chartZeroDspTime = AudioSettings.dspTime - chartStartTime;
        int nextNoteIndex = 0;

        ScheduleChartAudio(chart, chartZeroDspTime);

        while (isAttacking)
        {
            double chartTime = AudioSettings.dspTime - chartZeroDspTime;

            while (nextNoteIndex < notes.Count && notes[nextNoteIndex].SpawnTime <= chartTime)
            {
                SpawnChartNote(chart, notes[nextNoteIndex]);
                nextNoteIndex++;
            }

            if (nextNoteIndex >= notes.Count && chartTime >= chart.EffectiveDuration)
            {
                break;
            }

            yield return null;
        }

        StopChartAudio();
    }

    private void ScheduleChartAudio(RhythmChart chart, double chartZeroDspTime)
    {
        StopChartAudio();

        if (!playChartAudio || chart.AudioClip == null)
        {
            return;
        }

        if (chartAudioSource == null)
        {
            Debug.LogWarning($"{name} cannot play chart audio because no AudioSource is assigned.", this);
            return;
        }

        chartAudioSource.clip = chart.AudioClip;
        chartAudioSource.time = 0f;
        chartAudioSource.PlayScheduled(Math.Max(AudioSettings.dspTime, chartZeroDspTime));
    }

    private void StopChartAudio()
    {
        if (chartAudioSource != null)
        {
            chartAudioSource.Stop();
        }
    }

    private void SpawnChartNote(RhythmChart chart, RhythmNoteData noteData)
    {
        RhythmLaneData lane = chart.FindLane(noteData.LaneId);
        if (lane == null)
        {
            Debug.LogWarning($"Chart '{chart.name}' contains a note with a missing lane.", chart);
            return;
        }

        GameObject prefab = chart.ResolvePrefab(noteData);
        if (prefab == null)
        {
            Debug.LogWarning($"Chart '{chart.name}' has no prefab for {noteData.NoteType}.", chart);
            return;
        }

        GameObject noteObject = Instantiate(
            prefab,
            transform.position,
            Quaternion.identity,
            projectileObjectHolder);

        Note note = noteObject.GetComponent<Note>();
        if (note == null)
        {
            Debug.LogWarning($"Prefab '{prefab.name}' does not contain a Note component.", prefab);
            Destroy(noteObject);
            return;
        }

        note.SetNoteIdentity(lane.KeyIdentity);
        note.SetSpeed(Mathf.Max(0.01f, noteData.Speed));
        note.damage = Mathf.Max(0, noteData.Damage);
        note.state = noteData.PlayerState;
        note.hitEffect = noteData.HitEffect;
        ApplyInitializeMovement(note, noteData.InitializeMovementType);

        if (note is HoldNoteObject holdNote)
        {
            holdNote.length = noteData.LegacyHoldLength;
        }

        if (note is HoldLaserNote holdLaser)
        {
            holdLaser.length = noteData.LegacyHoldLength;
        }

        noteObject.SetActive(true);
        AttackAnimate?.Invoke();
    }

    private static void ApplyInitializeMovement(Note note, NoteInitializeMovementType movementType)
    {
        NoteInitializeMovement initializeMovement = note.GetComponent<NoteInitializeMovement>();
        if (initializeMovement == null && movementType != NoteInitializeMovementType.None)
        {
            initializeMovement = note.gameObject.AddComponent<NoteInitializeMovement>();
        }

        if (initializeMovement != null)
        {
            initializeMovement.SetMovementType(movementType);
        }
    }

    private IEnumerator GeneratePongNote()
    {
        yield return new WaitForSeconds(1f);
        if (!isAttacking || pongNoteObjectPrefab == null)
        {
            yield break;
        }

        GameObject pong = Instantiate(pongNoteObjectPrefab, transform.position, Quaternion.identity, projectileObjectHolder);
        PongNote note = pong.GetComponent<PongNote>();
        if (note != null)
        {
            note.SetNoteIdentity(GetRandomLaneIdentity());
            note.SetSpeed(3f);
        }

        pong.SetActive(true);
        AttackAnimate?.Invoke();

        while (isAttacking && pong != null)
        {
            yield return null;
        }
    }

    private IEnumerator GenerateRandomLaser(float duration)
    {
        float startTime = Time.time;
        int previousNoteIdentity = -1;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, laserGenerationSpeed));
            if (!isAttacking || lasePrefab == null)
            {
                continue;
            }

            int noteIdentity = GetRandomLaneIdentity(previousNoteIdentity);
            previousNoteIdentity = noteIdentity;
            SpawnLegacyNote(lasePrefab, noteIdentity, 0f, PlayerState.Default, HitEffect.Default, false);
        }
    }

    private IEnumerator GenerateRandomHoldLaser(float duration)
    {
        float startTime = Time.time;
        int previousNoteIdentity = -1;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, holdLaserGenerationSpeed));
            if (!isAttacking || holdLaserPrefab == null)
            {
                continue;
            }

            int noteIdentity = GetRandomLaneIdentity(previousNoteIdentity);
            previousNoteIdentity = noteIdentity;
            GameObject noteObject = SpawnLegacyNote(
                holdLaserPrefab,
                noteIdentity,
                4f,
                PlayerState.Default,
                HitEffect.Default,
                false);

            HoldLaserNote holdLaser = noteObject != null ? noteObject.GetComponent<HoldLaserNote>() : null;
            if (holdLaser != null)
            {
                holdLaser.length = UnityEngine.Random.Range(5f, 6f);
            }
        }
    }

    private IEnumerator GenerateWaveNotesForDuration(float duration)
    {
        float startTime = Time.time;
        int[] wavePattern = GetWaveLaneIdentities();
        int index = 0;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(0.1f);
            if (!isAttacking || noteObjectPrefab == null || wavePattern.Length == 0)
            {
                continue;
            }

            SpawnLegacyNote(
                noteObjectPrefab,
                wavePattern[index],
                8f,
                PlayerState.Default,
                HitEffect.Default,
                true);
            index = (index + 1) % wavePattern.Length;
        }
    }

    private IEnumerator GenerateNormalNotesForDuration(float duration)
    {
        float startTime = Time.time;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, normalNoteGenerationSpeed));
            if (!isAttacking || noteObjectPrefab == null)
            {
                continue;
            }

            SpawnLegacyNote(
                noteObjectPrefab,
                GetRandomLaneIdentity(),
                10f,
                PlayerState.Default,
                HitEffect.Default,
                true);
        }
    }

    private IEnumerator GenerateHoldNotesForDuration(float duration)
    {
        float startTime = Time.time;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, holdNoteGenerationSpeed));
            if (!isAttacking || longNoteObjectPrefab == null)
            {
                continue;
            }

            float holdSpeed = Mathf.Max(0.01f, holdNoteSpeed);
            float minDuration = Mathf.Max(0.05f, Mathf.Min(holdNoteDurationRange.x, holdNoteDurationRange.y));
            float maxDuration = Mathf.Max(minDuration, Mathf.Max(holdNoteDurationRange.x, holdNoteDurationRange.y));
            GameObject noteObject = SpawnLegacyNote(
                longNoteObjectPrefab,
                GetRandomLaneIdentity(),
                holdSpeed,
                PlayerState.Default,
                HitEffect.Default,
                false);

            HoldNoteObject holdNote = noteObject != null ? noteObject.GetComponent<HoldNoteObject>() : null;
            if (holdNote != null)
            {
                holdNote.length = UnityEngine.Random.Range(minDuration, maxDuration) * holdSpeed;
            }
        }
    }

    private IEnumerator GenerateSimultaneousNotes(float duration, int maxProjectileCount, bool randomAmount)
    {
        float startTime = Time.time;

        while (isAttacking && Time.time - startTime < duration)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, generationSpeed));
            if (!isAttacking || noteObjectPrefab == null)
            {
                continue;
            }

            List<int> availableLanes = GetAvailableLaneIdentities();
            int maximum = Mathf.Min(Mathf.Max(1, maxProjectileCount), availableLanes.Count);
            int projectileCount = randomAmount ? UnityEngine.Random.Range(1, maximum + 1) : maximum;
            Shuffle(availableLanes);

            for (int i = 0; i < projectileCount; i++)
            {
                SpawnLegacyNote(
                    noteObjectPrefab,
                    availableLanes[i],
                    10f,
                    PlayerState.Default,
                    HitEffect.Default,
                    true);
            }
        }
    }

    private GameObject SpawnLegacyNote(
        GameObject prefab,
        int noteIdentity,
        float speed,
        PlayerState state,
        HitEffect hitEffect,
        bool allowRandomInitializeMovement)
    {
        GameObject noteObject = Instantiate(prefab, transform.position, Quaternion.identity, projectileObjectHolder);
        Note note = noteObject.GetComponent<Note>();
        if (note == null)
        {
            Debug.LogWarning($"Prefab '{prefab.name}' does not contain a Note component.", prefab);
            Destroy(noteObject);
            return null;
        }

        note.SetNoteIdentity(noteIdentity);
        if (speed > 0f)
        {
            note.SetSpeed(speed);
        }

        note.state = state;
        note.hitEffect = hitEffect;
        if (allowRandomInitializeMovement)
        {
            ApplyRandomInitializeMovement(note);
        }

        noteObject.SetActive(true);
        AttackAnimate?.Invoke();
        return noteObject;
    }

    private int GetRandomLaneIdentity(int identityToAvoid = int.MinValue)
    {
        List<int> lanes = GetAvailableLaneIdentities();
        if (lanes.Count > 1)
        {
            lanes.Remove(identityToAvoid);
        }

        return lanes[UnityEngine.Random.Range(0, lanes.Count)];
    }

    private List<int> GetAvailableLaneIdentities()
    {
        if (rhythmChart != null)
        {
            List<int> chartLanes = rhythmChart.Lanes
                .Where(lane => lane != null)
                .Select(lane => lane.KeyIdentity)
                .Distinct()
                .ToList();
            if (chartLanes.Count > 0)
            {
                return chartLanes;
            }
        }

        int laneCount = keyCodesAsign != null && keyCodesAsign.Length > 0 ? keyCodesAsign.Length : 5;
        return Enumerable.Range(1, laneCount).ToList();
    }

    private int[] GetWaveLaneIdentities()
    {
        List<int> lanes = GetAvailableLaneIdentities();
        if (lanes.Count <= 1)
        {
            return lanes.ToArray();
        }

        return lanes.Concat(lanes.Skip(1).Take(lanes.Count - 2).Reverse()).ToArray();
    }

    private void ApplyRandomInitializeMovement(Note note)
    {
        if (!enableMissileInitializeMovement
            || note == null
            || UnityEngine.Random.value > missileInitializeChance)
        {
            return;
        }

        ApplyInitializeMovement(note, NoteInitializeMovementType.MissileSCurve);
    }

    [ContextMenu("Reset Attack Sequence To Defaults")]
    private void ResetAttackSequenceToDefaults()
    {
        attackSequence = NoteAttackPattern.CreateDefaultSequence();
    }

    [ContextMenu("Use Assigned Rhythm Chart Only")]
    private void UseAssignedRhythmChartOnly()
    {
        attackSequence = new List<NoteAttackPattern>
        {
            new NoteAttackPattern(NoteAttackPatternType.RhythmChart, "Assigned Rhythm Chart")
        };
    }

    private void Reset()
    {
        ResetAttackSequenceToDefaults();
    }

    private void OnValidate()
    {
        attackSequence ??= NoteAttackPattern.CreateDefaultSequence();
        attackDuration = Mathf.Max(0.01f, attackDuration);
        generationSpeed = Mathf.Max(0.01f, generationSpeed);
        normalNoteGenerationSpeed = Mathf.Max(0.01f, normalNoteGenerationSpeed);
        laserGenerationSpeed = Mathf.Max(0.01f, laserGenerationSpeed);
        holdLaserGenerationSpeed = Mathf.Max(0.01f, holdLaserGenerationSpeed);
        holdNoteGenerationSpeed = Mathf.Max(0.01f, holdNoteGenerationSpeed);
        holdNoteSpeed = Mathf.Max(0.01f, holdNoteSpeed);

        foreach (NoteAttackPattern pattern in attackSequence.Where(pattern => pattern != null))
        {
            pattern.Validate();
        }
    }
}
