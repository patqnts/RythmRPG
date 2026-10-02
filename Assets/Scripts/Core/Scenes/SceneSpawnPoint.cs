using UnityEngine;

namespace RythmRPG.Core
{
    [AddComponentMenu("Rythm RPG/Scenes/Scene Spawn Point")]
    public sealed class SceneSpawnPoint : MonoBehaviour
    {
        [Tooltip("An exit's Entry Point uses this ID. Each entry ID must be unique within this scene.")]
        public string id;
        [Tooltip("Use this position when entering this area without an Entry Point and without a placed character.")]
        public bool defaultSpawn;
        private void OnDrawGizmos() { Gizmos.color = Color.white; Gizmos.DrawWireSphere(transform.position, .35f); Gizmos.DrawRay(transform.position, transform.forward); }
    }
}
