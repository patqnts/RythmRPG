using System;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using RythmRPG.Rhythm;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// Note Designer: create, convert and edit note / projectile prefabs (Combat Note). The top is a frame timeline of
    /// the note's life (spawn, approach, the beat, the hold) with one row per view: drag cue markers and spans to time the
    /// animations, effects, links and enemy moves against the beat. Below: the note's kind and settings, its views, an
    /// animator builder from sprite frames, chart assignment and checks. In Play mode with a Combat Preview battle up,
    /// Spawn plays it in combat and the playhead follows the note.
    /// </summary>
    public sealed class NoteDesignerWindow : EditorWindow
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private float previewTravel = 2.5f;
        [SerializeField] private float previewHold = 1f;
        [SerializeField] private NoteRecipeOptions recipe = new();
        [SerializeField] private NotePreviewSettings spawn = new();
        [SerializeField] private GameObject previewEnemy;
        [SerializeField] private RhythmChart chart;
        [SerializeField] private bool showNote = true;
        [SerializeField] private bool showBuilder;
        [SerializeField] private bool showCharts;
        [SerializeField] private bool showSpawn;
        [SerializeField] private bool showCreate;
        [SerializeField] private bool loop;
        [SerializeField] private List<SpriteAnimatorBuilder.Phase> builderPhases = SpriteAnimatorBuilder.DefaultPhases();
        [SerializeField] private float builderFps = 12f;

        private readonly FrameTimeline timeline = new();
        private readonly Dictionary<string, NoteTimelineItem> itemsByKey = new();
        private readonly HashSet<int> openViews = new();
        private SerializedObject serialized;
        private CombatNote boundNote;
        private Vector2 scroll;
        private string message = string.Empty;
        private bool fitted;
        private bool needsSave;
        private bool wasBusy;
        private double loopAt = -1d;
        private float lastPlayhead = float.NaN;

        [MenuItem("Tools/Rythm RPG/Combat/Note Designer", false, 22)]
        public static void Open() => Open(null);

        public static void Open(GameObject target)
        {
            NoteDesignerWindow window = GetWindow<NoteDesignerWindow>();
            window.titleContent = new GUIContent("Note Designer");
            window.minSize = new Vector2(560f, 480f);
            if (target != null) window.SetPrefab(target);
            window.Show();
        }

        private void SetPrefab(GameObject value)
        {
            if (prefab == value) return;
            SaveIfNeeded();
            prefab = value;
            serialized = null;
            boundNote = null;
            fitted = false;
            openViews.Clear();
            message = string.Empty;
            CombatNote note = Note;
            if (note != null)
            {
                previewHold = NoteKinds.IsHold(note.Kind) ? Mathf.Max(0.25f, previewHold) : 0f;
                for (int i = 0; i < note.Views.Count; i++) openViews.Add(i);
            }
        }

        private CombatNote Note => prefab != null ? prefab.GetComponent<CombatNote>() : null;

        private void OnEnable()
        {
            timeline.FrameRate = 24f;
            timeline.BeginEdit = () =>
            {
                if (Note != null) Undo.RecordObject(Note, "Edit Note Timing");
            };
            timeline.MoveStart = MoveStart;
            timeline.MoveEnd = MoveEnd;
            timeline.Scrubbed = time => lastPlayhead = time;
            timeline.Selected = key =>
            {
                if (key is string k && k.StartsWith("v", StringComparison.Ordinal) && int.TryParse(k.Substring(1).Split(':')[0], out int view))
                    openViews.Add(view);
                Repaint();
            };
            timeline.EndEdit = () =>
            {
                if (Note != null) EditorUtility.SetDirty(Note);
                needsSave = true;
            };
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            SaveIfNeeded();
        }

        private void OnUndo()
        {
            serialized?.Update();
            Repaint();
        }

        private void OnPlayMode(PlayModeStateChange change)
        {
            SaveIfNeeded();
            loopAt = -1d;
            wasBusy = false;
            Repaint();
        }

        private void OnLostFocus() => SaveIfNeeded();

        private void SaveIfNeeded()
        {
            if (!needsSave || prefab == null) return;
            needsSave = false;
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        // ---------- update ----------

        private void Update()
        {
            if (needsSave && GUIUtility.hotControl == 0 && focusedWindow != this) SaveIfNeeded();
            if (!EditorApplication.isPlaying) return;
            CombatPreviewDriver driver = Driver();
            bool busy = driver != null && driver.IsPlayingNotes;
            CombatNote playing = driver != null ? driver.SpawnedNotes.OfType<CombatNote>().FirstOrDefault(n => n != null) : null;
            if (playing != null) lastPlayhead = (float)(playing.Context.Now - playing.Context.HitTime);
            if (wasBusy && !busy && loop) loopAt = EditorApplication.timeSinceStartup + 0.6d;
            wasBusy = busy;
            if (loopAt > 0d && EditorApplication.timeSinceStartup >= loopAt)
            {
                loopAt = -1d;
                if (loop) Spawn();
            }
            if (busy) Repaint();
        }

        private static CombatPreviewDriver Driver()
        {
            if (!EditorApplication.isPlaying) return null;
            CombatController combat = FindAnyObjectByType<CombatController>();
            return combat != null ? combat.GetComponent<CombatPreviewDriver>() : null;
        }

        // ---------- GUI ----------

        private void OnGUI()
        {
            DrawToolbar();
            CombatNote note = Note;
            if (prefab == null)
            {
                DrawWelcome();
                return;
            }
            if (note == null)
            {
                DrawConvert();
                return;
            }
            if (serialized == null || boundNote != note)
            {
                serialized = new SerializedObject(note);
                boundNote = note;
            }
            serialized.Update();

            DrawHeader(note);
            BuildTimeline(note);
            if (!fitted && position.width > 100f)
            {
                fitted = true;
                timeline.FitAll(position.width);
            }
            Rect rect = GUILayoutUtility.GetRect(position.width, Mathf.Max(timeline.Height, 110f), GUILayout.ExpandWidth(true));
            timeline.Draw(rect);
            DrawChecks(note);
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawNoteSettings(note);
            DrawViews(note);
            DrawBuilder(note);
            DrawSpawnSettings();
            DrawCharts(note);
            DrawCreate();
            EditorGUILayout.EndScrollView();
            if (serialized != null && serialized.ApplyModifiedProperties())
            {
                needsSave = true;
                Repaint();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            var picked = (GameObject)EditorGUILayout.ObjectField(prefab, typeof(GameObject), false, GUILayout.Width(220f));
            if (EditorGUI.EndChangeCheck()) SetPrefab(picked);
            if (GUILayout.Button("New...", EditorStyles.toolbarButton, GUILayout.Width(46f)))
            {
                showCreate = true;
                SetPrefab(null);
            }
            if (prefab != null && GUILayout.Button(new GUIContent("Select", "Select the prefab in the Project window."), EditorStyles.toolbarButton, GUILayout.Width(46f)))
            {
                UnityEditor.Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            if (prefab != null && GUILayout.Button(new GUIContent("Open Prefab", "Edit its objects in Prefab Mode."), EditorStyles.toolbarButton, GUILayout.Width(76f)))
                AssetDatabase.OpenAsset(prefab);
            GUILayout.FlexibleSpace();
            timeline.ToolbarGUI(position.width);
            GUILayout.Space(8f);
            CombatPreviewDriver driver = Driver();
            loop = GUILayout.Toggle(loop, "Loop", EditorStyles.toolbarButton, GUILayout.Width(40f));
            bool canSpawn = Note != null && driver != null && driver.IsReady && !driver.IsBusy;
            using (new EditorGUI.DisabledScope(!canSpawn))
            {
                string tip = !EditorApplication.isPlaying ? "Enter Play mode and start a preview battle in Combat Preview."
                    : driver == null || !driver.IsReady ? "Start a preview battle in Combat Preview first." : "Spawn it in the preview battle.";
                if (GUILayout.Button(new GUIContent("Spawn", tip), EditorStyles.toolbarButton, GUILayout.Width(48f))) Spawn();
            }
            using (new EditorGUI.DisabledScope(driver == null || !driver.IsBusy))
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    loop = false;
                    driver.Stop();
                }
            if (GUILayout.Button(new GUIContent("Preview...", "Open Combat Preview (start the preview battle there)."), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                CombatPreviewWindow.Open();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWelcome()
        {
            EditorGUILayout.HelpBox("Pick a note prefab above to edit it, convert an old one, or create a new note from a recipe below.", MessageType.Info);
            showCreate = true;
            DrawCreate();
        }

        private void DrawConvert()
        {
            NoteKind? kind = NoteKitAuthoring.LegacyKind(prefab);
            if (kind == null)
            {
                EditorGUILayout.HelpBox($"'{prefab.name}' is not a note (no Note component). Pick a note prefab, or create a new note below.", MessageType.Warning);
                DrawCreate();
                return;
            }
            EditorGUILayout.HelpBox($"'{prefab.name}' is an old note ({prefab.GetComponent<Note>().GetType().Name}, plays as {kind}). " +
                                    "Convert makes a Combat Note copy next to it with the same timing and its visuals as views. " +
                                    "The original stays untouched, so charts using it keep working until you switch them over.", MessageType.Info);
            if (GUILayout.Button("Convert To Combat Note (copy)", GUILayout.Height(28f))) Later(ConvertPrefab);
        }

        private void ConvertPrefab()
        {
            GameObject converted = NoteKitAuthoring.ConvertLegacy(prefab, out string error);
            if (converted != null)
            {
                SetPrefab(converted);
                message = $"Made '{converted.name}'. Check its views below, then use it in your charts (Use In Charts).";
            }
            else message = error;
        }

        private void DrawHeader(CombatNote note)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{prefab.name}  ·  {note.Kind}  ·  chart type {note.ChartNoteType}", EditorStyles.boldLabel, GUILayout.MinWidth(200f));
            GUILayout.FlexibleSpace();
            EditorGUIUtility.labelWidth = 60f;
            previewTravel = Mathf.Max(0.1f, EditorGUILayout.FloatField(new GUIContent("Travel", "Timeline only: seconds from spawn to the beat (the chart's Travel Time)."), previewTravel, GUILayout.Width(120f)));
            using (new EditorGUI.DisabledScope(!NoteKinds.IsHold(note.Kind)))
                previewHold = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Hold", "Timeline only: seconds the hold lasts."), NoteKinds.IsHold(note.Kind) ? previewHold : 0f, GUILayout.Width(120f)));
            EditorGUIUtility.labelWidth = 0f;
            EditorGUILayout.EndHorizontal();
        }

        // ---------- timeline ----------

        private float Hold(CombatNote note) => NoteKinds.IsHold(note.Kind) ? previewHold : 0f;

        private void BuildTimeline(CombatNote note)
        {
            timeline.Tracks.Clear();
            timeline.Bands.Clear();
            timeline.Lines.Clear();
            itemsByKey.Clear();
            float travel = previewTravel, hold = Hold(note);
            timeline.MinTime = -travel;
            timeline.Bands.Add(new FrameTimeline.Band { Start = -travel, End = 0f, Color = new Color(0.3f, 0.5f, 0.9f, 0.10f), Label = NoteKinds.IsStationary(note.Kind) ? "charge" : "approach" });
            if (hold > 0f) timeline.Bands.Add(new FrameTimeline.Band { Start = 0f, End = hold, Color = new Color(0.3f, 0.9f, 0.5f, 0.10f), Label = "hold" });
            float end = hold;
            timeline.Bands.Add(new FrameTimeline.Band { Start = end, End = end + note.Linger, Color = new Color(1f, 1f, 1f, 0.05f), Label = "linger" });
            timeline.Lines.Add(new FrameTimeline.Line { Time = -travel, Color = new Color(0.5f, 0.7f, 1f, 0.8f), Label = "spawn" });
            timeline.Lines.Add(new FrameTimeline.Line { Time = 0f, Color = new Color(1f, 0.45f, 0.35f, 0.95f), Label = "BEAT" });
            if (hold > 0f) timeline.Lines.Add(new FrameTimeline.Line { Time = hold, Color = new Color(0.4f, 1f, 0.55f, 0.85f), Label = "hold end" });

            for (int v = 0; v < note.Views.Count; v++)
            {
                NoteView view = note.Views[v];
                if (view == null) continue;
                var items = new List<NoteTimelineItem>();
                try { view.DescribeTimeline(items); }
                catch (Exception e) { Debug.LogException(e); }
                Color trackColor = items.Count > 0 ? items[0].Color : NoteTimelineColors.Info;
                var track = new FrameTimeline.Track
                {
                    Label = (view.Enabled ? string.Empty : "(off) ") + view.Title,
                    Color = view.Enabled ? trackColor : NoteTimelineColors.Info,
                    Key = "v" + v
                };
                for (int i = 0; i < items.Count; i++)
                {
                    NoteTimelineItem item = items[i];
                    string key = $"v{v}:{i}";
                    itemsByKey[key] = item;
                    float start = Moments.TimeOf(item.Cue, travel, hold) - item.LeadIn;
                    float stop = item.EndCue != null ? Moments.TimeOf(item.EndCue, travel, hold)
                        : item.Length > 0f ? start + item.LeadIn + item.Length : start;
                    var entry = new FrameTimeline.Item
                    {
                        Label = item.Label,
                        Tooltip = item.Cue + (item.EndCue != null ? " → " + item.EndCue : string.Empty),
                        Start = start,
                        End = Mathf.Max(start, stop),
                        Color = view.Enabled ? item.Color : NoteTimelineColors.Info,
                        Draggable = item.Editable && item.Cue != null,
                        EndResizable = item.Editable && (item.EndCue != null || item.SetLength != null),
                        Key = key
                    };
                    float clip = ClipSeconds(note, view, item);
                    if (clip > 0f) entry.GhostEnd = start + clip;
                    track.Items.Add(entry);
                }
                timeline.Tracks.Add(track);
            }
            timeline.Playhead = lastPlayhead;
        }

        private void MoveStart(FrameTimeline.Item entry, float time)
        {
            if (entry.Key is not string key || !itemsByKey.TryGetValue(key, out NoteTimelineItem item) || item.Cue == null) return;
            CombatNote note = Note;
            float travel = previewTravel, hold = Hold(note);
            float before = Moments.TimeOf(item.Cue, travel, hold);
            Moments.SetTime(item.Cue, time + item.LeadIn, travel, hold);
            // A span between two cues moves as a whole.
            if (item.EndCue != null)
            {
                float delta = Moments.TimeOf(item.Cue, travel, hold) - before;
                Moments.SetTime(item.EndCue, Moments.TimeOf(item.EndCue, travel, hold) + delta, travel, hold);
            }
            AfterEdit();
        }

        private void MoveEnd(FrameTimeline.Item entry, float time)
        {
            if (entry.Key is not string key || !itemsByKey.TryGetValue(key, out NoteTimelineItem item)) return;
            CombatNote note = Note;
            float travel = previewTravel, hold = Hold(note);
            if (item.EndCue != null) Moments.SetTime(item.EndCue, time, travel, hold);
            else item.SetLength?.Invoke(Mathf.Max(0f, time - (Moments.TimeOf(item.Cue, travel, hold) - item.LeadIn) - item.LeadIn));
            AfterEdit();
        }

        private void AfterEdit()
        {
            if (Note != null) EditorUtility.SetDirty(Note);
            serialized?.Update();
            needsSave = true;
            Repaint();
        }

        /// <summary>Length of the animator state an item plays (note animator, or the preview enemy's).</summary>
        private float ClipSeconds(CombatNote note, NoteView view, NoteTimelineItem item)
        {
            if (string.IsNullOrWhiteSpace(item.AnimatorState)) return 0f;
            RuntimeAnimatorController controller = null;
            if (item.AnimatorOwner == AnimatorView.Who.Note)
            {
                Animator animator = (view as AnimatorView)?.NoteAnimator;
                if (animator == null) animator = note.GetComponentInChildren<Animator>(true);
                controller = animator != null ? animator.runtimeAnimatorController : null;
            }
            else
            {
                GameObject enemy = PreviewEnemy();
                Animator animator = enemy != null ? enemy.GetComponentInChildren<Animator>(true) : null;
                controller = animator != null ? animator.runtimeAnimatorController : null;
            }
            return AnimatorClipLengths.StateSeconds(controller, item.AnimatorState);
        }

        private GameObject PreviewEnemy()
        {
            if (previewEnemy != null) return previewEnemy;
            if (!EditorApplication.isPlaying) return null;
            CombatController combat = FindAnyObjectByType<CombatController>();
            return combat != null && combat.IsBattleActive && combat.Encounter.Enemy != null ? combat.Encounter.Enemy.gameObject : null;
        }

        // ---------- checks ----------

        private void DrawChecks(CombatNote note)
        {
            var problems = new List<string>();
            foreach (NoteView view in note.Views)
            {
                if (view == null)
                {
                    problems.Add("A view slot is empty (its script was renamed or removed). Remove it.");
                    continue;
                }
                if (!view.Enabled) continue;
                try { problems.AddRange(view.Validate(note)); }
                catch (Exception e) { problems.Add(view.Title + ": " + e.Message); }
            }
            if (note.Views.Count == 0) problems.Add("No views: the note is invisible. Add a view (Animator, Effect, Link...).");
            GameObject enemy = PreviewEnemy();
            if (enemy != null)
                foreach (string socket in Sockets(note).Distinct())
                    if (CombatSocket.Find(enemy.transform, socket) == null)
                        problems.Add($"'{enemy.name}' has no socket '{socket}' (add a Combat Socket, or a child named {socket}); the enemy's own position is used.");
            foreach (string problem in problems.Distinct()) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        private static IEnumerable<string> Sockets(CombatNote note)
        {
            foreach (NoteView view in note.Views)
            {
                switch (view)
                {
                    case EffectView effect when effect.Enabled:
                        if (effect.At is NoteAnchor.Source or NoteAnchor.Target && !string.IsNullOrWhiteSpace(effect.Socket)) yield return effect.Socket;
                        if (effect.AimAt is NoteAnchor.Source or NoteAnchor.Target && !string.IsNullOrWhiteSpace(effect.AimSocket)) yield return effect.AimSocket;
                        break;
                    case LinkView link when link.Enabled:
                        if (link.From is NoteAnchor.Source or NoteAnchor.Target && !string.IsNullOrWhiteSpace(link.FromSocket)) yield return link.FromSocket;
                        break;
                    case TravelView travel when travel.Enabled:
                        if (travel.From is NoteAnchor.Source or NoteAnchor.Target && !string.IsNullOrWhiteSpace(travel.FromSocket)) yield return travel.FromSocket;
                        if (travel.To is NoteAnchor.Source or NoteAnchor.Target && !string.IsNullOrWhiteSpace(travel.ToSocket)) yield return travel.ToSocket;
                        break;
                }
            }
        }

        // ---------- note and views ----------

        private void DrawNoteSettings(CombatNote note)
        {
            showNote = EditorGUILayout.BeginFoldoutHeaderGroup(showNote, "Note");
            if (showNote)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serialized.FindProperty("kind"));
                switch (note.Kind)
                {
                    case NoteKind.Stationary:
                        EditorGUILayout.PropertyField(serialized.FindProperty("stationary"), true);
                        break;
                    case NoteKind.StationaryHold:
                        EditorGUILayout.PropertyField(serialized.FindProperty("stationary"), true);
                        EditorGUILayout.PropertyField(serialized.FindProperty("hold"), true);
                        break;
                    case NoteKind.Hold:
                        EditorGUILayout.PropertyField(serialized.FindProperty("hold"), true);
                        break;
                    case NoteKind.Mash:
                        EditorGUILayout.PropertyField(serialized.FindProperty("mash"), true);
                        break;
                }
                EditorGUILayout.PropertyField(serialized.FindProperty("lingerSeconds"));
                NoteSpawnOffset offset = prefab.GetComponent<NoteSpawnOffset>();
                EditorGUILayout.LabelField("Spawn point", offset != null ? $"offset {offset.Offset} ({offset.Space})" : "at the enemy / centre stage (add a Note Spawn Offset in Combat Preview to move it)", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawViews(CombatNote note)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Views", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add View", GUILayout.Width(90f))) AddViewMenu(note);
            EditorGUILayout.EndHorizontal();

            SerializedProperty views = serialized.FindProperty("views");
            for (int i = 0; i < views.arraySize; i++)
            {
                SerializedProperty element = views.GetArrayElementAtIndex(i);
                NoteView view = i < note.Views.Count ? note.Views[i] : null;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                bool open = openViews.Contains(i);
                bool nowOpen = EditorGUILayout.Foldout(open, view != null ? view.Title : "(missing)", true);
                if (nowOpen != open)
                {
                    if (nowOpen) openViews.Add(i);
                    else openViews.Remove(i);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(24f)) && i > 0) views.MoveArrayElement(i, i - 1);
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(24f)) && i < views.arraySize - 1) views.MoveArrayElement(i, i + 1);
                if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(24f)))
                {
                    views.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                if (nowOpen && element.managedReferenceValue != null)
                {
                    EditorGUI.indentLevel++;
                    SerializedProperty iterator = element.Copy();
                    SerializedProperty end = element.GetEndProperty();
                    bool enter = true;
                    while (iterator.NextVisible(enter) && !SerializedProperty.EqualContents(iterator, end))
                    {
                        enter = false;
                        EditorGUILayout.PropertyField(iterator, true);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void AddViewMenu(CombatNote note)
        {
            var menu = new GenericMenu();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<NoteView>().Where(t => !t.IsAbstract && !t.IsGenericType).OrderBy(t => t.Name))
            {
                Type viewType = type;
                string name = ObjectNames.NicifyVariableName(type.Name.EndsWith("View") ? type.Name.Substring(0, type.Name.Length - 4) : type.Name);
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    Undo.RecordObject(note, "Add Note View");
                    note.Views.Add((NoteView)Activator.CreateInstance(viewType));
                    openViews.Add(note.Views.Count - 1);
                    AfterEdit();
                });
            }
            menu.ShowAsContext();
        }

        // ---------- animator from sprite frames ----------

        private void DrawBuilder(CombatNote note)
        {
            EditorGUILayout.Space(4f);
            showBuilder = EditorGUILayout.BeginFoldoutHeaderGroup(showBuilder, "Build Animator From Sprite Frames");
            if (showBuilder)
            {
                EditorGUILayout.HelpBox("Drop sprite frames on each phase. Build makes one clip per phase and an Animator Controller next to the prefab, " +
                                        "puts it on the note's sprite (a 'Visual' child is added if the note has none) and maps the phases in an Animator View.", MessageType.None);
                builderFps = EditorGUILayout.Slider("Frames Per Second", builderFps, 1f, 60f);
                foreach (SpriteAnimatorBuilder.Phase phase in builderPhases) DrawPhase(phase);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Phase", GUILayout.Width(90f))) builderPhases.Add(new SpriteAnimatorBuilder.Phase("State", false));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!builderPhases.Any(p => p.HasFrames)))
                    if (GUILayout.Button("Build Animator", GUILayout.Width(130f), GUILayout.Height(22f))) Later(() => BuildAnimator(note));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPhase(SpriteAnimatorBuilder.Phase phase)
        {
            EditorGUILayout.BeginHorizontal();
            phase.state = EditorGUILayout.TextField(phase.state, GUILayout.Width(110f));
            phase.loop = GUILayout.Toggle(phase.loop, "Loop", GUILayout.Width(48f));
            Rect drop = GUILayoutUtility.GetRect(100f, 22f, GUILayout.ExpandWidth(true));
            int count = phase.frames.Count(f => f != null);
            GUI.Box(drop, count > 0 ? $"{count} frame(s): {string.Join(", ", phase.frames.Where(f => f != null).Take(4).Select(f => f.name))}{(count > 4 ? "..." : "")}" : "Drop sprites here", EditorStyles.helpBox);
            HandleSpriteDrop(drop, phase);
            if (GUILayout.Button("Clear", GUILayout.Width(46f))) phase.frames.Clear();
            EditorGUILayout.EndHorizontal();
        }

        private static void HandleSpriteDrop(Rect area, SpriteAnimatorBuilder.Phase phase)
        {
            Event e = Event.current;
            if (!area.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var sprites = new List<Sprite>();
                foreach (Object dropped in DragAndDrop.objectReferences)
                {
                    switch (dropped)
                    {
                        case Sprite sprite:
                            sprites.Add(sprite);
                            break;
                        case Texture2D texture:
                            sprites.AddRange(AssetDatabase.LoadAllAssetRepresentationsAtPath(AssetDatabase.GetAssetPath(texture)).OfType<Sprite>());
                            break;
                    }
                }
                phase.frames.AddRange(sprites.OrderBy(s => s.name, new NaturalComparer()));
            }
            e.Use();
        }

        private void BuildAnimator(CombatNote note)
        {
            SaveIfNeeded(); // timeline edits first, so the prefab load below sees them
            string path = AssetDatabase.GetAssetPath(prefab);
            string folder = SpriteAnimatorBuilder.FolderOf(prefab) + $"/{prefab.name} Animations";
            var controller = SpriteAnimatorBuilder.Build(folder, prefab.name, builderPhases, builderFps, string.Empty);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CombatNote contents = root.GetComponent<CombatNote>();
                Animator animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    SpriteRenderer existing = root.GetComponentInChildren<SpriteRenderer>(true);
                    GameObject host = existing != null ? existing.gameObject : new GameObject("Visual");
                    if (existing == null)
                    {
                        host.transform.SetParent(root.transform, false);
                        host.AddComponent<SpriteRenderer>();
                    }
                    animator = host.AddComponent<Animator>();
                }
                if (animator.GetComponent<SpriteRenderer>() == null) animator.gameObject.AddComponent<SpriteRenderer>();
                animator.runtimeAnimatorController = controller;
                Sprite first = builderPhases.SelectMany(p => p.frames).FirstOrDefault(f => f != null);
                SpriteRenderer renderer = animator.GetComponent<SpriteRenderer>();
                if (renderer.sprite == null) renderer.sprite = first;

                AnimatorView view = contents.Views.OfType<AnimatorView>().FirstOrDefault(v => v.Animate == AnimatorView.Who.Note);
                if (view == null)
                {
                    view = new AnimatorView { Animate = AnimatorView.Who.Note };
                    view.States.Clear();
                    contents.Views.Insert(0, view);
                }
                view.NoteAnimator = animator;
                foreach (SpriteAnimatorBuilder.Phase phase in builderPhases.Where(p => p.HasFrames))
                {
                    NoteMoment moment = phase.state switch
                    {
                        "Approach" => NoteMoment.Spawned,
                        "Hold" => NoteMoment.HoldStarted,
                        "Hit" => NoteMoment.Hit,
                        "Miss" => NoteMoment.Missed,
                        _ => NoteMoment.None
                    };
                    if (moment == NoteMoment.None || view.States.Any(s => s != null && s.state == phase.state)) continue;
                    view.States.Add(new MomentState(moment, phase.state));
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                message = $"Built '{controller.name}' ({builderPhases.Count(p => p.HasFrames)} clips) and put it on '{animator.name}'.";
            }
            catch (Exception e)
            {
                message = "Build failed: " + e.Message;
                Debug.LogException(e);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            serialized = null;
        }

        // ---------- spawn in Combat Preview ----------

        private void DrawSpawnSettings()
        {
            EditorGUILayout.Space(4f);
            showSpawn = EditorGUILayout.BeginFoldoutHeaderGroup(showSpawn, "Spawn In Combat Preview");
            if (showSpawn)
            {
                EditorGUI.indentLevel++;
                spawn.origin = (PreviewNoteOrigin)EditorGUILayout.EnumPopup("Spawn From", spawn.origin);
                spawn.laneCount = EditorGUILayout.IntSlider("Lanes Shown", spawn.laneCount, 1, RhythmChart.MaxLanes);
                spawn.laneMask = EditorGUILayout.MaskField("Spawn On", spawn.laneMask, Enumerable.Range(1, spawn.laneCount).Select(i => "Lane " + i).ToArray());
                spawn.count = EditorGUILayout.IntSlider("Notes Per Lane", spawn.count, 1, 16);
                if (spawn.count > 1) spawn.interval = EditorGUILayout.Slider("Interval (s)", spawn.interval, 0.05f, 2f);
                spawn.play = (PreviewNotePlay)EditorGUILayout.EnumPopup("At The Hit Line", spawn.play);
                if (spawn.play == PreviewNotePlay.AutoPlay) spawn.autoPlayOffset = EditorGUILayout.Slider("Press Offset (s)", spawn.autoPlayOffset, -0.5f, 0.5f);
                previewEnemy = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Preview Enemy", "For sockets and enemy animation lengths when no preview battle runs."), previewEnemy, typeof(GameObject), true);
                EditorGUILayout.LabelField("Travel and hold come from the timeline settings above.", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void Spawn()
        {
            CombatPreviewDriver driver = Driver();
            CombatNote note = Note;
            if (driver == null || note == null) return;
            SaveIfNeeded();
            spawn.prefab = prefab;
            spawn.kind = PreviewNoteKind.Auto;
            spawn.travelSeconds = previewTravel;
            spawn.holdSeconds = Mathf.Max(0.1f, previewHold);
            if (spawn.laneMask == 0) spawn.laneMask = 1 << 1;
            lastPlayhead = float.NaN;
            if (!driver.PlayNotes(spawn)) message = string.IsNullOrEmpty(driver.Status) ? "Combat Preview is not ready." : driver.Status;
        }

        // ---------- charts ----------

        private void DrawCharts(CombatNote note)
        {
            EditorGUILayout.Space(4f);
            showCharts = EditorGUILayout.BeginFoldoutHeaderGroup(showCharts, "Use In Charts");
            if (showCharts)
            {
                EditorGUILayout.HelpBox($"A chart spawns this prefab for its '{note.ChartNoteType}' notes once it is that type's prefab in the chart's Note Definitions. " +
                                        "Single notes can also use it through their Prefab Override.", MessageType.None);
                EditorGUILayout.BeginHorizontal();
                chart = (RhythmChart)EditorGUILayout.ObjectField("Chart", chart, typeof(RhythmChart), false);
                using (new EditorGUI.DisabledScope(chart == null))
                    if (GUILayout.Button("Assign", GUILayout.Width(70f)))
                    {
                        NoteKitAuthoring.AssignToChart(chart, prefab, note.Kind);
                        AssetDatabase.SaveAssetIfDirty(chart);
                        message = $"'{chart.name}' now spawns '{prefab.name}' for its {note.ChartNoteType} notes.";
                    }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // ---------- create ----------

        private void DrawCreate()
        {
            EditorGUILayout.Space(4f);
            showCreate = EditorGUILayout.BeginFoldoutHeaderGroup(showCreate, "Create A New Note");
            if (showCreate)
            {
                EditorGUI.indentLevel++;
                recipe.name = EditorGUILayout.TextField("Name", recipe.name);
                EditorGUILayout.BeginHorizontal();
                recipe.folder = EditorGUILayout.TextField("Folder", recipe.folder);
                if (GUILayout.Button("...", GUILayout.Width(26f)))
                {
                    string chosen = EditorUtility.OpenFolderPanel("Note folder", recipe.folder, string.Empty);
                    if (!string.IsNullOrEmpty(chosen) && chosen.Contains("/Assets"))
                        recipe.folder = "Assets" + chosen.Substring(chosen.IndexOf("/Assets", StringComparison.Ordinal) + 7);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                recipe.recipe = (NoteRecipe)EditorGUILayout.EnumPopup("Recipe", recipe.recipe);
                if (EditorGUI.EndChangeCheck()) recipe.kind = NoteKitAuthoring.DefaultKind(recipe.recipe);
                EditorGUILayout.LabelField(NoteKitAuthoring.Describe(recipe.recipe), EditorStyles.wordWrappedMiniLabel);
                recipe.kind = (NoteKind)EditorGUILayout.EnumPopup(new GUIContent("Kind", "How it is played: Tap, Hold, Stationary, Stationary Hold, Mash, Pong."), recipe.kind);

                switch (recipe.recipe)
                {
                    case NoteRecipe.SpriteAnimator:
                        recipe.controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField("Own Controller", recipe.controller, typeof(RuntimeAnimatorController), false);
                        if (recipe.controller == null)
                        {
                            recipe.framesPerSecond = EditorGUILayout.Slider("Frames Per Second", recipe.framesPerSecond, 1f, 60f);
                            foreach (SpriteAnimatorBuilder.Phase phase in recipe.phases) DrawPhase(phase);
                        }
                        break;
                    case NoteRecipe.Effect:
                        recipe.effectPrefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", recipe.effectPrefab, typeof(GameObject), false);
                        if (NoteKinds.IsStationary(recipe.kind))
                        {
                            recipe.socket = EditorGUILayout.TextField(new GUIContent("Enemy Socket", "Where on the enemy it starts (Combat Socket id or child name). Empty = the enemy."), recipe.socket);
                            recipe.aimAtHitPoint = EditorGUILayout.Toggle("Aim At The Lane", recipe.aimAtHitPoint);
                        }
                        break;
                    case NoteRecipe.Chain:
                        recipe.socket = EditorGUILayout.TextField(new GUIContent("Enemy Socket", "Where the chain comes out (e.g. Mouth)."), recipe.socket);
                        recipe.chainMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Line Material", "A tiling material for chains / ropes. Empty = plain line."), recipe.chainMaterial, typeof(Material), false);
                        recipe.chainSegment = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Or Segment Prefab", "Chain links placed along the way instead of a line."), recipe.chainSegment, typeof(GameObject), false);
                        recipe.sprite = (Sprite)EditorGUILayout.ObjectField(new GUIContent("Head Sprite", "Optional sprite at the end (the note itself)."), recipe.sprite, typeof(Sprite), false);
                        break;
                    case NoteRecipe.EnemyIsTheNote:
                    case NoteRecipe.EnemyAndEffect:
                        recipe.windUpState = EditorGUILayout.TextField(new GUIContent("Wind-Up State", "Enemy animator state played before the beat."), recipe.windUpState);
                        recipe.windUpSeconds = EditorGUILayout.Slider("Wind-Up Before Beat (s)", recipe.windUpSeconds, 0f, 1.5f);
                        recipe.strikeState = EditorGUILayout.TextField(new GUIContent("Strike State", "Optional state played on the beat."), recipe.strikeState);
                        recipe.idleState = EditorGUILayout.TextField(new GUIContent("Back To State", "Played after the note (e.g. Idle)."), recipe.idleState);
                        recipe.enemyStepsIn = EditorGUILayout.Toggle(new GUIContent("Enemy Steps In", "The enemy walks up to the lane and back."), recipe.enemyStepsIn);
                        if (recipe.recipe == NoteRecipe.EnemyAndEffect)
                        {
                            recipe.effectPrefab = (GameObject)EditorGUILayout.ObjectField("Effect Prefab", recipe.effectPrefab, typeof(GameObject), false);
                            recipe.socket = EditorGUILayout.TextField("Enemy Socket", recipe.socket);
                            recipe.aimAtHitPoint = EditorGUILayout.Toggle("Aim At The Lane", recipe.aimAtHitPoint);
                        }
                        break;
                    case NoteRecipe.ThrowAndWarp:
                        recipe.weapon = (Sprite)EditorGUILayout.ObjectField(new GUIContent("Weapon Sprite", "Art pointing right. Empty = a generated pixel sword."), recipe.weapon, typeof(Sprite), false);
                        recipe.handSocket = EditorGUILayout.TextField(new GUIContent("Hand Socket", "Combat Socket / child the weapon leaves from. Empty = in front of the chest."), recipe.handSocket);
                        recipe.throwState = EditorGUILayout.TextField(new GUIContent("Throw State", "Attacker's animator state, 0.9 s before the beat."), recipe.throwState);
                        recipe.slashState = EditorGUILayout.TextField(new GUIContent("Slash State", "Played when it warps in, 0.1 s before the beat."), recipe.slashState);
                        recipe.jumpState = EditorGUILayout.TextField(new GUIContent("Jump State", "Played when it jumps back."), recipe.jumpState);
                        recipe.idleState = EditorGUILayout.TextField(new GUIContent("Back To State", "Played at the end (e.g. Idle)."), recipe.idleState);
                        recipe.vanishStyle = (VanishStyle)EditorGUILayout.EnumPopup(new GUIContent("Vanish", "Fade, a dissolve shader's float, or hide (pair with an animation)."), recipe.vanishStyle);
                        recipe.flakeColor = EditorGUILayout.ColorField("Flake Colour", recipe.flakeColor);
                        EditorGUILayout.LabelField("States the attacker's animator lacks are skipped. Time everything on the timeline after.", EditorStyles.wordWrappedMiniLabel);
                        break;
                }
                EditorGUILayout.Space(2f);
                if (GUILayout.Button("Create Note Prefab", GUILayout.Height(26f))) Later(CreateFromRecipe);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void CreateFromRecipe()
        {
            GameObject created = NoteKitAuthoring.Create(recipe, out string error);
            if (created != null)
            {
                showCreate = false;
                SetPrefab(created);
                message = $"Created '{AssetDatabase.GetAssetPath(created)}'. Time its views on the timeline, then Spawn it in Combat Preview.";
            }
            else message = error;
        }

        /// <summary>Makes the sample warp-strike note (Throw And Warp recipe) and opens it here.</summary>
        [MenuItem("Tools/Rythm RPG/Combat/Samples/Create Warp Strike Note", false, 40)]
        public static void CreateWarpStrikeSample()
        {
            var options = new NoteRecipeOptions
            {
                name = "Warp Strike",
                folder = "Assets/Prefab/Notes/Samples",
                recipe = NoteRecipe.ThrowAndWarp,
                kind = NoteKind.Stationary
            };
            GameObject created = NoteKitAuthoring.Create(options, out string error);
            if (created == null)
            {
                Debug.LogError("Warp Strike sample: " + error);
                return;
            }
            Open(created);
            UnityEditor.Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
        }

        /// <summary>Runs asset work (copy, import, save) after this GUI pass instead of in the middle of its layout.</summary>
        private void Later(Action work)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                work();
                Repaint();
            };
        }

        /// <summary>Sorts "frame_2" before "frame_10".</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            public int Compare(string a, string b) => EditorUtility.NaturalCompare(a ?? string.Empty, b ?? string.Empty);
        }
    }
}
