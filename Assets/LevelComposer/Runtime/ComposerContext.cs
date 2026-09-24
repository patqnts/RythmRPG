using System;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Types;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>The services every view shares. Created once by <see cref="ComposerApp"/>.</summary>
    public sealed class ComposerContext
    {
        public NoteTypeRegistry Types;
        public LevelEditSession Session;
        public AudioLibrary Library;
        public PreviewController Preview;
        public ComposerPrefs Prefs;
        public ModalHost Modal;
        public FileBrowser Files;
        public VisualElement Root;
        public ComposerApp App;

        /// <summary>Note type armed for drawing on the timeline.</summary>
        public string BrushType = BuiltInNoteTypes.Normal;
        public TimelineTool Tool = TimelineTool.Draw;
        public event Action BrushChanged;
        public event Action PrefsChanged;
        /// <summary>A view asks others to show a note (e.g. clicking a problem).</summary>
        public event Action<string, double> FocusRequested;

        public void SetBrush(string typeId)
        {
            if (Types.Find(typeId) == null) return;
            BrushType = typeId;
            if (Tool != TimelineTool.Draw) Tool = TimelineTool.Draw;
            if (BrushChanged != null) BrushChanged();
        }

        public void SetTool(TimelineTool tool)
        {
            Tool = tool;
            if (BrushChanged != null) BrushChanged();
        }

        public void NotifyPrefsChanged()
        {
            Prefs.Save();
            if (PrefsChanged != null) PrefsChanged();
        }

        public void RequestFocus(string noteId, double beat)
        {
            if (FocusRequested != null) FocusRequested(noteId, beat);
        }

        public bool IsTyping() { return Ui.IsTyping(Root); }

        /// <summary>Absolute path of a music section of the open level ("" when unset).</summary>
        public string SectionPath(MusicSection section)
        {
            return LevelPaths.Resolve(Session.Level.Music.Get(section), Session.FilePath);
        }

        /// <summary>Loaded (or loading) audio of a section, or null when unset.</summary>
        public AudioEntry Section(MusicSection section)
        {
            string path = SectionPath(section);
            return string.IsNullOrEmpty(path) ? null : Library.Get(path);
        }
    }

    public enum TimelineTool
    {
        Select,
        Draw
    }
}
