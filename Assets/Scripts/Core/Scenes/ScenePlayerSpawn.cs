using UnityEngine;
using UnityEngine.SceneManagement;

namespace RythmRPG.Core
{
    [AddComponentMenu("Rythm RPG/Scenes/Scene Player Spawn")]
    public sealed class ScenePlayerSpawn : MonoBehaviour
    {
        [Tooltip("The camera target on this character. If empty, cameras follow the character root.")]
        [SerializeField] private Transform cameraTarget;

        public Transform CameraTarget => cameraTarget != null ? cameraTarget : transform;

        public static void Place(Transform player, Scene scene, string entryPoint, Vector3 fallbackPosition,
            Quaternion fallbackRotation)
        {
            if (player == null) return;
            SceneSpawnPoint match = null;
            bool duplicate = false;
            if (!string.IsNullOrEmpty(entryPoint))
                foreach (var point in FindObjectsByType<SceneSpawnPoint>(FindObjectsInactive.Exclude))
                    if (point.gameObject.scene == scene && point.id == entryPoint)
                    {
                        if (match != null) { duplicate = true; match = null; break; }
                        match = point;
                    }
            if (duplicate) Debug.LogWarning("[Scenes] Duplicate entry point: " + entryPoint + ". Using the area's starting position.");
            else if (match == null && !string.IsNullOrEmpty(entryPoint))
                Debug.LogWarning("[Scenes] Entry point not found: " + entryPoint + ". Using the area's starting position.");
            if (match == null)
                foreach (var point in FindObjectsByType<SceneSpawnPoint>(FindObjectsInactive.Exclude))
                    if (point.gameObject.scene == scene && point.defaultSpawn)
                    {
                        if (match != null) { Debug.LogWarning("[Scenes] Multiple default spawn points in " + scene.name); match = null; break; }
                        match = point;
                    }

            Vector3 position = match != null ? match.transform.position : fallbackPosition;
            Quaternion rotation = match != null ? match.transform.rotation : fallbackRotation;
            var controller = player.GetComponent<CharacterController>();
            bool enabledController = controller != null && controller.enabled;
            if (enabledController) controller.enabled = false;
            Vector3 delta = position - player.position;
            player.SetPositionAndRotation(position, rotation);
            var body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            var body2D = player.GetComponent<Rigidbody2D>();
            if (body2D != null)
            {
                body2D.position = position;
                body2D.linearVelocity = Vector2.zero;
                body2D.angularVelocity = 0;
            }
            if (enabledController) controller.enabled = true;
            Unity.Cinemachine.CinemachineCore.OnTargetObjectWarped(player, delta);
            var marker = player.GetComponent<ScenePlayerSpawn>();
            if (marker != null && marker.CameraTarget != player)
                Unity.Cinemachine.CinemachineCore.OnTargetObjectWarped(marker.CameraTarget, delta);
        }

        // Keep the original scene-only API for area tools that explicitly place a scene-owned character.
        public static void Place(Scene scene, string entryPoint)
        {
            if (string.IsNullOrEmpty(entryPoint)) return;
            Transform player = null;
            foreach (var marker in FindObjectsByType<ScenePlayerSpawn>(FindObjectsInactive.Exclude))
                if (marker.gameObject.scene == scene) { player = marker.transform; break; }
            if (player == null)
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var candidate in root.GetComponentsInChildren<Transform>())
                        if (candidate.CompareTag("Player")) { player = candidate; break; }
            if (player != null) Place(player, scene, entryPoint, player.position, player.rotation);
            else Debug.LogWarning("[Scenes] Add Scene Player Spawn to the destination's player.");
        }
    }
}
