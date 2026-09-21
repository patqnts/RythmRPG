using RythmRPG.Rhythm;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    /// <summary>Editor shortcut to try a Ping-Pong sequence attack on a chart until the composer has a sequence track.</summary>
    public static class SequenceActivationMenu
    {
        private const string MenuPath = "Tools/Rhythm/Add Ping-Pong Sequence To Selected Chart";

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            return Selection.activeObject is RhythmChart;
        }

        [MenuItem(MenuPath)]
        private static void AddPingPong()
        {
            RhythmChart chart = Selection.activeObject as RhythmChart;
            if (chart == null) return;
            Undo.RecordObject(chart, "Add Ping-Pong Sequence");
            var sequence = new SequenceActivationData { Kind = SequenceKind.PingPong, StartTime = 1f };
            sequence.EnsureId();
            int count = chart.Lanes.Count;
            int first = count >= 4 ? 1 : 0;
            int last = count >= 4 ? count - 2 : count - 1;
            for (int i = first; i <= last; i++) sequence.LaneIds.Add(chart.Lanes[i].Id);
            chart.Sequences.Add(sequence);
            EditorUtility.SetDirty(chart);
            AssetDatabase.SaveAssets();
            Debug.Log("Added a Ping-Pong sequence to " + chart.name + " at 1 s (edit it in the chart inspector, list 'sequences'). "
                + "Enemy notes need a Pong note definition with a prefab on the chart, or a Sequence Note Prefab on the runner.");
        }
    }
}
