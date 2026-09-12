using System;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public sealed class RhythmComposerWindow : EditorWindow
    {
        private static readonly string[] SnapLabels = { "1/1", "1/2", "1/4", "1/8", "1/16" };
        private static readonly RhythmSnapDivision[] SnapValues =
        {
            RhythmSnapDivision.OneBeat,
            RhythmSnapDivision.HalfBeat,
            RhythmSnapDivision.QuarterBeat,
            RhythmSnapDivision.EighthBeat,
            RhythmSnapDivision.SixteenthBeat
        };

        [SerializeField] private RhythmChart chart;
        [SerializeField] private RhythmNoteType drawingType;
        [SerializeField] private bool showChartConfiguration;

        private SerializedObject serializedChart;
        private RhythmTimelineRenderer timelineRenderer;
        private RhythmTimelineInputController input;
        private RhythmNoteInspector noteInspector;
        private RhythmPreviewController preview;
        private RhythmTimelineLayout lastLayout;
        private bool hasLayout;
        private Vector2 configurationScroll;

        [MenuItem("Tools/Rhythm RPG/Rhythm Composer", false, 100)]
        public static RhythmComposerWindow OpenWindow()
        {
            RhythmComposerWindow window = GetWindow<RhythmComposerWindow>();
            window.titleContent = new GUIContent("Rhythm Composer");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
            return window;
        }

        public static void Open(RhythmChart rhythmChart)
        {
            RhythmComposerWindow window = OpenWindow();
            window.SetChart(rhythmChart);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Rhythm Composer");
            minSize = new Vector2(760f, 420f);
            timelineRenderer = new RhythmTimelineRenderer();
            input = new RhythmTimelineInputController();
            noteInspector = new RhythmNoteInspector();
            preview = new RhythmPreviewController(Repaint);
            input.SeekRequested = preview.Seek;
            Undo.undoRedoPerformed += OnUndoRedo;
            SetChart(chart);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            preview?.Dispose();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is RhythmChart selectedChart && selectedChart != chart)
            {
                SetChart(selectedChart);
            }
        }

        private void OnGUI()
        {
            HandleKeyboard(Event.current);
            DrawChartToolbar();

            if (chart == null)
            {
                DrawEmptyState();
                return;
            }

            DrawSettingsToolbar();
            DrawChartConfiguration();
            DrawWorkspace();
        }

        private void DrawChartToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            RhythmChart nextChart = (RhythmChart)EditorGUILayout.ObjectField(chart, typeof(RhythmChart), false, GUILayout.MinWidth(180f));
            if (EditorGUI.EndChangeCheck())
            {
                SetChart(nextChart);
            }

            if (GUILayout.Button("New Chart", EditorStyles.toolbarButton, GUILayout.Width(75f)))
            {
                RhythmChart created = RhythmComposerAssetFactory.CreateChartWithSavePanel();
                if (created != null) SetChart(created);
            }

            GUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(chart == null))
            {
                if (GUILayout.Button(preview != null && preview.IsPlaying ? "Pause" : "Play", EditorStyles.toolbarButton, GUILayout.Width(52f))) preview?.TogglePlayPause();
                if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(46f))) preview?.Stop();
                if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(48f))) preview?.Reset();
            }

            GUILayout.Space(8f);
            GUILayout.Label(FormatTime(preview?.Playhead ?? 0d), EditorStyles.miniLabel, GUILayout.Width(84f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(48f))) SaveAssets();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsToolbar()
        {
            serializedChart.Update();
            SerializedProperty bpm = serializedChart.FindProperty("bpm");
            SerializedProperty beatsPerMeasure = serializedChart.FindProperty("beatsPerMeasure");
            SerializedProperty duration = serializedChart.FindProperty("compositionDuration");
            SerializedProperty audioClip = serializedChart.FindProperty("audioClip");
            SerializedProperty snapEnabled = serializedChart.FindProperty("snapEnabled");
            SerializedProperty snapDivision = serializedChart.FindProperty("snapDivision");
            AudioClip previousClip = chart.AudioClip;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            bpm.floatValue = Mathf.Max(0.01f, EditorGUILayout.FloatField("BPM", bpm.floatValue, GUILayout.Width(125f)));
            beatsPerMeasure.intValue = Mathf.Max(1, EditorGUILayout.IntField("Beats/Measure", beatsPerMeasure.intValue, GUILayout.Width(155f)));
            duration.doubleValue = Math.Max(0.01d, EditorGUILayout.DoubleField("Duration", duration.doubleValue, GUILayout.Width(145f)));
            EditorGUILayout.PropertyField(audioClip, GUIContent.none, GUILayout.MinWidth(100f));
            GUILayout.Label("Draw", GUILayout.Width(32f));
            drawingType = (RhythmNoteType)EditorGUILayout.EnumPopup(drawingType, GUILayout.Width(86f));
            snapEnabled.boolValue = GUILayout.Toggle(snapEnabled.boolValue, "Snap", GUILayout.Width(50f));
            int snapIndex = Array.IndexOf(SnapValues, (RhythmSnapDivision)snapDivision.intValue);
            snapIndex = EditorGUILayout.Popup(Mathf.Max(0, snapIndex), SnapLabels, GUILayout.Width(48f));
            snapDivision.intValue = (int)SnapValues[snapIndex];

            if (GUILayout.Button("-", GUILayout.Width(24f)) && hasLayout) input.ZoomBy(0.8f, chart, lastLayout);
            GUILayout.Label($"{input.PixelsPerSecond:0} px/s", EditorStyles.miniLabel, GUILayout.Width(58f));
            if (GUILayout.Button("+", GUILayout.Width(24f)) && hasLayout) input.ZoomBy(1.25f, chart, lastLayout);
            showChartConfiguration = GUILayout.Toggle(showChartConfiguration, "Lanes & Types", EditorStyles.miniButton, GUILayout.Width(90f));

            if (EditorGUI.EndChangeCheck())
            {
                serializedChart.ApplyModifiedProperties();
                chart.EnsureIdentifiers();
                RhythmComposerMutationService.RepairOrphanedLaneReferences(chart);
                EditorUtility.SetDirty(chart);
                if (previousClip != chart.AudioClip)
                {
                    preview.SetChart(chart);
                }
            }
            else
            {
                serializedChart.ApplyModifiedProperties();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(preview.AudioWarning))
            {
                EditorGUILayout.HelpBox(preview.AudioWarning, MessageType.Warning);
            }
        }

        private void DrawChartConfiguration()
        {
            if (!showChartConfiguration)
            {
                return;
            }

            serializedChart.Update();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxHeight(230f));
            configurationScroll = EditorGUILayout.BeginScrollView(configurationScroll, GUILayout.MaxHeight(210f));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedChart.FindProperty("lanes"), new GUIContent("Lane Definitions"), true);
            EditorGUILayout.PropertyField(serializedChart.FindProperty("noteDefinitions"), new GUIContent("Note Type Defaults"), true);
            if (EditorGUI.EndChangeCheck())
            {
                serializedChart.ApplyModifiedProperties();
                chart.EnsureIdentifiers();
                RhythmComposerMutationService.RepairOrphanedLaneReferences(chart);
                EditorUtility.SetDirty(chart);
            }
            else
            {
                serializedChart.ApplyModifiedProperties();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawWorkspace()
        {
            Rect workspace = GUILayoutUtility.GetRect(200f, 100000f, 140f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float inspectorWidth = Mathf.Clamp(workspace.width * 0.26f, 255f, 350f);
            Rect timelineRect = new Rect(workspace.x, workspace.y, Mathf.Max(300f, workspace.width - inspectorWidth - 4f), workspace.height);
            Rect inspectorRect = new Rect(timelineRect.xMax + 4f, workspace.y, workspace.xMax - timelineRect.xMax - 4f, workspace.height);
            lastLayout = new RhythmTimelineLayout(timelineRect);
            hasLayout = true;
            input.ClampScroll(chart, lastLayout);

            timelineRenderer.Draw(chart, lastLayout, input.PixelsPerSecond, input.ScrollTime, input.VerticalScroll, preview.Playhead, input.SelectedNoteId);
            DrawScrollbars(lastLayout);
            if (input.Handle(Event.current, chart, lastLayout, drawingType))
            {
                Repaint();
            }

            DrawInspector(inspectorRect);
        }

        private void DrawScrollbars(RhythmTimelineLayout layout)
        {
            double visibleDuration = layout.LanesRect.width / Math.Max(1f, input.PixelsPerSecond);
            float totalDuration = (float)Math.Max(chart.EffectiveDuration, visibleDuration);
            input.ScrollTime = GUI.HorizontalScrollbar(layout.HorizontalScrollbarRect, (float)input.ScrollTime, (float)visibleDuration, 0f, totalDuration);

            float visibleHeight = layout.LanesRect.height;
            float contentHeight = Mathf.Max(visibleHeight, chart.Lanes.Count * RhythmTimelineGeometry.LaneHeight);
            input.VerticalScroll = GUI.VerticalScrollbar(layout.VerticalScrollbarRect, input.VerticalScroll, visibleHeight, 0f, contentHeight);
        }

        private void DrawInspector(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.135f, 0.15f));
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 7f, Mathf.Max(1f, rect.width - 16f), Mathf.Max(1f, rect.height - 14f)));
            bool delete = noteInspector.Draw(chart, serializedChart, input.SelectedNoteId);
            GUILayout.EndArea();
            if (delete)
            {
                DeleteSelectedNote();
            }
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(360f));
            GUILayout.Label("Rhythm Composer", new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label("Select a RhythmChart asset or create a new chart to begin composing.", new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleCenter });
            if (GUILayout.Button("Create Rhythm Chart"))
            {
                RhythmChart created = RhythmComposerAssetFactory.CreateChartWithSavePanel();
                if (created != null) SetChart(created);
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void SetChart(RhythmChart value)
        {
            chart = value;
            serializedChart = chart == null ? null : new SerializedObject(chart);
            if (chart != null)
            {
                chart.EnsureIdentifiers();
                EditorUtility.SetDirty(chart);
            }

            if (input != null)
            {
                input.SelectedNoteId = null;
                input.ScrollTime = 0d;
                input.VerticalScroll = 0f;
            }

            preview?.SetChart(chart);
            Repaint();
        }

        private void HandleKeyboard(Event current)
        {
            if (current.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
            {
                return;
            }

            if (current.keyCode == KeyCode.Space)
            {
                preview?.TogglePlayPause();
                current.Use();
            }
            else if (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace)
            {
                DeleteSelectedNote();
                current.Use();
            }
            else if (current.keyCode == KeyCode.S && (current.control || current.command))
            {
                SaveAssets();
                current.Use();
            }
        }

        private void DeleteSelectedNote()
        {
            if (chart != null && RhythmComposerMutationService.DeleteNote(chart, input.SelectedNoteId))
            {
                input.SelectedNoteId = null;
                Repaint();
            }
        }

        private static void SaveAssets()
        {
            AssetDatabase.SaveAssets();
        }

        private void OnUndoRedo()
        {
            if (chart != null && RhythmComposerMutationService.FindNote(chart, input.SelectedNoteId) == null)
            {
                input.SelectedNoteId = null;
            }
            serializedChart?.UpdateIfRequiredOrScript();
            Repaint();
        }

        private static string FormatTime(double time)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0d, time));
            return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
        }
    }
}
