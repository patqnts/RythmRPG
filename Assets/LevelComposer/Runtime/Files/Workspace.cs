using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Simulation;
using UnityEngine;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// The team member's composer folder (Documents/RythmRPG Composer): Levels, Audio, NoteTypes and Autosave.
    /// Created on first run. Levels can live anywhere (e.g. inside the game repository); this is only the default.
    /// </summary>
    public static class Workspace
    {
        public const string FolderName = "RythmRPG Composer";

        public static string Root
        {
            get
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = Application.persistentDataPath;
                return Path.Combine(docs, FolderName);
            }
        }

        public static string Levels { get { return Path.Combine(Root, "Levels"); } }
        public static string Audio { get { return Path.Combine(Root, "Audio"); } }
        public static string NoteTypes { get { return Path.Combine(Root, "NoteTypes"); } }
        public static string Autosave { get { return Path.Combine(Root, "Autosave"); } }

        /// <summary>Note types shipped with the app (Assets/StreamingAssets/ComposerNoteTypes in the project).</summary>
        public static string BuiltInNoteTypes { get { return Path.Combine(Application.streamingAssetsPath, "ComposerNoteTypes"); } }

        public static void Ensure()
        {
            try
            {
                Directory.CreateDirectory(Levels);
                Directory.CreateDirectory(Audio);
                Directory.CreateDirectory(NoteTypes);
                Directory.CreateDirectory(Autosave);
                string readme = Path.Combine(Root, "README.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "RythmRPG Level Composer workspace\n\n" +
                        "Levels    - your .combatlevel.json files (you can also save them anywhere, e.g. in the game repo).\n" +
                        "Audio     - music you use in levels. WAV or OGG are best (MP3 adds gaps at loop points).\n" +
                        "NoteTypes - extra note-type (gimmick) definitions as .json. They appear in the palette after a restart\n" +
                        "            or Settings > Reload note types. See Assets/LevelComposer/README.md in the game project.\n" +
                        "Autosave  - automatic backups of unsaved work.\n");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Level Composer: could not create the workspace folder: " + e.Message);
            }
        }

        public static List<FileBrowser.Place> Places()
        {
            var list = new List<FileBrowser.Place>();
            list.Add(new FileBrowser.Place("Levels", Levels));
            list.Add(new FileBrowser.Place("Audio", Audio));
            list.Add(new FileBrowser.Place("Workspace", Root));
            list.Add(new FileBrowser.Place("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
            list.Add(new FileBrowser.Place("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            list.Add(new FileBrowser.Place("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)));
            list.Add(new FileBrowser.Place("Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            try
            {
                foreach (string drive in Directory.GetLogicalDrives()) list.Add(new FileBrowser.Place(drive, drive));
            }
            catch (Exception)
            {
                // Drive listing is optional.
            }

            return list;
        }
    }

    /// <summary>Per-user settings, stored in PlayerPrefs.</summary>
    public sealed class ComposerPrefs
    {
        private const string Prefix = "RythmRPG.LevelComposer.";
        public const int MaxRecent = 8;

        public float MusicVolume = 0.8f;
        public float HitSoundVolume = 0.6f;
        public float MetronomeVolume = 0.6f;
        public bool HitSounds = true;
        public bool Metronome;
        public bool AutoHit = true;
        public bool FollowPlayhead = true;
        public bool ShowPreview = true;
        /// <summary>Milliseconds added to key presses to compensate for audio output latency.</summary>
        public float InputOffsetMs;
        public float UiScale = 1f;
        public JudgementWindows Windows = new JudgementWindows();
        public List<string> Recent = new List<string>();

        public static ComposerPrefs Load()
        {
            var p = new ComposerPrefs();
            p.MusicVolume = PlayerPrefs.GetFloat(Prefix + "musicVolume", p.MusicVolume);
            p.HitSoundVolume = PlayerPrefs.GetFloat(Prefix + "hitVolume", p.HitSoundVolume);
            p.MetronomeVolume = PlayerPrefs.GetFloat(Prefix + "metronomeVolume", p.MetronomeVolume);
            p.HitSounds = PlayerPrefs.GetInt(Prefix + "hitSounds", 1) == 1;
            p.Metronome = PlayerPrefs.GetInt(Prefix + "metronome", 0) == 1;
            p.AutoHit = PlayerPrefs.GetInt(Prefix + "autoHit", 1) == 1;
            p.FollowPlayhead = PlayerPrefs.GetInt(Prefix + "follow", 1) == 1;
            p.ShowPreview = PlayerPrefs.GetInt(Prefix + "showPreview", 1) == 1;
            p.InputOffsetMs = PlayerPrefs.GetFloat(Prefix + "inputOffsetMs", 0f);
            p.UiScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "uiScale", 1f), 0.75f, 1.5f);
            p.Windows.Perfect = PlayerPrefs.GetFloat(Prefix + "perfectMs", 45f) / 1000d;
            p.Windows.Good = PlayerPrefs.GetFloat(Prefix + "goodMs", 90f) / 1000d;
            p.Windows.Bad = PlayerPrefs.GetFloat(Prefix + "badMs", 125f) / 1000d;
            p.Windows.Miss = PlayerPrefs.GetFloat(Prefix + "missMs", 180f) / 1000d;
            p.Windows.Sanitize();
            string recent = PlayerPrefs.GetString(Prefix + "recent", "");
            foreach (string r in recent.Split('|'))
                if (!string.IsNullOrEmpty(r)) p.Recent.Add(r);
            return p;
        }

        public void Save()
        {
            PlayerPrefs.SetFloat(Prefix + "musicVolume", MusicVolume);
            PlayerPrefs.SetFloat(Prefix + "hitVolume", HitSoundVolume);
            PlayerPrefs.SetFloat(Prefix + "metronomeVolume", MetronomeVolume);
            PlayerPrefs.SetInt(Prefix + "hitSounds", HitSounds ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "metronome", Metronome ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "autoHit", AutoHit ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "follow", FollowPlayhead ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "showPreview", ShowPreview ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "inputOffsetMs", InputOffsetMs);
            PlayerPrefs.SetFloat(Prefix + "uiScale", UiScale);
            PlayerPrefs.SetFloat(Prefix + "perfectMs", (float)(Windows.Perfect * 1000d));
            PlayerPrefs.SetFloat(Prefix + "goodMs", (float)(Windows.Good * 1000d));
            PlayerPrefs.SetFloat(Prefix + "badMs", (float)(Windows.Bad * 1000d));
            PlayerPrefs.SetFloat(Prefix + "missMs", (float)(Windows.Miss * 1000d));
            PlayerPrefs.SetString(Prefix + "recent", string.Join("|", Recent.ToArray()));
            PlayerPrefs.Save();
        }

        public void AddRecent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Recent.RemoveAll(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, path);
            while (Recent.Count > MaxRecent) Recent.RemoveAt(Recent.Count - 1);
            Save();
        }
    }
}
