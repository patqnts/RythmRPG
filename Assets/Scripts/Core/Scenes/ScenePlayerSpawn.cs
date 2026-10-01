using UnityEngine;
using UnityEngine.SceneManagement;

namespace RythmRPG.Core
{
    [AddComponentMenu("Rythm RPG/Scenes/Scene Player Spawn")]
    public sealed class ScenePlayerSpawn : MonoBehaviour
    {
        public static void Place(Scene scene, string entryPoint)
        {
            if (string.IsNullOrEmpty(entryPoint)) return;
            SceneSpawnPoint match = null;
            foreach (var point in FindObjectsByType<SceneSpawnPoint>(FindObjectsInactive.Exclude))
                if (point.gameObject.scene == scene && point.id == entryPoint)
                {
                    if (match != null) { Debug.LogWarning("[Scenes] Duplicate entry point: " + entryPoint); return; }
                    match = point;
                }
            if (match == null) { Debug.LogWarning("[Scenes] Entry point not found: " + entryPoint + ". Keeping authored player position."); return; }
            Transform player = null;
            foreach (var candidate in FindObjectsByType<ScenePlayerSpawn>(FindObjectsInactive.Exclude))
                if (candidate.gameObject.scene == scene) { player = candidate.transform; break; }
            if (player == null)
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var candidate in root.GetComponentsInChildren<Transform>())
                        if (candidate.CompareTag("Player")) { player = candidate; break; }
            if (player == null) { Debug.LogWarning("[Scenes] Add Scene Player Spawn to the destination's player."); return; }
            var controller = player.GetComponent<CharacterController>();
            bool enabledController = controller != null && controller.enabled;
            if (enabledController) controller.enabled = false;
            Vector3 delta = match.transform.position - player.position;
            player.SetPositionAndRotation(match.transform.position, match.transform.rotation);
            var body = player.GetComponent<Rigidbody>();
            if (body != null) { body.position = player.position; body.rotation = player.rotation; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            var body2D = player.GetComponent<Rigidbody2D>();
            if (body2D != null) { body2D.position = player.position; body2D.linearVelocity = Vector2.zero; body2D.angularVelocity = 0; }
            if (enabledController) controller.enabled = true;
            Unity.Cinemachine.CinemachineCore.OnTargetObjectWarped(player, delta);
        }
    }
}
