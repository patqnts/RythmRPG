using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// One small top-down texture around the view that stores how the grass is pushed: RG = push direction times
    /// amount (world XZ), B = how flattened. Every frame all <see cref="GrassInteractor"/>s are stamped into it on the
    /// GPU and the previous frame decays, so grass bends away instantly and springs back smoothly after you leave.
    /// The grass shader samples it at each blade's root: interaction costs the same for 10 or 100 000 blades.
    /// Shockwaves (<see cref="Grass.Shockwave"/>) are stamped the same way, as expanding rings that push outward.
    /// Driven by the first active <see cref="GrassField"/>; nothing to set up.
    /// </summary>
    public static class GrassInteractionMap
    {
        private const string ShaderPath = "Rendering/GrassInteraction";
        private const string ComputePath = "Rendering/GrassInteractionCS";

        private static readonly int PrevTexId = Shader.PropertyToID("_PrevTex");
        private static readonly int MapParamsId = Shader.PropertyToID("_MapParams");
        private static readonly int DecayId = Shader.PropertyToID("_Decay");
        private static readonly int InteractorsId = Shader.PropertyToID("_Interactors");
        private static readonly int VelocitiesId = Shader.PropertyToID("_InteractorVelocities");
        private static readonly int CountId = Shader.PropertyToID("_InteractorCount");
        private static readonly int PrevId = Shader.PropertyToID("_Prev");
        private static readonly int NextId = Shader.PropertyToID("_Next");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int GlobalTexId = Shader.PropertyToID("_GrassInteractionTex");
        private static readonly int GlobalParamsId = Shader.PropertyToID("_GrassInteractionParams");
        private static readonly int ShockwavesId = Shader.PropertyToID("_Shockwaves");
        private static readonly int ShockwaveShapeId = Shader.PropertyToID("_ShockwaveShape");
        private static readonly int ShockwaveCountId = Shader.PropertyToID("_ShockwaveCount");
        private static readonly int ShockwaveDirId = Shader.PropertyToID("_ShockwaveDir");

        /// <summary>Most shockwaves at once (the oldest is dropped).</summary>
        public const int MaxShockwaves = 8;

        private struct Wave
        {
            public Vector2 Center;
            public float Start;
            public float MaxRadius;
            public float Speed;
            public float Strength;
            public float Width;
            public GrassShockwaveShape Shape;
            public Vector2 Direction;
            public float Angle;
            public float LineLength;
        }

        private static readonly List<Wave> waves = new();
        private static readonly Vector4[] waveData = new Vector4[MaxShockwaves];
        private static readonly Vector4[] waveShape = new Vector4[MaxShockwaves];
        private static readonly Vector4[] waveDirection = new Vector4[MaxShockwaves];

        private static readonly RenderTexture[] targets = new RenderTexture[2];
        private static readonly Vector4[] interactors = new Vector4[GrassInteractor.MaxActive];
        private static readonly Vector4[] velocities = new Vector4[GrassInteractor.MaxActive];
        private static readonly List<GrassInteractor> candidates = new();
        private static Material material;
        private static ComputeShader compute;
        private static int kernel = -1;
        private static int current;
        private static int lastFrame = -1;
        private static Vector2 origin;
        private static bool hasOrigin;
        private static bool missingShaderReported;

        /// <summary>World XZ of the map's lower-left corner, and its size (for debugging / other effects).</summary>
        public static Vector3 OriginAndSize { get; private set; }

        /// <summary>The current map, or null when interaction is off (other shaders may read it too).</summary>
        public static Texture Texture => targets[current];

        /// <summary>
        /// Starts a ring at <paramref name="center"/> that grows at <paramref name="speed"/> up to
        /// <paramref name="maxRadius"/>, pushing the grass outward with <paramref name="strength"/> (fading as it grows).
        /// Use <see cref="Grass.Shockwave"/> to also cut or burn the grass.
        /// </summary>
        public static void AddShockwave(Vector3 center, float maxRadius, float speed, float strength, float width)
        {
            GrassShockwave wave = GrassShockwave.Ring(center, maxRadius);
            wave.speed = speed;
            wave.strength = strength;
            wave.width = width;
            AddShockwave(wave);
        }

        /// <summary>Starts a ring, cone or line shockwave (only its push; <see cref="Grass.Shockwave(GrassShockwave)"/>
        /// also applies its cut / fire effects).</summary>
        public static void AddShockwave(GrassShockwave wave)
        {
            if (waves.Count >= MaxShockwaves) waves.RemoveAt(0);
            waves.Add(new Wave
            {
                Center = new Vector2(wave.origin.x, wave.origin.z),
                Start = Time.time,
                MaxRadius = Mathf.Max(0.1f, wave.distance),
                Speed = Mathf.Max(0.1f, wave.speed),
                Strength = Mathf.Clamp(wave.strength, 0f, 2f),
                Width = Mathf.Max(0.05f, wave.width),
                Shape = wave.shape,
                Direction = wave.FlatDirection,
                Angle = Mathf.Clamp(wave.angle, 1f, 360f),
                LineLength = Mathf.Max(0.1f, wave.lineLength)
            });
        }

        /// <summary>Updates the map once per frame (called from the first <see cref="GrassField"/>'s LateUpdate).</summary>
        public static void Tick(GrassField settings, Camera camera)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;

            if (!Application.isPlaying || settings == null || !settings.Interactive || camera == null)
            {
                Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
                return;
            }
            if (!EnsureResources(settings.InteractionResolution)) return;

            float size = settings.InteractionMapSize;
            // The height the blades actually stand on (not the field object's position, which can be anywhere).
            float planeHeight = settings.GroundHeight;
            Vector2 focus = FocusPoint(camera, planeHeight);
            float texel = size / targets[0].width;
            Vector2 newOrigin = new(
                Mathf.Round((focus.x - size * 0.5f) / texel) * texel,
                Mathf.Round((focus.y - size * 0.5f) / texel) * texel);
            Vector2 shift = hasOrigin ? (newOrigin - origin) / size : Vector2.zero;
            bool reset = !hasOrigin || Mathf.Abs(shift.x) >= 1f || Mathf.Abs(shift.y) >= 1f;
            int resolution = targets[0].width;
            int shiftX = Mathf.RoundToInt(shift.x * resolution);
            int shiftY = Mathf.RoundToInt(shift.y * resolution);
            origin = newOrigin;
            hasOrigin = true;

            float dt = Time.deltaTime;
            int count = CollectInteractors(focus, size, planeHeight, dt);
            int waveCount = CollectShockwaves(Time.time);

            var mapParams = new Vector4(origin.x, origin.y, size, reset ? 0f : 1f);
            float pushDecay = Mathf.Exp(-settings.RecoverySpeed * dt);
            float flattenDecay = Mathf.Exp(-settings.RecoverySpeed * 0.6f * dt);
            int next = 1 - current;

            if (compute != null)
            {
                // Compute path (all desktop GPUs): texel-exact, no render state involved.
                compute.SetVector(MapParamsId, mapParams);
                compute.SetVector(DecayId, new Vector4(pushDecay, flattenDecay, shiftX, shiftY));
                compute.SetVectorArray(InteractorsId, interactors);
                compute.SetVectorArray(VelocitiesId, velocities);
                compute.SetInt(CountId, count);
                compute.SetVectorArray(ShockwavesId, waveData);
                compute.SetVectorArray(ShockwaveShapeId, waveShape);
                compute.SetVectorArray(ShockwaveDirId, waveDirection);
                compute.SetInt(ShockwaveCountId, waveCount);
                compute.SetInt(ResolutionId, resolution);
                compute.SetTexture(kernel, PrevId, targets[current]);
                compute.SetTexture(kernel, NextId, targets[next]);
                int groups = (resolution + 7) / 8;
                compute.Dispatch(kernel, groups, groups, 1);
            }
            else
            {
                // Fallback for GPUs without compute: a fragment-shader blit.
                material.SetVector(MapParamsId, mapParams);
                material.SetVector(DecayId, new Vector4(pushDecay, flattenDecay, shift.x, shift.y));
                material.SetVectorArray(InteractorsId, interactors);
                material.SetVectorArray(VelocitiesId, velocities);
                material.SetFloat(CountId, count);
                material.SetVectorArray(ShockwavesId, waveData);
                material.SetVectorArray(ShockwaveShapeId, waveShape);
                material.SetVectorArray(ShockwaveDirId, waveDirection);
                material.SetFloat(ShockwaveCountId, waveCount);
                material.SetTexture(PrevTexId, targets[current]);
                Graphics.Blit(targets[current], targets[next], material, 0);
            }
            current = next;

            OriginAndSize = new Vector3(origin.x, origin.y, size);
            Shader.SetGlobalTexture(GlobalTexId, targets[current]);
            Shader.SetGlobalVector(GlobalParamsId, new Vector4(origin.x, origin.y, 1f / size, 1f));
        }

        /// <summary>Frees the textures (called when the last grass field goes away).</summary>
        public static void Release()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].Release();
                Object.DestroyImmediate(targets[i]);
                targets[i] = null;
            }
            if (material != null) Object.DestroyImmediate(material);
            material = null;
            compute = null;
            kernel = -1;
            hasOrigin = false;
            waves.Clear();
            Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
        }

        // Ground point at the centre of the view, through the (possibly oblique) camera.
        private static Vector2 FocusPoint(Camera camera, float planeHeight)
        {
            Ray ray = ObliqueProjection.ViewportPointToRay(camera, new Vector3(0.5f, 0.5f, 0f));
            Plane plane = new(Vector3.up, new Vector3(0f, planeHeight, 0f));
            Vector3 point = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : camera.transform.position;
            return new Vector2(point.x, point.z);
        }

        private static int CollectInteractors(Vector2 focus, float size, float planeHeight, float dt)
        {
            candidates.Clear();
            float reach = size * 0.75f;
            foreach (GrassInteractor interactor in GrassInteractor.Active)
            {
                if (interactor == null) continue;
                interactor.Sample(dt);
                Vector3 p = interactor.Position;
                if (Mathf.Abs(p.x - focus.x) > reach || Mathf.Abs(p.z - focus.y) > reach) continue;
                if (interactor.FeetHeight - planeHeight > interactor.MaxHeightAboveGround) continue;
                candidates.Add(interactor);
            }
            if (candidates.Count > GrassInteractor.MaxActive)
            {
                candidates.Sort((a, b) =>
                    SqrFlat(a.Position, focus).CompareTo(SqrFlat(b.Position, focus)));
            }

            int count = Mathf.Min(candidates.Count, GrassInteractor.MaxActive);
            for (int i = 0; i < GrassInteractor.MaxActive; i++)
            {
                if (i >= count)
                {
                    interactors[i] = Vector4.zero;
                    velocities[i] = Vector4.zero;
                    continue;
                }
                GrassInteractor interactor = candidates[i];
                Vector3 p = interactor.Position;
                // Fade out as the interactor's feet rise above the grass.
                float height = Mathf.Max(0f, interactor.FeetHeight - planeHeight);
                float lift = interactor.MaxHeightAboveGround > 0.0001f
                    ? 1f - Mathf.Clamp01(height / interactor.MaxHeightAboveGround)
                    : 1f;
                interactors[i] = new Vector4(p.x, p.z, interactor.Radius, interactor.Strength * lift);
                Vector3 v = interactor.Velocity;
                velocities[i] = new Vector4(v.x, v.z, 0f, 0f);
            }
            candidates.Clear();
            return count;
        }

        // Current waves: xy = centre XZ, z = distance travelled, w = strength now;
        // shape: x = width, y = flatten, z = 0 ring / 1 cone / 2 line;
        // direction: xy = travel direction, then cone: zw = cos(half angle), cos(half angle + soft edge);
        // line: z = half its length, w = soft edge.
        private static int CollectShockwaves(float now)
        {
            waves.RemoveAll(w => (now - w.Start) * w.Speed > w.MaxRadius + w.Width);
            int count = 0;
            foreach (Wave wave in waves)
            {
                if (count >= MaxShockwaves) break;
                float radius = Mathf.Max(0f, (now - wave.Start) * wave.Speed);
                float fade = 1f - Mathf.Clamp01(radius / wave.MaxRadius);
                waveData[count] = new Vector4(wave.Center.x, wave.Center.y, radius, wave.Strength * fade * fade);
                waveShape[count] = new Vector4(wave.Width, 0.8f, (float)wave.Shape, 0f);
                if (wave.Shape == GrassShockwaveShape.Cone)
                {
                    float half = wave.Angle * 0.5f;
                    waveDirection[count] = new Vector4(wave.Direction.x, wave.Direction.y,
                        Mathf.Cos(half * Mathf.Deg2Rad), Mathf.Cos(Mathf.Min(half + 12f, 180f) * Mathf.Deg2Rad));
                }
                else
                {
                    waveDirection[count] = new Vector4(wave.Direction.x, wave.Direction.y, wave.LineLength * 0.5f,
                        Mathf.Max(0.3f, wave.Width));
                }
                count++;
            }
            for (int i = count; i < MaxShockwaves; i++)
            {
                waveData[i] = Vector4.zero;
                waveShape[i] = Vector4.zero;
                waveDirection[i] = Vector4.zero;
            }
            return count;
        }

        private static float SqrFlat(Vector3 p, Vector2 focus)
        {
            float dx = p.x - focus.x, dz = p.z - focus.y;
            return dx * dx + dz * dz;
        }

        private static bool EnsureResources(int resolution)
        {
            if (compute == null && SystemInfo.supportsComputeShaders)
            {
                compute = Resources.Load<ComputeShader>(ComputePath);
                kernel = compute != null ? compute.FindKernel("UpdateMap") : -1;
                if (kernel < 0) compute = null;
            }
            if (compute == null && material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    if (!missingShaderReported)
                        Debug.LogWarning("[Grass] Interaction shader missing (Assets/Resources/Rendering/GrassInteraction.shader).");
                    missingShaderReported = true;
                    return false;
                }
                material = new Material(shader) { name = "Grass Interaction (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }

            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 32, 1024);
            if (targets[0] != null && targets[0].width == resolution) return true;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Release();
                    Object.DestroyImmediate(targets[i]);
                }
                // Half floats: the push direction is signed.
                targets[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Grass Interaction " + i,
                    enableRandomWrite = compute != null,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                targets[i].Create();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = targets[i];
                GL.Clear(false, true, Color.clear);
                RenderTexture.active = previous;
            }
            hasOrigin = false;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            lastFrame = -1;
            hasOrigin = false;
            waves.Clear();
        }
    }
}
