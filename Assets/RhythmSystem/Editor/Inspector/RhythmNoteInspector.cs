using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public sealed class RhythmNoteInspector
    {
        private Vector2 scroll;

        public bool Draw(RhythmChart chart, SerializedObject serializedChart, string selectedNoteId)
        {
            EditorGUILayout.LabelField("NOTE INSPECTOR", EditorStyles.boldLabel);
            EditorGUILayout.Space(3f);

            RhythmNoteData selected = RhythmComposerMutationService.FindNote(chart, selectedNoteId);
            if (selected == null)
            {
                EditorGUILayout.HelpBox("Select a note on the timeline to inspect it.", MessageType.Info);
                DrawChartValidation(chart, null);
                return false;
            }

            int noteIndex = chart.Notes.IndexOf(selected);
            serializedChart.Update();
            SerializedProperty note = serializedChart.FindProperty("notes").GetArrayElementAtIndex(noteIndex);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            DrawLanePopup(chart, note.FindPropertyRelative("laneId"));
            SerializedProperty typeProperty = note.FindPropertyRelative("noteType");
            EditorGUILayout.PropertyField(typeProperty, new GUIContent("Note Type"));
            RhythmNoteType type = (RhythmNoteType)typeProperty.enumValueIndex;

            SerializedProperty hitTime = note.FindPropertyRelative("hitTime");
            hitTime.doubleValue = Math.Max(0d, EditorGUILayout.DoubleField("Hit Time (s)", hitTime.doubleValue));
            EditorGUILayout.LabelField("Selected", $"{type} @ {hitTime.doubleValue:0.###}s", EditorStyles.miniBoldLabel);

            if (RhythmTimingUtility.IsHoldType(type))
            {
                SerializedProperty holdDuration = note.FindPropertyRelative("holdDuration");
                holdDuration.doubleValue = Math.Max(RhythmComposerMutationService.MinimumDuration(chart), EditorGUILayout.DoubleField("Hold Duration (s)", holdDuration.doubleValue));
                EditorGUILayout.LabelField("Legacy Length", (holdDuration.doubleValue * Math.Max(0f, note.FindPropertyRelative("speed").floatValue)).ToString("0.###"));
            }

            EditorGUILayout.PropertyField(note.FindPropertyRelative("initializeMovementType"), new GUIContent("Initialize Movement"));
            EditorGUILayout.PropertyField(note.FindPropertyRelative("hitEffect"), new GUIContent("Hit Effect / Modifier"));
            EditorGUILayout.PropertyField(note.FindPropertyRelative("playerState"), new GUIContent("Player State Effect"));

            SerializedProperty damage = note.FindPropertyRelative("damage");
            damage.intValue = Math.Max(0, EditorGUILayout.IntField("Damage", damage.intValue));
            SerializedProperty speed = note.FindPropertyRelative("speed");
            speed.floatValue = Mathf.Max(0.01f, EditorGUILayout.FloatField("Speed", speed.floatValue));
            SerializedProperty travelTime = note.FindPropertyRelative("travelTime");

            if (IsLaserType(type))
            {
                DrawStationaryTimingFields(note, travelTime);
            }
            else
            {
                travelTime.doubleValue = Math.Max(0d, EditorGUILayout.DoubleField("Travel Time (s)", travelTime.doubleValue));
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(note.FindPropertyRelative("prefabOverride"), new GUIContent("Prefab Override"));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Resolved Prefab", chart.ResolvePrefab(selected), typeof(GameObject), false);
                EditorGUILayout.DoubleField("Derived Spawn Time", hitTime.doubleValue - travelTime.doubleValue);
            }

            EditorGUILayout.Space(5f);
            EditorGUILayout.PropertyField(note.FindPropertyRelative("metadata"), new GUIContent("Extensible Metadata"), true);

            if (EditorGUI.EndChangeCheck())
            {
                serializedChart.ApplyModifiedProperties();
                RhythmNoteData changed = RhythmComposerMutationService.FindNote(chart, selectedNoteId);
                if (changed != null)
                {
                    RhythmComposerMutationService.ExtendDuration(chart, changed.EndTime);
                }
                EditorUtility.SetDirty(chart);
            }
            else
            {
                serializedChart.ApplyModifiedProperties();
            }

            EditorGUILayout.Space(8f);
            bool delete = GUILayout.Button("Delete Note");
            DrawChartValidation(chart, selected.Id);
            EditorGUILayout.EndScrollView();
            return delete;
        }

        private static void DrawStationaryTimingFields(SerializedProperty note, SerializedProperty travelTime)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Stationary Note Timing", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("The note is hittable during anticipation. Judgement improves as the charge gets closer to completion.", MessageType.Info);

            travelTime.doubleValue = Math.Max(0d,
                EditorGUILayout.DoubleField("Anticipation Time (s)", travelTime.doubleValue));

            SerializedProperty badWindow = note.FindPropertyRelative("stationaryBadWindow");
            badWindow.floatValue = Mathf.Max(0f,
                EditorGUILayout.FloatField("Bad Threshold (s before end)", badWindow.floatValue));

            SerializedProperty goodWindow = note.FindPropertyRelative("stationaryGoodWindow");
            goodWindow.floatValue = Mathf.Clamp(
                EditorGUILayout.FloatField("Good Threshold (s before end)", goodWindow.floatValue), 0f, badWindow.floatValue);

            SerializedProperty perfectWindow = note.FindPropertyRelative("stationaryPerfectWindow");
            perfectWindow.floatValue = Mathf.Clamp(
                EditorGUILayout.FloatField("Perfect Threshold (s before end)", perfectWindow.floatValue), 0f, goodWindow.floatValue);
        }

        private static bool IsLaserType(RhythmNoteType type)
        {
            return type == RhythmNoteType.Laser || type == RhythmNoteType.HoldLaser;
        }

        private static void DrawLanePopup(RhythmChart chart, SerializedProperty laneId)
        {
            if (chart.Lanes.Count == 0)
            {
                EditorGUILayout.HelpBox("Add a lane before editing this note.", MessageType.Error);
                return;
            }

            string[] labels = new string[chart.Lanes.Count];
            int current = 0;
            for (int i = 0; i < chart.Lanes.Count; i++)
            {
                RhythmLaneData lane = chart.Lanes[i];
                labels[i] = lane == null ? $"Missing Lane {i + 1}" : $"{lane.DisplayName} [{lane.KeyIdentity}] / {lane.KeyType}";
                if (lane != null && lane.Id == laneId.stringValue)
                {
                    current = i;
                }
            }

            int next = EditorGUILayout.Popup("Lane / Key", current, labels);
            if (chart.Lanes[next] != null)
            {
                laneId.stringValue = chart.Lanes[next].Id;
            }
        }

        private static void DrawChartValidation(RhythmChart chart, string noteId)
        {
            IReadOnlyList<RhythmValidationIssue> issues = RhythmChartValidator.Validate(chart);
            int shown = 0;
            foreach (RhythmValidationIssue issue in issues)
            {
                if (!string.IsNullOrEmpty(issue.NoteId) && issue.NoteId != noteId)
                {
                    continue;
                }

                if (shown++ == 0)
                {
                    EditorGUILayout.Space(8f);
                    EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                }

                MessageType messageType = issue.Severity == RhythmValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == RhythmValidationSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, messageType);
                if (shown >= 4)
                {
                    break;
                }
            }
        }
    }
}
