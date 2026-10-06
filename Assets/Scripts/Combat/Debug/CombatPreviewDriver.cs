using System;
using System.Collections;
using System.Collections.Generic;
using RythmRPG.Core;
using RythmRPG.Rhythm;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Where preview notes come from.</summary>
    public enum PreviewNoteOrigin
    {
        /// <summary>The enemy, like an enemy attack.</summary>
        Enemy,
        /// <summary>Centre stage, like the projectiles of the player's ability chart.</summary>
        CentreStage
    }

    /// <summary>What happens to preview notes when they reach the hit line.</summary>
    public enum PreviewNotePlay
    {
        /// <summary>Pressed for you on time (plus the timing offset), so hit effects show.</summary>
        AutoPlay,
        /// <summary>Nobody presses: they pass the line and resolve as misses (no damage in a preview).</summary>
        LetThemPass,
        /// <summary>Your lane keys play them (click the Game view first).</summary>
        PlayYourself
    }

    /// <summary>Note type of the preview notes. Auto picks it from the prefab's note component.</summary>
    public enum PreviewNoteKind
    {
        Auto,
        Normal,
        Hold,
        Laser,
        HoldLaser,
        Pong,
        Arrow,
        Cluster,
        Mash
    }

    /// <summary>One note spawn request of the Combat Preview window.</summary>
    [Serializable]
    public sealed class NotePreviewSettings
    {
        public GameObject prefab;
        public PreviewNoteKind kind = PreviewNoteKind.Auto;
        [Tooltip("Lanes shown in the preview (a chart's lane count, 1-4).")]
        [Range(1, RhythmChart.MaxLanes)] public int laneCount = 4;
        [Tooltip("Lanes to spawn on: bit 0 = lane 1 ... bit 3 = lane 4.")]
        public int laneMask = 1 << 1;
        [Tooltip("Notes per lane.")]
        [Min(1)] public int count = 1;
        [Tooltip("Seconds between notes on the same lane.")]
        [Min(0f)] public float interval = 0.5f;
        [Tooltip("Seconds from spawn to the hit line (the chart's Travel Time).")]
        [Min(0.05f)] public float travelSeconds = 2.5f;
        [Min(0.01f)] public float speed = 8f;
        [Tooltip("Hold / Hold Laser length in seconds.")]
        [Min(0f)] public float holdSeconds = 1f;
        [Min(1)] public int mashPresses = 8;
        public NoteInitializeMovementType initializeMovement = NoteInitializeMovementType.None;
        public PreviewNoteOrigin origin = PreviewNoteOrigin.Enemy;
        public PreviewNotePlay play = PreviewNotePlay.AutoPlay;
        [Tooltip("Auto Play presses this many seconds late (+) or early (-): try it to see Good / Bad hits.")]
        [Range(-0.5f, 0.5f)] public float autoPlayOffset;
        [Tooltip("Optional enemy animator state played before the notes spawn (e.g. its attack wind-up).")]
        public string windUpAnimation = string.Empty;
        [Tooltip("Seconds between the wind-up animation and the first note spawning.")]
        [Min(0f)] public float windUpSeconds;

        public bool UsesLane(int lane) => lane >= 1 && lane <= laneCount && (laneMask & (1 << (lane - 1))) != 0;

        public RhythmNoteType ResolveType()
        {
            if (kind != PreviewNoteKind.Auto) return (RhythmNoteType)((int)kind - 1);
            return DetectType(prefab);
        }

        /// <summary>The note type a prefab's note component plays as.</summary>
        public static RhythmNoteType DetectType(GameObject notePrefab)
        {
            Note note = notePrefab != null ? notePrefab.GetComponent<Note>() : null;
            return note switch
            {
                CombatNote combat => combat.ChartNoteType,
                HoldLaserNote => RhythmNoteType.HoldLaser,
                HoldNoteObject => RhythmNoteType.Hold,
                StationaryHoldNote => RhythmNoteType.Hold,
                LaserNote => RhythmNoteType.Laser,
                MashNote => RhythmNoteType.Mash,
                PongNote => RhythmNoteType.Pong,
                ArrowNote => RhythmNoteType.Arrow,
                ClusterNote => RhythmNoteType.Cluster,
                _ => RhythmNoteType.Normal
            };
        }
    }

    /// <summary>
    /// Runtime side of the Combat Preview window (Tools > Rythm RPG > Combat > Combat Preview). Starts a preview battle
    /// on the scene's <see cref="CombatController"/> (staged like a real battle, but no turns, damage or mana) and plays
    /// the player's <see cref="CharacterAttackSequence"/> or spawns note prefabs through the real pattern runner, so they
    /// look exactly as they will in combat. Edit the assets in the Inspector and play again: ScriptableObject and prefab
    /// edits made in Play mode are kept.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatPreviewDriver : MonoBehaviour
    {
        private const float MashRate = 14f;

        private CombatController controller;
        private Coroutine attackRoutine;
        private Coroutine notesRoutine;
        // Kept so Stop can dispose them: StopCoroutine alone skips their finally blocks (the player would stay out of
        // the hit line, off its spot, with its character controller off).
        private IEnumerator attackEnumerator;
        private IEnumerator notesEnumerator;
        private RhythmChart previewChart;
        private float runStartedAt;
        private readonly List<string> log = new();

        public CombatController Controller => controller;
        public bool IsActive => controller != null && controller.IsPreviewing;
        public bool IsReady => IsActive && controller.PreviewReady;
        public bool IsPlayingAttack => attackRoutine != null;
        public bool IsPlayingNotes => notesRoutine != null;
        public bool IsBusy => IsPlayingAttack || IsPlayingNotes;
        /// <summary>What the last run did (hit times and shares, notes spawned, judgements).</summary>
        public IReadOnlyList<string> Log => log;
        public string Status { get; private set; } = string.Empty;
        private readonly List<Note> spawnedNotes = new();
        /// <summary>Notes the last (or current) Spawn Notes run spawned (the Note Designer's playhead follows the first).</summary>
        public IReadOnlyList<Note> SpawnedNotes => spawnedNotes;
        /// <summary>Raised when the log or status changes (the window repaints).</summary>
        public event Action Changed;

        /// <summary>The driver on the scene's combat controller (added on demand; null outside Play mode or without one).</summary>
        public static CombatPreviewDriver Find()
        {
            if (!Application.isPlaying) return null;
            CombatController combat = FindAnyObjectByType<CombatController>();
            if (combat == null) return null;
            CombatPreviewDriver driver = combat.GetComponent<CombatPreviewDriver>();
            if (driver == null) driver = combat.gameObject.AddComponent<CombatPreviewDriver>();
            driver.controller = combat;
            return driver;
        }

        private void Awake()
        {
            if (controller == null) controller = GetComponent<CombatController>();
        }

        // ---------- battle ----------

        public bool Begin(PlayerCombatant player, EnemyCombatant enemy, out string error)
        {
            error = null;
            if (controller == null) controller = GetComponent<CombatController>();
            if (controller == null) error = "No CombatController in the scene.";
            else if (player == null) error = "No PlayerCombatant in the scene.";
            else if (enemy == null) error = "Pick an enemy.";
            else if (controller.IsBattleActive && !controller.IsPreviewing) error = "A real battle is running. End it first.";
            if (error != null) return false;
            if (controller.IsPreviewing) End();
            player.SendMessage("DisableMovement", SendMessageOptions.DontRequireReceiver);
            if (!controller.BeginPreview(new CombatEncounterContext(player, enemy)))
            {
                error = "The preview battle could not start.";
                return false;
            }
            SetStatus($"Staging the encounter with {enemy.name}...");
            return true;
        }

        public void End()
        {
            Stop();
            if (controller != null) controller.EndPreview();
            SetStatus("Preview ended.");
        }

        /// <summary>Stops whatever is playing and clears preview notes (the preview battle stays up).</summary>
        public void Stop()
        {
            if (attackRoutine != null) StopCoroutine(attackRoutine);
            attackRoutine = null;
            (attackEnumerator as IDisposable)?.Dispose();
            attackEnumerator = null;
            if (notesRoutine != null) StopCoroutine(notesRoutine);
            notesRoutine = null;
            (notesEnumerator as IDisposable)?.Dispose();
            notesEnumerator = null;
            RhythmPatternRunner runner = controller != null ? controller.PatternRunner : null;
            if (runner != null && runner.IsRunning) runner.CancelCurrentPattern(false);
            if (controller != null && controller.InputRouter != null && controller.IsPreviewing)
                controller.InputRouter.SetState(CombatState.BattleStart);
            DestroyPreviewChart();
        }

        private void OnDisable() => Stop();

        // ---------- player attack ----------

        /// <summary>
        /// Plays an attack sequence like the end of an ability turn: the character steps out of the hit line, walks to the
        /// stage, runs the steps and walks back. Hits flash the enemy and show their share, but deal no damage.
        /// </summary>
        public bool PlayAttack(CharacterAttackSequence sequence, Color accent, AbilityVFXProfile timing, bool stepOutOfHitLine,
            AbilityRuntimeInstance fallbackImpact = null)
        {
            if (!IsReady || (sequence == null && fallbackImpact == null) || IsBusy) return false;
            attackEnumerator = AttackRoutine(sequence, accent, timing, stepOutOfHitLine, fallbackImpact);
            attackRoutine = StartCoroutine(attackEnumerator);
            return true;
        }

        private IEnumerator AttackRoutine(CharacterAttackSequence sequence, Color accent, AbilityVFXProfile timing,
            bool stepOutOfHitLine, AbilityRuntimeInstance fallbackImpact)
        {
            BeginLog(sequence != null ? $"Attack: {sequence.name}" : $"Impact: {fallbackImpact.Definition.name}");
            PlayerCombatant player = controller.Encounter.Player;
            EnemyCombatant enemy = controller.Encounter.Enemy;
            CombatVFXController vfx = controller.Vfx;
            bool steppedOut = sequence != null && stepOutOfHitLine && vfx != null;
            IEnumerator perform = null;
            try
            {
                if (steppedOut) vfx.StepOutOfHitLine();
                if (timing != null && timing.ImpactAnticipationDuration > 0f)
                    yield return new WaitForSeconds(timing.ImpactAnticipationDuration);
                if (steppedOut)
                {
                    float waitUntil = Time.time + 2f;
                    while (vfx.CharacterMorphing && Time.time < waitUntil) yield return null;
                }

                if (sequence != null)
                {
                    float total = sequence.TotalHitWeight;
                    int hits = 0;
                    Write($"{sequence.Steps.Count} steps, total hit weight {total:0.##}" + (total <= 0f ? " (no hits: the ability's effects land at the end)" : ""));
                    CharacterAttackPerformer performer = controller.GetComponent<CharacterAttackPerformer>();
                    if (performer == null) performer = controller.gameObject.AddComponent<CharacterAttackPerformer>();
                    perform = performer.Perform(sequence, player, enemy, accent, weight =>
                    {
                        hits++;
                        float share = total > 0f ? weight / total : 1f;
                        Write($"Hit {hits}: weight {weight:0.##} = {share:P0} of the damage");
                        PreviewHit(enemy, vfx, $"HIT {hits}  {share:P0}");
                    });
                    // Stepped by hand (not "yield return perform") so a Stop can dispose it and its finally puts the
                    // player back on its spot.
                    while (perform.MoveNext()) yield return perform.Current;
                    perform = null;
                    if (hits == 0) PreviewHit(enemy, vfx, "IMPACT");
                }
                else if (vfx != null)
                {
                    Write("No attack sequence: the default impact projectile.");
                    yield return vfx.PlayAbilityImpact(fallbackImpact, vfx.GetCenterLaneViewTransform(), enemy != null ? enemy.transform : null);
                    PreviewHit(enemy, vfx, "IMPACT");
                }

                if (steppedOut) vfx.StepBackIntoHitLine();
                steppedOut = false;
                if (timing != null && timing.ImpactSettleDuration > 0f)
                    yield return new WaitForSeconds(timing.ImpactSettleDuration);
                Write("Done.");
                SetStatus("Attack finished.");
            }
            finally
            {
                (perform as IDisposable)?.Dispose();
                if (steppedOut && vfx != null) vfx.StepBackIntoHitLine();
                attackRoutine = null;
                attackEnumerator = null;
                Changed?.Invoke();
            }
        }

        private static void PreviewHit(EnemyCombatant enemy, CombatVFXController vfx, string label)
        {
            if (enemy != null && enemy.Definition != null) enemy.PlayAnimation(enemy.Definition.HitAnimationName);
            vfx?.PreviewEnemyHit(label);
        }

        // ---------- notes ----------

        /// <summary>Spawns note prefabs through the real pattern runner (a small chart made on the fly).</summary>
        public bool PlayNotes(NotePreviewSettings settings)
        {
            if (!IsReady || settings == null || settings.prefab == null || IsBusy) return false;
            if (settings.prefab.GetComponent<Note>() == null)
            {
                SetStatus($"'{settings.prefab.name}' has no Note component, so combat can't spawn it.");
                return false;
            }
            notesEnumerator = NotesRoutine(settings);
            notesRoutine = StartCoroutine(notesEnumerator);
            return true;
        }

        private IEnumerator NotesRoutine(NotePreviewSettings settings)
        {
            RhythmPatternRunner runner = controller.PatternRunner;
            EnemyCombatant enemy = controller.Encounter.Enemy;
            PlayerCombatant player = controller.Encounter.Player;
            List<Note> spawned = spawnedNotes;
            spawned.Clear();
            void OnSpawned(Note note)
            {
                if (note != null) spawned.Add(note);
            }
            void OnResolved(RhythmJudgementResult result)
            {
                if (result.Source == NoteResolutionSource.SystemClear) return;
                Write($"{Clock:0.00}s  lane {result.LaneId}: {result.Judgement}" + (result.Source == NoteResolutionSource.Timeout ? " (passed)" : ""));
            }

            RhythmNoteType type = settings.ResolveType();
            BeginLog($"Notes: {settings.prefab.name} as {type}");
            controller.PreviewUseLaneCount(settings.laneCount);
            bool enemyOrigin = settings.origin == PreviewNoteOrigin.Enemy;
            if (enemyOrigin && enemy != null && !string.IsNullOrWhiteSpace(settings.windUpAnimation))
            {
                enemy.PlayAnimation(settings.windUpAnimation);
                if (settings.windUpSeconds > 0f) yield return new WaitForSeconds(settings.windUpSeconds);
            }

            previewChart = BuildChart(settings, type, out int noteCount);
            if (noteCount == 0)
            {
                SetStatus("Pick at least one lane.");
                DestroyPreviewChart();
                notesRoutine = null;
                notesEnumerator = null;
                yield break;
            }

            PatternRunMode mode = enemyOrigin ? PatternRunMode.EnemyDefense : PatternRunMode.PlayerAbility;
            Transform origin = enemyOrigin ? enemy != null ? enemy.transform : null
                : controller.Vfx != null ? controller.Vfx.AbilityPatternSpawnOrigin : null;
            if (settings.play == PreviewNotePlay.PlayYourself)
                controller.InputRouter.SetState(enemyOrigin ? CombatState.EnemyTurnExecuting : CombatState.PlayerAbilityExecuting);

            runner.NoteSpawned += OnSpawned;
            controller.JudgementSystem.OnJudgementResolved += OnResolved;
            try
            {
                Write($"{noteCount} note(s), travel {settings.travelSeconds:0.##}s, from {(enemyOrigin ? "the enemy" : "centre stage")}");
                SetStatus("Notes playing...");
                // Chart time 0 is now; the first note spawns a moment later.
                runner.Run(previewChart, new PatternRunContext(mode, player, origin),
                    GameAudioClock.Now);
                if (settings.play == PreviewNotePlay.AutoPlay)
                    yield return AutoPlay(runner, spawned, settings.autoPlayOffset);
                else
                    while (runner.IsRunning) yield return null;
                SetStatus("Notes finished.");
            }
            finally
            {
                runner.NoteSpawned -= OnSpawned;
                if (controller.JudgementSystem != null) controller.JudgementSystem.OnJudgementResolved -= OnResolved;
                if (controller.IsPreviewing) controller.InputRouter.SetState(CombatState.BattleStart);
                DestroyPreviewChart();
                notesRoutine = null;
                notesEnumerator = null;
                Changed?.Invoke();
            }
        }

        /// <summary>A throwaway chart: one lane per key, the prefab as every note's prefab.</summary>
        public static RhythmChart BuildChart(NotePreviewSettings settings, RhythmNoteType type, out int noteCount)
        {
            const double lead = 0.05d;
            noteCount = 0;
            RhythmChart chart = ScriptableObject.CreateInstance<RhythmChart>();
            chart.hideFlags = HideFlags.DontSave;
            chart.name = "Preview " + (settings.prefab != null ? settings.prefab.name : "notes");
            chart.NoteDefinitions.Add(new RhythmNoteDefinition(type, Color.white, settings.speed, 1, settings.travelSeconds)
            {
                DefaultPrefab = settings.prefab
            });
            int laneCount = Mathf.Clamp(settings.laneCount, 1, RhythmChart.MaxLanes);
            for (int lane = 1; lane <= laneCount; lane++)
                chart.Lanes.Add(new RhythmLaneData("Lane " + lane, lane, Color.white));

            bool hold = RhythmTimingUtility.IsHoldType(type);
            for (int i = 0; i < Mathf.Max(1, settings.count); i++)
            {
                double hitTime = lead + settings.travelSeconds + i * (double)settings.interval;
                foreach (RhythmLaneData lane in chart.Lanes)
                {
                    if (!settings.UsesLane(lane.KeyIdentity)) continue;
                    chart.Notes.Add(new RhythmNoteData(lane.Id, hitTime, type)
                    {
                        TravelTime = settings.travelSeconds,
                        Speed = settings.speed,
                        Damage = 1,
                        HoldDuration = hold ? settings.holdSeconds : 0d,
                        MashRequiredPresses = settings.mashPresses,
                        InitializeMovementType = settings.initializeMovement,
                        PrefabOverride = settings.prefab
                    });
                    noteCount++;
                }
            }
            return chart;
        }

        // Presses for the player: taps on the hit time (+ offset), holds until their end, mashes at a steady rate.
        private IEnumerator AutoPlay(RhythmPatternRunner runner, List<Note> notes, float offset)
        {
            var pressed = new HashSet<Note>();
            var holding = new Dictionary<Note, int>();
            var nextMash = new Dictionary<Note, float>();
            while (runner.IsRunning)
            {
                double now = runner.ChartSeconds;
                foreach (Note note in notes.ToArray())
                {
                    if (note == null) continue;
                    RhythmNoteData data = note.Data;
                    if (data == null) continue;
                    int lane = note.GetNoteIdentity();
                    if (note is MashNote || (note is CombatNote combat && combat.IsMash))
                    {
                        if (note.IsResolved) continue;
                        double window = Math.Min(1.2d, data.TravelTime * 0.6d);
                        if (now < data.HitTime - window) continue;
                        if (!nextMash.TryGetValue(note, out float at) || Time.time >= at)
                        {
                            runner.HandleLanePressed(lane);
                            runner.HandleLaneReleased(lane);
                            nextMash[note] = Time.time + 1f / MashRate;
                        }
                        continue;
                    }

                    if (!pressed.Contains(note) && !note.IsResolved && now >= data.HitTime + offset)
                    {
                        pressed.Add(note);
                        runner.HandleLanePressed(lane);
                        if (data.IsHold) holding[note] = lane;
                        else runner.HandleLaneReleased(lane);
                    }

                    if (holding.TryGetValue(note, out int held) && now >= data.EndTime)
                    {
                        runner.HandleLaneReleased(held);
                        holding.Remove(note);
                    }
                }
                yield return null;
            }
            foreach (int lane in holding.Values) runner.HandleLaneReleased(lane);
        }

        // ---------- spawn points ----------

        /// <summary>Settings whose spawn points are drawn as gizmos (Scene view, and the Game view with Gizmos on). Null = none.</summary>
        public NotePreviewSettings ShowSpawnPointsFor { get; set; }

        /// <summary>
        /// Where the settings' prefab spawns on a lane (with its Note Spawn Offset) and where it would without one, plus
        /// the lane's forward direction (for converting a dragged handle back into the offset).
        /// </summary>
        public bool TryGetSpawnPoint(NotePreviewSettings settings, int lane, out Vector3 basePoint, out Vector3 point,
            out Vector3 laneForward)
        {
            basePoint = point = laneForward = default;
            RhythmPatternRunner runner = controller != null ? controller.PatternRunner : null;
            if (!IsReady || runner == null || settings == null) return false;
            bool enemyOrigin = settings.origin == PreviewNoteOrigin.Enemy;
            EnemyCombatant enemy = controller.Encounter.Enemy;
            Transform origin = enemyOrigin ? enemy != null ? enemy.transform : null
                : controller.Vfx != null ? controller.Vfx.AbilityPatternSpawnOrigin : null;
            if (origin == null) return false;
            PatternRunMode mode = enemyOrigin ? PatternRunMode.EnemyDefense : PatternRunMode.PlayerAbility;
            point = runner.ResolveSpawnPoint(settings.prefab, lane, origin, mode, out basePoint);
            laneForward = runner.LaneForward(lane);
            return true;
        }

        private void OnDrawGizmos()
        {
            NotePreviewSettings settings = ShowSpawnPointsFor;
            if (settings == null || !IsReady) return;
            for (int lane = 1; lane <= settings.laneCount; lane++)
            {
                if (!settings.UsesLane(lane) || !TryGetSpawnPoint(settings, lane, out Vector3 basePoint, out Vector3 point, out _)) continue;
                Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
                Gizmos.DrawWireSphere(basePoint, 0.08f);
                Gizmos.DrawLine(basePoint, point);
                Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.95f);
                Gizmos.DrawSphere(point, 0.12f);
            }
        }

        // ---------- log ----------

        private float Clock => Time.time - runStartedAt;

        private void BeginLog(string title)
        {
            log.Clear();
            runStartedAt = Time.time;
            log.Add(title);
            SetStatus(title);
        }

        private void Write(string line)
        {
            log.Add(line.StartsWith("Hit ", StringComparison.Ordinal) || line == "Done." ? $"{Clock:0.00}s  {line}" : line);
            Changed?.Invoke();
        }

        private void SetStatus(string status)
        {
            Status = status ?? string.Empty;
            Changed?.Invoke();
        }

        private void DestroyPreviewChart()
        {
            if (previewChart == null) return;
            Destroy(previewChart);
            previewChart = null;
        }
    }
}
