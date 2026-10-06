using System;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// Attack Sequence Editor: a Character Attack Sequence on a frame timeline. Each step is a bar placed exactly as the
    /// performer runs it (walk in, delays, steps that wait or run in the background, walk back). Drag a bar to change its
    /// delay, drag its end to change its length, drag the red diamonds to move hits. Select a step to edit it below.
    /// In Play mode with a Combat Preview battle up, Play runs it on the player (slow motion available) and the playhead
    /// follows.
    /// </summary>
    public sealed class AttackSequenceEditorWindow : EditorWindow
    {
        private sealed class Selection
        {
            public int StepIndex = -1;
        }

        [SerializeField] private CharacterAttackSequence sequence;
        [SerializeField] private AbilityDefinition ability;
        [SerializeField] private float playbackSpeed = 1f;
        [SerializeField] private bool loop;
        [SerializeField] private bool showSettings;
        [SerializeField] private float timelineHeight = 260f;

        private readonly FrameTimeline timeline = new();
        private readonly Selection selected = new();
        private AttackTimelineLayout layout;
        private AttackTimingContext timing;
        private SerializedObject serialized;
        private Vector2 inspectorScroll;
        private bool fittedOnce;
        private bool playing;
        private float restoreTimeScale = 1f;
        private bool timeScaleChanged;
        private double loopAt = -1d;
        private string message = string.Empty;
        private float scrubTime = float.NaN;

        private static readonly float[] Speeds = { 1f, 0.5f, 0.25f, 0.1f };
        private static readonly string[] SpeedNames = { "1x", "0.5x", "0.25x", "0.1x" };

        [MenuItem("Tools/Rythm RPG/Combat/Attack Sequence Editor", false, 21)]
        public static void Open() => Open(null);

        public static void Open(CharacterAttackSequence target)
        {
            AttackSequenceEditorWindow window = GetWindow<AttackSequenceEditorWindow>();
            window.titleContent = new GUIContent("Attack Sequence");
            window.minSize = new Vector2(520f, 420f);
            if (target != null) window.SetSequence(target);
            window.Show();
        }

        // Double-clicking a sequence asset opens it here. Unity 6.3+ identifies assets by EntityId (instance ids are obsolete).
#if UNITY_6000_3_OR_NEWER
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OpenFromProject(EntityId entityId, int line) => OpenIfSequence(EditorUtility.EntityIdToObject(entityId));
#else
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OpenFromProject(int instanceId, int line) => OpenIfSequence(EditorUtility.InstanceIDToObject(instanceId));
#endif

        private static bool OpenIfSequence(UnityEngine.Object opened)
        {
            if (opened is not CharacterAttackSequence asset) return false;
            Open(asset);
            return true;
        }

        private void SetSequence(CharacterAttackSequence value)
        {
            if (sequence == value) return;
            sequence = value;
            serialized = null;
            selected.StepIndex = -1;
            fittedOnce = false;
        }

        private void OnEnable()
        {
            timeline.FrameRate = 24f;
            timeline.MinTime = 0f;
            timeline.BeginEdit = () =>
            {
                if (sequence != null) Undo.RecordObject(sequence, "Edit Attack Timing");
            };
            timeline.MoveStart = (item, time) =>
            {
                if (item.Key is AttackTimelineEntry entry) AttackTimelineLayout.MoveStart(entry, time);
                Changed();
            };
            timeline.MoveEnd = (item, time) =>
            {
                if (item.Key is AttackTimelineEntry entry) AttackTimelineLayout.MoveEnd(entry, time, timing);
                Changed();
            };
            timeline.MovePoint = (item, point, time) =>
            {
                if (item.Key is AttackTimelineEntry entry && point.Key is AttackHitMark hit && hit.Move != null)
                    hit.Move(Mathf.Max(0f, time - entry.Start));
                Changed();
            };
            timeline.Selected = key =>
            {
                selected.StepIndex = key is AttackTimelineEntry entry ? entry.Index : -1;
                GUI.FocusControl(null);
                Repaint();
            };
            timeline.Scrubbed = time => scrubTime = time;
            timeline.EndEdit = () =>
            {
                if (sequence != null) EditorUtility.SetDirty(sequence);
            };
            EditorApplication.playModeStateChanged += HandlePlayMode;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayMode;
            Undo.undoRedoPerformed -= Repaint;
            StopPlayback();
        }

        private void HandlePlayMode(PlayModeStateChange change)
        {
            playing = false;
            timeScaleChanged = false; // Play mode resets the time scale itself
            loopAt = -1d;
            Repaint();
        }

        private void Changed()
        {
            if (sequence != null) EditorUtility.SetDirty(sequence);
            serialized?.Update();
            Repaint();
        }

        // ---------- update: playhead, loop ----------

        private void Update()
        {
            if (!EditorApplication.isPlaying) return;
            CharacterAttackPerformer performer = CharacterAttackPerformer.Latest;
            bool running = performer != null && performer.Playing != null && performer.Playing == sequence;
            CombatPreviewDriver driver = Driver();
            bool busy = driver != null && driver.IsPlayingAttack;
            if (playing && !busy)
            {
                playing = false;
                StopPlayback();
                if (loop) loopAt = EditorApplication.timeSinceStartup + 0.4d;
            }
            if (loopAt > 0d && EditorApplication.timeSinceStartup >= loopAt)
            {
                loopAt = -1d;
                if (loop) Play();
            }
            if (running || busy) Repaint();
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
            if (sequence == null)
            {
                EditorGUILayout.HelpBox("Pick a Character Attack Sequence (or an ability that has one). Double-clicking a sequence asset opens it here too.", MessageType.Info);
                if (GUILayout.Button("Create New Sequence...", GUILayout.Height(24f))) CreateSequence();
                return;
            }

            serialized ??= new SerializedObject(sequence);
            serialized.Update();
            timing = TimingFor(sequence);
            layout = AttackTimelineLayout.Build(sequence, timing);
            BuildTracks();
            if (!fittedOnce && position.width > 100f)
            {
                fittedOnce = true;
                timeline.FitAll(position.width);
            }

            Rect rect = GUILayoutUtility.GetRect(position.width, Mathf.Max(timeline.Height, 120f), GUILayout.ExpandWidth(true));
            timeline.Draw(rect);
            DrawSummary();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.Space(4f);

            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            DrawStepButtons();
            DrawSelectedStep();
            DrawSettings();
            EditorGUILayout.EndScrollView();
            if (serialized.ApplyModifiedProperties()) Repaint();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            var picked = (CharacterAttackSequence)EditorGUILayout.ObjectField(sequence, typeof(CharacterAttackSequence), false,
                GUILayout.Width(220f));
            if (EditorGUI.EndChangeCheck()) SetSequence(picked);
            EditorGUI.BeginChangeCheck();
            ability = (AbilityDefinition)EditorGUILayout.ObjectField(ability, typeof(AbilityDefinition), false, GUILayout.Width(160f));
            if (EditorGUI.EndChangeCheck() && ability != null && ability.AttackSequence != null) SetSequence(ability.AttackSequence);
            GUILayout.FlexibleSpace();
            timeline.ToolbarGUI(position.width);
            GUILayout.Space(8f);

            CombatPreviewDriver driver = Driver();
            bool canPlay = sequence != null && driver != null && driver.IsReady && !driver.IsBusy;
            int speed = Array.IndexOf(Speeds, playbackSpeed);
            int newSpeed = EditorGUILayout.Popup(Mathf.Max(0, speed), SpeedNames, EditorStyles.toolbarPopup, GUILayout.Width(52f));
            if (newSpeed != speed) playbackSpeed = Speeds[Mathf.Max(0, newSpeed)];
            loop = GUILayout.Toggle(loop, "Loop", EditorStyles.toolbarButton, GUILayout.Width(40f));
            using (new EditorGUI.DisabledScope(!canPlay))
            {
                string tip = !EditorApplication.isPlaying ? "Enter Play mode and start a preview battle in Combat Preview."
                    : driver == null || !driver.IsReady ? "Start a preview battle in Combat Preview first." : "Play it on the player.";
                if (GUILayout.Button(new GUIContent("Play", tip), EditorStyles.toolbarButton, GUILayout.Width(40f))) Play();
            }
            using (new EditorGUI.DisabledScope(driver == null || !driver.IsBusy))
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                {
                    loop = false;
                    driver.Stop();
                    StopPlayback();
                }
            if (GUILayout.Button(new GUIContent("Preview...", "Open Combat Preview (start the preview battle there)."), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                CombatPreviewWindow.Open();
            EditorGUILayout.EndHorizontal();
        }

        private AttackTimingContext TimingFor(CharacterAttackSequence target)
        {
            RuntimeAnimatorController controller = target.TimelineAnimator;
            if (controller == null && EditorApplication.isPlaying)
            {
                PlayerCombatant player = FindAnyObjectByType<PlayerCombatant>();
                Animator animator = player != null ? player.GetComponentInChildren<Animator>() : null;
                controller = animator != null ? animator.runtimeAnimatorController : null;
            }
            return new AttackTimingContext
            {
                Animator = controller,
                StateSeconds = state => AnimatorClipLengths.StateSeconds(controller, state)
            };
        }

        private void BuildTracks()
        {
            timeline.Tracks.Clear();
            timeline.Bands.Clear();
            timeline.Lines.Clear();
            if (layout.MoveIn > 0f)
            {
                var walk = new FrameTimeline.Track { Label = "Walk in", Color = NoteTimelineColors.Info };
                walk.Items.Add(new FrameTimeline.Item { Label = "walk to stage", Start = 0f, End = layout.MoveIn, Color = NoteTimelineColors.Info, Draggable = false });
                timeline.Tracks.Add(walk);
            }
            foreach (AttackTimelineEntry entry in layout.Entries)
            {
                AttackStep step = entry.Step;
                Color color = StepColor(step, entry.Background);
                var track = new FrameTimeline.Track
                {
                    Label = $"{entry.Index + 1}. {step.TimelineLabel}{(entry.Background ? "  (background)" : string.Empty)}",
                    Tooltip = entry.Background ? "Wait For Completion is off: the next step starts with this one." : "The next step waits for this one.",
                    Color = color,
                    Key = entry
                };
                var item = new FrameTimeline.Item
                {
                    Label = step.TimelineLabel,
                    Start = entry.Start,
                    End = entry.End,
                    GhostEnd = entry.VisualEnd > entry.End + 0.0001f ? entry.VisualEnd : float.NaN,
                    LeadStart = step.Delay > 0f ? entry.DelayStart : float.NaN,
                    Color = color,
                    EndResizable = step.CanSetLength,
                    Guess = entry.LengthIsGuess,
                    Key = entry
                };
                foreach (AttackHitMark hit in entry.Hits)
                {
                    float share = layout.TotalHitWeight > 0f ? hit.Weight / layout.TotalHitWeight : 0f;
                    item.Points.Add(new FrameTimeline.Point
                    {
                        Time = entry.Start + hit.Time,
                        Label = $"{hit.Label} {share:P0}",
                        Draggable = hit.Move != null,
                        Key = hit
                    });
                }
                track.Items.Add(item);
                timeline.Tracks.Add(track);
            }
            if (layout.MoveBack > 0f)
            {
                var back = new FrameTimeline.Track { Label = "Walk back", Color = NoteTimelineColors.Info };
                back.Items.Add(new FrameTimeline.Item { Label = "walk home", Start = layout.StepsEnd, End = layout.Total, Color = NoteTimelineColors.Info, Draggable = false });
                timeline.Tracks.Add(back);
            }
            if (layout.MoveIn > 0f) timeline.Lines.Add(new FrameTimeline.Line { Time = layout.MoveIn, Color = new Color(0.6f, 0.8f, 1f, 0.6f), Label = "on stage" });
            timeline.Lines.Add(new FrameTimeline.Line { Time = layout.StepsEnd, Color = new Color(1f, 1f, 1f, 0.45f), Label = "steps done" });
            timeline.SelectedKey = selected.StepIndex >= 0 ? layout.Entries.FirstOrDefault(e => e.Index == selected.StepIndex) : null;
            // The playhead follows the performer while it plays this sequence; otherwise it stays where it was scrubbed.
            CharacterAttackPerformer performer = EditorApplication.isPlaying ? CharacterAttackPerformer.Latest : null;
            timeline.Playhead = performer != null && performer.Playing == sequence ? performer.Elapsed : scrubTime;
        }

        private static Color StepColor(AttackStep step, bool background)
        {
            Color color = step switch
            {
                PlayAnimationStep or TimedHitsStep or AnimationEventHitsStep => NoteTimelineColors.Animation,
                ThrowProjectileStep or SpawnEffectStep or ImpactStep => NoteTimelineColors.Effect,
                ChannelDamageStep => new Color(1f, 0.4f, 0.35f, 1f),
                MoveCasterStep => NoteTimelineColors.Move,
                CameraShakeStep or PlaySoundStep => NoteTimelineColors.Feedback,
                _ => NoteTimelineColors.Info
            };
            return background ? Color.Lerp(color, new Color(0.3f, 0.85f, 0.8f, 1f), 0.35f) : color;
        }

        private void DrawSummary()
        {
            EditorGUILayout.BeginHorizontal();
            int hits = layout.Entries.Sum(e => e.Hits.Count);
            EditorGUILayout.LabelField($"Total {timeline.FormatTime(layout.Total)}   ·   {hits} hit(s), weight {layout.TotalHitWeight:0.##}" +
                                       (layout.TotalHitWeight <= 0f ? "  (no hits: the ability's effects land when it ends)" : string.Empty),
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            RuntimeAnimatorController controller = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                new GUIContent("Animation Lengths", "The character's Animator Controller: animation steps use its clip lengths. Without it (or in Play mode, the player's) a ? marks guessed lengths."),
                sequence.TimelineAnimator, typeof(RuntimeAnimatorController), false, GUILayout.Width(330f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(sequence, "Set Timeline Animator");
                sequence.TimelineAnimator = controller;
                EditorUtility.SetDirty(sequence);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStepButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Step", EditorStyles.miniButtonLeft, GUILayout.Width(80f))) AddStepMenu();
            using (new EditorGUI.DisabledScope(selected.StepIndex < 0))
            {
                if (GUILayout.Button("▲", EditorStyles.miniButtonMid, GUILayout.Width(26f))) MoveStep(-1);
                if (GUILayout.Button("▼", EditorStyles.miniButtonMid, GUILayout.Width(26f))) MoveStep(1);
                if (GUILayout.Button("Duplicate", EditorStyles.miniButtonMid, GUILayout.Width(70f))) DuplicateStep();
                if (GUILayout.Button("Delete", EditorStyles.miniButtonRight, GUILayout.Width(56f))) DeleteStep();
            }
            GUILayout.FlexibleSpace();
            showSettings = GUILayout.Toggle(showSettings, "Walk / stage settings", EditorStyles.miniButton);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectedStep()
        {
            SerializedProperty steps = serialized.FindProperty("steps");
            if (steps == null) return;
            if (selected.StepIndex < 0 || selected.StepIndex >= steps.arraySize)
            {
                EditorGUILayout.HelpBox("Click a step's bar or name to edit it here. Drag bars to change delays, drag a bar's end to change its length, drag red diamonds to move hits. Alt = no frame snapping.", MessageType.None);
                return;
            }
            SerializedProperty element = steps.GetArrayElementAtIndex(selected.StepIndex);
            EditorGUILayout.LabelField($"Step {selected.StepIndex + 1}", EditorStyles.boldLabel);
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

        private void DrawSettings()
        {
            if (!showSettings) return;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Stage", EditorStyles.boldLabel);
            foreach (string name in new[] { "moveToStage", "stageViewport", "moveInSeconds", "moveBackSeconds", "movingBoolParameter", "arriveState" })
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property != null) EditorGUILayout.PropertyField(property);
            }
        }

        // ---------- step list edits ----------

        private void AddStepMenu()
        {
            var menu = new GenericMenu();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<AttackStep>().Where(t => !t.IsAbstract && !t.IsGenericType).OrderBy(t => t.Name))
            {
                Type stepType = type;
                string name = ObjectNames.NicifyVariableName(type.Name.EndsWith("Step") ? type.Name.Substring(0, type.Name.Length - 4) : type.Name);
                menu.AddItem(new GUIContent(name), false, () =>
                {
                    Undo.RecordObject(sequence, "Add Attack Step");
                    int at = selected.StepIndex >= 0 ? selected.StepIndex + 1 : sequence.EditableSteps.Count;
                    sequence.EditableSteps.Insert(at, (AttackStep)Activator.CreateInstance(stepType));
                    selected.StepIndex = at;
                    Changed();
                });
            }
            menu.ShowAsContext();
        }

        private void MoveStep(int direction)
        {
            List<AttackStep> steps = sequence.EditableSteps;
            int from = selected.StepIndex, to = from + direction;
            if (from < 0 || to < 0 || to >= steps.Count) return;
            Undo.RecordObject(sequence, "Move Attack Step");
            (steps[from], steps[to]) = (steps[to], steps[from]);
            selected.StepIndex = to;
            Changed();
        }

        private void DuplicateStep()
        {
            List<AttackStep> steps = sequence.EditableSteps;
            int index = selected.StepIndex;
            if (index < 0 || index >= steps.Count || steps[index] == null) return;
            Undo.RecordObject(sequence, "Duplicate Attack Step");
            var copy = (AttackStep)Activator.CreateInstance(steps[index].GetType());
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(steps[index]), copy);
            steps.Insert(index + 1, copy);
            selected.StepIndex = index + 1;
            Changed();
        }

        private void DeleteStep()
        {
            List<AttackStep> steps = sequence.EditableSteps;
            int index = selected.StepIndex;
            if (index < 0 || index >= steps.Count) return;
            Undo.RecordObject(sequence, "Delete Attack Step");
            steps.RemoveAt(index);
            selected.StepIndex = Mathf.Min(index, steps.Count - 1);
            Changed();
        }

        private void CreateSequence()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Character Attack Sequence", "NewAttackSequence", "asset",
                "Where to save the sequence", "Assets/Resources/Combat/Abilities/AttackSequence");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<CharacterAttackSequence>();
            asset.EditorSetSteps(new PlayAnimationStep(), new ImpactStep());
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetSequence(asset);
        }

        // ---------- playback (Combat Preview) ----------

        private void Play()
        {
            CombatPreviewDriver driver = Driver();
            if (driver == null || sequence == null) return;
            message = string.Empty;
            restoreTimeScale = Time.timeScale;
            Time.timeScale = playbackSpeed;
            timeScaleChanged = true;
            AbilityVFXProfile profile = ability != null && ability.AttackSequence == sequence ? ability.VFXProfile : null;
            Color accent = profile != null ? profile.AccentColor : Color.white;
            if (!driver.PlayAttack(sequence, accent, profile, true))
            {
                StopPlayback();
                message = "Combat Preview is not ready (start a preview battle there, and wait for the staging to finish).";
                return;
            }
            playing = true;
        }

        private void StopPlayback()
        {
            // Only undo our own slow motion (leave the game's time scale alone otherwise).
            if (!timeScaleChanged) return;
            timeScaleChanged = false;
            if (EditorApplication.isPlaying) Time.timeScale = restoreTimeScale;
        }
    }
}
