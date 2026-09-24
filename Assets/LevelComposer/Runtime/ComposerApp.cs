using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.LevelComposer.Editing;
using RythmRPG.LevelComposer.Json;
using RythmRPG.LevelComposer.Model;
using RythmRPG.LevelComposer.Types;
using RythmRPG.LevelComposer.Validation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>
    /// The Level Composer app: builds the UI on this GameObject's UIDocument and owns the edit session, audio and
    /// preview. Put it in a scene with a camera (audio listener) and build that scene as its own player
    /// (Tools > Rythm RPG > Level Composer > Build Composer App).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ComposerApp : MonoBehaviour
    {
        [Tooltip("Theme. Falls back to Resources/LevelComposer/Composer.uss.")]
        [SerializeField] private StyleSheet styleSheet;
        [Tooltip("Seconds between automatic backups of unsaved work (0 = off).")]
        [SerializeField, Min(0f)] private float autosaveSeconds = 60f;

        private UIDocument document;
        private ComposerContext ctx;
        private Playback playback;
        private TopBar topBar;
        private LeftPanel leftPanel;
        private PaletteBar palette;
        private TimelineView timeline;
        private SimulatorView simulator;
        private VisualElement previewPanel;
        private InspectorPanel inspector;
        private StatusBar status;
        private List<Issue> issues = new List<Issue>();
        private float validateAt = -1f;
        private float nextAutosave;
        private bool allowQuit;
        private bool built;
        private readonly Dictionary<Key, float> repeatAt = new Dictionary<Key, float>();

        public ComposerContext Context { get { return ctx; } }

        private void Awake()
        {
            Application.runInBackground = true;
            if (Application.targetFrameRate <= 0) Application.targetFrameRate = 120;
            Workspace.Ensure();
        }

        private void Start()
        {
            // Built in Start (after every OnEnable) so the UIDocument has created its root element.
            document = GetComponent<UIDocument>();
            PanelSettings settings = document.panelSettings;
            if (settings == null)
            {
                // Without PanelSettings UI Toolkit draws nothing (only the camera background shows).
                settings = Resources.Load<PanelSettings>("LevelComposer/ComposerPanelSettings");
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<PanelSettings>();
                    settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("LevelComposer/ComposerTheme");
                    settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                    settings.scale = 1f;
                }

                Debug.Log("Level Composer: the UIDocument had no Panel Settings; using " + (settings.name.Length > 0 ? settings.name : "runtime defaults") + ".");
            }

            // Work on a copy so UI-scale changes never modify the asset.
            document.panelSettings = Instantiate(settings);
            Build();
        }

        private void OnEnable()
        {
            Application.wantsToQuit += OnWantsToQuit;
        }

        private void OnDisable()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            if (ctx != null) ctx.Prefs.Save();
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            VisualElement root = document.rootVisualElement;
            root.Clear();
            StyleSheet sheet = styleSheet != null ? styleSheet : Resources.Load<StyleSheet>("LevelComposer/Composer");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogWarning("Level Composer: Composer.uss not found; the UI will be unstyled.");
            root.AddToClassList("app-root");
            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.top = 0;
            root.style.right = 0;
            root.style.bottom = 0;

            ComposerPrefs prefs = ComposerPrefs.Load();
            NoteTypeRegistry types = NoteTypeRegistry.CreateDefault();
            types.LoadFolders(new[] { Workspace.BuiltInNoteTypes, Workspace.NoteTypes });

            ctx = new ComposerContext
            {
                App = this,
                Root = root,
                Prefs = prefs,
                Types = types,
                Session = new LevelEditSession(types),
                Library = new AudioLibrary(this)
            };
            playback = new Playback(gameObject, prefs);
            ctx.Preview = new PreviewController(ctx, playback);

            VisualElement app = Ui.Col("app");
            root.Add(app);
            topBar = new TopBar(ctx);
            app.Add(topBar);

            VisualElement main = Ui.Row("main");
            app.Add(main);
            leftPanel = new LeftPanel(ctx);
            main.Add(leftPanel);

            VisualElement center = Ui.Col("center");
            palette = new PaletteBar(ctx);
            center.Add(palette);
            timeline = new TimelineView(ctx);
            palette.Timeline = timeline;
            center.Add(timeline);
            main.Add(center);

            previewPanel = Ui.Col("panel preview-panel");
            VisualElement previewHead = Ui.Row("preview-panel__head");
            previewHead.Add(Ui.Text("Preview", "section-title"));
            previewHead.Add(Ui.Spacer());
            previewHead.Add(Ui.Text("A  S  J  K", "muted keys-hint"));
            previewPanel.Add(previewHead);
            simulator = new SimulatorView(ctx);
            previewPanel.Add(simulator);
            main.Add(previewPanel);

            inspector = new InspectorPanel(ctx);
            main.Add(inspector);

            status = new StatusBar(ctx);
            app.Add(status);

            ctx.Modal = new ModalHost(root);
            ctx.Files = new FileBrowser(ctx.Modal) { Places = Workspace.Places, Recent = () => ctx.Prefs.Recent };
            Tooltips.Install(root);

            // Clicking anywhere outside a text box ends typing, so the keyboard shortcuts work again.
            root.RegisterCallback<PointerDownEvent>(evt =>
            {
                var target = evt.target as VisualElement;
                if (target != null && (target is TextField || target.GetFirstAncestorOfType<TextField>() != null)) return;
                var focused = root.panel != null ? root.panel.focusController.focusedElement : null;
                if (focused != null) focused.Blur();
            }, TrickleDown.TrickleDown);

            ctx.Session.Changed += kind =>
            {
                validateAt = Time.unscaledTime + 0.25f;
                if ((kind & (ChangeKind.Steps | ChangeKind.CurrentStep)) != 0) timeline.Refresh();
            };
            ctx.PrefsChanged += () => Ui.Show(previewPanel, ctx.Prefs.ShowPreview);
            ctx.Library.Loaded += e =>
            {
                timeline.Refresh();
                validateAt = Time.unscaledTime + 0.1f;
                if (e.Error != null) ctx.Modal.Toast(e.FileName + ": " + e.Error, ToastKind.Error);
            };
            Ui.Show(previewPanel, ctx.Prefs.ShowPreview);
            ApplyScale();

            foreach (string m in types.LoadMessages) Debug.LogWarning("Level Composer: " + m);
            built = true;
            ctx.Session.Load(CombatLevel.CreateNew(), "");
            validateAt = 0f;
            root.schedule.Execute(() => { timeline.Fit(); ShowWelcome(); }).StartingIn(50);
        }

        public void ApplyScale()
        {
            if (document != null && document.panelSettings != null) document.panelSettings.scale = Mathf.Clamp(ctx.Prefs.UiScale, 0.75f, 1.5f);
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            if (!built) return;
            ctx.Library.Watch();
            ctx.Preview.Update();
            HandleKeys();

            topBar.Tick();
            if (ctx.Preview.IsPlaying)
            {
                if (ctx.Prefs.FollowPlayhead) timeline.Follow(ctx.Preview.PlayheadBeat);
                timeline.MarkDirtyRepaint();
            }

            if (ctx.Prefs.ShowPreview) simulator.Refresh();

            if (validateAt >= 0f && Time.unscaledTime >= validateAt)
            {
                validateAt = -1f;
                Validate();
            }

            if (autosaveSeconds > 0f && Time.unscaledTime >= nextAutosave)
            {
                nextAutosave = Time.unscaledTime + autosaveSeconds;
                Autosave();
            }
        }

        private void Validate()
        {
            try
            {
                issues = LevelValidator.Validate(ctx.Session.Level, ctx.Types, ctx.Session.FilePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                issues = new List<Issue>();
            }

            foreach (string m in ctx.Types.LoadMessages)
                issues.Add(new Issue { Severity = Severity.Info, Message = m });
            inspector.SetIssues(issues);
            leftPanel.SetIssues(issues);
            status.SetIssues(issues);
            timeline.SetMarks(issues, ctx.Session.StepIndex);
        }

        // ------------------------------------------------------------------ keyboard

        private void HandleKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (ctx.Modal.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) ctx.Modal.Escape();
                else if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && (!ctx.IsTyping() || FocusIsEnterConfirm())) ctx.Modal.Enter();
                return;
            }

            if (ctx.IsTyping()) return;
            bool ctrl = kb.ctrlKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed;
            bool shift = kb.shiftKey.isPressed;
            LevelEditSession s = ctx.Session;

            if (kb.f1Key.wasPressedThisFrame) ShowHelp();
            if (ctrl)
            {
                if (kb.zKey.wasPressedThisFrame) { if (shift) s.Redo(); else s.Undo(); }
                else if (kb.yKey.wasPressedThisFrame) s.Redo();
                else if (kb.cKey.wasPressedThisFrame) { s.Copy(); Say(s.SelectionCount + " notes copied"); }
                else if (kb.xKey.wasPressedThisFrame) s.Cut();
                else if (kb.vKey.wasPressedThisFrame) s.Paste(Math.Max(0d, ctx.Preview.PlayheadBeat));
                else if (kb.dKey.wasPressedThisFrame) s.DuplicateSelected();
                else if (kb.aKey.wasPressedThisFrame) s.SelectAll();
                else if (kb.sKey.wasPressedThisFrame) { if (shift) SaveAs(); else Save(); }
                else if (kb.nKey.wasPressedThisFrame) NewLevel();
                else if (kb.oKey.wasPressedThisFrame) OpenLevel();
                return;
            }

            if (kb.spaceKey.wasPressedThisFrame) ctx.Preview.TogglePlay();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (ctx.Preview.IsPlaying) ctx.Preview.Stop();
                else s.ClearSelection();
            }

            if (kb.homeKey.wasPressedThisFrame) ctx.Preview.Seek(ctx.Preview.StartTime());
            if (kb.lKey.wasPressedThisFrame) ctx.Preview.SetMode(ctx.Preview.Mode == PreviewMode.Step ? PreviewMode.Level : PreviewMode.Step);
            if (kb.tKey.wasPressedThisFrame) { ctx.Prefs.AutoHit = !ctx.Prefs.AutoHit; ctx.NotifyPrefsChanged(); Say(ctx.Prefs.AutoHit ? "Auto-hit on" : "Auto-hit off: play with A S J K"); }
            if (kb.mKey.wasPressedThisFrame) { ctx.Prefs.Metronome = !ctx.Prefs.Metronome; ctx.NotifyPrefsChanged(); }
            if (kb.fKey.wasPressedThisFrame) { ctx.Prefs.FollowPlayhead = !ctx.Prefs.FollowPlayhead; ctx.NotifyPrefsChanged(); }
            if (kb.pKey.wasPressedThisFrame) { ctx.Prefs.ShowPreview = !ctx.Prefs.ShowPreview; ctx.NotifyPrefsChanged(); }
            if (kb.bKey.wasPressedThisFrame) ctx.SetTool(TimelineTool.Draw);
            if (kb.vKey.wasPressedThisFrame) ctx.SetTool(TimelineTool.Select);
            if (kb.qKey.wasPressedThisFrame) s.QuantizeSelected();
            if (kb.xKey.wasPressedThisFrame) s.MirrorSelected();
            if (kb.zKey.wasPressedThisFrame) timeline.Fit();
            if (kb.leftBracketKey.wasPressedThisFrame) palette.StepSnap(-1);
            if (kb.rightBracketKey.wasPressedThisFrame) palette.StepSnap(1);
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) timeline.ZoomAtCenter(1.25f);
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) timeline.ZoomAtCenter(1f / 1.25f);
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) s.DeleteSelected();

            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++)
            {
                if (!kb[digits[i]].wasPressedThisFrame) continue;
                if (i < palette.Order.Count)
                {
                    if (s.SelectionCount > 0 && shift) s.SetTypeOfSelected(palette.Order[i]);
                    else ctx.SetBrush(palette.Order[i]);
                }
            }

            double step = 1d / Math.Max(1, s.SnapDivision);
            if (Repeat(kb, Key.LeftArrow)) Arrow(shift ? -1d : -step, 0);
            if (Repeat(kb, Key.RightArrow)) Arrow(shift ? 1d : step, 0);
            if (Repeat(kb, Key.UpArrow)) Arrow(0d, -1);
            if (Repeat(kb, Key.DownArrow)) Arrow(0d, 1);
        }

        private void Arrow(double beats, int lanes)
        {
            LevelEditSession s = ctx.Session;
            if (s.SelectionCount > 0)
            {
                s.MoveSelected(beats, lanes);
                return;
            }

            if (beats != 0d)
            {
                double b = s.Snap(ctx.Preview.PlayheadBeat) + beats;
                ctx.Preview.SeekToBeat(b);
                timeline.ScrollTo(b, false);
                timeline.MarkDirtyRepaint();
            }
        }

        /// <summary>True on press, then repeating while held.</summary>
        private bool Repeat(Keyboard kb, Key key)
        {
            var control = kb[key];
            if (control.wasPressedThisFrame)
            {
                repeatAt[key] = Time.unscaledTime + 0.35f;
                return true;
            }

            float at;
            if (control.isPressed && repeatAt.TryGetValue(key, out at) && Time.unscaledTime >= at)
            {
                repeatAt[key] = Time.unscaledTime + 0.05f;
                return true;
            }

            return false;
        }

        private bool FocusIsEnterConfirm()
        {
            var focused = ctx.Root.panel != null ? ctx.Root.panel.focusController.focusedElement as VisualElement : null;
            while (focused != null)
            {
                if (focused.ClassListContains("dialog__input") || focused.ClassListContains("enter-confirms")) return true;
                focused = focused.parent;
            }

            return false;
        }

        public void Say(string text)
        {
            if (status != null) status.Say(text);
        }

        // ------------------------------------------------------------------ files

        /// <summary>Runs <paramref name="then"/> after offering to save unsaved changes.</summary>
        private void GuardUnsaved(Action then)
        {
            if (!ctx.Session.Dirty) { then(); return; }
            ctx.Modal.Ask("Unsaved changes", "Save the changes to '" + ctx.Session.Level.Name + "' first?", new[] { "Save", "Don't save", "Cancel" }, i =>
            {
                if (i == 0) SaveThen(then);
                else if (i == 1) then();
            }, 2);
        }

        public void NewLevel()
        {
            GuardUnsaved(() =>
            {
                ctx.Preview.Pause();
                ctx.Session.Load(CombatLevel.CreateNew(), "");
                timeline.Fit();
                Say("New level");
            });
        }

        public void OpenLevel()
        {
            GuardUnsaved(() =>
            {
                string start = !string.IsNullOrEmpty(ctx.Session.FilePath) ? Path.GetDirectoryName(ctx.Session.FilePath) : Workspace.Levels;
                ctx.Files.Open("Open level", start, new[] { CombatLevel.FileExtension, ".json" }, "level", OpenPath);
            });
        }

        public void OpenPath(string path)
        {
            try
            {
                var warnings = new List<string>();
                CombatLevel level = LevelSerializer.FromJson(File.ReadAllText(path), warnings);
                ctx.Preview.Pause();
                ctx.Session.Load(level, path);
                ctx.Prefs.AddRecent(path);
                timeline.Fit();
                ctx.Preview.Seek(ctx.Preview.StartTime());
                Say("Opened " + path);
                ctx.Modal.Toast("Opened " + Path.GetFileName(path), ToastKind.Success);
                foreach (string w in warnings) ctx.Modal.Toast(w, ToastKind.Warning);
            }
            catch (Exception e)
            {
                ctx.Modal.Info("Could not open the level", Path.GetFileName(path) + "\n\n" + e.Message);
            }
        }

        public void Save()
        {
            SaveThen(null);
        }

        private void SaveThen(Action then)
        {
            if (string.IsNullOrEmpty(ctx.Session.FilePath)) SaveAs(then);
            else if (SaveTo(ctx.Session.FilePath) && then != null) then();
        }

        public void SaveAs() { SaveAs(null); }

        private void SaveAs(Action then)
        {
            string folder = !string.IsNullOrEmpty(ctx.Session.FilePath) ? Path.GetDirectoryName(ctx.Session.FilePath) : Workspace.Levels;
            string name = CombatLevel.Slug(string.IsNullOrEmpty(ctx.Session.Level.Id) || ctx.Session.Level.Id == "new-level" ? ctx.Session.Level.Name : ctx.Session.Level.Id);
            ctx.Files.Save("Save level", folder, name, new[] { CombatLevel.FileExtension }, "level", path =>
            {
                if (File.Exists(path) && !string.Equals(path, ctx.Session.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Modal.Ask("Replace file", Path.GetFileName(path) + " already exists. Replace it?", new[] { "Replace", "Cancel" }, i =>
                    {
                        if (i == 0 && SaveTo(path) && then != null) then();
                    });
                    return;
                }

                if (SaveTo(path) && then != null) then();
            });
        }

        /// <summary>Writes the level. Music paths are rewritten relative to the new file so level and music can move together.</summary>
        private bool SaveTo(string path)
        {
            try
            {
                CombatLevel level = ctx.Session.Level;
                string oldPath = ctx.Session.FilePath;
                if (level.Id == "new-level" || string.IsNullOrEmpty(level.Id))
                    level.Id = CombatLevel.Slug(Path.GetFileName(path).Replace(CombatLevel.FileExtension, ""));
                foreach (MusicSection section in (MusicSection[])Enum.GetValues(typeof(MusicSection)))
                {
                    string stored = level.Music.Get(section);
                    if (string.IsNullOrEmpty(stored)) continue;
                    string absolute = LevelPaths.Resolve(stored, oldPath);
                    level.Music.Set(section, LevelPaths.MakeRelative(absolute, path));
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string temp = path + ".tmp";
                File.WriteAllText(temp, LevelSerializer.ToJson(level));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                ctx.Session.MarkSaved(path);
                ctx.Prefs.AddRecent(path);
                ctx.Preview.MarkDirty();
                validateAt = Time.unscaledTime;
                Say("Saved " + path);
                ctx.Modal.Toast("Saved " + Path.GetFileName(path), ToastKind.Success);
                return true;
            }
            catch (Exception e)
            {
                ctx.Modal.Info("Could not save", e.Message);
                return false;
            }
        }

        private void Autosave()
        {
            if (ctx == null || !ctx.Session.Dirty) return;
            try
            {
                CombatLevel copy = ctx.Session.Level.Clone();
                foreach (MusicSection section in (MusicSection[])Enum.GetValues(typeof(MusicSection)))
                    copy.Music.Set(section, LevelPaths.Resolve(copy.Music.Get(section), ctx.Session.FilePath));
                string name = CombatLevel.Slug(copy.Name) + ".autosave" + CombatLevel.FileExtension;
                File.WriteAllText(Path.Combine(Workspace.Autosave, name), LevelSerializer.ToJson(copy));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Level Composer autosave failed: " + e.Message);
            }
        }

        private bool OnWantsToQuit()
        {
            if (allowQuit || ctx == null || !ctx.Session.Dirty) return true;
            ctx.Modal.Ask("Quit", "Save the changes to '" + ctx.Session.Level.Name + "' before quitting?", new[] { "Save and quit", "Quit without saving", "Cancel" }, i =>
            {
                if (i == 2) return;
                if (i == 0) SaveThen(Quit);
                else Quit();
            }, 2);
            return false;
        }

        private void Quit()
        {
            allowQuit = true;
            Application.Quit();
        }

        // ------------------------------------------------------------------ dialogs

        private void ShowWelcome()
        {
            VisualElement panel = Ui.Col("dialog welcome");
            panel.Add(Ui.Text("RythmRPG Level Composer", "welcome__title"));
            panel.Add(Ui.Text("Compose enemy attack sequences: music sections, attack steps and notes, with a live combat preview.", "muted"));
            VisualElement actions = Ui.Row("welcome__actions");
            actions.Add(Icon.Button(IconKind.File, () => { ctx.Modal.Close(); }, "Start a new level", "New level", "btn--accent"));
            actions.Add(Icon.Button(IconKind.Folder, () => { ctx.Modal.Close(); OpenLevel(); }, "Open a .combatlevel.json", "Open..."));
            actions.Add(Icon.Button(IconKind.Help, () => { ctx.Modal.Close(); ShowHelp(); }, "How it works and shortcuts", "How to"));
            panel.Add(actions);
            var recent = new List<string>();
            foreach (string r in ctx.Prefs.Recent) if (File.Exists(r)) recent.Add(r);
            if (recent.Count > 0)
            {
                panel.Add(Ui.SectionTitle("Recent"));
                foreach (string r in recent)
                {
                    string path = r;
                    Button b = Ui.Button(Path.GetFileName(r), () => { ctx.Modal.Close(); OpenPath(path); }, "list-item recent-item", r);
                    panel.Add(b);
                }
            }

            ctx.Modal.Open(panel, ctx.Modal.Close, ctx.Modal.Close);
        }

        public void ShowSettings() { Dialogs.Settings(ctx); }
        public void ShowHelp() { Dialogs.Help(ctx); }
        public void ShowMusicTab() { leftPanel.ShowTab(1); }

        public void ReloadNoteTypes()
        {
            NoteTypeRegistry fresh = NoteTypeRegistry.CreateDefault();
            int n = fresh.LoadFolders(new[] { Workspace.BuiltInNoteTypes, Workspace.NoteTypes });
            // Keep the same registry object (the views hold it): copy the new definitions in.
            foreach (NoteTypeDef d in fresh.All) ctx.Types.Register(d);
            ctx.Preview.MarkDirty();
            validateAt = Time.unscaledTime;
            ctx.Modal.Toast("Note types reloaded (" + n + " from files).", ToastKind.Success);
        }

        public void WriteExampleNoteType()
        {
            try
            {
                string path = Path.Combine(Workspace.NoteTypes, "_example_bomb.json.txt");
                NoteTypeDef example = ExampleNoteType();
                var root = new Dictionary<string, object>();
                root["noteTypes"] = new List<object> { NoteTypeRegistry.ToJson(example) };
                File.WriteAllText(path,
                    "// Example gimmick. Rename to .json (remove .txt) to load it, then Settings > Reload note types.\n" +
                    "// archetype = how it previews (tap, hold, stationary, stationary_hold, mash, pingpong).\n" +
                    "// legacyType = the RhythmNoteType the game spawns; prefab = optional prefab override in the game project.\n" +
                    "// Params with a \"bind\" write that game field; others are stored as note metadata for your new note script.\n" +
                    MiniJson.Serialize(root) + "\n");
                ctx.Modal.Toast("Wrote " + path, ToastKind.Success);
            }
            catch (Exception e)
            {
                ctx.Modal.Toast("Could not write the example: " + e.Message, ToastKind.Error);
            }
        }

        public static NoteTypeDef ExampleNoteType()
        {
            NoteTypeDef normal = BuiltInNoteTypes.Create()[0];
            var d = new NoteTypeDef
            {
                Id = "bomb",
                Name = "Bomb",
                Category = "Special",
                Description = "Do NOT press it: it explodes if hit. Example of a custom gimmick.",
                Color = "#FF4D4D",
                Shape = NoteShape.Square,
                Archetype = Archetypes.Tap,
                LegacyType = "Normal",
                Prefab = "Assets/Prefab/BombNote.prefab"
            };
            d.Params.Add(normal.FindParam("travel").Clone());
            d.Params.Add(new ParamDef { Key = "blastDamage", Label = "Blast damage", Kind = ParamKind.Int, Default = 2d, Min = 0, Max = 99, Step = 1, Tooltip = "Damage when the player presses it (stored as metadata 'blastDamage')." });
            d.Params.Add(new ParamDef { Key = "fuse", Label = "Fuse", Kind = ParamKind.Seconds, Default = 0.5d, Min = 0, Max = 3, Step = 0.05 });
            return d;
        }
    }
}
