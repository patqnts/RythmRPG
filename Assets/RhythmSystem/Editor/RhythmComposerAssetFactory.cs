using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public static class RhythmComposerAssetFactory
    {
        private static readonly Dictionary<RhythmNoteType, string> PrefabPaths = new Dictionary<RhythmNoteType, string>
        {
            { RhythmNoteType.Normal, "Assets/Prefab/NoteObject.prefab" },
            { RhythmNoteType.Hold, "Assets/Prefab/HoldNote.prefab" },
            { RhythmNoteType.Laser, "Assets/Prefab/Laser.prefab" },
            { RhythmNoteType.HoldLaser, "Assets/Prefab/Hold Laser.prefab" },
            { RhythmNoteType.Pong, "Assets/Prefab/PongNote.prefab" },
            { RhythmNoteType.Arrow, "Assets/Prefab/Arrow.prefab" },
            { RhythmNoteType.Cluster, "Assets/Prefab/ClusterNote.prefab" },
            { RhythmNoteType.Mash, "Assets/Prefab/Mash.prefab" }
        };

        /// <summary>Lanes a new chart starts with (change it per chart in the Rhythm Composer's Lanes menu).</summary>
        public const int DefaultLaneCount = RhythmChart.MaxLanes;

        private static readonly Color[] LaneColors =
        {
            new Color(0.20f, 0.62f, 0.95f),
            new Color(0.35f, 0.80f, 0.52f),
            new Color(0.95f, 0.70f, 0.22f),
            new Color(0.88f, 0.38f, 0.55f),
            new Color(0.62f, 0.45f, 0.92f)
        };

        /// <summary>Default colour of lane <paramref name="index"/> (0-based).</summary>
        public static Color LaneColor(int index) => LaneColors[Mathf.Abs(index) % LaneColors.Length];

        private static readonly Color[] NoteColors =
        {
            new Color(0.20f, 0.68f, 1.00f),
            new Color(0.28f, 0.86f, 0.56f),
            new Color(1.00f, 0.40f, 0.32f),
            new Color(0.85f, 0.32f, 0.92f),
            new Color(1.00f, 0.72f, 0.20f),
            new Color(0.48f, 0.78f, 1.00f),
            new Color(0.95f, 0.45f, 0.18f)
        };

        [MenuItem("Assets/Create/Rhythm RPG/Rhythm Chart", false, 210)]
        public static void CreateChartFromAssetsMenu()
        {
            RhythmChart chart = CreateChartWithSavePanel();
            if (chart != null)
            {
                RhythmComposerWindow.Open(chart);
            }
        }

        [MenuItem("Tools/Rhythm RPG/Create Rhythm Chart", false, 101)]
        public static void CreateChartFromToolsMenu()
        {
            CreateChartFromAssetsMenu();
        }

        public static RhythmChart CreateChartWithSavePanel()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Rhythm Chart",
                "RhythmChart",
                "asset",
                "Choose where to save the rhythm chart asset.");

            return string.IsNullOrEmpty(path) ? null : CreateChartAtPath(path);
        }

        public static RhythmChart CreateChartAtPath(string path)
        {
            RhythmChart chart = ScriptableObject.CreateInstance<RhythmChart>();
            PopulateDefaults(chart, true);
            AssetDatabase.CreateAsset(chart, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            Selection.activeObject = chart;
            EditorGUIUtility.PingObject(chart);
            return chart;
        }

        public static void PopulateDefaults(RhythmChart chart, bool resolvePrefabs)
        {
            if (chart == null)
            {
                return;
            }

            chart.Bpm = 120f;
            chart.BeatsPerMeasure = 4;
            chart.CompositionDuration = 30d;
            chart.SnapEnabled = true;
            chart.SnapDivision = RhythmSnapDivision.QuarterBeat;
            chart.Lanes.Clear();
            chart.NoteDefinitions.Clear();
            chart.Notes.Clear();

            for (int i = 0; i < DefaultLaneCount; i++)
            {
                chart.Lanes.Add(new RhythmLaneData($"Lane {i + 1}", i + 1, LaneColors[i], KeyType.DEFAULT));
            }

            RhythmNoteType[] types = (RhythmNoteType[])Enum.GetValues(typeof(RhythmNoteType));
            for (int i = 0; i < types.Length; i++)
            {
                RhythmNoteType type = types[i];
                RhythmNoteDefinition definition = new RhythmNoteDefinition(type, NoteColors[i % NoteColors.Length]);
                if (resolvePrefabs && PrefabPaths.TryGetValue(type, out string prefabPath))
                {
                    definition.DefaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                }

                chart.NoteDefinitions.Add(definition);
            }

            chart.EnsureIdentifiers();
            EditorUtility.SetDirty(chart);
        }
    }
}
