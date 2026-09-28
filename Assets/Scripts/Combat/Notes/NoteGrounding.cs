using System.Collections.Generic;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Makes a note / projectile look like it moves ON the ground (a crawling rat, a rolling or bouncing ball, a
    /// ground shockwave) or gives a thrown one a shadow on the ground, without touching its timing.
    /// <para>
    /// The note itself keeps travelling and being judged exactly as before (on the lane plane at note height). Only its
    /// visual child is moved, and it is moved along the camera's view ray: with the orthographic / oblique camera every
    /// point on that ray lands on the same screen pixel, so on screen the note is exactly where it always was and reaches
    /// the hit line on the beat. In the 3D world, though, it now stands on the ground: it sorts with the scenery, is lit
    /// by the lights around it, sits in the grass, and its feet cross the hit line at the player's feet.
    /// </para>
    /// Surfaces:
    /// <list type="bullet">
    /// <item><b>Air</b>: the visual stays where it is (thrown projectiles); only the contact shadow is added.</item>
    /// <item><b>Ground</b>: standing on the ground (rat, rolling ball, anything that crawls or slides).</item>
    /// <item><b>Bounce</b>: on the ground, hopping in arcs that land on the beat; the last landing is the hit.</item>
    /// <item><b>Flat</b>: lying flat on the ground, turned along its travel (shockwave, crack, puddle, burrow mound).</item>
    /// </list>
    /// Put it on the note prefab's root (next to the Note component) and set <see cref="Visual"/> to the child that
    /// draws it. Right-click the component for presets.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Combat/Note Grounding")]
    public sealed class NoteGrounding : MonoBehaviour
    {
        public enum Surface { Air, Ground, Bounce, Flat }
        public enum GroundSource { CombatGround, Raycast, FixedHeight }
        public enum RollMode { None, Rotate, Frames }
        public enum Preset { ThrownWithShadow, Crawl, Roll, Bounce, GroundWave }

        [Header("Surface")]
        [SerializeField] private Surface surface = Surface.Ground;
        [Tooltip("The child that draws the note (sprite, animator). Empty = the first child with a SpriteRenderer.")]
        [SerializeField] private Transform visual;
        [Tooltip("Other children that should move with the visual (glows, trails), keeping their offset to it.")]
        [SerializeField] private Transform[] followers = new Transform[0];
        [Tooltip("Combat Ground: the ground height the combat layout uses (under the player). Raycast: the first " +
                 "collider on Ground Layers along the view ray (uneven arenas). Fixed Height: Fixed Ground Height.")]
        [SerializeField] private GroundSource groundSource = GroundSource.CombatGround;
        [SerializeField] private LayerMask groundLayers = 1;
        [SerializeField] private float fixedGroundHeight;
        [Tooltip("Put the bottom of the sprite on the ground (otherwise its pivot).")]
        [SerializeField] private bool feetOnGround = true;
        [Tooltip("Extra height of the feet above the ground (world units).")]
        [SerializeField] private float groundLift;

        [Header("Drop in (from the thrower's hand to the ground)")]
        [SerializeField] private bool dropIn = true;
        [SerializeField, Min(0.01f)] private float dropInSeconds = 0.22f;
        [Tooltip("Start height above the ground. Negative = the note's own height above the ground (its hand).")]
        [SerializeField] private float dropInHeight = -1f;

        [Header("Bounce")]
        [Tooltip("Beats between landings. The last landing is exactly the hit time, so every landing is on the music.")]
        [SerializeField, Min(0.125f)] private float bounceBeats = 1f;
        [Tooltip("Arc height of each bounce (world units).")]
        [SerializeField, Min(0f)] private float bounceHeight = 1.1f;
        [Tooltip("Earlier bounces are this much higher per bounce before the hit (1 = all the same).")]
        [SerializeField, Range(1f, 2f)] private float earlierBounceGrowth = 1.12f;
        [SerializeField, Min(0f)] private float maxBounceHeight = 2.5f;

        [Header("Landing squash")]
        [SerializeField, Range(0f, 0.6f)] private float squash = 0.25f;
        [SerializeField, Min(0.01f)] private float squashSeconds = 0.09f;

        [Header("Motion")]
        [Tooltip("Flip the sprite to face the way it moves across the screen.")]
        [SerializeField] private bool faceTravel = true;
        [Tooltip("The sprite is drawn facing right (off = drawn facing left).")]
        [SerializeField] private bool spriteFacesRight = true;
        [Tooltip("Crawl bob: pixels the sprite hops up and down per step (0 = off).")]
        [SerializeField, Range(0, 3)] private int bobPixels;
        [SerializeField, Min(0.1f)] private float stepsPerUnit = 5f;
        [SerializeField] private RollMode roll = RollMode.None;
        [Tooltip("Roll radius (world units). 0 = half the sprite's width.")]
        [SerializeField, Min(0f)] private float rollRadius;
        [Tooltip("Frames mode: a rolling cycle, played by distance (better for pixel art than rotating).")]
        [SerializeField] private Sprite[] rollFrames = new Sprite[0];
        [Tooltip("Play the visual's Animator faster the faster it moves (walk cycles).")]
        [SerializeField] private bool animatorSpeedFromMotion;
        [Tooltip("Speed (world units / s) at which the Animator plays at normal speed.")]
        [SerializeField, Min(0.01f)] private float animatorReferenceSpeed = 3f;
        [Tooltip("Flat surface: turn the visual so its top points along its travel.")]
        [SerializeField] private bool flatAlongTravel = true;

        [Header("Contact shadow")]
        [SerializeField] private bool shadow = true;
        [Tooltip("Shadow width in world units (0 = 80% of the sprite's width).")]
        [SerializeField, Min(0f)] private float shadowWidth;
        [SerializeField, Range(0.1f, 1f)] private float shadowAspect = 0.4f;
        [SerializeField] private Color shadowColor = new(0.05f, 0.04f, 0.1f, 0.38f);
        [Tooltip("Height (world units) at which the shadow has shrunk and faded the most.")]
        [SerializeField, Min(0.1f)] private float shadowFadeHeight = 2.5f;
        [SerializeField, Range(0f, 1f)] private float shadowMinScale = 0.45f;
        [SerializeField, Range(0f, 1f)] private float shadowMinAlpha = 0.25f;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;

        [Header("Ground effects")]
        [Tooltip("Dust puff prefab (empty = built-in pixel dust).")]
        [SerializeField] private GameObject dustPrefab;
        [Tooltip("Ground / Flat: a dust puff every this many world units travelled (0 = off).")]
        [SerializeField, Min(0f)] private float dustEveryUnits;
        [SerializeField] private bool dustOnLanding = true;
        [SerializeField, Min(0.1f)] private float dustPrefabLifetime = 2f;
        [Tooltip("Part the grass under it (adds a Grass Interactor at the contact point).")]
        [SerializeField] private bool trampleGrass = true;
        [SerializeField, Min(0.05f)] private float grassRadius = 0.35f;
        [Tooltip("Send a grass shockwave every this many world units (0 = off). For ground waves.")]
        [SerializeField, Min(0f)] private float grassWaveEveryUnits;
        [SerializeField, Min(0.1f)] private float grassWaveRadius = 1.2f;

        private Note note;
        private SpriteRenderer spriteRenderer;
        private Animator animator;
        private Vector3 visualRestLocal;
        private Quaternion visualRestLocalRotation;
        private Vector3 visualRestScale;
        private Vector3[] followerOffsets;
        private Transform contact;
        private SpriteRenderer shadowRenderer;
        private GrassInteractor grassInteractor;
        private bool initialized;
        private bool reparentedFlat;
        private float spawnTime;
        private bool hasLastGround;
        private Vector3 lastGround;
        private float distance;
        private float dustDistance;
        private float grassWaveDistance;
        private int lastBounceIndex = int.MinValue;
        private float squashTimer;
        private bool dropLanded;
        private int facing = 1;

        private static CombatLanePresentation3D presentation;
        private static Material shadowMaterial;
        private static readonly Dictionary<long, Sprite> ShadowSprites = new();
        private static ParticleSystem builtInDust;

        public Surface Mode { get => surface; set => surface = value; }
        public Transform Visual { get => visual; set => visual = value; }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            note = GetComponent<Note>();
            if (visual == null)
            {
                SpriteRenderer first = GetComponentInChildren<SpriteRenderer>(true);
                if (first != null && first.transform != transform) visual = first.transform;
            }
            if (visual == null) return;

            spriteRenderer = visual.GetComponentInChildren<SpriteRenderer>(true);
            animator = visual.GetComponentInChildren<Animator>(true);
            visualRestLocal = visual.localPosition;
            visualRestLocalRotation = visual.localRotation;
            visualRestScale = visual.localScale;
            followerOffsets = new Vector3[followers.Length];
            for (int i = 0; i < followers.Length; i++)
                followerOffsets[i] = followers[i] != null ? followers[i].position - visual.position : Vector3.zero;
            initialized = true;
        }

        private void OnEnable()
        {
            spawnTime = Time.time;
            hasLastGround = false;
            dropLanded = !dropIn || surface == Surface.Air || surface == Surface.Flat;
        }

        private void OnDisable()
        {
            if (contact != null) contact.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (contact != null) Destroy(contact.gameObject);
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!initialized || visual == null) return;
            if (!ResolveCamera(out Camera cam, out Vector3 viewDir)) return;

            Vector3 lanePoint = transform.TransformPoint(visualRestLocal);
            Vector3 groundPoint = surface == Surface.Air
                ? GroundBelow(lanePoint)
                : GroundAlongView(lanePoint, viewDir);
            float groundY = groundPoint.y;

            // Travel on the ground this frame.
            Vector3 step = hasLastGround ? groundPoint - lastGround : Vector3.zero;
            step.y = 0f;
            lastGround = groundPoint;
            hasLastGround = true;
            float stepLength = step.magnitude;
            distance += stepLength;
            float screenStep = Vector3.Dot(step, cam.transform.right);
            if (Mathf.Abs(screenStep) > 0.0005f) facing = screenStep > 0f ? 1 : -1;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = stepLength / dt;

            float height = 0f;
            switch (surface)
            {
                case Surface.Air:
                    height = Mathf.Max(0f, lanePoint.y - groundY);
                    break;
                case Surface.Ground:
                    height = DropHeight(lanePoint, groundY) + Bob();
                    break;
                case Surface.Bounce:
                    height = BounceHeight(groundPoint) + (dropLanded ? 0f : DropHeight(lanePoint, groundY));
                    break;
                case Surface.Flat:
                    break;
            }

            if (surface != Surface.Air) PlaceVisual(groundPoint, height, step);
            ApplyMotionLooks(speed);
            UpdateContact(groundPoint, height);
            GroundEffects(groundPoint, stepLength);
        }

        // ------------------------------------------------------------------ camera & ground

        private static bool ResolveCamera(out Camera cam, out Vector3 viewDir)
        {
            if (presentation == null) presentation = FindFirstObjectByType<CombatLanePresentation3D>();
            cam = presentation != null ? presentation.RenderCamera : null;
            if (cam == null) cam = Camera.main;
            viewDir = cam != null ? ObliqueProjection.ViewDirection(cam) : Vector3.down;
            return cam != null;
        }

        private float FlatGroundHeight()
        {
            switch (groundSource)
            {
                case GroundSource.FixedHeight: return fixedGroundHeight;
                default: return presentation != null ? presentation.GroundHeight : fixedGroundHeight;
            }
        }

        /// <summary>Where the camera's view ray through this point meets the ground (same screen pixel).</summary>
        private Vector3 GroundAlongView(Vector3 point, Vector3 viewDir)
        {
            if (groundSource == GroundSource.Raycast &&
                Physics.Raycast(point - viewDir * 5f, viewDir, out RaycastHit hit, 200f, groundLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            float groundY = FlatGroundHeight();
            if (viewDir.y > -0.01f) return new Vector3(point.x, groundY, point.z);
            float t = (groundY - point.y) / viewDir.y;
            return point + viewDir * t;
        }

        /// <summary>The ground straight below this point (for thrown notes' shadows).</summary>
        private Vector3 GroundBelow(Vector3 point)
        {
            if (groundSource == GroundSource.Raycast &&
                Physics.Raycast(point + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 200f, groundLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return new Vector3(point.x, FlatGroundHeight(), point.z);
        }

        // ------------------------------------------------------------------ heights

        private float DropHeight(Vector3 lanePoint, float groundY)
        {
            if (dropLanded) return 0f;
            float t = (Time.time - spawnTime) / dropInSeconds;
            if (t >= 1f)
            {
                dropLanded = true;
                Land(lastGround);
                return 0f;
            }
            float start = dropInHeight >= 0f ? dropInHeight : Mathf.Max(0f, lanePoint.y - groundY);
            return start * (1f - t * t); // falls, speeding up
        }

        private float Bob()
        {
            if (bobPixels <= 0) return 0f;
            float s = Mathf.Abs(Mathf.Sin(distance * stepsPerUnit * Mathf.PI));
            return Mathf.Round(s * bobPixels) / pixelsPerUnit;
        }

        /// <summary>Arcs landing every <see cref="bounceBeats"/> beats, the last landing exactly at the hit time.</summary>
        private float BounceHeight(Vector3 groundPoint)
        {
            if (note == null) return 0f;
            float untilHit = note.SecondsUntilHit;
            if (float.IsNaN(untilHit)) return 0f;
            float interval = Mathf.Max(0.05f, bounceBeats * note.SecondsPerBeat);
            // Bounce index counts down to 0 at the hit; after the hit (a miss) it keeps bouncing.
            float u = untilHit / interval;
            int index = Mathf.FloorToInt(u);
            float f = u - index;
            if (index != lastBounceIndex)
            {
                if (lastBounceIndex != int.MinValue) Land(groundPoint);
                lastBounceIndex = index;
            }
            int bouncesBeforeHit = Mathf.Max(0, index);
            float arc = Mathf.Min(maxBounceHeight, bounceHeight * Mathf.Pow(earlierBounceGrowth, bouncesBeforeHit));
            return arc * 4f * f * (1f - f);
        }

        private void Land(Vector3 groundPoint)
        {
            squashTimer = squashSeconds;
            if (dustOnLanding) SpawnDust(groundPoint, 5);
        }

        // ------------------------------------------------------------------ visual placement & looks

        private void PlaceVisual(Vector3 groundPoint, float height, Vector3 step)
        {
            // Landing squash (wider and shorter for a moment).
            float s = 0f;
            if (squashTimer > 0f)
            {
                s = squash * (squashTimer / squashSeconds);
                squashTimer -= Time.deltaTime;
            }

            if (surface == Surface.Flat)
            {
                EnsureFlatParent();
                Vector3 along = step.sqrMagnitude > 1e-8f ? step.normalized : visual.up;
                along.y = 0f;
                if (along.sqrMagnitude < 1e-6f) along = Vector3.forward;
                // Lying on the ground, face up, top of the sprite along the travel.
                visual.position = groundPoint + Vector3.up * (0.01f + groundLift);
                visual.rotation = flatAlongTravel
                    ? Quaternion.LookRotation(Vector3.down, along.normalized)
                    : Quaternion.LookRotation(Vector3.down, Vector3.forward);
                visual.localScale = visualRestScale;
                MoveFollowers();
                return;
            }

            visual.localScale = new Vector3(visualRestScale.x * (1f + s), visualRestScale.y * (1f - s), visualRestScale.z);
            float feet = 0f;
            if (feetOnGround && spriteRenderer != null && spriteRenderer.sprite != null)
                feet = -spriteRenderer.sprite.bounds.min.y * Mathf.Abs(spriteRenderer.transform.lossyScale.y);

            visual.position = groundPoint + Vector3.up * (feet + height + groundLift);

            if (roll == RollMode.Rotate)
            {
                float radius = rollRadius > 0f ? rollRadius : HalfWidth();
                float degrees = distance / Mathf.Max(0.01f, radius) * Mathf.Rad2Deg;
                visual.localRotation = visualRestLocalRotation * Quaternion.Euler(0f, 0f, -degrees * facing);
            }
            MoveFollowers();
        }

        private void MoveFollowers()
        {
            for (int i = 0; i < followers.Length; i++)
                if (followers[i] != null) followers[i].position = visual.position + followerOffsets[i];
        }

        private void ApplyMotionLooks(float speed)
        {
            if (spriteRenderer != null && faceTravel && surface != Surface.Flat)
                spriteRenderer.flipX = spriteFacesRight ? facing < 0 : facing > 0;

            if (roll == RollMode.Frames && spriteRenderer != null && rollFrames != null && rollFrames.Length > 0)
            {
                float radius = rollRadius > 0f ? rollRadius : HalfWidth();
                float turns = distance / (2f * Mathf.PI * Mathf.Max(0.01f, radius));
                int frame = Mathf.FloorToInt(turns * rollFrames.Length);
                frame = ((frame % rollFrames.Length) + rollFrames.Length) % rollFrames.Length;
                if (rollFrames[frame] != null) spriteRenderer.sprite = rollFrames[frame];
            }

            if (animatorSpeedFromMotion && animator != null)
                animator.speed = Mathf.Clamp(speed / animatorReferenceSpeed, 0.2f, 3f);
        }

        private float HalfWidth()
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null) return 0.25f;
            return spriteRenderer.sprite.bounds.extents.x * Mathf.Abs(spriteRenderer.transform.lossyScale.x);
        }

        // The note root is often flattened (scale z = 0), which would squash a flat child to nothing: a flat visual
        // lives under the (unscaled) contact object instead, and is destroyed with it.
        private void EnsureFlatParent()
        {
            if (reparentedFlat) return;
            EnsureContact();
            visual.SetParent(contact, true);
            foreach (ObliqueBillboard billboard in visual.GetComponentsInChildren<ObliqueBillboard>(true))
                billboard.enabled = false;
            reparentedFlat = true;
        }

        // ------------------------------------------------------------------ contact shadow + grass

        private void EnsureContact()
        {
            if (contact != null) return;
            var go = new GameObject($"{name} (ground contact)");
            contact = go.transform;
        }

        private void UpdateContact(Vector3 groundPoint, float height)
        {
            bool wantShadow = shadow && surface != Surface.Flat;
            bool wantGrass = trampleGrass;
            if (!wantShadow && !wantGrass && !reparentedFlat)
            {
                if (contact != null) contact.gameObject.SetActive(false);
                return;
            }

            EnsureContact();
            if (!contact.gameObject.activeSelf) contact.gameObject.SetActive(true);
            contact.position = groundPoint;
            contact.rotation = Quaternion.identity;

            if (wantShadow)
            {
                if (shadowRenderer == null)
                {
                    var shadowGo = new GameObject("Shadow");
                    shadowGo.transform.SetParent(contact, false);
                    // Lying on the ground, facing up.
                    shadowGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
                    shadowRenderer.sharedMaterial = ShadowMaterial();
                    shadowRenderer.sortingOrder = -5;
                    shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    shadowRenderer.receiveShadows = false;
                }
                shadowRenderer.transform.localPosition = Vector3.up * 0.015f;
                float k = Mathf.Clamp01(height / shadowFadeHeight);
                float width = (shadowWidth > 0f ? shadowWidth : HalfWidth() * 1.6f) * Mathf.Lerp(1f, shadowMinScale, k);
                shadowRenderer.sprite = ShadowSprite(width, shadowAspect, pixelsPerUnit);
                Color c = shadowColor;
                c.a *= Mathf.Lerp(1f, shadowMinAlpha, k);
                shadowRenderer.color = c;
                if (!shadowRenderer.enabled) shadowRenderer.enabled = true;
            }
            else if (shadowRenderer != null && shadowRenderer.enabled)
            {
                shadowRenderer.enabled = false;
            }

            if (wantGrass)
            {
                if (grassInteractor == null) grassInteractor = contact.gameObject.AddComponent<GrassInteractor>();
                grassInteractor.Radius = grassRadius;
                // Only touches the grass while on (or near) the ground.
                grassInteractor.enabled = height < 0.35f;
            }
            else if (grassInteractor != null && grassInteractor.enabled)
            {
                grassInteractor.enabled = false;
            }
        }

        private void GroundEffects(Vector3 groundPoint, float stepLength)
        {
            bool onGround = surface == Surface.Ground || surface == Surface.Flat;
            if (onGround && dustEveryUnits > 0f && dropLanded)
            {
                dustDistance += stepLength;
                if (dustDistance >= dustEveryUnits)
                {
                    dustDistance -= dustEveryUnits;
                    SpawnDust(groundPoint, 2);
                }
            }
            if (grassWaveEveryUnits > 0f)
            {
                grassWaveDistance += stepLength;
                if (grassWaveDistance >= grassWaveEveryUnits)
                {
                    grassWaveDistance -= grassWaveEveryUnits;
                    Grass.Shockwave(groundPoint, grassWaveRadius, 10f, 1f);
                }
            }
        }

        // ------------------------------------------------------------------ shared assets

        private static Material ShadowMaterial()
        {
            if (shadowMaterial != null) return shadowMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            shadowMaterial = new Material(shader) { name = "Note Contact Shadow", hideFlags = HideFlags.DontSave };
            return shadowMaterial;
        }

        /// <summary>A crisp pixel-art ellipse of the given world width (cached per pixel size).</summary>
        private static Sprite ShadowSprite(float worldWidth, float aspect, float ppu)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(worldWidth * ppu), 2, 256);
            int h = Mathf.Clamp(Mathf.RoundToInt(worldWidth * aspect * ppu), 1, 256);
            long key = ((long)w << 32) | (uint)h | ((long)Mathf.RoundToInt(ppu) << 48);
            if (ShadowSprites.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
                name = $"Contact Shadow {w}x{h}",
            };
            var pixels = new Color32[w * h];
            float rx = w * 0.5f, ry = h * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f - rx) / rx, dy = (y + 0.5f - ry) / ry;
                pixels[y * w + x] = dx * dx + dy * dy <= 1f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.DontSave;
            ShadowSprites[key] = sprite;
            return sprite;
        }

        private void SpawnDust(Vector3 position, int count)
        {
            if (dustPrefab != null)
            {
                GameObject puff = Instantiate(dustPrefab, position, Quaternion.identity);
                Destroy(puff, dustPrefabLifetime);
                return;
            }
            ParticleSystem system = BuiltInDust();
            if (system == null) return;
            var emit = new ParticleSystem.EmitParams { position = position + Vector3.up * 0.03f, applyShapeToPosition = true };
            system.Emit(emit, count);
        }

        /// <summary>One shared pixel dust system (small grey squares puffing out and fading).</summary>
        private static ParticleSystem BuiltInDust()
        {
            if (builtInDust != null) return builtInDust;
            var go = new GameObject("Note Ground Dust (built-in)");
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f / 32f, 3f / 32f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.72f, 0.62f, 0.9f), new Color(0.6f, 0.55f, 0.48f, 0.8f));
            main.gravityModifier = 0.35f;
            main.maxParticles = 400;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.12f;

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ShadowMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            builtInDust = ps;
            return ps;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            presentation = null;
            builtInDust = null;
            ShadowSprites.Clear();
        }

        // ------------------------------------------------------------------ presets

        /// <summary>Sets up the component for a typical kind of ground / thrown projectile.</summary>
        public void ApplyPreset(Preset preset)
        {
            // Common defaults
            groundLift = 0f;
            feetOnGround = true;
            dropIn = true;
            dropInSeconds = 0.22f;
            dropInHeight = -1f;
            squash = 0.25f;
            faceTravel = true;
            bobPixels = 0;
            roll = RollMode.None;
            animatorSpeedFromMotion = false;
            shadow = true;
            shadowAspect = 0.4f;
            dustEveryUnits = 0f;
            dustOnLanding = true;
            trampleGrass = true;
            grassWaveEveryUnits = 0f;

            switch (preset)
            {
                case Preset.ThrownWithShadow:
                    surface = Surface.Air;
                    dropIn = false;
                    faceTravel = false;
                    trampleGrass = false;
                    dustOnLanding = false;
                    break;
                case Preset.Crawl:
                    surface = Surface.Ground;
                    bobPixels = 1;
                    stepsPerUnit = 5f;
                    dustEveryUnits = 0.7f;
                    animatorSpeedFromMotion = true;
                    shadowAspect = 0.35f;
                    break;
                case Preset.Roll:
                    surface = Surface.Ground;
                    faceTravel = false;
                    roll = rollFrames != null && rollFrames.Length > 0 ? RollMode.Frames : RollMode.Rotate;
                    dustEveryUnits = 1.2f;
                    break;
                case Preset.Bounce:
                    surface = Surface.Bounce;
                    faceTravel = false;
                    dropIn = false;
                    bounceBeats = 1f;
                    bounceHeight = 1.1f;
                    earlierBounceGrowth = 1.12f;
                    squash = 0.3f;
                    break;
                case Preset.GroundWave:
                    surface = Surface.Flat;
                    dropIn = false;
                    faceTravel = false;
                    shadow = false;
                    trampleGrass = true;
                    grassRadius = 0.6f;
                    dustEveryUnits = 0.5f;
                    dustOnLanding = false;
                    grassWaveEveryUnits = 0.6f;
                    grassWaveRadius = 1.2f;
                    break;
            }
        }

        [ContextMenu("Preset/Thrown (air) + shadow")] private void PresetThrown() => ApplyPreset(Preset.ThrownWithShadow);
        [ContextMenu("Preset/Crawl (rat)")] private void PresetCrawl() => ApplyPreset(Preset.Crawl);
        [ContextMenu("Preset/Roll (ball)")] private void PresetRoll() => ApplyPreset(Preset.Roll);
        [ContextMenu("Preset/Bounce (ball on the beat)")] private void PresetBounce() => ApplyPreset(Preset.Bounce);
        [ContextMenu("Preset/Ground wave (flat)")] private void PresetWave() => ApplyPreset(Preset.GroundWave);

        private void OnValidate()
        {
            if (followers == null) followers = new Transform[0];
            if (rollFrames == null) rollFrames = new Sprite[0];
        }
    }
}
