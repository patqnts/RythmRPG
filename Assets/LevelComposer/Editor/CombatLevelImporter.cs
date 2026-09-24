using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RythmRPG.Combat;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Timing;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;
using RythmRPG.Rhythm;
using RythmRPG.Rhythm.Editor;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.LevelComposer.EditorTools
{
    /// <summary>
    /// Turns a .combatlevel.json from the Level Composer app into game assets under Assets/CombatLevels/&lt;id&gt;/:
    /// a CombatSong, one RhythmChart per step and an EnemyAttackSequence. Re-importing updates the same assets in place
    /// (references from enemy phases survive). Music outside the project is copied into the level's Audio folder.
    /// </summary>
    public static class CombatLevelImporter
    {
        public const string RootFolder = "Assets/CombatLevels";
        private const string LastImportPref = "RythmRPG.LevelComposer.LastImport";

        [MenuItem("Tools/Rythm RPG/Level Composer/Import Combat Level...", false, 60)]
        public static void ImportMenu()
        {
            string last = EditorPrefs.GetString(LastImportPref, "");
            string dir = !string.IsNullOrEmpty(last) && File.Exists(last) ? Path.GetDirectoryName(last) : Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string path = EditorUtility.OpenFilePanel("Import combat level", dir, "json");
            if (string.IsNullOrEmpty(path)) return;
            ImportWithReport(path);
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Reimport Last Combat Level", false, 61)]
        public static void ReimportLast()
        {
            string last = EditorPrefs.GetString(LastImportPref, "");
            if (string.IsNullOrEmpty(last) || !File.Exists(last))
            {
                EditorUtility.DisplayDialog("Level Composer", "No combat level has been imported yet (or the file moved).", "OK");
                return;
            }

            ImportWithReport(last);
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Reimport Last Combat Level", true)]
        private static bool ReimportLastValidate()
        {
            return !string.IsNullOrEmpty(EditorPrefs.GetString(LastImportPref, ""));
        }

        public static void ImportWithReport(string path)
        {
            var log = new List<string>();
            EnemyAttackSequenceDefinition sequence = null;
            try
            {
                sequence = Import(path, log);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                log.Add("Import failed: " + e.Message);
            }

            string text = string.Join("\n", log.ToArray());
            if (sequence != null)
            {
                EditorPrefs.SetString(LastImportPref, path);
                Selection.activeObject = sequence;
                EditorGUIUtility.PingObject(sequence);
                Debug.Log("Level Composer import: " + Path.GetFileName(path) + "\n" + text);
                EditorUtility.DisplayDialog("Combat level imported", text.Length > 1500 ? text.Substring(0, 1500) + "\n..." : text, "OK");
            }
            else
            {
                Debug.LogError("Level Composer import failed: " + path + "\n" + text);
                EditorUtility.DisplayDialog("Combat level import failed", text, "OK");
            }
        }

        /// <summary>Note types: built-ins plus the JSON files in StreamingAssets/ComposerNoteTypes (shared with the app).</summary>
        public static NoteTypeRegistry LoadTypes(List<string> log)
        {
            NoteTypeRegistry types = NoteTypeRegistry.CreateDefault();
            types.LoadFolders(new[] { Path.Combine(Application.streamingAssetsPath, "ComposerNoteTypes") });
            if (log != null) foreach (string m in types.LoadMessages) log.Add(m);
            return types;
        }

        /// <summary>Imports a level file; returns the attack sequence asset. <paramref name="log"/> collects what happened.</summary>
        public static EnemyAttackSequenceDefinition Import(string jsonPath, List<string> log)
        {
            var warnings = new List<string>();
            CombatLevel level = LevelSerializer.FromJson(File.ReadAllText(jsonPath), warnings);
            log.AddRange(warnings);
            NoteTypeRegistry types = LoadTypes(log);

            foreach (Issue issue in LevelValidator.Validate(level, types, jsonPath))
                if (issue.Severity == Severity.Error) log.Add("Problem: " + issue.Message + (issue.StepIndex >= 0 ? " (step " + (issue.StepIndex + 1) + ")" : ""));

            string id = CombatLevel.Slug(level.Id);
            string folder = RootFolder + "/" + id;
            EnsureFolder(folder);
            var tempo = new LevelTempo(level.Music.Bpm, level.Music.BeatsPerBar);

            // ---- song
            CombatSong song = LoadOrCreate<CombatSong>(folder + "/" + id + "_Song.asset");
            MusicSettings m = level.Music;
            song.IntroClip = ResolveClip(m.Intro, jsonPath, folder, log, "Intro");
            song.Clip = ResolveClip(m.Loop, jsonPath, folder, log, "Main loop");
            song.PlayerTurnClip = ResolveClip(m.PlayerTurn, jsonPath, folder, log, "Player turn");
            song.EndClip = ResolveClip(m.End, jsonPath, folder, log, "End");
            song.DefeatEndClip = ResolveClip(m.DefeatEnd, jsonPath, folder, log, "Defeat end");
            song.Bpm = (float)m.Bpm;
            song.BeatsPerMeasure = m.BeatsPerBar;
            song.AudioOffsetSeconds = m.OffsetSeconds;
            song.ChartSync = ToSync(m.ChartSync);
            song.EndSync = ToSync(m.EndSync);
            song.ChartsWaitForLoop = m.ChartsWaitForLoop;
            var songSo = new SerializedObject(song);
            SetFloat(songSo, "volume", (float)m.Volume);
            SetFloat(songSo, "playerTurnVolume", (float)m.PlayerTurnVolume);
            SetFloat(songSo, "mainVolumeOnPlayerTurn", (float)m.MainVolumeOnPlayerTurn);
            songSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(song);
            foreach (string w in song.GetSectionWarnings()) log.Add("Song: " + w);

            // ---- charts
            var charts = new List<RhythmChart>();
            int noteCount = 0, sequenceCount = 0;
            for (int i = 0; i < level.Steps.Count; i++)
            {
                LevelStep step = level.Steps[i];
                string chartPath = folder + "/" + id + "_Step" + (i + 1).ToString("00") + ".asset";
                bool created;
                RhythmChart chart = LoadOrCreate(chartPath, out created);
                if (created || chart.NoteDefinitions.Count == 0) RhythmComposerAssetFactory.PopulateDefaults(chart, true);
                FillChart(chart, step, level, song, types, tempo, log, ref noteCount, ref sequenceCount);
                charts.Add(chart);
            }

            // Charts left over from an earlier import with more steps are kept (they may be used elsewhere) but reported.
            for (int i = level.Steps.Count; i < 99; i++)
            {
                string stale = folder + "/" + id + "_Step" + (i + 1).ToString("00") + ".asset";
                if (AssetDatabase.LoadAssetAtPath<RhythmChart>(stale) == null) break;
                log.Add("Note: " + stale + " is from an earlier import and is no longer used by the sequence. Delete it if nothing else uses it.");
            }

            // ---- attack sequence
            EnemyAttackSequenceDefinition sequence = LoadOrCreate<EnemyAttackSequenceDefinition>(folder + "/" + id + "_Sequence.asset");
            var so = new SerializedObject(sequence);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("selectionWeight").floatValue = (float)Math.Max(0.01d, level.SelectionWeight);
            so.FindProperty("song").objectReferenceValue = song;
            SerializedProperty steps = so.FindProperty("steps");
            steps.arraySize = level.Steps.Count;
            for (int i = 0; i < level.Steps.Count; i++)
            {
                LevelStep step = level.Steps[i];
                SerializedProperty e = steps.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("animationName").stringValue = step.Animation ?? "";
                e.FindPropertyRelative("anticipationDuration").floatValue = (float)step.Anticipation;
                e.FindPropertyRelative("rhythmPattern").objectReferenceValue = charts[i];
                e.FindPropertyRelative("endPolicy").enumValueIndex = (int)step.EndPolicy;
                // durationOverride and modifiers are game-side settings: kept as they are.
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(sequence);
            AssetDatabase.SaveAssets();

            log.Insert(0, "Level '" + level.Name + "' -> " + folder + "\n" + level.Steps.Count + " steps, " + noteCount + " notes, " + sequenceCount + " sequence attacks.\n"
                          + "Add " + id + "_Sequence to an enemy phase's Attack Sequences to use it.\n");
            return sequence;
        }

        // ------------------------------------------------------------------ charts

        private static void FillChart(RhythmChart chart, LevelStep step, CombatLevel level, CombatSong song, NoteTypeRegistry types, LevelTempo tempo,
            List<string> log, ref int noteCount, ref int sequenceCount)
        {
            chart.Bpm = (float)level.Music.Bpm;
            chart.BeatsPerMeasure = level.Music.BeatsPerBar;
            chart.Song = song;
            chart.AudioClip = song.Clip;
            chart.AudioOffsetSeconds = 0d; // beat 0 = chart time 0; combat lands it on the song's next bar line
            chart.SnapEnabled = true;
            chart.SnapDivision = RhythmSnapDivision.QuarterBeat;

            // Lanes: keep the existing ones (stable ids) when the count matches.
            int laneCount = Math.Max(1, Math.Min(RhythmChart.MaxLanes, step.LaneCount));
            bool lanesMatch = chart.Lanes.Count == laneCount;
            for (int i = 0; lanesMatch && i < laneCount; i++)
                if (chart.Lanes[i] == null || chart.Lanes[i].KeyIdentity != i + 1) lanesMatch = false;
            if (!lanesMatch)
            {
                chart.Lanes.Clear();
                for (int i = 0; i < laneCount; i++)
                    chart.Lanes.Add(new RhythmLaneData("Lane " + (i + 1), i + 1, RhythmComposerAssetFactory.LaneColor(i), KeyType.DEFAULT));
            }

            chart.Notes.Clear();
            chart.Patterns.Clear();
            chart.Sequences.Clear();
            double lastEnd = 0d;
            foreach (LevelNote n in step.Notes)
            {
                NoteTypeDef def = types.Find(n.Type);
                if (def == null)
                {
                    log.Add("Skipped a note of unknown type '" + n.Type + "' in step '" + step.Name + "'.");
                    continue;
                }

                string laneId = chart.Lanes[Math.Max(1, Math.Min(laneCount, n.Lane)) - 1].Id;
                if (def.Output == NoteOutput.Sequence)
                {
                    SequenceActivationData seq = BuildSequence(n, def, chart, laneCount, tempo, log);
                    if (seq != null) { chart.Sequences.Add(seq); sequenceCount++; }
                    continue;
                }

                RhythmNoteType legacy;
                if (!Enum.TryParse(def.LegacyType, true, out legacy))
                {
                    log.Add("Note type '" + def.Id + "' has an unknown legacyType '" + def.LegacyType + "'; imported as Normal.");
                    legacy = RhythmNoteType.Normal;
                }

                var data = new RhythmNoteData(laneId, tempo.BeatToSeconds(n.Beat), legacy);
                data.AssignId(n.Id);
                ApplyNoteParams(data, n, def, chart, tempo, log);
                chart.Notes.Add(data);
                noteCount++;
                lastEnd = Math.Max(lastEnd, data.EndTime);
            }

            chart.Notes.Sort((a, b) => a.HitTime.CompareTo(b.HitTime));
            chart.CompositionDuration = Math.Max(8d, lastEnd + 2d);
            chart.EnsureIdentifiers();
            EditorUtility.SetDirty(chart);
        }

        private static void ApplyNoteParams(RhythmNoteData data, LevelNote n, NoteTypeDef def, RhythmChart chart, LevelTempo tempo, List<string> log)
        {
            RhythmNoteDefinition defaults = chart.FindDefinition(data.NoteType);
            data.HoldDuration = def.HasLength ? tempo.BeatToSeconds(Math.Max(0d, n.Length)) : 0d;
            data.TravelTime = NoteParams.TravelSeconds(n, def, tempo);
            data.Speed = defaults != null ? defaults.DefaultSpeed : 8f;
            data.Damage = defaults != null ? defaults.DefaultDamage : 1;
            data.Metadata.Clear();

            foreach (ParamDef p in def.Params)
            {
                object v = NoteParams.Get(n, def, p.Key, tempo);
                double d = Json.JsonRead.ToDouble(v, 0d);
                string s = v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
                switch (p.Bind)
                {
                    case ParamBindings.TravelBeats: break; // already applied (seconds)
                    case ParamBindings.Damage: data.Damage = (int)Math.Round(d); break;
                    case ParamBindings.Speed: data.Speed = (float)d; break;
                    case ParamBindings.BadWindow: data.StationaryBadWindow = (float)d; break;
                    case ParamBindings.GoodWindow: data.StationaryGoodWindow = (float)d; break;
                    case ParamBindings.PerfectWindow: data.StationaryPerfectWindow = (float)d; break;
                    case ParamBindings.MashPresses: data.MashRequiredPresses = (int)Math.Round(d); break;
                    case ParamBindings.HitEffect: data.HitEffect = ParseEnum(s, HitEffect.Default, log, p.Key); break;
                    case ParamBindings.PlayerState: data.PlayerState = ParseEnum(s, PlayerState.Default, log, p.Key); break;
                    case ParamBindings.Movement: data.InitializeMovementType = ParseEnum(s, NoteInitializeMovementType.None, log, p.Key); break;
                    default:
                        // Unbound: store as metadata so a new note script can read it (RhythmNoteData.Metadata).
                        if (p.Key == "pattern") { log.Add("Parameter key 'pattern' is reserved by the game's pattern system; skipped."); break; }
                        AddMeta(data, p.Key, p.Kind == ParamKind.Bool ? (v is bool && (bool)v ? "true" : "false") : s);
                        break;
                }
            }

            // Extra keys in the file that the type does not declare are kept as metadata too.
            foreach (KeyValuePair<string, object> kv in n.Params)
                if (def.FindParam(kv.Key) == null && kv.Key != "pattern") AddMeta(data, kv.Key, Convert.ToString(kv.Value, CultureInfo.InvariantCulture));

            if (!IsBuiltIn(def.Id)) AddMeta(data, "noteType", def.Id);
            if (!string.IsNullOrEmpty(def.Prefab))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(def.Prefab);
                if (prefab != null) data.PrefabOverride = prefab;
                else log.Add("Note type '" + def.Id + "': prefab " + def.Prefab + " not found; the default " + data.NoteType + " prefab is used.");
            }
        }

        private static SequenceActivationData BuildSequence(LevelNote n, NoteTypeDef def, RhythmChart chart, int laneCount, LevelTempo tempo, List<string> log)
        {
            SequenceKind kind;
            if (!Enum.TryParse(def.SequenceKind, true, out kind))
            {
                log.Add("Sequence type '" + def.Id + "' has an unknown sequenceKind '" + def.SequenceKind + "'; skipped.");
                return null;
            }

            var s = new SequenceActivationData();
            s.EnsureId();
            s.Kind = kind;
            s.StartTime = (float)tempo.BeatToSeconds(n.Beat);
            ParamDef lanesParam = def.FindBound(ParamBindings.SeqLanes);
            List<int> lanes = lanesParam != null ? LevelValidator.ParseLaneList(NoteParams.GetString(n, def, lanesParam.Key, tempo)) : new List<int>();
            if (lanes.Count == 0) lanes.Add(n.Lane);
            foreach (int lane in lanes) s.LaneIds.Add(chart.Lanes[Math.Max(1, Math.Min(laneCount, lane)) - 1].Id);
            s.Volleys = (int)Math.Round(NoteParams.GetBound(n, def, ParamBindings.SeqVolleys, tempo, s.Volleys));
            s.InitialTravelSeconds = (float)NoteParams.GetBound(n, def, ParamBindings.SeqInitialTravel, tempo, s.InitialTravelSeconds);
            s.SpeedUpFactor = (float)NoteParams.GetBound(n, def, ParamBindings.SeqSpeedUp, tempo, s.SpeedUpFactor);
            s.MinTravelSeconds = (float)NoteParams.GetBound(n, def, ParamBindings.SeqMinTravel, tempo, s.MinTravelSeconds);
            s.ReturnSeconds = (float)NoteParams.GetBound(n, def, ParamBindings.SeqReturn, tempo, s.ReturnSeconds);
            s.Damage = (int)Math.Round(NoteParams.GetBound(n, def, ParamBindings.SeqDamage, tempo, s.Damage));
            s.MaxAgeSeconds = (float)NoteParams.GetBound(n, def, ParamBindings.SeqMaxAge, tempo, s.MaxAgeSeconds);
            ParamDef align = def.FindBound(ParamBindings.SeqAlignToBeat);
            if (align != null) s.AlignToBeat = NoteParams.GetBool(n, def, align.Key, tempo, true);
            return s;
        }

        private static bool IsBuiltIn(string id)
        {
            switch (id)
            {
                case BuiltInNoteTypes.Normal:
                case BuiltInNoteTypes.Hold:
                case BuiltInNoteTypes.Stationary:
                case BuiltInNoteTypes.StationaryHold:
                case BuiltInNoteTypes.Pong:
                case BuiltInNoteTypes.Mash:
                case BuiltInNoteTypes.PingPong:
                    return true;
                default:
                    return false;
            }
        }

        private static void AddMeta(RhythmNoteData data, string key, string value)
        {
            foreach (RhythmMetadataEntry e in data.Metadata)
                if (e.Key == key) { e.Value = value ?? ""; return; }
            var m = new RhythmMetadataEntry();
            m.Key = key;
            m.Value = value ?? "";
            data.Metadata.Add(m);
        }

        private static T ParseEnum<T>(string s, T fallback, List<string> log, string key) where T : struct
        {
            T v;
            if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out v)) return v;
            if (!string.IsNullOrEmpty(s)) log.Add("Unknown value '" + s + "' for " + key + "; used " + fallback + ".");
            return fallback;
        }

        private static MusicSync ToSync(MusicSyncMode mode)
        {
            switch (mode)
            {
                case MusicSyncMode.Immediate: return MusicSync.Immediate;
                case MusicSyncMode.NextBeat: return MusicSync.NextBeat;
                default: return MusicSync.NextBar;
            }
        }

        // ------------------------------------------------------------------ audio

        /// <summary>The AudioClip for a level music path: loaded in place if inside Assets, otherwise copied into the level's Audio folder.</summary>
        private static AudioClip ResolveClip(string stored, string jsonPath, string folder, List<string> log, string label)
        {
            if (string.IsNullOrEmpty(stored)) return null;
            string full = LevelPaths.Resolve(stored, jsonPath);
            if (!File.Exists(full))
            {
                log.Add(label + " music not found: " + full);
                return null;
            }

            string assetPath = ToAssetPath(full);
            if (assetPath == null)
            {
                string audioFolder = folder + "/Audio";
                EnsureFolder(audioFolder);
                assetPath = audioFolder + "/" + Path.GetFileName(full);
                string target = Path.GetFullPath(assetPath);
                bool copy = !File.Exists(target) || new FileInfo(target).Length != new FileInfo(full).Length
                            || File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(full);
                if (copy)
                {
                    File.Copy(full, target, true);
                    log.Add(label + ": copied " + Path.GetFileName(full) + " into " + audioFolder);
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null) log.Add(label + ": Unity could not import " + assetPath + " as audio.");
            else if (full.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) log.Add(label + " is an MP3: loop joins may click. Prefer WAV / Ogg.");
            return clip;
        }

        /// <summary>"Assets/..." path for a file inside this project, or null.</summary>
        public static string ToAssetPath(string fullPath)
        {
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string f = Path.GetFullPath(fullPath).Replace('\\', '/');
            if (!f.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + f.Substring(assets.Length);
        }

        // ------------------------------------------------------------------ assets

        public static void EnsureFolder(string assetFolder)
        {
            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static RhythmChart LoadOrCreate(string path, out bool created)
        {
            RhythmChart chart = AssetDatabase.LoadAssetAtPath<RhythmChart>(path);
            created = chart == null;
            if (!created) return chart;
            chart = ScriptableObject.CreateInstance<RhythmChart>();
            AssetDatabase.CreateAsset(chart, path);
            return chart;
        }

        private static void SetFloat(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null) p.floatValue = value;
        }
    }
}
