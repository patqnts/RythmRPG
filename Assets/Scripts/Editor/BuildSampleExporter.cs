using System.IO;
using System.Linq;
using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// Saves the code-defined sample build content (<see cref="SampleBuildLibrary"/>) as editable assets under
    /// Assets/Resources/Combat/Build/Samples, plus a catalog the runtime registry loads. Exported assets take priority
    /// over the code definitions (same ids), so you can tune numbers in the inspector afterwards.
    /// </summary>
    public static class BuildSampleExporter
    {
        private const string Root = "Assets/Resources/Combat/Build";
        private const string Folder = Root + "/Samples";

        [MenuItem("Tools/Rythm RPG/Combat/Build/Export Sample Build Content")]
        public static void Export()
        {
            if (Directory.Exists(Folder) && !EditorUtility.DisplayDialog("Export sample build content",
                    "Overwrite the assets in " + Folder + "? Edits made to them will be lost.", "Overwrite", "Cancel"))
                return;

            SampleBuildLibrary.Content content = SampleBuildLibrary.CreateFresh();
            Directory.CreateDirectory(Folder + "/Abilities");
            Directory.CreateDirectory(Folder + "/Upgrades");
            Directory.CreateDirectory(Folder + "/Passives");
            Directory.CreateDirectory(Folder + "/Presets");
            AssetDatabase.Refresh();

            foreach (AbilityDefinition ability in content.Abilities.Values) Save(ability, Folder + "/Abilities/" + ability.Id + ".asset");
            foreach (AbilityUpgradeDefinition upgrade in content.Upgrades.Values) Save(upgrade, Folder + "/Upgrades/" + upgrade.Id + ".asset");
            foreach (PassiveDefinition passive in content.Passives.Values) Save(passive, Folder + "/Passives/" + passive.Id + ".asset");
            foreach (BuildPreset preset in content.Presets) Save(preset, Folder + "/Presets/" + preset.Id + ".asset");

            BuildContentCatalog catalog = ScriptableObject.CreateInstance<BuildContentCatalog>();
            catalog.Set(content.Abilities.Values, content.Upgrades.Values, content.Passives.Values, content.Presets);
            Save(catalog, Root + "/SampleBuildCatalog.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            BuildContentRegistry.Reset();
            Selection.activeObject = catalog;
            Debug.Log($"[Build] Exported {content.Abilities.Count} abilities, {content.Upgrades.Count} upgrades, " +
                      $"{content.Passives.Count} passives and {content.Presets.Count} presets to {Folder}.");
        }

        [MenuItem("Tools/Rythm RPG/Combat/Build/Create Build Balance Rules Asset")]
        public static void CreateRules()
        {
            string path = "Assets/Resources/" + BuildBalanceRules.ResourcePath + ".asset";
            if (AssetDatabase.LoadAssetAtPath<BuildBalanceRules>(path) != null)
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<BuildBalanceRules>(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets/Resources");
            BuildBalanceRules rules = ScriptableObject.CreateInstance<BuildBalanceRules>();
            AssetDatabase.CreateAsset(rules, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = rules;
        }

        private static void Save(Object asset, string path)
        {
            asset.hideFlags = HideFlags.None;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }
    }
}
