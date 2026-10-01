using System.Collections.Generic;
using RythmRPG.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class GameSceneCatalogTools : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;
    [MenuItem("Tools/Rythm RPG/Scenes/Sync Catalog to Build Settings")]
    public static void Sync()
    {
        var catalog = UnityEngine.Resources.Load<GameSceneCatalog>(GameSceneCatalog.ResourcePath);
        if (catalog == null) throw new BuildFailedException("Create Resources/Scenes/GameSceneCatalog first.");
        var paths = new HashSet<string>();
        foreach (var definition in catalog.scenes)
        {
            if (definition == null) throw new BuildFailedException("Scene catalog contains an empty entry.");
            string prior = definition.Path;
            definition.RefreshPath();
            if (prior != definition.Path) { EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition); }
            string problem = catalog.Problem(definition);
            if (problem != null) throw new BuildFailedException(problem);
            if (!paths.Add(definition.Path)) throw new BuildFailedException("Scene appears twice in catalog: " + definition.Path);
        }
        if (catalog.Problem(catalog.menu) != null || !catalog.menu.isMenu) throw new BuildFailedException("Choose a registered menu scene in the catalog.");
        if (catalog.Problem(catalog.firstArea) != null || catalog.firstArea.isMenu) throw new BuildFailedException("Choose a registered gameplay scene as First Area.");
        var settings = new List<EditorBuildSettingsScene> { new(catalog.menu.Path, true) };
        foreach (var definition in catalog.scenes)
            if (definition != catalog.menu) settings.Add(new EditorBuildSettingsScene(definition.Path, true));
        foreach (var existing in EditorBuildSettings.scenes)
            if (!paths.Contains(existing.path)) settings.Add(existing);
        EditorBuildSettings.scenes = settings.ToArray();
        // Mark only the scene-list settings for Unity's normal project-settings persistence.
        var buildSettings = UnityEngine.Resources.FindObjectsOfTypeAll<EditorBuildSettings>();
        foreach (var settingsObject in buildSettings) EditorUtility.SetDirty(settingsObject);
        var profile = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
        if (profile != null) { EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile); }
    }
    public void OnPreprocessBuild(BuildReport report) => Sync();
}
