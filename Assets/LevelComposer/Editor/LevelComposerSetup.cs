using System;
using System.IO;
using RythmRPG.LevelComposer.App;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.EditorTools
{
    /// <summary>
    /// Creates the Level Composer scene (camera + audio listener, UIDocument with the composer, EventSystem) and builds
    /// it as a standalone app for the team. Menu: Tools > Rythm RPG > Level Composer.
    /// </summary>
    public static class LevelComposerSetup
    {
        public const string ScenePath = "Assets/Scenes/LevelComposer.unity";
        // In Resources so the app can also load them at run time if a scene lost its reference.
        public const string SettingsFolder = "Assets/LevelComposer/Runtime/Resources/LevelComposer";
        public const string PanelSettingsPath = SettingsFolder + "/ComposerPanelSettings.asset";
        public const string ThemePath = SettingsFolder + "/ComposerTheme.tss";
        public const string StyleSheetPath = "Assets/LevelComposer/Runtime/Resources/LevelComposer/Composer.uss";
        public const string AppName = "RythmRPG Level Composer";
        private const string BuildFolderPref = "RythmRPG.LevelComposer.BuildFolder";

        [MenuItem("Tools/Rythm RPG/Level Composer/Open Composer Scene", false, 1)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Play Composer In Editor", false, 2)]
        public static void PlayInEditor()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) CreateScene();
            else EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Create (Or Rebuild) Composer Scene", false, 20)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsurePanelSettings();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Load the assets only now: NewScene unloads assets nothing references, which would leave these references empty.
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (panel == null) Debug.LogError("Level Composer: could not load " + PanelSettingsPath);
            if (sheet == null) Debug.LogWarning("Level Composer: could not load " + StyleSheetPath + " (the app falls back to Resources).");

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
            camera.orthographic = true;
            camera.cullingMask = 0;
            cameraGo.AddComponent<AudioListener>();

            var appGo = new GameObject("Level Composer");
            var doc = appGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var app = appGo.AddComponent<ComposerApp>();
            var so = new SerializedObject(app);
            SerializedProperty sheetProp = so.FindProperty("styleSheet");
            if (sheetProp != null) sheetProp.objectReferenceValue = sheet;
            so.ApplyModifiedPropertiesWithoutUndo();

            // EventSystem + Input System UI module, added by type name so this editor assembly needs no uGUI reference.
            var events = new GameObject("EventSystem");
            Type eventSystem = Type.GetType("UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            Type inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (eventSystem != null) events.AddComponent(eventSystem);
            if (inputModule != null) events.AddComponent(inputModule);
            if (eventSystem == null || inputModule == null)
                Debug.Log("Level Composer: no uGUI EventSystem / Input System UI module found; UI Toolkit will use its built-in input handling.");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("Level Composer: created " + ScenePath + ". Press Play to try it, or Tools > Rythm RPG > Level Composer > Build Composer App.");
        }

        /// <summary>Panel settings (constant pixel size; the app applies the user's UI scale) with the default runtime theme.</summary>
        public static PanelSettings EnsurePanelSettings()
        {
            Directory.CreateDirectory(SettingsFolder);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
                theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }

            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            panel.sortingOrder = 0;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            return panel;
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Build Composer App (Windows)", false, 40)]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, AppName + ".exe");
        }

        [MenuItem("Tools/Rythm RPG/Level Composer/Build Composer App (macOS)", false, 41)]
        public static void BuildMac()
        {
            Build(BuildTarget.StandaloneOSX, AppName + ".app");
        }

        /// <summary>
        /// Builds only the composer scene into its own player. Player settings that would otherwise make it look like the
        /// game (name, full screen) are switched for the build and restored afterwards.
        /// </summary>
        public static void Build(BuildTarget target, string executable)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) CreateScene();

            string defaultFolder = EditorPrefs.GetString(BuildFolderPref, Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "LevelComposer")));
            string folder = EditorUtility.SaveFolderPanel("Build the Level Composer into...", Path.GetDirectoryName(defaultFolder), Path.GetFileName(defaultFolder));
            if (string.IsNullOrEmpty(folder)) return;
            EditorPrefs.SetString(BuildFolderPref, folder);

            string productName = PlayerSettings.productName;
            FullScreenMode fullScreen = PlayerSettings.fullScreenMode;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            bool resizable = PlayerSettings.resizableWindow;
            bool runInBackground = PlayerSettings.runInBackground;
            try
            {
                PlayerSettings.productName = AppName;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1600;
                PlayerSettings.defaultScreenHeight = 900;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.runInBackground = true;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(folder, executable),
                    target = target,
                    options = BuildOptions.None
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result == BuildResult.Succeeded)
                {
                    Debug.Log("Level Composer built: " + options.locationPathName + " (" + (report.summary.totalSize / (1024 * 1024)) + " MB)");
                    EditorUtility.RevealInFinder(options.locationPathName);
                }
                else
                {
                    EditorUtility.DisplayDialog("Level Composer", "The build did not succeed (" + report.summary.result + "). See the Console.", "OK");
                }
            }
            finally
            {
                PlayerSettings.productName = productName;
                PlayerSettings.fullScreenMode = fullScreen;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.runInBackground = runInBackground;
            }
        }
    }
}
