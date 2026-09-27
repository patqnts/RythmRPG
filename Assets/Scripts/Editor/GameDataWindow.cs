using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.EditorTools
{
    public sealed class GameDataWindow : EditorWindow
    {
        private const string AllTypes = "All types";
        [SerializeField] private ScriptableObject selectedAsset;
        [SerializeField] private string search = "";
        [SerializeField] private string typeFilter = AllTypes;
        [SerializeField] private bool favoritesOnly;
        [SerializeField] private bool includeOtherAssets;

        private sealed class Entry
        {
            public ScriptableObject Asset;
            public string Id;
            public string Path;
            public string Type;
            public bool IsGameData;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<Entry> filtered = new List<Entry>();
        private readonly HashSet<string> favorites = new HashSet<string>();
        private ListView assetList;
        private DropdownField typeDropdown;
        private VisualElement inspectorHost;
        private Label countLabel;
        private Label emptyLabel;
        private Button favoriteButton;
        private Button saveButton;
        private IVisualElementScheduledItem pendingRefresh;
        private string FavoritesKey => "RythmRPG.GameData.Favorites." + Hash128.Compute(Application.dataPath);

        [MenuItem("Tools/Rythm RPG/Game Data", false, 0)]
        public static void Open()
        {
            GetWindow<GameDataWindow>("Game Data");
        }

        private void OnEnable()
        {
            minSize = new Vector2(720, 420);
            favorites.Clear();
            foreach (string id in EditorPrefs.GetString(FavoritesKey, "").Split('|'))
                if (!string.IsNullOrEmpty(id)) favorites.Add(id);
            EditorApplication.projectChanged += QueueRefresh;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= QueueRefresh;
            Undo.undoRedoPerformed -= OnUndoRedo;
            pendingRefresh?.Pause();
            rootVisualElement.Clear();
            assetList = null;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("game-data");
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                scriptPath.Substring(0, scriptPath.LastIndexOf('/') + 1) + "GameDataWindow.uss");
            if (style != null && !rootVisualElement.styleSheets.Contains(style))
                rootVisualElement.styleSheets.Add(style);

            var toolbar = new Toolbar();
            toolbar.Add(new Label("GAME DATA"));
            var searchField = new ToolbarSearchField { value = search, tooltip = "Search by asset name, type, or folder" };
            searchField.AddToClassList("game-data-search");
            searchField.RegisterValueChangedCallback(evt => { search = evt.newValue; ApplyFilters(); });
            toolbar.Add(searchField);
            toolbar.Add(new ToolbarButton(RefreshAssets) { text = "Refresh" });
            rootVisualElement.Add(toolbar);

            var split = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal)
            { viewDataKey = "game-data-split" };
            split.AddToClassList("game-data-body");
            rootVisualElement.Add(split);

            var library = new VisualElement();
            library.AddToClassList("game-data-library");
            split.Add(library);
            typeDropdown = new DropdownField("Type", new List<string> { AllTypes }, 0, FormatType, FormatType);
            typeDropdown.RegisterValueChangedCallback(evt => { typeFilter = evt.newValue; ApplyFilters(); });
            library.Add(typeDropdown);
            var favoritesToggle = new Toggle("Favorites only") { value = favoritesOnly };
            favoritesToggle.RegisterValueChangedCallback(evt => { favoritesOnly = evt.newValue; ApplyFilters(); });
            library.Add(favoritesToggle);
            var otherToggle = new Toggle("Include other ScriptableObjects")
            {
                value = includeOtherAssets,
                tooltip = "Also show package settings, fonts, and other ScriptableObjects stored under Assets."
            };
            otherToggle.RegisterValueChangedCallback(evt =>
            {
                includeOtherAssets = evt.newValue;
                RefreshAssets();
            });
            library.Add(otherToggle);
            countLabel = new Label();
            countLabel.AddToClassList("game-data-count");
            library.Add(countLabel);

            assetList = new ListView(filtered, 48, MakeRow, BindRow)
            {
                selectionType = SelectionType.Single,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight
            };
            assetList.AddToClassList("game-data-list");
            assetList.selectionChanged += selection =>
            {
                selectedAsset = (selection.FirstOrDefault() as Entry)?.Asset;
                ShowInspector();
            };
            assetList.itemsChosen += selection =>
            {
                var entry = selection.FirstOrDefault() as Entry;
                if (entry != null) EditorGUIUtility.PingObject(entry.Asset);
            };
            library.Add(assetList);
            emptyLabel = new Label("No matching assets. Try clearing the search or changing the filters.");
            emptyLabel.AddToClassList("game-data-empty");
            library.Add(emptyLabel);

            inspectorHost = new VisualElement();
            inspectorHost.AddToClassList("game-data-inspector");
            split.Add(inspectorHost);
            var note = new Label("Edit the asset directly. Undo is available. Use Save Asset to write changes to disk; Play Mode edits can persist.");
            note.AddToClassList("game-data-footer");
            rootVisualElement.Add(note);
            RefreshAssets();
        }

        private static string FormatType(string type)
        {
            return type == AllTypes ? type : ObjectNames.NicifyVariableName(type.Substring(type.LastIndexOf('.') + 1));
        }

        private static VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("game-data-row");
            var name = new Label { name = "assetName" };
            name.AddToClassList("game-data-row-name");
            row.Add(name);
            var type = new Label { name = "assetType" };
            type.AddToClassList("game-data-row-type");
            row.Add(type);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            Entry entry = filtered[index];
            row.Q<Label>("assetName").text = entry.Asset == null ? "Missing asset" :
                (favorites.Contains(entry.Id) ? "★ " : "") + entry.Asset.name;
            row.Q<Label>("assetType").text = entry.Asset == null ? entry.Type :
                ObjectNames.NicifyVariableName(entry.Asset.GetType().Name);
            row.tooltip = entry.Path;
        }

        private void RefreshAssets()
        {
            if (assetList == null) return;
            entries.Clear();
            string query = "t:ScriptableObject";
            if (!includeOtherAssets)
            {
                var types = MonoImporter.GetAllRuntimeMonoScripts()
                    .Where(script => IsGameSource(AssetDatabase.GetAssetPath(script)))
                    .Select(script => script.GetClass())
                    .Where(type => type != null && !type.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(type))
                    .Select(type => "t:" + type.Name).Distinct().ToArray();
                if (types.Length == 0)
                {
                    UpdateTypeChoices();
                    ApplyFilters();
                    return;
                }
                query = string.Join(" ", types);
            }
            foreach (string guid in AssetDatabase.FindAssets(query, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>())
                {
                    if ((asset.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                    var script = MonoScript.FromScriptableObject(asset);
                    string source = script == null ? "" : AssetDatabase.GetAssetPath(script);
                    entries.Add(new Entry
                    {
                        Asset = asset,
                        Id = GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString(),
                        Path = path,
                        Type = asset.GetType().FullName,
                        IsGameData = IsGameSource(source)
                    });
                }
            }
            entries.Sort((a, b) =>
            {
                int byName = StringComparer.OrdinalIgnoreCase.Compare(a.Asset.name, b.Asset.name);
                return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Path, b.Path);
            });
            UpdateTypeChoices();
            ApplyFilters();
        }

        private static bool IsGameSource(string source)
        {
            return (source.StartsWith("Assets/Scripts/", StringComparison.Ordinal) ||
                    source.StartsWith("Assets/RhythmSystem/", StringComparison.Ordinal) ||
                    source.StartsWith("Assets/LevelComposer/", StringComparison.Ordinal)) &&
                   !source.Contains("/Editor/") && !source.Contains("/Tests/");
        }

        private void UpdateTypeChoices()
        {
            var choices = entries.Where(e => includeOtherAssets || e.IsGameData)
                .Select(e => e.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
            choices.Insert(0, AllTypes);
            if (!choices.Contains(typeFilter)) typeFilter = AllTypes;
            typeDropdown.choices = choices;
            typeDropdown.SetValueWithoutNotify(typeFilter);
        }

        private void ApplyFilters()
        {
            if (assetList == null) return;
            string[] terms = (search ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            filtered.Clear();
            filtered.AddRange(entries.Where(e => e.Asset != null &&
                (includeOtherAssets || e.IsGameData) &&
                (typeFilter == AllTypes || e.Type == typeFilter) &&
                (!favoritesOnly || favorites.Contains(e.Id)) &&
                terms.All(term => (e.Asset.name + " " + e.Type + " " + e.Path)
                    .IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)));
            assetList.RefreshItems();
            int index = filtered.FindIndex(e => e.Asset == selectedAsset);
            if (index < 0 && filtered.Count > 0) index = 0;
            assetList.SetSelectionWithoutNotify(index < 0 ? Array.Empty<int>() : new[] { index });
            selectedAsset = index < 0 ? null : filtered[index].Asset;
            countLabel.text = $"{filtered.Count} assets";
            emptyLabel.EnableInClassList("game-data-hidden", filtered.Count > 0);
            ShowInspector();
        }

        private void ShowInspector()
        {
            inspectorHost.Clear();
            favoriteButton = null;
            saveButton = null;
            if (selectedAsset == null)
            {
                var message = new Label("Choose a ScriptableObject to adjust its properties.");
                message.AddToClassList("game-data-empty");
                inspectorHost.Add(message);
                return;
            }

            var title = new Label(selectedAsset.name);
            title.AddToClassList("game-data-title");
            inspectorHost.Add(title);
            var path = new Label(AssetDatabase.GetAssetPath(selectedAsset));
            path.AddToClassList("game-data-path");
            inspectorHost.Add(path);
            var actions = new Toolbar();
            favoriteButton = new ToolbarButton(ToggleFavorite);
            actions.Add(favoriteButton);
            actions.Add(new ToolbarButton(() => EditorGUIUtility.PingObject(selectedAsset)) { text = "Locate" });
            actions.Add(new ToolbarButton(() => Selection.activeObject = selectedAsset) { text = "Select in Project" });
            saveButton = new ToolbarButton(() =>
            {
                if (selectedAsset == null) return;
                AssetDatabase.SaveAssetIfDirty(selectedAsset);
                UpdateSaveState();
            }) { text = "Save Asset" };
            actions.Add(saveButton);
            inspectorHost.Add(actions);
            UpdateFavoriteButton();
            UpdateSaveState();

            var scroll = new ScrollView(ScrollViewMode.Vertical) { viewDataKey = "game-data-properties" };
            scroll.AddToClassList("game-data-properties");
            scroll.Add(new InspectorElement(selectedAsset));
            inspectorHost.Add(scroll);
            saveButton.schedule.Execute(UpdateSaveState).Every(250);
        }

        private void ToggleFavorite()
        {
            Entry entry = entries.Find(e => e.Asset == selectedAsset);
            if (entry == null) return;
            if (!favorites.Add(entry.Id)) favorites.Remove(entry.Id);
            EditorPrefs.SetString(FavoritesKey, string.Join("|", favorites.OrderBy(id => id)));
            if (favoritesOnly) ApplyFilters();
            else { assetList.RefreshItems(); UpdateFavoriteButton(); }
        }

        private void UpdateFavoriteButton()
        {
            Entry entry = entries.Find(e => e.Asset == selectedAsset);
            if (favoriteButton != null)
                favoriteButton.text = entry != null && favorites.Contains(entry.Id) ? "★ Favorited" : "☆ Favorite";
        }

        private void UpdateSaveState()
        {
            if (saveButton == null) return;
            bool dirty = selectedAsset != null && EditorUtility.IsDirty(selectedAsset);
            saveButton.text = dirty ? "Save Asset *" : "Save Asset";
            saveButton.SetEnabled(dirty);
        }

        private void QueueRefresh()
        {
            if (assetList == null) return;
            pendingRefresh?.Pause();
            pendingRefresh = rootVisualElement.schedule.Execute(RefreshAssets).StartingIn(200);
        }

        private void OnUndoRedo()
        {
            assetList?.RefreshItems();
            UpdateSaveState();
        }
    }
}
