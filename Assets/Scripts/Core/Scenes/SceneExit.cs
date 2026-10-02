using UnityEngine;
using UnityEngine.Events;

namespace RythmRPG.Core
{
    [AddComponentMenu("Rythm RPG/Scenes/Scene Exit")]
    public sealed class SceneExit : MonoBehaviour
    {
        [Tooltip("Select a scene from the catalog. Load() can also be called by a UI button or dialogue UnityEvent.")]
        public GameSceneDefinition destination;
        [Tooltip("ID of a Scene Spawn Point in the destination. Empty uses its authored player position.")]
        public string entryPoint;
        public bool loadOnPlayerEnter;
        public bool requireInteract = true;
        [Tooltip("If gameplay is temporarily blocking travel (for example, this is called from a dialogue entry), " +
                 "wait and load as soon as the blocker ends.")]
        public bool waitUntilAvailable = true;
        public UnityEvent<string> onRejected = new();

        public void Load()
        {
            var loader = GameSceneLoader.Ensure();
            var request = loader.TravelRequest(destination, entryPoint);
            bool accepted = waitUntilAvailable ? loader.RequestWhenAvailable(request) : loader.Request(request);
            if (!accepted) onRejected.Invoke(loader.LastError);
        }
        private void OnTriggerEnter(Collider other) { if (loadOnPlayerEnter && !requireInteract && IsPlayer(other)) Load(); }
        private void OnTriggerStay(Collider other) { if (loadOnPlayerEnter && requireInteract && GameInput.InteractPressed && IsPlayer(other)) Load(); }
        private void OnTriggerEnter2D(Collider2D other) { if (loadOnPlayerEnter && !requireInteract && IsPlayer(other)) Load(); }
        private void OnTriggerStay2D(Collider2D other) { if (loadOnPlayerEnter && requireInteract && GameInput.InteractPressed && IsPlayer(other)) Load(); }
        private static bool IsPlayer(Component other) => other.GetComponentInParent<ScenePlayerSpawn>() != null || other.transform.root.CompareTag("Player") || other.CompareTag("Player");
    }
}
