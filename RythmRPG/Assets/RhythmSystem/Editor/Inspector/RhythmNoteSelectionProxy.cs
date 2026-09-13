using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public sealed class RhythmNoteSelectionProxy : ScriptableObject
    {
        [SerializeField] private RhythmChart chart;
        [SerializeField] private string noteId;

        private static RhythmNoteSelectionProxy instance;
        private System.Action repaintComposer;

        public RhythmChart Chart => chart;
        public string NoteId => noteId;

        public static void Select(RhythmChart selectedChart, string selectedNoteId, System.Action repaint)
        {
            if (selectedChart == null || string.IsNullOrEmpty(selectedNoteId))
            {
                return;
            }

            instance ??= CreateInstance<RhythmNoteSelectionProxy>();
            instance.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.DontUnloadUnusedAsset;
            instance.Configure(selectedChart, selectedNoteId, repaint);
            Selection.activeObject = instance;
            EditorUtility.SetDirty(instance);
        }

        public static void ClearIfSelected(RhythmChart selectedChart)
        {
            if (instance == null || instance.chart != selectedChart)
            {
                return;
            }

            instance.noteId = string.Empty;
            Selection.activeObject = selectedChart;
        }

        public void Configure(RhythmChart selectedChart, string selectedNoteId, System.Action repaint)
        {
            chart = selectedChart;
            noteId = selectedNoteId ?? string.Empty;
            repaintComposer = repaint;
            name = ResolveDisplayName();
        }

        public void NotifyEdited()
        {
            repaintComposer?.Invoke();
        }

        private string ResolveDisplayName()
        {
            RhythmNoteData note = RhythmComposerMutationService.FindNote(chart, noteId);
            return note == null ? "Rhythm Note Selection" : $"{note.NoteType} Note @ {note.HitTime:0.###}s";
        }
    }

    [CustomEditor(typeof(RhythmNoteSelectionProxy))]
    public sealed class RhythmNoteSelectionProxyEditor : UnityEditor.Editor
    {
        private RhythmNoteInspector noteInspector;
        private SerializedObject serializedChart;
        private RhythmChart boundChart;

        private void OnEnable()
        {
            noteInspector = new RhythmNoteInspector();
            RebindChart();
        }

        public override void OnInspectorGUI()
        {
            RhythmNoteSelectionProxy proxy = (RhythmNoteSelectionProxy)target;
            if (proxy.Chart == null)
            {
                EditorGUILayout.HelpBox("No rhythm chart is connected to this note selection.", MessageType.Info);
                return;
            }

            if (boundChart != proxy.Chart || serializedChart == null)
            {
                RebindChart();
            }

            EditorGUILayout.ObjectField("Chart", proxy.Chart, typeof(RhythmChart), false);
            EditorGUILayout.Space(4f);

            bool delete = noteInspector.Draw(proxy.Chart, serializedChart, proxy.NoteId);
            if (delete)
            {
                RhythmChart chart = proxy.Chart;
                RhythmComposerMutationService.DeleteNote(chart, proxy.NoteId);
                EditorUtility.SetDirty(chart);
                RhythmNoteSelectionProxy.ClearIfSelected(chart);
                proxy.NotifyEdited();
                return;
            }

            proxy.NotifyEdited();
        }

        private void RebindChart()
        {
            RhythmNoteSelectionProxy proxy = target as RhythmNoteSelectionProxy;
            boundChart = proxy != null ? proxy.Chart : null;
            serializedChart = boundChart != null ? new SerializedObject(boundChart) : null;
        }
    }
}
