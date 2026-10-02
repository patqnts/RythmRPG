using UnityEngine;

namespace RythmRPG.Core
{
    [CreateAssetMenu(menuName = "Rythm RPG/Scenes/Scene", fileName = "Scene")]
    public sealed class GameSceneDefinition : ScriptableObject
    {
        [Tooltip("Stable key used by code and dialogue. Keep it unchanged when renaming the scene.")]
        public string id;
        public string displayName;
        public bool isMenu;
        [Tooltip("Entering this scene through an exit or Load(id) begins a fresh run. ReloadCurrent still keeps the current run.")]
        public bool startsNewRun;
#if UNITY_EDITOR
        [Tooltip("Drag the Unity scene here. Its path is stored automatically for builds.")]
        public UnityEditor.SceneAsset scene;
#endif
        [SerializeField, HideInInspector] private string scenePath;
        public string Path => scenePath;
        public string Label => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
#if UNITY_EDITOR
        private void OnValidate() => RefreshPath();
        public void RefreshPath() => scenePath = scene != null ? UnityEditor.AssetDatabase.GetAssetPath(scene) : string.Empty;
#endif
    }
}
