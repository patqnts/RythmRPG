using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.Combat;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.Rhythm;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.LevelComposer.EditorTools
{
    /// <summary>
    /// Writes an existing EnemyAttackSequence (its song and step charts) as a .combatlevel.json so it can be opened and
    /// edited in the Level Composer app. Import it back with Import Combat Level (creates assets under Assets/CombatLevels).
    /// </summary>
    public static class CombatLevelExporter
    {
        [MenuItem("Tools/Rythm RPG/Level Composer/Export Selected Attack Sequence...", false, 80)]
        [MenuItem("Assets/Rythm RPG/Export To Combat Level (.json)...", false, 300)]
        public static void ExportSelected()
        {
            var sequence = Selection.activeObject as EnemyAttackSequenceDefinition;
            if (sequence == null)
            {
                EditorUtility.DisplayDialog("Level Composer", "Select an Enemy Attack Sequence asset in the Project window first.", "OK");
                return;
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string folder = Path.Combine(root, "CombatLevels");
            Directory.CreateDirectory(folder);
            string path = EditorUtility.SaveFilePanel("Export combat level", folder, CombatLevel.Slug(sequence.name) + ".combatlevel", "json");
            if (string.IsNullOrEmpty(path)) return;
            var log = new List<string>();
            CombatLevel level = Export(sequence, path, log);
            File.WriteAllText(path, LevelSerializer.ToJson(level));
            string text = "Wrote " + path + "\n" + level.Steps.Count + " steps, " + level.TotalNotes + " notes.\n" + string.Join("\n", log.ToArray());
            Debug.Log("Level Composer export: " + text);
            EditorUtility.DisplayDialog("Combat level exported", text.Length > 1500 ? text.Substring(0, 1500) + "\n..." : text, "OK");
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Export Selected Attack Sequence...", true)]
        [MenuItem("Assets/Rythm RPG/Export To Combat Level (.json)...", true)]
        private static bool ExportSelectedValidate()
        {
            return Selection.activeObject is EnemyAttackSequenceDefinition;
        }

        public static CombatLevel Export(EnemyAttackSequenceDefinition sequence, string jsonPath, List<string> log)
        {
            NoteTypeRegistry types = CombatLevelImporter.LoadTypes(log);
            var level = new CombatLevel
            {
                Id = CombatLevel.Slug(string.IsNullOrEmpty(sequence.Id) ? sequence.name : sequence.Id),
                Name = sequence.name,
                SelectionWeight = sequence.SelectionWeight
            };

            CombatSong song = sequence.Song;
            MusicSettings m = level.Music;
            if (song != null)
            {
                m.Bpm = song.Bpm;
                m.BeatsPerBar = song.BeatsPerMeasure;
                m.OffsetSeconds = song.AudioOffsetSeconds;
                m.Volume = song.Volume;
                m.PlayerTurnVolume = song.PlayerTurnVolume;
                m.MainVolumeOnPlayerTurn = song.MainVolumeOnPlayerTurn;
                m.ChartSync = FromSync(song.ChartSync);
                m.EndSync = FromSync(song.EndSync);
                m.ChartsWaitForLoop = song.ChartsWaitForLoop;
                m.Intro = ClipPath(song.IntroClip, jsonPath);
                m.Loop = ClipPath(song.Clip, jsonPath);
                m.PlayerTurn = ClipPath(song.PlayerTurnClip, jsonPath);
                m.End = ClipPath(song.EndClip, jsonPath);
                m.DefeatEnd = song.DefeatEndClip != song.EndClip ? ClipPath(song.DefeatEndClip, jsonPath) : "";
            }
            else
            {
                log.Add("The sequence has no song; using the first chart's tempo.");
                foreach (EnemyAttackStepDefinition s in sequence.Steps)
                {
                    if (s.RhythmPattern == null) continue;
                    m.Bpm = s.RhythmPattern.Bpm;
                    m.BeatsPerBar = s.RhythmPattern.BeatsPerMeasure;
                    break;
                }
            }

            var tempo = new LevelTempo(m.Bpm, m.BeatsPerBar);
            int index = 0;
            foreach (EnemyAttackStepDefinition s in sequence.Steps)
            {
                index++;
                RhythmChart chart = s.RhythmPattern;
                var step = new LevelStep
                {
                    Name = chart != null ? chart.name : "Step " + index,
                    Animation = s.AnimationName,
                    Anticipation = s.AnticipationDuration,
                    EndPolicy = (StepEndPolicy)(int)s.EndPolicy
                };
                level.Steps.Add(step);
                if (chart == null)
                {
                    log.Add("Step " + index + " has no chart.");
                    continue;
                }

                if (Math.Abs(chart.Bpm - m.Bpm) > 0.01f)
                    log.Add("Chart '" + chart.name + "' is at " + chart.Bpm + " BPM but the song is " + m.Bpm + " BPM; note times are kept in seconds.");
                ExportChart(chart, step, types, tempo, log);
            }

            if (level.Steps.Count == 0) level.Steps.Add(new LevelStep { Name = "Step 1" });
            return level;
        }

        private static void ExportChart(RhythmChart chart, LevelStep step, NoteTypeRegistry types, LevelTempo tempo, List<string> log)
        {
            // Lane ids -> 1-based lane numbers by key identity.
            var laneNumber = new Dictionary<string, int>();
            int laneCount = 1;
            foreach (RhythmLaneData lane in chart.Lanes)
            {
                if (lane == null) continue;
                int number = Math.Max(1, Math.Min(RhythmChart.MaxLanes, lane.KeyIdentity));
                laneNumber[lane.Id] = number;
                laneCount = Math.Max(laneCount, number);
            }

            step.LaneCount = laneCount;
            double offset = chart.AudioOffsetSeconds;
            int skipped = 0, fromPatterns = 0;
            foreach (RhythmNoteData data in chart.Notes)
            {
                if (data == null) continue;
                string typeId = MetaValue(data, "noteType");
                if (string.IsNullOrEmpty(typeId) || types.Find(typeId) == null) typeId = NoteMigration.DefinitionIdFor(data.NoteType);
                NoteTypeDef def = types.Find(typeId);
                if (def == null) { skipped++; continue; }
                if (!string.IsNullOrEmpty(MetaValue(data, "pattern"))) fromPatterns++;

                int lane;
                if (!laneNumber.TryGetValue(data.LaneId, out lane)) lane = 1;
                var n = new LevelNote
                {
                    Id = data.Id,
                    Type = def.Id,
                    Lane = lane,
                    Beat = Math.Max(0d, Math.Round(tempo.SecondsToBeat(data.HitTime - offset), 6)),
                    Length = def.HasLength ? Math.Round(tempo.SecondsToBeat(Math.Max(0d, data.HoldDuration)), 6) : 0d
                };

                foreach (ParamDef p in def.Params)
                {
                    object value = null;
                    switch (p.Bind)
                    {
                        case ParamBindings.TravelBeats: value = Math.Round(tempo.SecondsToBeat(data.TravelTime), 6); break;
                        case ParamBindings.Damage: value = (double)data.Damage; break;
                        case ParamBindings.Speed: value = (double)data.Speed; break;
                        case ParamBindings.BadWindow: value = (double)data.StationaryBadWindow; break;
                        case ParamBindings.GoodWindow: value = (double)data.StationaryGoodWindow; break;
                        case ParamBindings.PerfectWindow: value = (double)data.StationaryPerfectWindow; break;
                        case ParamBindings.MashPresses: value = (double)data.MashRequiredPresses; break;
                        case ParamBindings.HitEffect: value = data.HitEffect.ToString(); break;
                        case ParamBindings.PlayerState: value = data.PlayerState.ToString(); break;
                        case ParamBindings.Movement: value = data.InitializeMovementType.ToString(); break;
                        default:
                            string meta = MetaValue(data, p.Key);
                            if (meta != null) value = meta;
                            break;
                    }

                    if (value != null) NoteParams.Set(n, def, p.Key, value, tempo);
                }

                step.Notes.Add(n);
            }

            foreach (SequenceActivationData s in chart.Sequences)
            {
                if (s == null) continue;
                NoteTypeDef def = null;
                foreach (NoteTypeDef d in types.All)
                    if (d.Output == NoteOutput.Sequence && string.Equals(d.SequenceKind, s.Kind.ToString(), StringComparison.OrdinalIgnoreCase)) { def = d; break; }
                if (def == null) { skipped++; continue; }
                var lanes = new List<string>();
                int firstLane = 0;
                foreach (string id in s.LaneIds)
                {
                    int ln;
                    if (!laneNumber.TryGetValue(id, out ln)) continue;
                    if (firstLane == 0) firstLane = ln;
                    lanes.Add(ln.ToString());
                }

                var n = new LevelNote
                {
                    Type = def.Id,
                    Lane = firstLane > 0 ? firstLane : Math.Max(1, (laneCount + 1) / 2),
                    Beat = Math.Max(0d, Math.Round(tempo.SecondsToBeat(s.StartTime - offset), 6))
                };
                Set(n, def, ParamBindings.SeqVolleys, (double)s.Volleys, tempo);
                Set(n, def, ParamBindings.SeqInitialTravel, (double)s.InitialTravelSeconds, tempo);
                Set(n, def, ParamBindings.SeqSpeedUp, (double)s.SpeedUpFactor, tempo);
                Set(n, def, ParamBindings.SeqMinTravel, (double)s.MinTravelSeconds, tempo);
                Set(n, def, ParamBindings.SeqReturn, (double)s.ReturnSeconds, tempo);
                Set(n, def, ParamBindings.SeqAlignToBeat, s.AlignToBeat, tempo);
                Set(n, def, ParamBindings.SeqDamage, (double)s.Damage, tempo);
                Set(n, def, ParamBindings.SeqMaxAge, (double)s.MaxAgeSeconds, tempo);
                if (lanes.Count > 1) Set(n, def, ParamBindings.SeqLanes, string.Join(" ", lanes.ToArray()), tempo);
                step.Notes.Add(n);
            }

            if (skipped > 0) log.Add("Chart '" + chart.name + "': " + skipped + " notes of unsupported types (Arrow / Cluster or unknown) were left out.");
            if (fromPatterns > 0) log.Add("Chart '" + chart.name + "': " + fromPatterns + " notes generated by programmed patterns were exported as ordinary notes.");
        }

        private static void Set(LevelNote n, NoteTypeDef def, string bind, object value, LevelTempo tempo)
        {
            ParamDef p = def.FindBound(bind);
            if (p != null) NoteParams.Set(n, def, p.Key, value, tempo);
        }

        private static string MetaValue(RhythmNoteData data, string key)
        {
            foreach (RhythmMetadataEntry e in data.Metadata)
                if (e != null && e.Key == key) return e.Value;
            return null;
        }

        private static string ClipPath(AudioClip clip, string jsonPath)
        {
            if (clip == null) return "";
            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath)) return "";
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            return LevelPaths.MakeRelative(full, jsonPath);
        }

        private static MusicSyncMode FromSync(MusicSync sync)
        {
            switch (sync)
            {
                case MusicSync.Immediate: return MusicSyncMode.Immediate;
                case MusicSync.NextBeat: return MusicSyncMode.NextBeat;
                default: return MusicSyncMode.NextBar;
            }
        }
    }
}
