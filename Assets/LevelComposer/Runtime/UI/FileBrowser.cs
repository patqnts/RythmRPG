using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// In-app file dialog (Unity players have no native one): quick places, folder navigation, extension filter,
    /// and a file-name box when saving. Double-click or Enter confirms.
    /// </summary>
    public sealed class FileBrowser
    {
        public sealed class Place
        {
            public string Name;
            public string Path;
            public Place(string name, string path) { Name = name; Path = path; }
        }

        private readonly ModalHost modal;
        private string folder;
        private string[] extensions;
        private bool saving;
        private string selectedPath;
        private Action<string> onPick;
        private TextField pathField;
        private TextField nameField;
        private ScrollView list;
        private Label hint;
        private readonly Dictionary<string, string> lastFolderByKind = new Dictionary<string, string>();

        public FileBrowser(ModalHost modal)
        {
            this.modal = modal;
        }

        public Func<List<Place>> Places;
        public Func<List<string>> Recent;

        /// <summary>Lets the user choose an existing file.</summary>
        public void Open(string title, string startFolder, string[] exts, string kind, Action<string> picked)
        {
            Show(title, startFolder, exts, kind, false, "", picked);
        }

        /// <summary>Lets the user choose a folder and file name to save to. The first extension is added when missing.</summary>
        public void Save(string title, string startFolder, string defaultName, string[] exts, string kind, Action<string> picked)
        {
            Show(title, startFolder, exts, kind, true, defaultName, picked);
        }

        private void Show(string title, string startFolder, string[] exts, string kind, bool save, string defaultName, Action<string> picked)
        {
            extensions = exts ?? new string[0];
            saving = save;
            selectedPath = null;
            string remembered;
            if (!string.IsNullOrEmpty(kind) && lastFolderByKind.TryGetValue(kind, out remembered) && Directory.Exists(remembered)) startFolder = remembered;
            folder = Directory.Exists(startFolder) ? startFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            onPick = path =>
            {
                if (!string.IsNullOrEmpty(kind)) lastFolderByKind[kind] = Path.GetDirectoryName(path);
                if (picked != null) picked(path);
            };

            VisualElement panel = Ui.Col("dialog file-browser");
            panel.Add(Ui.Text(title, "dialog__title"));

            VisualElement pathRow = Ui.Row("file-browser__path");
            pathRow.Add(Ui.Button("↑", () => Navigate(Directory.GetParent(folder) != null ? Directory.GetParent(folder).FullName : folder), "btn--icon", "Up one folder"));
            pathField = new TextField();
            pathField.isDelayed = true;
            pathField.AddToClassList("grow");
            pathField.RegisterValueChangedCallback(evt =>
            {
                string p = evt.newValue.Trim().Trim('"');
                if (Directory.Exists(p)) Navigate(p);
                else if (File.Exists(p)) { selectedPath = p; Confirm(); }
                else pathField.SetValueWithoutNotify(folder);
            });
            pathRow.Add(pathField);
            pathRow.Add(Ui.Button("New folder", NewFolder, null, "Create a folder here"));
            panel.Add(pathRow);

            VisualElement body = Ui.Row("file-browser__body");
            ScrollView places = new ScrollView(ScrollViewMode.Vertical);
            places.AddToClassList("file-browser__places");
            if (Places != null)
            {
                foreach (Place p in Places())
                {
                    if (string.IsNullOrEmpty(p.Path) || !Directory.Exists(p.Path)) continue;
                    string target = p.Path;
                    places.Add(Ui.Button(p.Name, () => Navigate(target), "list-item", target));
                }
            }

            if (!saving && Recent != null)
            {
                List<string> recent = Recent();
                if (recent.Count > 0)
                {
                    places.Add(Ui.Text("Recent", "section-title"));
                    foreach (string r in recent)
                    {
                        if (!File.Exists(r)) continue;
                        string target = r;
                        places.Add(Ui.Button(System.IO.Path.GetFileName(r), () => { selectedPath = target; Confirm(); }, "list-item", target));
                    }
                }
            }

            body.Add(places);
            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("file-browser__list");
            body.Add(list);
            panel.Add(body);

            VisualElement bottom = Ui.Row("dialog__buttons");
            if (saving)
            {
                nameField = new TextField("File name") { value = defaultName ?? "" };
                nameField.AddToClassList("grow");
                nameField.AddToClassList("enter-confirms");
                bottom.Add(nameField);
            }
            else
            {
                hint = Ui.Text(extensions.Length > 0 ? "Showing " + string.Join(", ", extensions) : "", "muted grow");
                bottom.Add(hint);
            }

            bottom.Add(Ui.Button("Cancel", modal.Close));
            bottom.Add(Ui.Button(saving ? "Save" : "Open", Confirm, "btn--accent"));
            panel.Add(bottom);

            modal.Open(panel, modal.Close, Confirm);
            Navigate(folder);
            if (saving) nameField.schedule.Execute(() => nameField.Focus()).StartingIn(30);
        }

        private void Navigate(string path)
        {
            try
            {
                folder = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            pathField.SetValueWithoutNotify(folder);
            list.Clear();
            selectedPath = null;
            try
            {
                var dirs = new List<string>(Directory.GetDirectories(folder));
                dirs.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    var info = new DirectoryInfo(d);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    string target = d;
                    VisualElement item = Item("▸  " + info.Name, "file-item file-item--dir");
                    item.RegisterCallback<PointerDownEvent>(evt => { if (evt.clickCount >= 2) Navigate(target); else Select(item, null); });
                    list.Add(item);
                }

                var files = new List<string>(Directory.GetFiles(folder));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    if (!Matches(f)) continue;
                    string target = f;
                    VisualElement item = Item(Path.GetFileName(f), "file-item");
                    item.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        Select(item, target);
                        if (evt.clickCount >= 2) Confirm();
                    });
                    list.Add(item);
                }

                if (list.childCount == 0) list.Add(Ui.Text("Empty folder", "muted file-item"));
            }
            catch (Exception e)
            {
                list.Add(Ui.Text("Cannot open this folder: " + e.Message, "muted file-item"));
            }
        }

        private static VisualElement Item(string text, string cls)
        {
            Label l = Ui.Text(text, cls);
            return l;
        }

        private void Select(VisualElement item, string path)
        {
            foreach (VisualElement child in list.Children()) child.RemoveFromClassList("file-item--selected");
            item.AddToClassList("file-item--selected");
            selectedPath = path;
            if (saving && path != null) nameField.SetValueWithoutNotify(StripKnownExtension(Path.GetFileName(path)));
        }

        private bool Matches(string file)
        {
            if (extensions.Length == 0) return true;
            foreach (string ext in extensions)
                if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private string StripKnownExtension(string name)
        {
            foreach (string ext in extensions)
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name.Substring(0, name.Length - ext.Length);
            return name;
        }

        private void NewFolder()
        {
            string current = folder;
            // The prompt replaces this dialog, so remember the state and come back afterwards.
            string[] exts = extensions;
            bool wasSaving = saving;
            string name = saving ? nameField.value : "";
            Action<string> pick = onPick;
            modal.Prompt("New folder", "Name", "New Folder", n =>
            {
                string created = current;
                try
                {
                    if (!string.IsNullOrEmpty(n)) created = Directory.CreateDirectory(Path.Combine(current, n.Trim())).FullName;
                }
                catch (Exception e)
                {
                    modal.Toast("Could not create the folder: " + e.Message, ToastKind.Error);
                }

                Show(wasSaving ? "Save" : "Open", created, exts, null, wasSaving, name, pick);
            });
        }

        private void Confirm()
        {
            if (saving)
            {
                string name = (nameField.value ?? "").Trim();
                if (name.Length == 0) { modal.Toast("Type a file name.", ToastKind.Warning); return; }
                foreach (char c in Path.GetInvalidFileNameChars())
                    if (name.IndexOf(c) >= 0) { modal.Toast("The name contains '" + c + "', which is not allowed.", ToastKind.Warning); return; }
                if (extensions.Length > 0 && !Matches(name)) name += extensions[0];
                string path = Path.Combine(folder, name);
                Action<string> pick = onPick;
                modal.Close();
                if (pick != null) pick(path);
                return;
            }

            if (string.IsNullOrEmpty(selectedPath) || !File.Exists(selectedPath))
            {
                modal.Toast("Choose a file.", ToastKind.Warning);
                return;
            }

            string chosen = selectedPath;
            Action<string> cb = onPick;
            modal.Close();
            if (cb != null) cb(chosen);
        }
    }
}
