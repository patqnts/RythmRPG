using UnityEditor;
using UnityEngine;

public static class ScreenAlignmentTools
{
    private const string Root = "GameObject/Rhythm RPG/";

    [MenuItem(Root + "Face Main Camera", false, 20)]
    private static void FaceMainCamera()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        Apply("Face Main Camera", transform =>
        {
            Vector3 forward = transform.position - camera.transform.position;
            if (forward.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        });
    }

    [MenuItem(Root + "Lay Flat on XZ", false, 21)]
    private static void LayFlat() => Apply("Lay Flat on XZ",
        transform => transform.rotation = Quaternion.Euler(-90f, 0f, 0f));

    [MenuItem(Root + "Level World Axes", false, 22)]
    private static void LevelWorldAxes() => Apply("Level World Axes",
        transform => transform.rotation = Quaternion.identity);

    [MenuItem(Root + "Face Main Camera", true)]
    [MenuItem(Root + "Lay Flat on XZ", true)]
    [MenuItem(Root + "Level World Axes", true)]
    private static bool ValidateSelection() => Selection.transforms.Length > 0;

    private static void Apply(string undoName, System.Action<Transform> operation)
    {
        Transform[] selection = Selection.transforms;
        Undo.RecordObjects(selection, undoName);
        foreach (Transform transform in selection)
        {
            operation(transform);
            EditorUtility.SetDirty(transform);
        }
    }
}
