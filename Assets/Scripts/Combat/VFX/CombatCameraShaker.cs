using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Camera shake that works with Cinemachine: the brain rewrites the camera transform every LateUpdate, so a tween
    /// on the camera is undone. This runs after the brain (and after the lane/hit-line layout, which samples the
    /// unshaken camera) and adds an offset just before rendering. Two channels add up:
    /// <list type="bullet">
    /// <item><see cref="Shake"/>: a one-shot kick that decays (hits, pops).</item>
    /// <item><see cref="SetSustained"/>: a steady rumble held at a set strength until changed (charging an ability).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class CombatCameraShaker : MonoBehaviour
    {
        private float strength;
        private float duration;
        private float elapsed;
        private float seed;
        private float sustainAmount;
        private float sustainFrequency = 28f;
        private float sustainTime;
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

        /// <summary>
        /// Steady shake of <paramref name="amount"/> world units until called again (0 stops it). Call it every frame
        /// with a growing amount for a charge-up rumble.
        /// </summary>
        public static void SetSustained(Camera camera, float amount, float frequency = 28f)
        {
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            CombatCameraShaker shaker = camera.GetComponent<CombatCameraShaker>();
            if (shaker == null)
            {
                if (amount <= 0f) return;
                shaker = camera.gameObject.AddComponent<CombatCameraShaker>();
            }
            if (shaker.sustainAmount <= 0f && amount > 0f) shaker.sustainTime = Random.value * 50f;
            shaker.sustainAmount = Mathf.Max(0f, amount);
            shaker.sustainFrequency = Mathf.Max(1f, frequency);
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

            float dt = Time.unscaledDeltaTime;
            Vector3 offset = Vector3.zero;
            if (duration > 0f && elapsed < duration)
            {
                elapsed += dt;
                float amount = strength * Decay(Mathf.Clamp01(elapsed / duration));
                float t = elapsed * 25f;
                float x = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f;
                float y = (Mathf.PerlinNoise(seed + 37.1f, t) - 0.5f) * 2f;
                offset += (transform.right * x + transform.up * y) * amount;
            }

            if (sustainAmount > 0f && !GamePause.IsPaused)
            {
                sustainTime += dt;
                float t = sustainTime * sustainFrequency;
                float x = (Mathf.PerlinNoise(t, 11.3f) - 0.5f) * 2f;
                float y = (Mathf.PerlinNoise(71.9f, t) - 0.5f) * 2f;
                offset += (transform.right * x + transform.up * y) * sustainAmount;
            }

            if (offset == Vector3.zero) return;
            lastOffset = offset;
            transform.position += offset;
            lastWrittenPosition = transform.position;
            wrote = true;
        }

        private static float Decay(float t) => (1f - t) * (1f - t);
    }
}
