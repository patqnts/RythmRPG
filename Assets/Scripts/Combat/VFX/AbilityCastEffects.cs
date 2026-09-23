using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Small helpers shared by the ability cast effects: a pixel glow sprite, camera facing, sparks.</summary>
    public static class CastVisuals
    {
        public const int SortingOrder = 505;
        private static Sprite glowSprite;

        /// <summary>A 16 px round glow with stepped (pixel-art) falloff, white so it can be tinted.</summary>
        public static Sprite GlowSprite
        {
            get
            {
                if (glowSprite != null) return glowSprite;
                const int size = 16;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "Cast Glow"
                };
                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / (size * 0.5f);
                        float alpha = Mathf.Clamp01(1f - distance);
                        alpha = Mathf.Floor(alpha * alpha * 5f) / 4f; // 4 hard steps: reads as pixel art
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha)));
                    }
                }
                texture.Apply();
                glowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                glowSprite.name = "Cast Glow";
                return glowSprite;
            }
        }

        public static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite, Color color, int orderOffset = 0)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = SortingOrder + orderOffset;
            return renderer;
        }

        public static Vector3 ScreenUp(Camera camera) => camera != null ? camera.transform.up : Vector3.up;
        public static Vector3 ScreenRight(Camera camera) => camera != null ? camera.transform.right : Vector3.right;

        public static Color Brighten(Color color, float towardWhite) =>
            Color.Lerp(color, Color.white, Mathf.Clamp01(towardWhite));

        /// <summary>Spawns a prefab (auto-destroyed after <paramref name="lifetime"/>) or, without one, built-in sparks.</summary>
        public static void Burst(Vector3 position, GameObject prefab, Color color, float size, int sparks, float lifetime, Camera camera)
        {
            if (prefab != null)
            {
                GameObject instance = Object.Instantiate(prefab, position, prefab.transform.rotation);
                Object.Destroy(instance, Mathf.Max(0.5f, lifetime));
                return;
            }

            // Flash + sparks flying out in the screen plane.
            CastParticle.Spawn(position, Vector3.zero, GlowSprite, Brighten(color, 0.6f), size * 0.6f, size * 2.6f,
                Mathf.Max(0.1f, lifetime * 0.8f), 0f, camera, 2);
            Vector3 right = ScreenRight(camera), up = ScreenUp(camera);
            for (int i = 0; i < sparks; i++)
            {
                float angle = (i + Random.value * 0.6f) / sparks * Mathf.PI * 2f;
                Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                float speed = Random.Range(2.5f, 5.5f) * size;
                CastParticle.Spawn(position, direction * speed, GlowSprite, Brighten(color, Random.value * 0.5f),
                    size * Random.Range(0.18f, 0.3f), 0.02f, Random.Range(0.25f, 0.45f), 6f, camera, 1);
            }
        }
    }

    /// <summary>A self-destroying, camera-facing sprite that moves, slows, shrinks and fades. Used for motes, trail
    /// ghosts, sparks and flashes, so the built-in effects need no particle materials (safe in any render pipeline).</summary>
    public sealed class CastParticle : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;
        private Vector3 velocity;
        private float drag;
        private float life;
        private float age;
        private float startSize;
        private float endSize;
        private Color color;
        private Camera viewCamera;

        public static CastParticle Spawn(Vector3 position, Vector3 velocity, Sprite sprite, Color color, float startSize,
            float endSize, float life, float drag, Camera camera, int orderOffset = 0)
        {
            var go = new GameObject("Cast Particle");
            go.transform.position = position;
            CastParticle particle = go.AddComponent<CastParticle>();
            particle.spriteRenderer = go.AddComponent<SpriteRenderer>();
            particle.spriteRenderer.sprite = sprite;
            particle.spriteRenderer.color = color;
            particle.spriteRenderer.sortingOrder = CastVisuals.SortingOrder + orderOffset;
            particle.velocity = velocity;
            particle.color = color;
            particle.startSize = startSize;
            particle.endSize = endSize;
            particle.life = Mathf.Max(0.01f, life);
            particle.drag = drag;
            particle.viewCamera = camera;
            particle.LateUpdate();
            return particle;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            age += dt;
            float t = Mathf.Clamp01(age / life);
            velocity *= Mathf.Max(0f, 1f - drag * dt);
            transform.position += velocity * dt;
            transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, t);
            spriteRenderer.color = new Color(color.r, color.g, color.b, color.a * (1f - t * t));
            if (viewCamera != null) transform.rotation = viewCamera.transform.rotation;
            if (age >= life) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Charge-up around the player while an ability key is held. With a prefab its particle systems emit more as the hold
    /// fills; without one, built-in motes gather from a ring into a growing, pulsing core.
    /// </summary>
    public sealed class AbilityChargeEffect : MonoBehaviour
    {
        private enum State { Charging, Released, Cancelled }

        private struct Mote
        {
            public SpriteRenderer Renderer;
            public Vector3 From;
            public float Age;
            public float Life;
            public float Size;
            public bool Alive;
        }

        private const int MaxMotes = 48;
        private Transform follow;
        private Vector3 offset;
        private Camera viewCamera;
        private Color color;
        private float size;
        private float progress;
        private State state;
        private float stateTime;

        private ParticleSystem[] systems;
        private float[] baseRates;
        private SpriteRenderer core;
        private SpriteRenderer halo;
        private readonly List<Mote> motes = new();
        private float spawnCarry;

        public Vector3 CorePosition => transform.position;

        public static AbilityChargeEffect Create(Transform follow, Vector3 offset, GameObject prefab, Color color, float size, Camera camera)
        {
            var go = new GameObject("Ability Charge");
            AbilityChargeEffect effect = go.AddComponent<AbilityChargeEffect>();
            effect.follow = follow;
            effect.offset = offset;
            effect.viewCamera = camera;
            effect.color = color;
            effect.size = Mathf.Max(0.05f, size);
            go.transform.position = follow != null ? follow.position + offset : offset;

            if (prefab != null)
            {
                GameObject instance = Instantiate(prefab, go.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localScale = prefab.transform.localScale * effect.size;
                effect.systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                effect.baseRates = new float[effect.systems.Length];
                for (int i = 0; i < effect.systems.Length; i++)
                    effect.baseRates[i] = effect.systems[i].emission.rateOverTimeMultiplier;
            }
            else
            {
                effect.halo = CastVisuals.AddSprite(go.transform, "Halo", CastVisuals.GlowSprite, color, 0);
                effect.core = CastVisuals.AddSprite(go.transform, "Core", CastVisuals.GlowSprite, CastVisuals.Brighten(color, 0.5f), 2);
            }

            effect.SetProgress(0f);
            return effect;
        }

        /// <summary>0..1 hold progress.</summary>
        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            if (systems == null) return;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                ParticleSystem.EmissionModule emission = systems[i].emission;
                emission.rateOverTimeMultiplier = baseRates[i] * Mathf.Lerp(0.3f, 1.3f, progress);
            }
        }

        /// <summary>Hold completed: flash and let the gathered energy go (the wisp takes over).</summary>
        public void Release() => End(State.Released);

        /// <summary>Key released early: fade away.</summary>
        public void Cancel() => End(State.Cancelled);

        private void End(State next)
        {
            if (state != State.Charging) return;
            state = next;
            stateTime = 0f;
            if (systems != null)
                foreach (ParticleSystem system in systems)
                    if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, systems != null ? 1.5f : 0.6f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            stateTime += dt;
            if (follow != null) transform.position = follow.position + offset;
            if (systems != null) return;

            if (viewCamera != null)
            {
                core.transform.rotation = viewCamera.transform.rotation;
                halo.transform.rotation = viewCamera.transform.rotation;
            }

            // Core: grows and pulses faster as the hold fills; flashes on release, fades on cancel.
            float pulse = 1f + Mathf.Sin(Time.time * Mathf.Lerp(7f, 20f, progress)) * Mathf.Lerp(0.06f, 0.16f, progress);
            float coreSize = size * Mathf.Lerp(0.22f, 0.7f, progress) * pulse;
            float coreAlpha = Mathf.Lerp(0.45f, 1f, progress);
            float haloAlpha = 0.3f * progress;
            if (state == State.Released)
            {
                float t = Mathf.Clamp01(stateTime / 0.25f);
                coreSize *= Mathf.Lerp(1.6f, 2.4f, t);
                coreAlpha *= 1f - t;
                haloAlpha *= 1f - t;
            }
            else if (state == State.Cancelled)
            {
                float t = Mathf.Clamp01(stateTime / 0.2f);
                coreSize *= 1f - t;
                coreAlpha *= 1f - t;
                haloAlpha *= 1f - t;
            }
            core.transform.localScale = Vector3.one * coreSize;
            core.color = WithAlpha(CastVisuals.Brighten(color, 0.3f + 0.5f * progress), coreAlpha);
            halo.transform.localScale = Vector3.one * coreSize * 2.4f;
            halo.color = WithAlpha(color, haloAlpha);

            // Motes: spawn on a ring around the player (in the screen plane) and rush into the core.
            if (state == State.Charging)
            {
                spawnCarry += dt * Mathf.Lerp(10f, 48f, progress);
                while (spawnCarry >= 1f)
                {
                    spawnCarry -= 1f;
                    SpawnMote();
                }
            }
            UpdateMotes(dt);
        }

        private void SpawnMote()
        {
            int index = motes.FindIndex(m => !m.Alive);
            if (index < 0)
            {
                if (motes.Count >= MaxMotes) return;
                motes.Add(new Mote { Renderer = CastVisuals.AddSprite(transform, "Mote", CastVisuals.GlowSprite, color, 1) });
                index = motes.Count - 1;
            }

            float angle = Random.value * Mathf.PI * 2f;
            Vector3 direction = CastVisuals.ScreenRight(viewCamera) * Mathf.Cos(angle) + CastVisuals.ScreenUp(viewCamera) * Mathf.Sin(angle);
            Mote mote = motes[index];
            mote.From = direction * size * Random.Range(0.8f, 1.25f);
            mote.Age = 0f;
            mote.Life = Mathf.Lerp(0.55f, 0.3f, progress) * Random.Range(0.85f, 1.15f);
            mote.Size = size * Random.Range(0.1f, 0.2f);
            mote.Alive = true;
            mote.Renderer.enabled = true;
            motes[index] = mote;
        }

        private void UpdateMotes(float dt)
        {
            for (int i = 0; i < motes.Count; i++)
            {
                Mote mote = motes[i];
                if (!mote.Alive) continue;
                mote.Age += dt;
                float t = Mathf.Clamp01(mote.Age / mote.Life);
                float eased = t * t; // accelerate into the core
                Transform moteTransform = mote.Renderer.transform;
                moteTransform.localPosition = Vector3.Lerp(mote.From, Vector3.zero, eased);
                moteTransform.localScale = Vector3.one * mote.Size * Mathf.Lerp(1f, 0.4f, t);
                if (viewCamera != null) moteTransform.rotation = viewCamera.transform.rotation;
                float alpha = Mathf.Min(1f, t / 0.2f);
                if (state == State.Cancelled) alpha *= 1f - Mathf.Clamp01(stateTime / 0.2f);
                mote.Renderer.color = WithAlpha(CastVisuals.Brighten(color, t * 0.7f), alpha);
                if (t >= 1f)
                {
                    mote.Alive = false;
                    mote.Renderer.enabled = false;
                }
                motes[i] = mote;
            }
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, Mathf.Clamp01(a));
    }

    /// <summary>
    /// The ability as a wisp: forms from its icon, arcs to centre stage leaving an afterimage trail, floats there
    /// (pulsing harder as the pop nears) and pops. With a prefab, the prefab is moved along the same path.
    /// </summary>
    public sealed class AbilityWisp : MonoBehaviour
    {
        private Camera viewCamera;
        private Color color;
        private float size;
        private GameObject instance;
        private SpriteRenderer glow;
        private SpriteRenderer core;
        private SpriteRenderer icon;
        private float morph = 1f; // 1 = still the icon, 0 = fully a wisp
        private float pulse = 1f;
        private float trailCarry;
        private bool trailOn;

        public static AbilityWisp Create(Vector3 position, GameObject prefab, Sprite iconSprite, bool morphFromIcon,
            Color color, float size, Camera camera)
        {
            var go = new GameObject("Ability Wisp");
            go.transform.position = position;
            AbilityWisp wisp = go.AddComponent<AbilityWisp>();
            wisp.viewCamera = camera;
            wisp.color = color;
            wisp.size = Mathf.Max(0.05f, size);

            if (prefab != null)
            {
                wisp.instance = Instantiate(prefab, go.transform);
                wisp.instance.transform.localPosition = Vector3.zero;
            }
            else
            {
                wisp.glow = CastVisuals.AddSprite(go.transform, "Glow", CastVisuals.GlowSprite, color, 0);
                wisp.core = CastVisuals.AddSprite(go.transform, "Core", CastVisuals.GlowSprite, CastVisuals.Brighten(color, 0.75f), 2);
            }

            if (morphFromIcon && iconSprite != null)
                wisp.icon = CastVisuals.AddSprite(go.transform, "Icon", iconSprite, Color.white, 3);
            else
                wisp.morph = 0f;

            wisp.LateUpdate();
            Destroy(go, 10f); // safety net if the cast is interrupted before it pops
            return wisp;
        }

        /// <summary>Arcs (screen-up) from the current position to <paramref name="destination"/>.</summary>
        public IEnumerator Fly(Vector3 destination, float seconds, float arcHeight)
        {
            Vector3 from = transform.position;
            Vector3 up = CastVisuals.ScreenUp(viewCamera);
            float duration = Mathf.Max(0.05f, seconds);
            trailOn = true;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                morph = 1f - Mathf.Clamp01(t / 0.35f);
                transform.position = Vector3.Lerp(from, destination, eased) + up * (arcHeight * 4f * t * (1f - t));
                yield return null;
            }
            morph = 0f;
            transform.position = destination;
            trailOn = false;
        }

        /// <summary>Floats in place at least <paramref name="minSeconds"/> and until the audio clock reaches
        /// <paramref name="popDspTime"/> (capped at 4 s), pulsing harder as the pop nears.</summary>
        public IEnumerator HoverUntil(double popDspTime, float minSeconds)
        {
            Vector3 anchor = transform.position;
            Vector3 up = CastVisuals.ScreenUp(viewCamera);
            float elapsed = 0f;
            while (elapsed < minSeconds || (AudioSettings.dspTime < popDspTime && elapsed < 4f))
            {
                elapsed += Time.deltaTime;
                float remaining = (float)(popDspTime - AudioSettings.dspTime);
                float urgency = 1f - Mathf.Clamp01(remaining / 0.6f);
                pulse = 1f + Mathf.Sin(elapsed * Mathf.Lerp(10f, 30f, urgency)) * Mathf.Lerp(0.08f, 0.22f, urgency)
                        + urgency * 0.25f;
                transform.position = anchor + up * (Mathf.Sin(elapsed * 5f) * 0.05f * (1f - urgency));
                yield return null;
            }
            transform.position = anchor;
        }

        /// <summary>Bursts (prefab or built-in sparks) and removes the wisp.</summary>
        public void Pop(GameObject popPrefab, float popSeconds)
        {
            CastVisuals.Burst(transform.position, popPrefab, color, size, 14, Mathf.Max(0.2f, popSeconds + 0.2f), viewCamera);
            if (instance != null)
            {
                // Let trails and particles finish instead of cutting them off.
                foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>())
                    system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                foreach (TrailRenderer trail in instance.GetComponentsInChildren<TrailRenderer>()) trail.emitting = false;
                foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
                    if (r is SpriteRenderer || r is MeshRenderer) r.enabled = false;
                transform.DetachChildren();
                Destroy(instance, 1.5f);
            }
            Destroy(gameObject);
        }

        private void LateUpdate()
        {
            Quaternion facing = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
            if (glow != null)
            {
                float wispScale = size * Mathf.Lerp(1f, 0.35f, morph) * pulse;
                glow.transform.localScale = Vector3.one * wispScale * 1.8f;
                glow.transform.rotation = facing;
                glow.color = new Color(color.r, color.g, color.b, 0.55f * (1f - morph * 0.6f));
                core.transform.localScale = Vector3.one * wispScale * 0.8f;
                core.transform.rotation = facing;
            }
            if (icon != null)
            {
                icon.transform.localScale = Vector3.one * size * 1.4f * morph;
                icon.transform.rotation = facing;
                icon.color = new Color(1f, 1f, 1f, morph);
                icon.enabled = morph > 0.01f;
            }

            // Afterimage trail for the built-in wisp.
            if (trailOn && instance == null)
            {
                trailCarry += Time.deltaTime / 0.025f;
                while (trailCarry >= 1f)
                {
                    trailCarry -= 1f;
                    CastParticle.Spawn(transform.position, Vector3.zero, CastVisuals.GlowSprite,
                        new Color(color.r, color.g, color.b, 0.6f), size * 0.9f, size * 0.15f, 0.28f, 0f, viewCamera, -1);
                }
            }
        }
    }
}
