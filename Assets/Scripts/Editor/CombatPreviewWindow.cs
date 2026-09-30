using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using RythmRPG.Rhythm;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// Combat Preview: set up how player attacks and enemy notes look in combat. Enter Play mode in a combat scene,
    /// start a preview battle against an enemy (staged exactly like a real battle, but no turns, no damage), then play a
    /// Character Attack Sequence or spawn note prefabs. Edit the sequence right here (or the prefab in Prefab Mode) and
    /// play again; changes to assets and prefabs made in Play mode are kept.
    /// </summary>
    public sealed class CombatPreviewWindow : EditorWindow
    {
        private enum AttackSource { Ability, Sequence }
        private enum LastPlayed { None, Attack, Notes }

        [SerializeField] private AttackSource attackSource = AttackSource.Ability;
        [SerializeField] private AbilityDefinition ability;
        [SerializeField] private CharacterAttackSequence sequence;
        [SerializeField] private Color accent = Color.white;
        [SerializeField] private bool stepOutOfHitLine = true;
        [SerializeField] private bool useAbilityTiming = true;
        [SerializeField] private bool showSequenceEditor = true;
        [SerializeField] private NotePreviewSettings notes = new();
        [SerializeField] private bool loop;
        [SerializeField, Min(0f)] private float loopGap = 0.75f;
        [SerializeField] private string enemyName = string.Empty;
        [SerializeField] private bool showSpawnHandle = true;

        private Vector2 scroll;
        private Vector2 logScroll;
        private EnemyCombatant[] enemies = new EnemyCombatant[0];
        private EnemyCombatant enemy;
        private double nextEnemyRefresh;
        private double nextRepaint;
        private double loopAt = -1d;
        private LastPlayed lastPlayed;
        private bool wasBusy;
        private string message = string.Empty;
        private UnityEditor.Editor sequenceEditor;
        private GUIStyle logStyle;
        private SerializedObject spawnOffsetObject;
        private bool spawnOffsetNeedsSave;

        [MenuItem("Tools/Rythm RPG/Combat/Combat Preview", false, 20)]
        public static void Open()
        {
            CombatPreviewWindow window = GetWindow<CombatPreviewWindow>();
            window.titleContent = new GUIContent("Combat Preview");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        // The settings are serialized fields, so they survive Play mode, recompiles and editor restarts (window layout).
        private void OnEnable()
        {
            notes ??= new NotePreviewSettings();
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            CombatPreviewDriver driver = ExistingDriver();
            if (driver != null) driver.ShowSpawnPointsFor = null;
            SaveSpawnOffset();
            if (sequenceEditor != null) DestroyImmediate(sequenceEditor);
        }

        private void HandlePlayModeChanged(PlayModeStateChange change)
        {
            enemies = new EnemyCombatant[0];
            enemy = null;
            loopAt = -1d;
            lastPlayed = LastPlayed.None;
            wasBusy = false;
            message = string.Empty;
            Repaint();
        }

        // ---------- update: repaint, loop ----------

        private void Update()
        {
            // Spawn offset edits are written to the prefab once the mouse is let go, not on every drag frame.
            if (spawnOffsetNeedsSave && GUIUtility.hotControl == 0) SaveSpawnOffset();
            if (!EditorApplication.isPlaying) return;
            CombatPreviewDriver driver = ExistingDriver();
            bool busy = driver != null && driver.IsBusy;
            if (wasBusy && !busy && loop && lastPlayed != LastPlayed.None) loopAt = EditorApplication.timeSinceStartup + loopGap;
            wasBusy = busy;
            if (loopAt > 0d && EditorApplication.timeSinceStartup >= loopAt)
            {
                loopAt = -1d;
                if (loop && driver != null && driver.IsReady && !driver.IsBusy)
                {
                    if (lastPlayed == LastPlayed.Attack) PlayAttack(driver);
                    else if (lastPlayed == LastPlayed.Notes) PlayNotes(driver);
                }
            }

            bool staging = driver != null && driver.IsActive && !driver.IsReady;
            if ((busy || staging) && EditorApplication.timeSinceStartup >= nextRepaint)
            {
                nextRepaint = EditorApplication.timeSinceStartup + 0.1d;
                Repaint();
            }
        }

        private static CombatPreviewDriver ExistingDriver()
        {
            if (!EditorApplication.isPlaying) return null;
            CombatController combat = FindAnyObjectByType<CombatController>();
            return combat != null ? combat.GetComponent<CombatPreviewDriver>() : null;
        }

        // ---------- GUI ----------

        private void OnGUI()
        {
            logStyle ??= new GUIStyle(EditorStyles.miniLabel) { richText = false, wordWrap = true };
            scroll = EditorGUILayout.BeginScrollView(scroll);
            CombatPreviewDriver driver = ExistingDriver();
            DrawBattle(driver);
            EditorGUILayout.Space(6f);
            DrawAttack(driver);
            EditorGUILayout.Space(6f);
            DrawNotes(driver);
            EditorGUILayout.Space(6f);
            DrawLoopAndLog(driver);
            EditorGUILayout.EndScrollView();
        }

        private void DrawBattle(CombatPreviewDriver driver)
        {
            EditorGUILayout.LabelField("Preview Battle", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode in a combat scene (one with a Combat Controller, the player and an enemy), " +
                                        "then start a preview battle. You can set everything below up now.", MessageType.Info);
                if (GUILayout.Button("Enter Play Mode", GUILayout.Height(24f))) EditorApplication.isPlaying = true;
                return;
            }

            CombatController combat = FindAnyObjectByType<CombatController>();
            if (combat == null)
            {
                EditorGUILayout.HelpBox("No Combat Controller in the open scene.", MessageType.Warning);
                return;
            }

            RefreshEnemies();
            bool previewing = combat.IsPreviewing;
            using (new EditorGUI.DisabledScope(previewing))
            {
                EditorGUILayout.BeginHorizontal();
                string[] names = enemies.Select(e => e != null ? e.name : "(missing)").ToArray();
                int index = System.Array.IndexOf(enemies, enemy);
                int picked = EditorGUILayout.Popup("Enemy", Mathf.Max(0, index), names.Length > 0 ? names : new[] { "(no enemies in the scene)" });
                if (picked >= 0 && picked < enemies.Length && enemies[picked] != enemy)
                {
                    enemy = enemies[picked];
                    enemyName = enemy.name;
                }
                if (GUILayout.Button(new GUIContent("Selected", "Use the enemy selected in the Hierarchy."), GUILayout.Width(70f)))
                {
                    EnemyCombatant selected = Selection.activeGameObject != null
                        ? Selection.activeGameObject.GetComponentInParent<EnemyCombatant>() : null;
                    if (selected != null)
                    {
                        enemy = selected;
                        enemyName = selected.name;
                    }
                    else message = "Select a GameObject with an Enemy Combatant in the Hierarchy.";
                }
                EditorGUILayout.EndHorizontal();
            }

            if (combat.IsBattleActive && !previewing)
            {
                EditorGUILayout.HelpBox("A real battle is running. Finish it (or F4 / F5) before previewing.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (!previewing)
            {
                using (new EditorGUI.DisabledScope(enemy == null))
                    if (GUILayout.Button("Start Preview Battle", GUILayout.Height(26f))) StartPreview();
            }
            else
            {
                if (GUILayout.Button("End Preview", GUILayout.Height(26f)))
                {
                    driver?.End();
                    CancelLoop();
                }
                using (new EditorGUI.DisabledScope(driver == null || !driver.IsBusy))
                    if (GUILayout.Button("Stop", GUILayout.Width(70f), GUILayout.Height(26f)))
                    {
                        driver.Stop();
                        CancelLoop();
                    }
            }
            EditorGUILayout.EndHorizontal();

            string status = !previewing ? "Not previewing."
                : driver != null && !driver.IsReady ? "Staging the encounter (player moving, camera settling)..."
                : driver != null && !string.IsNullOrEmpty(driver.Status) ? driver.Status
                : "Ready.";
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
        }

        // Stopping by hand must not count as "finished", or the loop would start it again.
        private void CancelLoop()
        {
            loopAt = -1d;
            wasBusy = false;
            lastPlayed = LastPlayed.None;
        }

        private void StartPreview()
        {
            message = string.Empty;
            CombatPreviewDriver driver = CombatPreviewDriver.Find();
            PlayerEnemyInteractor3D interactor = FindAnyObjectByType<PlayerEnemyInteractor3D>();
            PlayerCombatant player = interactor != null ? interactor.GetComponent<PlayerCombatant>() : null;
            if (player == null) player = FindAnyObjectByType<PlayerCombatant>();
            if (driver == null)
            {
                message = "No Combat Controller in the open scene.";
                return;
            }
            if (!driver.Begin(player, enemy, out string error))
            {
                message = error;
                return;
            }
            if (interactor != null) interactor.SetBattlePresentation(true);
        }

        private void RefreshEnemies()
        {
            if (EditorApplication.timeSinceStartup < nextEnemyRefresh && enemies.All(e => e != null)) return;
            nextEnemyRefresh = EditorApplication.timeSinceStartup + 1d;
            PlayerCombatant player = FindAnyObjectByType<PlayerCombatant>();
            Vector3 from = player != null ? player.transform.position : Vector3.zero;
            enemies = FindObjectsByType<EnemyCombatant>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(e => (e.transform.position - from).sqrMagnitude).ToArray();
            if (enemy == null && enemies.Length > 0)
                enemy = enemies.FirstOrDefault(e => e.name == enemyName) ?? enemies[0];
        }

        // ---------- player attack ----------

        private void DrawAttack(CombatPreviewDriver driver)
        {
            EditorGUILayout.LabelField("Player Attack", EditorStyles.boldLabel);
            attackSource = (AttackSource)GUILayout.Toolbar((int)attackSource, new[] { "From Ability", "Attack Sequence" });
            CharacterAttackSequence shown;
            if (attackSource == AttackSource.Ability)
            {
                ability = (AbilityDefinition)EditorGUILayout.ObjectField("Ability", ability, typeof(AbilityDefinition), false);
                shown = ability != null ? ability.AttackSequence : null;
                if (ability != null)
                {
                    string what = shown != null ? $"Sequence: {shown.name}"
                        : IsImpactAbility(ability) ? "No attack sequence: plays the default impact projectile."
                        : "No attack sequence and not an attack: nothing to show.";
                    EditorGUILayout.LabelField(what, EditorStyles.miniLabel);
                }
                useAbilityTiming = EditorGUILayout.Toggle(new GUIContent("Use Ability Timing",
                    "Wait the ability's VFX Profile Impact Anticipation before and Impact Settle after, like in combat."), useAbilityTiming);
            }
            else
            {
                sequence = (CharacterAttackSequence)EditorGUILayout.ObjectField("Sequence", sequence, typeof(CharacterAttackSequence), false);
                accent = EditorGUILayout.ColorField(new GUIContent("Accent", "The ability's accent colour (tints effects that use it)."), accent);
                shown = sequence;
            }
            stepOutOfHitLine = EditorGUILayout.Toggle(new GUIContent("Step Out Of Hit Line",
                "In combat the character is the hit line during the chart and takes its own form for the attack."), stepOutOfHitLine);

            EditorGUILayout.BeginHorizontal();
            bool canPlay = driver != null && driver.IsReady && !driver.IsBusy
                && (shown != null || (attackSource == AttackSource.Ability && ability != null && IsImpactAbility(ability)));
            using (new EditorGUI.DisabledScope(!canPlay))
                if (GUILayout.Button("Play Attack", GUILayout.Height(24f))) PlayAttack(driver);
            if (shown != null && GUILayout.Button("Select Asset", GUILayout.Width(90f), GUILayout.Height(24f)))
            {
                Selection.activeObject = shown;
                EditorGUIUtility.PingObject(shown);
            }
            EditorGUILayout.EndHorizontal();

            if (shown == null) return;
            showSequenceEditor = EditorGUILayout.Foldout(showSequenceEditor, $"Edit {shown.name}", true);
            if (!showSequenceEditor) return;
            UnityEditor.Editor.CreateCachedEditor(shown, null, ref sequenceEditor);
            EditorGUI.indentLevel++;
            sequenceEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }

        private static bool IsImpactAbility(AbilityDefinition definition) =>
            definition.AbilityType == AbilityType.BasicAttack || definition.AbilityType == AbilityType.SpecialAttack;

        private void PlayAttack(CombatPreviewDriver driver)
        {
            if (driver == null) return;
            bool started;
            if (attackSource == AttackSource.Ability)
            {
                if (ability == null) return;
                AbilityVFXProfile profile = ability.VFXProfile;
                Color tint = profile != null ? profile.AccentColor : Color.white;
                AbilityRuntimeInstance impact = ability.AttackSequence == null && IsImpactAbility(ability) ? new AbilityRuntimeInstance(ability) : null;
                started = driver.PlayAttack(ability.AttackSequence, tint, useAbilityTiming ? profile : null, stepOutOfHitLine, impact);
            }
            else
            {
                started = driver.PlayAttack(sequence, accent, null, stepOutOfHitLine);
            }
            if (started) lastPlayed = LastPlayed.Attack;
        }

        // ---------- enemy notes ----------

        private void DrawNotes(CombatPreviewDriver driver)
        {
            EditorGUILayout.LabelField("Notes / Projectiles", EditorStyles.boldLabel);
            notes.prefab = (GameObject)EditorGUILayout.ObjectField("Note Prefab", notes.prefab, typeof(GameObject), false);
            if (notes.prefab != null && notes.prefab.GetComponent<Note>() == null)
                EditorGUILayout.HelpBox("This prefab has no Note component (Note, Hold Note Object, Mash Note...), so combat can't spawn it.", MessageType.Warning);

            notes.kind = (PreviewNoteKind)EditorGUILayout.EnumPopup(new GUIContent("Note Type",
                "Auto reads it from the prefab's note component."), notes.kind);
            RhythmNoteType type = notes.ResolveType();
            if (notes.kind == PreviewNoteKind.Auto && notes.prefab != null)
                EditorGUILayout.LabelField(" ", $"Plays as {type}", EditorStyles.miniLabel);

            notes.origin = (PreviewNoteOrigin)EditorGUILayout.EnumPopup(new GUIContent("Spawn From",
                "Enemy: like an enemy attack. Centre Stage: like the projectiles of the player's ability chart."), notes.origin);
            DrawSpawnOffset(driver);
            notes.laneCount = EditorGUILayout.IntSlider(new GUIContent("Lanes Shown", "How many lanes the chart has (1-4)."),
                notes.laneCount, 1, RhythmChart.MaxLanes);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Spawn On", "Lanes that get notes."));
            for (int lane = 1; lane <= notes.laneCount; lane++)
            {
                bool on = (notes.laneMask & (1 << (lane - 1))) != 0;
                bool next = GUILayout.Toggle(on, lane.ToString(), EditorStyles.miniButton, GUILayout.Width(32f));
                if (next != on) notes.laneMask ^= 1 << (lane - 1);
            }
            if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(34f))) notes.laneMask = (1 << notes.laneCount) - 1;
            EditorGUILayout.EndHorizontal();

            notes.count = EditorGUILayout.IntSlider(new GUIContent("Notes Per Lane"), notes.count, 1, 16);
            if (notes.count > 1)
                notes.interval = EditorGUILayout.Slider(new GUIContent("Interval (s)", "Seconds between notes on the same lane."), notes.interval, 0.05f, 2f);
            notes.travelSeconds = EditorGUILayout.Slider(new GUIContent("Travel Time (s)", "Spawn to hit line, like the chart's Travel Time."),
                notes.travelSeconds, 0.2f, 6f);
            notes.speed = EditorGUILayout.FloatField(new GUIContent("Speed", "The note's Speed (used by holds for their length)."), notes.speed);
            if (RhythmTimingUtility.IsHoldType(type))
                notes.holdSeconds = EditorGUILayout.Slider(new GUIContent("Hold Length (s)"), notes.holdSeconds, 0.1f, 4f);
            if (type == RhythmNoteType.Mash)
                notes.mashPresses = EditorGUILayout.IntSlider(new GUIContent("Mash Presses"), notes.mashPresses, 1, 40);
            notes.initializeMovement = (NoteInitializeMovementType)EditorGUILayout.EnumPopup(new GUIContent("Initialize Movement",
                "Intro movement before the note joins its lane (vertical gameplay only)."), notes.initializeMovement);

            notes.play = (PreviewNotePlay)EditorGUILayout.EnumPopup(new GUIContent("At The Hit Line",
                "Auto Play: pressed for you. Let Them Pass: misses. Play Yourself: your lane keys (click the Game view first)."), notes.play);
            if (notes.play == PreviewNotePlay.AutoPlay)
                notes.autoPlayOffset = EditorGUILayout.Slider(new GUIContent("Press Offset (s)",
                    "0 = Perfect. Push it early (-) or late (+) to see Good / Bad hits."), notes.autoPlayOffset, -0.5f, 0.5f);

            if (notes.origin == PreviewNoteOrigin.Enemy)
            {
                notes.windUpAnimation = EditorGUILayout.TextField(new GUIContent("Enemy Wind-Up State",
                    "Optional: enemy animator state played first (e.g. its attack animation)."), notes.windUpAnimation);
                if (!string.IsNullOrWhiteSpace(notes.windUpAnimation))
                    notes.windUpSeconds = EditorGUILayout.Slider(new GUIContent("Wind-Up (s)",
                        "Seconds from the wind-up to the first note, like a step's Anticipation Duration."), notes.windUpSeconds, 0f, 3f);
            }

            EditorGUILayout.BeginHorizontal();
            bool canPlay = driver != null && driver.IsReady && !driver.IsBusy && notes.prefab != null && notes.laneMask != 0;
            using (new EditorGUI.DisabledScope(!canPlay))
                if (GUILayout.Button("Spawn Notes", GUILayout.Height(24f))) PlayNotes(driver);
            using (new EditorGUI.DisabledScope(notes.prefab == null))
                if (GUILayout.Button(new GUIContent("Open Prefab", "Edit it in Prefab Mode; the next spawn uses your changes."),
                        GUILayout.Width(90f), GUILayout.Height(24f)))
                    AssetDatabase.OpenAsset(notes.prefab);
            EditorGUILayout.EndHorizontal();
        }

        // ---------- spawn point (Note Spawn Offset on the prefab) ----------

        private void DrawSpawnOffset(CombatPreviewDriver driver)
        {
            if (driver != null) driver.ShowSpawnPointsFor = showSpawnHandle && notes.prefab != null ? notes : null;
            if (notes.prefab == null) return;
            NoteSpawnOffset spawn = notes.prefab.GetComponent<NoteSpawnOffset>();
            EditorGUI.indentLevel++;
            if (spawn == null)
            {
                spawnOffsetObject = null;
                EditorGUILayout.HelpBox("It spawns exactly at the " + (notes.origin == PreviewNoteOrigin.Enemy ? "enemy" : "centre stage") +
                                        ". Add a Note Spawn Offset to the prefab to move its spawn point (e.g. out from behind the enemy).",
                    MessageType.None);
                if (GUILayout.Button("Add Spawn Offset To Prefab")) AddSpawnOffset(notes.prefab);
            }
            else
            {
                if (spawnOffsetObject == null || spawnOffsetObject.targetObject != spawn) spawnOffsetObject = new SerializedObject(spawn);
                spawnOffsetObject.Update();
                EditorGUILayout.PropertyField(spawnOffsetObject.FindProperty("space"), new GUIContent("Offset Space",
                    "Lane: X = sideways, Y = up, Z = forward along the lane toward the hit line. World: world axes."));
                EditorGUILayout.PropertyField(spawnOffsetObject.FindProperty("offset"), new GUIContent("Spawn Offset",
                    "Added to the spawn point. Lane space: +Z brings it forward, out from behind the enemy."));
                EditorGUILayout.PropertyField(spawnOffsetObject.FindProperty("enemyAttacks"));
                EditorGUILayout.PropertyField(spawnOffsetObject.FindProperty("playerAbilityCharts"));
                if (spawnOffsetObject.ApplyModifiedProperties())
                {
                    spawnOffsetNeedsSave = true;
                    SceneView.RepaintAll();
                }
            }
            showSpawnHandle = EditorGUILayout.Toggle(new GUIContent("Show Spawn Point",
                "Scene view: an orange point with a move handle you can drag (the offset follows). " +
                "Game view: the same point as a gizmo when Gizmos are on."), showSpawnHandle);
            EditorGUI.indentLevel--;
        }

        private void AddSpawnOffset(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
            {
                message = "Pick the prefab from the Project window (an asset), not a scene object.";
                return;
            }
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<NoteSpawnOffset>() == null) root.AddComponent<NoteSpawnOffset>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            spawnOffsetObject = null;
        }

        private void SaveSpawnOffset()
        {
            spawnOffsetNeedsSave = false;
            NoteSpawnOffset spawn = notes?.prefab != null ? notes.prefab.GetComponent<NoteSpawnOffset>() : null;
            if (spawn != null) AssetDatabase.SaveAssetIfDirty(spawn);
        }

        private void OnSceneGUI(SceneView view)
        {
            if (!showSpawnHandle || notes?.prefab == null || !EditorApplication.isPlaying) return;
            CombatPreviewDriver driver = ExistingDriver();
            if (driver == null || !driver.IsReady) return;
            int lane = 1;
            while (lane < notes.laneCount && !notes.UsesLane(lane)) lane++;
            if (!driver.TryGetSpawnPoint(notes, lane, out Vector3 basePoint, out Vector3 point, out Vector3 forward)) return;

            Handles.color = new Color(1f, 1f, 1f, 0.6f);
            Handles.DrawDottedLine(basePoint, point, 4f);
            Handles.Label(point + Vector3.up * HandleUtility.GetHandleSize(point) * 0.3f, notes.prefab.name + " spawn");
            NoteSpawnOffset spawn = notes.prefab.GetComponent<NoteSpawnOffset>();
            Handles.color = new Color(1f, 0.55f, 0.1f, 1f);
            Handles.SphereHandleCap(0, point, Quaternion.identity, HandleUtility.GetHandleSize(point) * 0.12f, EventType.Repaint);
            if (spawn == null) return;

            NoteSpawnOffset.LaneFrame(forward, out _, out Vector3 up, out Vector3 laneForward);
            Quaternion frame = spawn.Space == NoteSpawnOffset.OffsetSpace.Lane ? Quaternion.LookRotation(laneForward, up) : Quaternion.identity;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(point, frame);
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(spawn, "Move Note Spawn Point");
            spawn.Offset = spawn.FromWorld(moved - basePoint, forward);
            EditorUtility.SetDirty(spawn);
            spawnOffsetNeedsSave = true;
            Repaint();
        }

        private void PlayNotes(CombatPreviewDriver driver)
        {
            if (driver != null && driver.PlayNotes(notes)) lastPlayed = LastPlayed.Notes;
        }

        // ---------- loop and log ----------

        private void DrawLoopAndLog(CombatPreviewDriver driver)
        {
            EditorGUILayout.BeginHorizontal();
            bool newLoop = EditorGUILayout.ToggleLeft(new GUIContent("Loop", "Replay the last attack or notes when they finish."), loop, GUILayout.Width(60f));
            if (newLoop != loop)
            {
                loop = newLoop;
                if (!loop) loopAt = -1d;
            }
            using (new EditorGUI.DisabledScope(!loop))
                loopGap = EditorGUILayout.Slider("Gap (s)", loopGap, 0f, 5f);
            EditorGUILayout.EndHorizontal();

            IReadOnlyList<string> lines = driver != null ? driver.Log : null;
            if (lines == null || lines.Count == 0) return;
            EditorGUILayout.LabelField("Last Run", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, EditorStyles.helpBox, GUILayout.MinHeight(80f), GUILayout.MaxHeight(200f));
            foreach (string line in lines) EditorGUILayout.LabelField(line, logStyle);
            EditorGUILayout.EndScrollView();
        }
    }
}
