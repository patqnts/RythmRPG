using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace RythmRPG.Rhythm.Editor
{
    public static class RhythmComposerMutationService
    {
        public static RhythmNoteData FindNote(RhythmChart chart, string noteId)
        {
            return chart?.Notes.FirstOrDefault(note => note != null && note.Id == noteId);
        }

        public static RhythmNoteData AddNote(RhythmChart chart, RhythmLaneData lane, double hitTime, RhythmNoteType type)
        {
            if (chart == null || lane == null)
            {
                return null;
            }

            Undo.RecordObject(chart, "Add Rhythm Note");
            RhythmNoteData note = new RhythmNoteData(lane.Id, Math.Max(0d, hitTime), type);
            RhythmNoteDefinition definition = chart.FindDefinition(type);
            if (definition != null)
            {
                note.Speed = definition.DefaultSpeed;
                note.Damage = definition.DefaultDamage;
                note.TravelTime = definition.DefaultTravelTime;
            }

            if (note.IsHold)
            {
                note.HoldDuration = chart.SecondsPerBeat;
            }

            chart.Notes.Add(note);
            ExtendDuration(chart, note.EndTime);
            EditorUtility.SetDirty(chart);
            return note;
        }

        public static bool DeleteNote(RhythmChart chart, string noteId)
        {
            RhythmNoteData note = FindNote(chart, noteId);
            if (note == null)
            {
                return false;
            }

            Undo.RecordObject(chart, "Delete Rhythm Note");
            chart.Notes.Remove(note);
            EditorUtility.SetDirty(chart);
            return true;
        }

        public static double Snap(RhythmChart chart, double time)
        {
            if (chart == null)
            {
                return Math.Max(0d, time);
            }

            return chart.SnapEnabled
                ? RhythmTimingUtility.SnapTime(time, chart.Bpm, chart.SnapDivision)
                : Math.Max(0d, time);
        }

        public static double MinimumDuration(RhythmChart chart)
        {
            return chart != null && chart.SnapEnabled
                ? RhythmTimingUtility.GetSnapInterval(chart.Bpm, chart.SnapDivision)
                : 0.01d;
        }

        public static void ExtendDuration(RhythmChart chart, double requiredEnd)
        {
            if (chart != null && requiredEnd > chart.CompositionDuration)
            {
                chart.CompositionDuration = requiredEnd;
            }
        }

        public static void RepairOrphanedLaneReferences(RhythmChart chart)
        {
            if (chart == null || chart.Lanes.Count == 0)
            {
                return;
            }

            HashSet<string> laneIds = new HashSet<string>(chart.Lanes.Where(lane => lane != null).Select(lane => lane.Id));
            RhythmLaneData fallback = chart.Lanes.FirstOrDefault(lane => lane != null);
            if (fallback == null)
            {
                return;
            }

            foreach (RhythmNoteData note in chart.Notes.Where(note => note != null))
            {
                if (!laneIds.Contains(note.LaneId))
                {
                    note.LaneId = fallback.Id;
                }
            }
        }
    }
}
