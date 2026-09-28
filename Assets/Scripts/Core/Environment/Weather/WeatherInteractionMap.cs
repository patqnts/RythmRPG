using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// A top-down texture around the view that remembers how characters disturbed the weather:
    /// R = packed snow (footprints / trails, refills slowly), GB = how far the ground mist is pushed (world XZ),
    /// A = how much the mist is cleared (flows back in a couple of seconds).
    /// Every <see cref="GrassInteractor"/> is used (Terrain Weather adds one to each CharacterController), so
    /// whatever parts the grass also leaves footprints and parts the mist. Updated on the GPU once per frame by the
    /// first active <see cref="TerrainWeather"/>; the Terrain Weather shader reads it.
    /// </summary>
    public static class WeatherInteractionMap
    {
        private const string ComputePath = "Rendering/Weather/WeatherInteractionCS";
        public const int MaxSnowStamps = 32;
        public const int MaxMovers = 16;

        private static readonly int PrevId = Shader.PropertyToID("_Prev");
        private static readonly int NextId = Shader.PropertyToID("_Next");
        private static readonly int MapParamsId = Shader.PropertyToID("_MapParams");
        private static readonly int DecayId = Shader.PropertyToID("_Decay");
        private static readonly int ShiftId = Shader.PropertyToID("_Shift");
        private static readonly int StampsId = Shader.PropertyToID("_SnowStamps");
        private static readonly int StampParamsId = Shader.PropertyToID("_SnowStampParams");
        private static readonly int StampCountId = Shader.PropertyToID("_SnowStampCount");
        private static readonly int MoversId = Shader.PropertyToID("_Movers");
        private static readonly int MoverVelId = Shader.PropertyToID("_MoverVelocities");
        private static readonly int MoverCountId = Shader.PropertyToID("_MoverCount");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        internal static readonly int GlobalTexId = Shader.PropertyToID("_WeatherMapTex");
        internal static readonly int GlobalParamsId = Shader.PropertyToID("_WeatherMapParams");

        private sealed class Tracker
        {
            public Vector3 last;
            public float stride;
            public bool left;
            public bool grounded;
            public int seenFrame;
            public Vector3 velocity;
        }

        private static readonly RenderTexture[] targets = new RenderTexture[2];
        private static readonly Vector4[] stamps = new Vector4[MaxSnowStamps];
        private static readonly Vector4[] stampParams = new Vector4[MaxSnowStamps];
        private static readonly Vector4[] movers = new Vector4[MaxMovers];
        private static readonly Vector4[] moverVelocities = new Vector4[MaxMovers];
        private static readonly Dictionary<GrassInteractor, Tracker> trackers = new();
        private static readonly List<GrassInteractor> stale = new();
        private static readonly List<(Vector4 seg, Vector4 prm)> pending = new();
        private static ComputeShader compute;
        private static int kernel = -1;
        private static int current;
        private static Vector2 origin;
        private static bool hasOrigin;
        private static bool warned;
        private static int lastFrame = -1;

        /// <summary>The current map (null when not running).</summary>
        public static Texture Texture => targets[current];

        /// <summary>Presses a footprint / furrow into the snow from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static void PressSnow(Vector3 a, Vector3 b, float radius, float strength = 1f)
        {
            if (pending.Count >= 256) pending.RemoveAt(0);
            pending.Add((new Vector4(a.x, a.z, b.x, b.z), new Vector4(Mathf.Max(0.01f, radius), Mathf.Clamp01(strength), 0f, 0f)));
        }

        /// <summary>World size of the square around the view that remembers footprints and parted mist.</summary>
        public static float Size = 20f;
        /// <summary>Texels across that square (512 over 20 units = about 1 texel per screen pixel).</summary>
        public static int Resolution = 512;

        private static TerrainWeather snowSettings;
        private static TerrainWeather airSettings;

        internal static void Tick(Camera camera)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            snowSettings = null;
            airSettings = null;
            foreach (TerrainWeather field in TerrainWeather.Active)
            {
                if (field == null || !field.Interactive) continue;
                if (field.Kind == TerrainWeatherKind.Snow) { if (snowSettings == null) snowSettings = field; }
                else if (airSettings == null) airSettings = field;
            }
            if (camera == null || (snowSettings == null && airSettings == null) || !EnsureResources(Resolution))
            {
                Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
                return;
            }

            float size = Mathf.Max(4f, Size);
            int resolution = targets[0].width;
            float texel = size / resolution;
            float groundY = ObliqueProjection.TryGet(camera, out ObliqueProjection oblique) ? oblique.GroundHeight : 0f;
            Vector3 focus = ViewGroundPoint(camera, groundY);
            Vector2 newOrigin = new(
                Mathf.Round((focus.x - size * 0.5f) / texel) * texel,
                Mathf.Round((focus.z - size * 0.5f) / texel) * texel);
            Vector2 shift = hasOrigin ? (newOrigin - origin) / texel : Vector2.zero;
            bool reset = !hasOrigin || Mathf.Abs(shift.x) >= resolution || Mathf.Abs(shift.y) >= resolution;
            origin = newOrigin;
            hasOrigin = true;

            float dt = Mathf.Max(0f, Time.deltaTime);
            int stampCount = CollectStamps(groundY, dt);
            int moverCount = CollectMovers(focus, size, groundY);

            float refillSeconds = snowSettings != null ? snowSettings.TrailRefillSeconds : 30f;
            float refill = refillSeconds / (1f + Weather.Current.snowfall * 3f);
            float recover = airSettings != null ? airSettings.RecoverSeconds : 2.5f;
            float snowDecay = Mathf.Exp(-dt / Mathf.Max(0.1f, refill) * 3f);
            float pushDecay = Mathf.Exp(-dt / Mathf.Max(0.05f, recover) * 3f);
            float clearDecay = Mathf.Exp(-dt / Mathf.Max(0.05f, recover) * 2f);

            int next = 1 - current;
            compute.SetVector(MapParamsId, new Vector4(origin.x, origin.y, size, reset ? 0f : 1f));
            compute.SetVector(DecayId, new Vector4(snowDecay, pushDecay, clearDecay, 0f));
            compute.SetInts(ShiftId, Mathf.RoundToInt(shift.x), Mathf.RoundToInt(shift.y), 0, 0);
            compute.SetVectorArray(StampsId, stamps);
            compute.SetVectorArray(StampParamsId, stampParams);
            compute.SetInt(StampCountId, stampCount);
            compute.SetVectorArray(MoversId, movers);
            compute.SetVectorArray(MoverVelId, moverVelocities);
            compute.SetInt(MoverCountId, moverCount);
            compute.SetInt(ResolutionId, resolution);
            compute.SetTexture(kernel, PrevId, targets[current]);
            compute.SetTexture(kernel, NextId, targets[next]);
            int groups = (resolution + 7) / 8;
            compute.Dispatch(kernel, groups, groups, 1);
            current = next;

            Shader.SetGlobalTexture(GlobalTexId, targets[current]);
            Shader.SetGlobalVector(GlobalParamsId, new Vector4(origin.x, origin.y, 1f / size, 1f));
        }

        // Footprints (alternating left / right every stride) or a continuous furrow, as capsule stamps.
        private static int CollectStamps(float groundY, float dt)
        {
            TerrainWeather snow = snowSettings;
            int frame = Time.frameCount;
            bool snowy = snow != null;
            SnowTrailStyle style = snow != null ? snow.TrailStyle : SnowTrailStyle.Footprints;
            float trailWidth = snow != null ? snow.TrailWidth : 0.5f;
            float strideSetting = snow != null ? snow.StrideLength : 0.42f;
            foreach (GrassInteractor interactor in GrassInteractor.Active)
            {
                if (interactor == null) continue;
                Vector3 p = interactor.Position;
                p.y = interactor.FeetHeight;
                if (!trackers.TryGetValue(interactor, out Tracker tracker))
                {
                    tracker = new Tracker { last = p };
                    trackers[interactor] = tracker;
                }
                tracker.seenFrame = frame;
                CharacterController body = interactor.GetComponent<CharacterController>();
                bool grounded = body != null ? body.isGrounded || p.y - groundY < 0.3f : p.y - groundY < 0.3f;
                Vector3 move = p - tracker.last;
                move.y = 0f;
                float distance = move.magnitude;
                tracker.velocity = dt > 0.0001f && distance < 3f ? move / dt : Vector3.zero;
                float width = interactor.Radius * trailWidth;

                if (snowy && grounded && distance > 0.0001f && distance < 3f)
                {
                    Vector3 dir = move / distance;
                    if (style == SnowTrailStyle.Trail)
                    {
                        PressSnow(tracker.last, p, width * 0.5f, interactor.Strength);
                    }
                    else
                    {
                        tracker.stride += distance;
                        float strideLength = Mathf.Max(0.05f, strideSetting);
                        while (tracker.stride >= strideLength)
                        {
                            tracker.stride -= strideLength;
                            // Place the print back along the path to where the stride completed.
                            Vector3 at = p - dir * tracker.stride;
                            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.45f) * (tracker.left ? 1f : -1f);
                            tracker.left = !tracker.left;
                            float footLength = width * 0.35f;
                            Vector3 c = at + side;
                            PressSnow(c - dir * footLength, c + dir * footLength, width * 0.22f, interactor.Strength);
                        }
                    }
                }
                else if (snowy && grounded && !tracker.grounded)
                {
                    // Landing: a pair of prints.
                    Vector3 side = Vector3.right * (width * 0.3f);
                    PressSnow(p - side, p - side, width * 0.25f, interactor.Strength);
                    PressSnow(p + side, p + side, width * 0.25f, interactor.Strength);
                }
                tracker.grounded = grounded;
                tracker.last = p;
            }

            // Forget interactors that went away.
            stale.Clear();
            foreach (KeyValuePair<GrassInteractor, Tracker> pair in trackers)
                if (pair.Key == null || pair.Value.seenFrame != frame) stale.Add(pair.Key);
            foreach (GrassInteractor key in stale) trackers.Remove(key);

            int count = 0;
            while (count < MaxSnowStamps && pending.Count > 0)
            {
                (Vector4 seg, Vector4 prm) = pending[0];
                pending.RemoveAt(0);
                stamps[count] = seg;
                stampParams[count] = prm;
                count++;
            }
            for (int i = count; i < MaxSnowStamps; i++)
            {
                stamps[i] = Vector4.zero;
                stampParams[i] = Vector4.zero;
            }
            return count;
        }

        private static int CollectMovers(Vector3 focus, float size, float groundY)
        {
            int count = 0;
            float reach = size * 0.6f;
            foreach (GrassInteractor interactor in GrassInteractor.Active)
            {
                if (count >= MaxMovers) break;
                if (interactor == null) continue;
                Vector3 p = interactor.Position;
                if (Mathf.Abs(p.x - focus.x) > reach || Mathf.Abs(p.z - focus.z) > reach) continue;
                if (airSettings == null) break;
                // Fog can hang high: interactors part it up to a few units above the ground.
                float height = interactor.FeetHeight - groundY;
                if (height > 4f) continue;
                float lift = 1f - Mathf.Clamp01((height - 2.5f) / 1.5f);
                Vector3 velocity = trackers.TryGetValue(interactor, out Tracker tracker) ? tracker.velocity : Vector3.zero;
                movers[count] = new Vector4(p.x, p.z, interactor.Radius * 1.8f, interactor.Strength * lift);
                moverVelocities[count] = new Vector4(velocity.x, velocity.z, 0f, 0f);
                count++;
            }
            for (int i = count; i < MaxMovers; i++)
            {
                movers[i] = Vector4.zero;
                moverVelocities[i] = Vector4.zero;
            }
            return count;
        }

        private static Vector3 ViewGroundPoint(Camera camera, float groundY)
        {
            Ray ray = ObliqueProjection.ViewportPointToRay(camera, new Vector3(0.5f, 0.5f, 0f));
            Plane plane = new(Vector3.up, new Vector3(0f, groundY, 0f));
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance)
                : new Vector3(camera.transform.position.x, groundY, camera.transform.position.z);
        }

        private static bool EnsureResources(int resolution)
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                if (!warned) Debug.LogWarning("[Weather] Compute shaders unsupported: no snow trails or mist parting.");
                warned = true;
                return false;
            }
            if (compute == null)
            {
                compute = Resources.Load<ComputeShader>(ComputePath);
                kernel = compute != null ? compute.FindKernel("UpdateMap") : -1;
                if (kernel < 0)
                {
                    if (!warned) Debug.LogWarning("[Weather] Missing Resources/" + ComputePath + ".compute.");
                    warned = true;
                    compute = null;
                    return false;
                }
            }
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 64, 1024);
            if (targets[0] != null && targets[0].width == resolution) return true;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Release();
                    Object.DestroyImmediate(targets[i]);
                }
                targets[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Weather Interaction " + i,
                    enableRandomWrite = true,
                    filterMode = FilterMode.Point,
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

        public static void Release()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].Release();
                Object.DestroyImmediate(targets[i]);
                targets[i] = null;
            }
            compute = null;
            kernel = -1;
            hasOrigin = false;
            trackers.Clear();
            pending.Clear();
            Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            hasOrigin = false;
            trackers.Clear();
            pending.Clear();
            lastFrame = -1;
            warned = false;
        }
    }
}
