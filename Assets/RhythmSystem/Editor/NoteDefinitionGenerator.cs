using System.IO;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    /// <summary>Creates the default NoteDefinition assets (only missing ones; never overwrites edits).</summary>
    public static class NoteDefinitionGenerator
    {
        public const string Folder = "Assets/Resources/Combat/NoteDefinitions";

        [MenuItem("Tools/Rhythm/Generate Default Note Definitions")]
        public static void Generate()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Combat");
            EnsureFolder(Folder);

            Make(NoteMigration.DefinitionIdNormal, "Normal", NoteBehaviorKind.Moving, "Assets/Prefab/NoteObject.prefab", new Color(0.35f, 0.75f, 1f));
            Make(NoteMigration.DefinitionIdHold, "Hold", NoteBehaviorKind.MovingHold, "Assets/Prefab/HoldNote.prefab", new Color(0.4f, 1f, 0.55f));
            Make(NoteMigration.DefinitionIdStationary, "Stationary", NoteBehaviorKind.Stationary, "Assets/Prefab/Laser.prefab", new Color(1f, 0.4f, 0.4f));
            Make(NoteMigration.DefinitionIdStationaryHold, "Stationary Hold", NoteBehaviorKind.StationaryHold, "Assets/Prefab/Hold Laser.prefab", new Color(1f, 0.6f, 0.3f));
            Make(NoteMigration.DefinitionIdMash, "Mash", NoteBehaviorKind.Mash, "Assets/Prefab/Mash.prefab", new Color(1f, 0.85f, 0.2f));
            Make(NoteMigration.DefinitionIdPong, "Pong", NoteBehaviorKind.Pong, "Assets/Prefab/PongNote.prefab", new Color(0.8f, 0.5f, 1f));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void Make(string id, string name, NoteBehaviorKind kind, string prefabPath, Color color)
        {
            string path = Folder + "/" + id + ".asset";
            if (File.Exists(path)) return;
            NoteDefinition def = ScriptableObject.CreateInstance<NoteDefinition>();
            def.DefinitionId = id;
            def.DisplayName = name;
            def.Behavior = kind;
            def.DisplayColor = color;
            def.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            AssetDatabase.CreateAsset(def, path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
