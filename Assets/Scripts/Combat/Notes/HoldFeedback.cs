using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.Combat
{
    /// <summary>Effect slots for hold notes (set on the note prefab). Every slot is optional.</summary>
    [Serializable]
    public sealed class HoldFxSettings
    {
        [Tooltip("Looping effect at the lane's key marker while the note is held (particles, glow...). It is stopped when " +
                 "the hold ends. Empty = built-in sparks (if on).")]
        public GameObject holdingEffectPrefab;
        [Tooltip("Small sparks rising from the key marker while held, when no Holding Effect Prefab is set.")]
        public bool builtInHoldingSparks = true;
        [Tooltip("One-shot effect at the key marker when the hold is completed. Empty = built-in burst.")]
        public GameObject completeEffectPrefab;
        [Tooltip("Moving Hold notes: one-shot effect at the key marker when the note is hit and its head bursts, leaving " +
                 "the tail. Empty = built-in burst.")]
        public GameObject headHitEffectPrefab;
        [Tooltip("Colour the lane's key marker fills with while held. Alpha 0 = automatic: the projectile's own colour.")]
        public Color markerFillColor = new(1f, 1f, 1f, 0f);
        [Tooltip("Seconds before a one-shot prefab effect is removed.")]
        [Min(0.1f)] public float oneShotLifetime = 2f;
    }

    /// <summary>
    /// Runs the "being held" feedback of one note: registers the hold with <see cref="LaneHold"/> (the key marker fills),
    /// spawns the holding effect at the key marker and the one-shot bursts. Safe to end more than once.
    /// </summary>
    public sealed class HoldFeedback
    {
        private Object owner;
        private GameObject holdingInstance;
        private HoldingSparks sparks;
        private HoldFxSettings fx;
        private Color color = Color.white;
        private Transform anchor;
        private bool active;

        public bool IsActive => active;
        public Color Color => color;

        /// <summary>Starts the hold feedback at <paramref name="anchor"/> (the lane target on the hit line).</summary>
        public void Begin(Object noteOwner, int laneId, Transform markerAnchor, Vector3 markerPosition, HoldFxSettings settings,
            Color holdColor)
        {
            End(false, markerPosition);
            owner = noteOwner;
            fx = settings ?? new HoldFxSettings();
            color = holdColor;
            anchor = markerAnchor;
            active = true;
            LaneHold.Begin(owner, laneId, color);

            if (fx.holdingEffectPrefab != null)
            {
                holdingInstance = Object.Instantiate(fx.holdingEffectPrefab, markerPosition, fx.holdingEffectPrefab.transform.rotation);
                if (anchor != null) holdingInstance.transform.SetParent(anchor, true);
            }
            else if (fx.builtInHoldingSparks)
            {
                sparks = HoldingSparks.Create(anchor, markerPosition, color);
            }
        }

        /// <summary>Stops the hold feedback; a completed hold also bursts.</summary>
        public void End(bool completed, Vector3 markerPosition)
        {
            if (!active) return;
            active = false;
            LaneHold.End(owner);
            if (anchor != null) markerPosition = anchor.position;

            if (holdingInstance != null)
            {
                foreach (ParticleSystem system in holdingInstance.GetComponentsInChildren<ParticleSystem>())
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                foreach (TrailRenderer trail in holdingInstance.GetComponentsInChildren<TrailRenderer>()) trail.emitting = false;
                holdingInstance.transform.SetParent(null, true);
                Object.Destroy(holdingInstance, 1.5f);
                holdingInstance = null;
            }
            if (sparks != null)
            {
                sparks.Stop();
                sparks = null;
            }

            if (completed) OneShot(fx != null ? fx.completeEffectPrefab : null, markerPosition, color, 0.55f, 12, fx);
        }

        /// <summary>A prefab one-shot, or the built-in spark burst without one.</summary>
        public static void OneShot(GameObject prefab, Vector3 position, Color color, float size, int sparkCount, HoldFxSettings settings)
        {
            if (prefab != null)
            {
                GameObject instance = Object.Instantiate(prefab, position, prefab.transform.rotation);
                Object.Destroy(instance, settings != null ? settings.oneShotLifetime : 2f);
                return;
            }
            CastVisuals.Burst(position, null, color, size, sparkCount, 0.4f, ViewCamera());
        }

        private static Camera cachedCamera;

        /// <summary>The camera the combat is rendered with (for camera-facing sprites).</summary>
        public static Camera ViewCamera()
        {
            if (cachedCamera != null && cachedCamera.isActiveAndEnabled) return cachedCamera;
            CombatLanePresentation3D lanes = Object.FindAnyObjectByType<CombatLanePresentation3D>();
            cachedCamera = lanes != null && lanes.RenderCamera != null ? lanes.RenderCamera : Camera.main;
            return cachedCamera;
        }
    }

    /// <summary>Built-in holding effect: small sparks rising from the key marker, in the hold colour.</summary>
    public sealed class HoldingSparks : MonoBehaviour
    {
        private Color color;
        private float carry;
        private bool stopping;
        private Camera viewCamera;

        public static HoldingSparks Create(Transform anchor, Vector3 position, Color color)
        {
            var go = new GameObject("Holding Sparks");
            go.transform.position = position;
            if (anchor != null) go.transform.SetParent(anchor, true);
            HoldingSparks sparks = go.AddComponent<HoldingSparks>();
            sparks.color = color;
            sparks.viewCamera = HoldFeedback.ViewCamera();
            return sparks;
        }

        public void Stop()
        {
            stopping = true;
            Destroy(gameObject);
        }

        private void Update()
        {
            if (stopping) return;
            carry += Time.deltaTime * 22f;
            Vector3 up = CastVisuals.ScreenUp(viewCamera);
            Vector3 right = CastVisuals.ScreenRight(viewCamera);
            while (carry >= 1f)
            {
                carry -= 1f;
                Vector3 offset = right * UnityEngine.Random.Range(-0.28f, 0.28f);
                Vector3 velocity = up * UnityEngine.Random.Range(1.2f, 2.4f) + right * UnityEngine.Random.Range(-0.3f, 0.3f);
                CastParticle.Spawn(transform.position + offset, velocity, CastVisuals.GlowSprite,
                    CastVisuals.Brighten(color, UnityEngine.Random.Range(0.1f, 0.6f)), UnityEngine.Random.Range(0.08f, 0.14f),
                    0.02f, UnityEngine.Random.Range(0.25f, 0.4f), 2f, viewCamera, 3);
            }
        }
    }

    /// <summary>Finds the colour of a projectile from its art, so effects and the key marker can match it.</summary>
    public static class ProjectileColor
    {
        private static readonly Dictionary<Sprite, Color> spriteCache = new();

        /// <summary>
        /// <paramref name="preferred"/> when its alpha is above 0; otherwise the note's own colour: a tinted sprite's tint,
        /// else the dominant colour of its sprite art, else a particle start colour, else <paramref name="fallback"/>.
        /// </summary>
        public static Color Resolve(Component root, IEnumerable<Renderer> exclude, Color preferred, Color fallback)
        {
            if (preferred.a > 0.001f) return new Color(preferred.r, preferred.g, preferred.b, 1f);
            if (root == null) return fallback;
            var skip = exclude != null ? new HashSet<Renderer>(exclude) : new HashSet<Renderer>();

            SpriteRenderer best = null;
            float bestArea = 0f;
            foreach (SpriteRenderer sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite == null || sprite.sprite == null || skip.Contains(sprite)) continue;
                if (IsTinted(sprite.color)) return Opaque(sprite.color);
                float area = sprite.bounds.size.x * sprite.bounds.size.y;
                if (best == null || area > bestArea)
                {
                    best = sprite;
                    bestArea = area;
                }
            }
            if (best != null && TrySpriteColor(best.sprite, out Color art)) return art;

            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system == null || skip.Contains(system.GetComponent<Renderer>())) continue;
                Color start = system.main.startColor.color;
                if (IsTinted(start)) return Opaque(start);
            }
            return fallback;
        }

        private static bool IsTinted(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            return c.a > 0.05f && (s > 0.15f || v < 0.85f);
        }

        private static Color Opaque(Color c) => new(c.r, c.g, c.b, 1f);

        /// <summary>Dominant colour of the sprite's pixels (weighted by opacity and saturation), read back through the
        /// GPU so the texture need not be Read/Write enabled. Cached per sprite.</summary>
        public static bool TrySpriteColor(Sprite sprite, out Color color)
        {
            color = Color.white;
            if (sprite == null || sprite.texture == null) return false;
            if (spriteCache.TryGetValue(sprite, out color)) return true;

            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            Texture2D read = null;
            try
            {
                Texture2D texture = sprite.texture;
                Rect rect = sprite.textureRect;
                int w = Mathf.Clamp(Mathf.RoundToInt(rect.width), 1, 64);
                int h = Mathf.Clamp(Mathf.RoundToInt(rect.height), 1, 64);
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Vector2 scale = new(rect.width / texture.width, rect.height / texture.height);
                Vector2 offset = new(rect.x / texture.width, rect.y / texture.height);
                Graphics.Blit(texture, rt, scale, offset);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                read.Apply(false);

                float r = 0f, g = 0f, b = 0f, total = 0f;
                foreach (Color32 pixel in read.GetPixels32())
                {
                    if (pixel.a < 16) continue;
                    Color c = pixel;
                    Color.RGBToHSV(c, out _, out float s, out float v);
                    float weight = c.a * (0.15f + s) * (0.3f + v);
                    r += c.r * weight;
                    g += c.g * weight;
                    b += c.b * weight;
                    total += weight;
                }
                if (total <= 0.0001f) return false;
                Color average = new(r / total, g / total, b / total, 1f);
                Color.RGBToHSV(average, out float hue, out float sat, out float val);
                color = Color.HSVToRGB(hue, Mathf.Clamp01(sat * 1.15f), Mathf.Max(val, 0.85f));
                spriteCache[sprite] = color;
                return true;
            }
            catch (Exception)
            {
                return false; // tightly packed atlas sprites, headless GPU...: fall back
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.Destroy(read);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => spriteCache.Clear();
    }
}
