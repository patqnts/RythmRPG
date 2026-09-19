using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Rhythm;
using UnityEngine;
using UnityEngine.Serialization;

namespace RythmRPG.Combat
{
    public readonly struct PatternRunContext
    {
        public readonly PatternRunMode Mode;
        public readonly PlayerCombatant Player;
        public readonly Transform SpawnOrigin;
        public readonly float DurationOverride;
        public readonly AttackStepEndPolicy EndPolicy;

        public PatternRunContext(PatternRunMode mode, PlayerCombatant player, Transform spawnOrigin = null,
            float durationOverride = 0f, AttackStepEndPolicy endPolicy = AttackStepEndPolicy.WaitForResolvedNotes)
        {
            Mode = mode;
            Player = player;
            SpawnOrigin = spawnOrigin;
            DurationOverride = durationOverride;
            EndPolicy = endPolicy;
        }
    }

    public readonly struct PatternRunResult
    {
        public readonly PatternRunMode Mode;
        public readonly RhythmPerformanceResult Performance;
        public readonly bool WasCancelled;

        public PatternRunResult(PatternRunMode mode, RhythmPerformanceResult performance, bool wasCancelled)
        {
            Mode = mode;
            Performance = performance;
            WasCancelled = wasCancelled;
        }
    }

    public sealed class RhythmPatternRunner : MonoBehaviour
    {
        [Header("Pattern Presentation")]
        [FormerlySerializedAs("rhythmChart")]
        [SerializeField] private RhythmChart fallbackChart;
        [SerializeField] private Transform projectileObjectHolder;
        [SerializeField] private AudioSource chartAudioSource;
        [SerializeField] private bool playChartAudio;
        [SerializeField] private JudgementConfig judgementConfig;

        private readonly List<Note> activeNotes = new();
        private readonly List<RhythmJudgementResult> results = new();
        private Coroutine runCoroutine;
        private PatternRunContext currentContext;
        private int expectedNoteCount;
        private bool cancelled;
        private KeyButton[] cachedKeys;

        public bool IsRunning => runCoroutine != null;
        public RhythmChart FallbackChart => fallbackChart;
        public float BadWindow => judgementConfig != null ? judgementConfig.BadWindow : 0.9f;
        public IReadOnlyList<Note> ActiveNotes => activeNotes;
        public event Action<RhythmChart, PatternRunContext> PatternStarted;
        public event Action<Note> NoteSpawned;
        public event Action<RhythmJudgementResult> NoteResolved;
        public event Action<PatternRunResult> PatternCompleted;

        private void Awake()
        {
            judgementConfig ??= Resources.Load<JudgementConfig>("Combat/Judgement/JudgementConfig");
            if (projectileObjectHolder == null)
            {
                GameObject holder = GameObject.Find("ProjectileHolder");
                projectileObjectHolder = holder != null ? holder.transform : transform;
            }
        }

        public void ConfigureInput(LaneInputRouter inputRouter)
        {
            cachedKeys = inputRouter != null ? inputRouter.GetViews() : null;
        }

        private void Update()
        {
            if (!IsRunning) return;
            foreach (Note note in activeNotes.ToArray())
            {
                if (note == null)
                {
                    activeNotes.Remove(note);
                    continue;
                }
                KeyButton key = note.keys?.FirstOrDefault(candidate => candidate != null && candidate.keyIdentity == note.GetNoteIdentity());
                if (note.ShouldAutoMissByPosition
                    && key != null
                    && note.HasPassedMissWindow(key, BadWindow))
                {
                    note.ForceMiss(key);
                }
            }
        }

        public Coroutine Run(RhythmChart chart, PatternRunContext context)
        {
            CancelCurrentPattern(false);
            currentContext = context;
            cancelled = false;
            runCoroutine = StartCoroutine(RunRoutine(chart));
            return runCoroutine;
        }

        public void HandleLanePressed(int laneId)
        {
            if (!IsRunning) return;
            KeyButton key = GetKeys().FirstOrDefault(candidate => candidate.keyIdentity == laneId);
            if (key == null) return;
            Note best = activeNotes.Where(note => note != null && note.CanReceiveHit(key))
                .OrderBy(note => note.GetTimingError(key)).FirstOrDefault();
            if (best == null) return;
            float distance = best.GetTimingError(key);
            HitJudgement judgement = Evaluate(distance);
            judgement = best.AdjustJudgement(judgement, distance);
            RhythmJudgementResult result = new(best.RuntimeNoteId, laneId, judgement, distance,
                best.GetJudgementWorldPosition(), NoteResolutionSource.PlayerInput);
            if (judgement == HitJudgement.Miss)
            {
                if (best.ShouldResolveMissOnPlayerInput) best.ForceResolve(result);
                return;
            }
            best.TryHitFromKey(key, result);
        }

        public void HandleLaneReleased(int laneId)
        {
            KeyButton key = GetKeys().FirstOrDefault(candidate => candidate.keyIdentity == laneId);
            if (key == null) return;
            foreach (Note note in activeNotes.Where(note => note != null && note.IsUsingKey(key)).ToArray())
                note.OnKeyReleased(key);
        }

        public void ResolveNote(Note note, RhythmJudgementResult result)
        {
            if (note == null || !activeNotes.Remove(note)) return;
            results.Add(result);
            NoteResolved?.Invoke(result);
            if (currentContext.Mode == PatternRunMode.EnemyDefense
                && note.ShouldDamagePlayerOnResolve(result)
                && currentContext.Player != null)
            {
                currentContext.Player.ApplyDamage(note.damage);
                if (currentContext.Player.IsDefeated) CancelCurrentPattern(true);
            }
        }

        public void ResolveCollateral(IEnumerable<Note> notes)
        {
            foreach (Note note in notes?.Where(note => note != null && activeNotes.Contains(note)).ToArray() ?? Array.Empty<Note>())
            {
                RhythmJudgementResult result = new(note.RuntimeNoteId, note.GetNoteIdentity(), HitJudgement.Perfect, 0f,
                    note.GetJudgementWorldPosition(), NoteResolutionSource.Modifier);
                note.TryHitFromKey(GetKeys().FirstOrDefault(key => key.keyIdentity == note.GetNoteIdentity()), result);
            }
        }

        public void CancelCurrentPattern(bool reportCompletion = true)
        {
            if (runCoroutine != null)
            {
                StopCoroutine(runCoroutine);
                runCoroutine = null;
            }
            cancelled = true;
            StopAudio();
            ClearRemaining(false);
            if (reportCompletion) Complete(true);
        }

        public void ClearRemaining(bool asMisses)
        {
            foreach (Note note in activeNotes.ToArray())
            {
                if (note == null) continue;
                if (asMisses)
                {
                    RhythmJudgementResult miss = new(note.RuntimeNoteId, note.GetNoteIdentity(), HitJudgement.Miss,
                        BadWindow, note.GetJudgementWorldPosition(), NoteResolutionSource.SystemClear);
                    note.ForceResolve(miss);
                }
                else
                {
                    activeNotes.Remove(note);
                    note.ClearWithoutResult();
                }
            }
        }

        private IEnumerator RunRoutine(RhythmChart chart)
        {
            results.Clear();
            expectedNoteCount = chart?.Notes.Count(note => note != null) ?? 0;
            PatternStarted?.Invoke(chart, currentContext);
            if (chart == null)
            {
                Complete(false);
                yield break;
            }

            List<RhythmNoteData> notes = chart.GetNotesBySpawnTime().ToList();
            double playbackStart = RhythmTimingUtility.GetPlaybackStartTime(chart);
            double zeroDspTime = AudioSettings.dspTime - playbackStart;
            int nextIndex = 0;
            ScheduleAudio(chart, zeroDspTime);
            double configuredEnd = currentContext.DurationOverride > 0f ? currentContext.DurationOverride : chart.EffectiveDuration;

            while (!cancelled)
            {
                double chartTime = AudioSettings.dspTime - zeroDspTime;
                while (nextIndex < notes.Count && notes[nextIndex].SpawnTime <= chartTime)
                    Spawn(chart, notes[nextIndex++]);

                if (chartTime >= configuredEnd)
                {
                    if (currentContext.EndPolicy == AttackStepEndPolicy.ClearAsMisses) ClearRemaining(true);
                    else if (currentContext.EndPolicy == AttackStepEndPolicy.ClearWithoutPenalty) ClearRemaining(false);
                    if (currentContext.EndPolicy != AttackStepEndPolicy.WaitForResolvedNotes || activeNotes.Count == 0) break;
                }
                if (nextIndex >= notes.Count && activeNotes.Count == 0 && chartTime >= chart.EffectiveDuration) break;
                if (chartTime > Math.Max(chart.EffectiveDuration, configuredEnd) + 8d)
                {
                    ClearRemaining(true);
                    break;
                }
                yield return null;
            }
            Complete(cancelled);
        }

        private void Spawn(RhythmChart chart, RhythmNoteData data)
        {
            RhythmLaneData lane = chart.FindLane(data.LaneId);
            GameObject prefab = chart.ResolvePrefab(data);
            if (lane == null || prefab == null) return;
            Transform origin = currentContext.SpawnOrigin != null ? currentContext.SpawnOrigin : transform;
            Quaternion rotation = projectileObjectHolder != null ? projectileObjectHolder.rotation : Quaternion.identity;
            GameObject instance = Instantiate(prefab, origin.position, rotation, projectileObjectHolder);
            RhythmNoteVisualLayer visualLayer = instance.GetComponent<RhythmNoteVisualLayer>();
            if (visualLayer == null) visualLayer = instance.AddComponent<RhythmNoteVisualLayer>();
            visualLayer.Configure();
            Note note = instance.GetComponent<Note>();
            if (note == null)
            {
                Destroy(instance);
                return;
            }
            note.Initialize(new RhythmNoteSpawnContext(this, data, data.Id, lane.KeyIdentity,
                Mathf.Max(0.01f, data.Speed), Mathf.Max(0, data.Damage), GetKeys()));
            ApplyInitializeMovement(note, data.InitializeMovementType);
            if (note is HoldNoteObject hold) hold.length = data.LegacyHoldLength;
            if (note is HoldLaserNote holdLaser) holdLaser.length = data.LegacyHoldLength;
            activeNotes.Add(note);
            instance.SetActive(true);
            NoteSpawned?.Invoke(note);
        }

        private void Complete(bool wasCancelled)
        {
            runCoroutine = null;
            StopAudio();
            RhythmPerformanceResult performance = RhythmPerformanceCalculator.Calculate(expectedNoteCount, results, null);
            PatternCompleted?.Invoke(new PatternRunResult(currentContext.Mode, performance, wasCancelled));
        }

        private HitJudgement Evaluate(float distance)
        {
            if (judgementConfig != null) return judgementConfig.Evaluate(distance);
            if (distance <= 0.15f) return HitJudgement.Perfect;
            if (distance <= 0.45f) return HitJudgement.Good;
            if (distance <= 0.9f) return HitJudgement.Bad;
            return HitJudgement.Miss;
        }

        private KeyButton[] GetKeys()
        {
            if (cachedKeys == null || cachedKeys.Length == 0)
                cachedKeys = FindObjectsByType<KeyButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return cachedKeys;
        }

        private void ScheduleAudio(RhythmChart chart, double zeroDspTime)
        {
            if (!playChartAudio || chartAudioSource == null || chart.AudioClip == null) return;
            chartAudioSource.clip = chart.AudioClip;
            chartAudioSource.PlayScheduled(Math.Max(AudioSettings.dspTime, zeroDspTime));
        }

        private void StopAudio()
        {
            if (chartAudioSource != null) chartAudioSource.Stop();
        }

        private static void ApplyInitializeMovement(Note note, NoteInitializeMovementType type)
        {
            NoteInitializeMovement movement = note.GetComponent<NoteInitializeMovement>();
            if (movement == null && type != NoteInitializeMovementType.None)
                movement = note.gameObject.AddComponent<NoteInitializeMovement>();
            if (movement != null) movement.SetMovementType(type);
        }
    }
}
