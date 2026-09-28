using System.IO;
using RythmRPG.Combat;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds example ground-moving note prefabs (copies; the originals are not touched) with <see cref="NoteGrounding"/>
/// set up: a crawling rat, a rolling ball, a ball bouncing on the beat, a flat ground wave and a thrown note with a
/// ground shadow.
/// </summary>
public static class GroundNoteExamples
{
    private const string Folder = "Assets/Prefab/Ground Notes";
    private const string RatSource = "Assets/Prefab/Rat Note.prefab";
    private const string BallSource = "Assets/Prefab/NoteObject.prefab";

    [MenuItem("Tools/Rythm RPG/Combat/Create Ground Note Examples")]
    public static void Create()
    {
        Directory.CreateDirectory(Folder);
        int made = 0;
        made += Make(RatSource, "Rat Note (Crawl)", NoteGrounding.Preset.Crawl) ? 1 : 0;
        made += Make(BallSource, "Ball Note (Roll)", NoteGrounding.Preset.Roll) ? 1 : 0;
        made += Make(BallSource, "Ball Note (Bounce)", NoteGrounding.Preset.Bounce) ? 1 : 0;
        made += Make(BallSource, "Ground Wave Note", NoteGrounding.Preset.GroundWave) ? 1 : 0;
        made += Make(BallSource, "Thrown Note (Shadow)", NoteGrounding.Preset.ThrownWithShadow) ? 1 : 0;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Ground Notes] {made} example prefab(s) in {Folder}. Swap in your own sprites / animations; " +
                  "right-click Note Grounding for other presets.");
        Object folder = AssetDatabase.LoadAssetAtPath<Object>(Folder);
        if (folder != null) EditorGUIUtility.PingObject(folder);
    }

    private static bool Make(string sourcePath, string name, NoteGrounding.Preset preset)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
        {
            Debug.LogWarning($"[Ground Notes] Source prefab not found: {sourcePath}");
            return false;
        }
        string path = $"{Folder}/{name}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            Debug.Log($"[Ground Notes] {path} already exists; left as it is.");
            return false;
        }
        if (!AssetDatabase.CopyAsset(sourcePath, path)) return false;

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            NoteGrounding grounding = root.GetComponent<NoteGrounding>();
            if (grounding == null) grounding = root.AddComponent<NoteGrounding>();
            Transform visual = root.transform.Find("Square");
            if (visual == null)
            {
                SpriteRenderer first = root.GetComponentInChildren<SpriteRenderer>(true);
                if (first != null) visual = first.transform;
            }
            grounding.Visual = visual;
            grounding.ApplyPreset(preset);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return true;
    }
}
