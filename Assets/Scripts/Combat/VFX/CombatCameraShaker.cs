using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Camera shake that works with Cinemachine: the brain rewrites the camera transform every LateUpdate, so a tween
    /// on the camera is undone. This runs after the brain (and after the lane/hit-line layout, which samples the
    /// unshaken camera) and adds a decaying offset just before rendering.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class CombatCameraShaker : MonoBehaviour
    {
        private float strength;
        private float duration;
        private float elapsed;
        private float seed;
        private Vector3 lastOffset;
        private Vector3 lastWrittenPosition;
        private bool wrote;

        /// <summary>Shakes <paramref name="camera"/> (Camera.main when null). Stronger/longer requests override weaker ones.</summary>
        public static void Shake(Camera camera, float strength, float seconds)
        {
            if (camera == null) camera = Camera.main;
            if (camera == null || strength <= 0f || seconds <= 0f) return;
            CombatCameraShaker shaker = camera.GetComponent<CombatCameraShaker>();
            if (shaker == null) shaker = camera.gameObject.AddComponent<CombatCameraShaker>();
            shaker.Begin(strength, seconds);
        }

        private void Begin(float amount, float seconds)
        {
            float remaining = Mathf.Max(0f, duration - elapsed);
            float current = duration > 0f ? strength * Decay(elapsed / duration) : 0f;
            if (amount < current && seconds < remaining) return;
            strength = Mathf.Max(amount, current);
            duration = Mathf.Max(seconds, remaining);
            elapsed = 0f;
            seed = Random.value * 100f;
        }

        private void LateUpdate()
        {
            // Without a brain resetting the camera, undo our previous offset first.
            if (wrote && transform.position == lastWrittenPosition) transform.position -= lastOffset;
            wrote = false;
            if (duration <= 0f || elapsed >= duration) return;

            elapsed += Time.unscaledDeltaTime;
            float amount = strength * Decay(Mathf.Clamp01(elapsed / duration));
            float t = elapsed * 25f;
            float x = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(seed + 37.1f, t) - 0.5f) * 2f;
            lastOffset = (transform.right * x + transform.up * y) * amount;
            transform.position += lastOffset;
            lastWrittenPosition = transform.position;
            wrote = true;
        }

        private static float Decay(float t) => (1f - t) * (1f - t);
    }
}
