using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Static access to Pixel Water: splashes, surface queries and enter / leave events.
    /// <code>
    /// Water.Splash(hitPoint, 0.6f, 1.5f);                 // a projectile lands in the lake
    /// if (Water.TryGetSurface(pos, out float y)) ...      // is this point over water, and how high is it?
    /// Water.Entered += (who, where) => ...;
    /// </code>
    /// </summary>
    public static class Water
    {
        /// <summary>Raised when an interactor steps into water (object, world point on the surface).</summary>
        public static event Action<GameObject, Vector3> Entered;
        /// <summary>Raised when an interactor leaves water.</summary>
        public static event Action<GameObject, Vector3> Exited;

        /// <summary>Makes a ripple ring on the water at <paramref name="point"/> (ignored away from water).</summary>
        public static void Splash(Vector3 point, float radius = 0.4f, float strength = 1f)
        {
            if (!TryGetBody(point, 0.5f, out _)) return;
            WaterSimulation.AddDrop(point, radius, strength);
            WaterSimulation.AddSplash(point, radius, strength);
        }

        /// <summary>The water body under / at <paramref name="point"/> (XZ inside it and not higher than
        /// <paramref name="above"/> over its surface).</summary>
        public static bool TryGetBody(Vector3 point, float above, out PixelWater body)
        {
            foreach (PixelWater water in PixelWater.All)
            {
                if (water == null || !water.isActiveAndEnabled) continue;
                if (water.Contains(point, above))
                {
                    body = water;
                    return true;
                }
            }
            body = null;
            return false;
        }

        /// <summary>True (with the surface height) when <paramref name="point"/> is over a Pixel Water surface.</summary>
        public static bool TryGetSurface(Vector3 point, out float height)
        {
            foreach (PixelWater water in PixelWater.All)
            {
                if (water == null || !water.isActiveAndEnabled) continue;
                if (water.SurfaceRect.Contains(new Vector2(point.x, point.z)))
                {
                    height = water.SurfaceHeight;
                    return true;
                }
            }
            height = 0f;
            return false;
        }

        /// <summary>True when <paramref name="point"/> is in (at or below the surface of) any Pixel Water.</summary>
        public static bool IsInWater(Vector3 point) => TryGetBody(point, 0f, out _);

        internal static void RaiseEntered(GameObject who, Vector3 where) => Entered?.Invoke(who, where);
        internal static void RaiseExited(GameObject who, Vector3 where) => Exited?.Invoke(who, where);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            Entered = null;
            Exited = null;
        }
    }

    /// <summary>
    /// The ripple simulation shared by all Pixel Water: a height field around the view (wave equation on the GPU),
    /// fed by interactors (splashes on entering, wakes while moving, idle ripples), rain drops and
    /// <see cref="Water.Splash"/>. Waves stop at the edges of the water surfaces. Driven by the first
    /// <see cref="PixelWater"/>; the water shader reads <c>_WaterSimTex</c> / <c>_WaterSimParams</c>.
    /// </summary>
    public static class WaterSimulation
    {
        private const string ComputePath = "Rendering/Weather/WaterSimCS";
        public const int MaxDrops = 64;
        public const int MaxBodies = 8;
        private const float StepSeconds = 1f / 60f;

        /// <summary>World size of the simulated square around the view, and its resolution.</summary>
        public static float Size = 16f;
        public static int Resolution = 256;
        /// <summary>How long ripples last (0..1 per step; higher = longer).</summary>
        public static float Damping = 0.985f;

        private static readonly int PrevId = Shader.PropertyToID("_Prev");
        private static readonly int NextId = Shader.PropertyToID("_Next");
        private static readonly int ParamsId = Shader.PropertyToID("_SimParams");
        private static readonly int ShiftId = Shader.PropertyToID("_Shift");
        private static readonly int DropsId = Shader.PropertyToID("_Drops");
        private static readonly int DropCountId = Shader.PropertyToID("_DropCount");
        private static readonly int BodiesId = Shader.PropertyToID("_Bodies");
        private static readonly int BodyCountId = Shader.PropertyToID("_BodyCount");
        private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
        private static readonly int GlobalTexId = Shader.PropertyToID("_WaterSimTex");
        private static readonly int GlobalParamsId = Shader.PropertyToID("_WaterSimParams");
        private static readonly int GlobalTexelId = Shader.PropertyToID("_WaterSimTexel");
        private static readonly int MoversId = Shader.PropertyToID("_WaterMovers");
        private static readonly int MoverVelocitiesId = Shader.PropertyToID("_WaterMoverVelocities");
        private static readonly int MoverCountId = Shader.PropertyToID("_WaterMoverCount");

        private static readonly int RingsId = Shader.PropertyToID("_WaterRings");
        private static readonly int RingShapesId = Shader.PropertyToID("_WaterRingShapes");
        private static readonly int RingCountId = Shader.PropertyToID("_WaterRingCount");
        private static readonly int WaterTimeId = Shader.PropertyToID("_WaterTime");

        /// <summary>Most ring events (trail rings, splashes, rain drops) alive at once.</summary>
        public const int MaxRings = 128;
        /// <summary>Of those, at most this many rain drops (so rain never crowds out splashes and trails).</summary>
        public const int MaxRainRings = 72;

        private struct Ring
        {
            public Vector4 where;   // x, z, start time, type (0 ring, 1 splash, 2 rain)
            public Vector4 shape;   // start radius, speed, lifetime, strength
        }

        private static readonly List<Ring> rings = new();
        private static readonly Vector4[] ringData = new Vector4[MaxRings];
        private static readonly Vector4[] ringShapes = new Vector4[MaxRings];
        private static int rainRings;

        /// <summary>A ring of 1-pixel lines spreading out on the water (drawn by the Pixel Water shader).</summary>
        public static void AddRing(Vector3 point, float startRadius, float speed, float lifetime, float strength,
            float delay = 0f) => PushRing(point, startRadius, speed, lifetime, strength, delay, 0f);

        /// <summary>A splash: a double ring plus pixel droplets thrown out (drawn by the Pixel Water shader).</summary>
        public static void AddSplash(Vector3 point, float radius, float strength)
        {
            radius = Mathf.Max(0.05f, radius);
            PushRing(point, radius * 0.5f, 0.55f + radius * 0.8f, 0.9f, Mathf.Clamp(strength, 0.3f, 1.5f), 0f, 1f);
            PushRing(point, radius * 0.4f, 0.45f + radius * 0.6f, 1.0f, Mathf.Clamp(strength * 0.7f, 0.2f, 1f), 0.18f, 0f);
        }

        private static void PushRing(Vector3 point, float startRadius, float speed, float lifetime, float strength,
            float delay, float type)
        {
            bool rain = type > 1.5f;
            if (rain && rainRings >= MaxRainRings) return;
            if (rings.Count >= MaxRings)
            {
                // Drop the oldest (rain first).
                int drop = rings.FindIndex(r => r.where.w > 1.5f);
                if (drop < 0) drop = 0;
                if (rings[drop].where.w > 1.5f) rainRings--;
                rings.RemoveAt(drop);
            }
            rings.Add(new Ring
            {
                where = new Vector4(point.x, point.z, Time.time + delay, type),
                shape = new Vector4(Mathf.Max(0f, startRadius), Mathf.Max(0f, speed), Mathf.Max(0.05f, lifetime), strength)
            });
            if (rain) rainRings++;
        }

        private static void PublishRings()
        {
            float now = Time.time;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                Ring r = rings[i];
                if (now - r.where.z <= r.shape.z) continue;
                if (r.where.w > 1.5f) rainRings--;
                rings.RemoveAt(i);
            }
            int count = Mathf.Min(rings.Count, MaxRings);
            for (int i = 0; i < count; i++)
            {
                ringData[i] = rings[i].where;
                ringShapes[i] = rings[i].shape;
            }
            Shader.SetGlobalVectorArray(RingsId, ringData);
            Shader.SetGlobalVectorArray(RingShapesId, ringShapes);
            Shader.SetGlobalFloat(RingCountId, count);
            Shader.SetGlobalFloat(WaterTimeId, now);
        }

        /// <summary>Most characters in water that draw contact ripples at once.</summary>
        public const int MaxMovers = 16;
        private static readonly Vector4[] movers = new Vector4[MaxMovers];
        private static readonly Vector4[] moverVelocities = new Vector4[MaxMovers];
        private static int moverCount;

        private sealed class Contact
        {
            public bool inWater;
            public Vector3 last;
            public float nextIdle;
            public float wakeDistance;
            public float trailDistance;
            public int seenFrame;
        }

        private static readonly RenderTexture[] targets = new RenderTexture[2];
        private static readonly Vector4[] drops = new Vector4[MaxDrops];
        private static readonly Vector4[] bodyRects = new Vector4[MaxBodies];
        private static readonly List<Vector4> pending = new();
        private static readonly Dictionary<GrassInteractor, Contact> contacts = new();
        private static readonly List<GrassInteractor> stale = new();
        private static ComputeShader compute;
        private static int kernel = -1;
        private static int current;
        private static Vector2 origin;
        private static bool hasOrigin;
        private static float accumulator;
        private static float rainCarry;
        private static Vector2 pendingShift;
        private static bool pendingReset;
        private static bool warned;
        private static int lastFrame = -1;

        /// <summary>Queues a ripple (world point, radius, strength) for the next step.</summary>
        public static void AddDrop(Vector3 point, float radius, float strength)
        {
            if (pending.Count >= 512) pending.RemoveAt(0);
            pending.Add(new Vector4(point.x, point.z, Mathf.Max(0.02f, radius), strength));
        }

        internal static void Tick(Camera camera)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;

            float dt = Time.deltaTime;
            UpdateContacts(dt);
            AddRain(dt, camera);
            // Characters standing in water: the water shader draws rings spreading out from their contact outline.
            Shader.SetGlobalVectorArray(MoversId, movers);
            Shader.SetGlobalVectorArray(MoverVelocitiesId, moverVelocities);
            Shader.SetGlobalFloat(MoverCountId, moverCount);
            PublishRings();

            if (camera == null || !EnsureResources())
            {
                Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
                return;
            }

            int resolution = targets[0].width;
            float size = Mathf.Max(2f, Size);
            float texel = size / resolution;
            Vector3 focus = Weather.Controller != null ? Weather.ViewFocus : ViewGroundPoint(camera);
            Vector2 newOrigin = new(
                Mathf.Round((focus.x - size * 0.5f) / texel) * texel,
                Mathf.Round((focus.z - size * 0.5f) / texel) * texel);
            Vector2 shift = hasOrigin ? (newOrigin - origin) / texel : Vector2.zero;
            pendingShift += shift;
            pendingReset |= !hasOrigin || Mathf.Abs(pendingShift.x) >= resolution || Mathf.Abs(pendingShift.y) >= resolution;
            origin = newOrigin;
            hasOrigin = true;

            int bodyCount = 0;
            foreach (PixelWater water in PixelWater.All)
            {
                if (bodyCount >= MaxBodies) break;
                if (water == null || !water.isActiveAndEnabled || !water.Interactive) continue;
                Rect r = water.SurfaceRect;
                bodyRects[bodyCount++] = new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);
            }
            for (int i = bodyCount; i < MaxBodies; i++) bodyRects[i] = Vector4.zero;

            // Fixed steps so the ripple speed does not depend on the frame rate.
            accumulator = Mathf.Min(accumulator + dt, StepSeconds * 4f);
            int steps = 0;
            bool first = true;
            while (accumulator >= StepSeconds || (first && pendingReset))
            {
                accumulator = Mathf.Max(0f, accumulator - StepSeconds);
                int dropCount = first ? FillDrops() : 0;
                // The view scroll collected since the last step is applied once, on the first step.
                Step(resolution, size, first ? pendingShift : Vector2.zero, first && pendingReset, dropCount, bodyCount);
                if (first)
                {
                    pendingShift = Vector2.zero;
                    pendingReset = false;
                }
                first = false;
                if (++steps >= 4) break;
            }

            Shader.SetGlobalTexture(GlobalTexId, targets[current]);
            Shader.SetGlobalVector(GlobalParamsId, new Vector4(origin.x, origin.y, 1f / size, 1f));
            Shader.SetGlobalVector(GlobalTexelId, new Vector4(1f / resolution, size / resolution, 0f, 0f));
        }

        private static void Step(int resolution, float size, Vector2 shift, bool reset, int dropCount, int bodyCount)
        {
            int next = 1 - current;
            compute.SetVector(ParamsId, new Vector4(origin.x, origin.y, size, reset ? 0f : Mathf.Clamp(Damping, 0.8f, 0.999f)));
            compute.SetInts(ShiftId, Mathf.RoundToInt(shift.x), Mathf.RoundToInt(shift.y), 0, 0);
            compute.SetVectorArray(DropsId, drops);
            compute.SetInt(DropCountId, dropCount);
            compute.SetVectorArray(BodiesId, bodyRects);
            compute.SetInt(BodyCountId, bodyCount);
            compute.SetInt(ResolutionId, resolution);
            compute.SetTexture(kernel, PrevId, targets[current]);
            compute.SetTexture(kernel, NextId, targets[next]);
            int groups = (resolution + 7) / 8;
            compute.Dispatch(kernel, groups, groups, 1);
            current = next;
        }

        private static int FillDrops()
        {
            int count = 0;
            while (count < MaxDrops && pending.Count > 0)
            {
                drops[count++] = pending[0];
                pending.RemoveAt(0);
            }
            for (int i = count; i < MaxDrops; i++) drops[i] = Vector4.zero;
            return count;
        }

        private static void UpdateContacts(float dt)
        {
            int frame = Time.frameCount;
            float now = Time.time;
            moverCount = 0;
            foreach (GrassInteractor interactor in GrassInteractor.Active)
            {
                if (interactor == null) continue;
                Vector3 feet = interactor.Position;
                feet.y = interactor.FeetHeight;
                if (!contacts.TryGetValue(interactor, out Contact contact))
                {
                    contact = new Contact { last = feet };
                    contacts[interactor] = contact;
                }
                contact.seenFrame = frame;

                bool inWater = false;
                PixelWater body = null;
                foreach (PixelWater water in PixelWater.All)
                {
                    if (water == null || !water.isActiveAndEnabled || !water.Interactive) continue;
                    if (!water.SurfaceRect.Contains(new Vector2(feet.x, feet.z))) continue;
                    if (feet.y > water.SurfaceHeight - water.ContactDepth) continue;
                    // Only while the body reaches the surface (not deep under it, e.g. a sunken object).
                    if (interactor.Position.y < water.SurfaceHeight - 3f) continue;
                    inWater = true;
                    body = water;
                    break;
                }

                float radius = interactor.Radius;
                if (inWater && body != null && moverCount < MaxMovers)
                {
                    Vector3 step = feet - contact.last;
                    Vector2 velocity = dt > 0.0001f && step.sqrMagnitude < 9f ? new Vector2(step.x, step.z) / dt : Vector2.zero;
                    movers[moverCount] = new Vector4(feet.x, body.SurfaceHeight, feet.z, radius);
                    moverVelocities[moverCount] = new Vector4(velocity.x, velocity.y, velocity.magnitude, 0f);
                    moverCount++;
                }

                if (inWater != contact.inWater)
                {
                    PixelWater at = body;
                    if (at == null) Water.TryGetBody(feet, 1f, out at);
                    float surface = at != null ? at.SurfaceHeight : feet.y;
                    Vector3 point = new(feet.x, surface, feet.z);
                    float strength = at != null ? at.SplashStrength : 1f;
                    AddDrop(point, radius * 0.8f, strength);
                    // Falling in makes a bigger splash (the faster, the bigger); walking out a small ring.
                    float fall = dt > 0.0001f ? Mathf.Max(0f, (contact.last.y - feet.y) / dt) : 0f;
                    float splashSize = at != null ? at.SplashSize : 1f;
                    if (inWater && strength > 0f)
                        AddSplash(point, radius * splashSize * (1f + Mathf.Min(fall * 0.08f, 1f)), strength * (0.7f + Mathf.Min(fall * 0.1f, 0.8f)));
                    else AddRing(point, radius * 0.5f, 0.5f, 0.7f, strength * 0.5f);
                    if (at != null && at.SplashEffect != null)
                        UnityEngine.Object.Instantiate(at.SplashEffect, point, Quaternion.identity);
                    if (inWater) Water.RaiseEntered(interactor.gameObject, point);
                    else Water.RaiseExited(interactor.gameObject, point);
                    contact.inWater = inWater;
                    contact.nextIdle = now + (at != null ? at.IdleRippleSeconds : 1.4f);
                }
                else if (inWater && body != null)
                {
                    Vector3 move = feet - contact.last;
                    move.y = 0f;
                    float distance = move.magnitude;
                    if (distance > 0.001f && distance < 3f)
                    {
                        float speed = dt > 0.0001f ? distance / dt : 0f;
                        contact.wakeDistance += distance;
                        contact.trailDistance += distance;
                        // Trail: a ring left behind every short step, spreading out and fading.
                        if (contact.trailDistance >= body.TrailSpacing && body.TrailStrength > 0f)
                        {
                            contact.trailDistance = 0f;
                            AddRing(new Vector3(feet.x, body.SurfaceHeight, feet.z), radius * body.TrailStartSize,
                                body.TrailSpreadSpeed + Mathf.Min(speed, 4f) * body.TrailSpeedBoost, body.TrailLifetime,
                                Mathf.Clamp(body.TrailStrength * (0.6f + speed * 0.15f), 0.05f, 1f));
                        }
                        // A wake ring every short distance, stronger when faster.
                        float spacing = Mathf.Max(0.08f, radius * 0.35f);
                        if (contact.wakeDistance >= spacing)
                        {
                            contact.wakeDistance = 0f;
                            AddDrop(new Vector3(feet.x, body.SurfaceHeight, feet.z), radius * 0.55f,
                                body.WakeStrength * Mathf.Clamp(speed * 0.35f, 0.25f, 1.5f));
                        }
                        contact.nextIdle = now + body.IdleRippleSeconds;
                    }
                    else if (body.IdleRippleSeconds > 0f && now >= contact.nextIdle)
                    {
                        contact.nextIdle = now + body.IdleRippleSeconds;
                        AddDrop(new Vector3(feet.x, body.SurfaceHeight, feet.z), radius * 0.5f, 0.25f);
                    }
                }
                contact.last = feet;
            }

            stale.Clear();
            foreach (KeyValuePair<GrassInteractor, Contact> pair in contacts)
                if (pair.Key == null || pair.Value.seenFrame != frame) stale.Add(pair.Key);
            foreach (GrassInteractor key in stale) contacts.Remove(key);
        }

        // Rain rings: random drops inside the water surfaces that overlap the simulated square.
        private static void AddRain(float dt, Camera camera)
        {
            float rain = Weather.Current.rain;
            if (rain <= 0.01f) return;
            float size = Mathf.Max(2f, Size);
            Vector2 corner = origin;
            if (!hasOrigin)
            {
                if (camera == null) return;
                Vector3 focus = Weather.Controller != null ? Weather.ViewFocus : ViewGroundPoint(camera);
                corner = new Vector2(focus.x - size * 0.5f, focus.z - size * 0.5f);
            }
            Rect square = new(corner.x, corner.y, size, size);
            foreach (PixelWater water in PixelWater.All)
            {
                if (water == null || !water.isActiveAndEnabled || !water.Interactive) continue;
                Rect r = water.SurfaceRect;
                float xMin = Mathf.Max(r.xMin, square.xMin), xMax = Mathf.Min(r.xMax, square.xMax);
                float zMin = Mathf.Max(r.yMin, square.yMin), zMax = Mathf.Min(r.yMax, square.yMax);
                if (xMax <= xMin || zMax <= zMin) continue;
                float area = (xMax - xMin) * (zMax - zMin);
                rainCarry += area * water.RainDropsPerUnit * rain * dt;
                int count = Mathf.Min(24, Mathf.FloorToInt(rainCarry));
                rainCarry -= count;
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = new(UnityEngine.Random.Range(xMin, xMax), water.SurfaceHeight, UnityEngine.Random.Range(zMin, zMax));
                    AddDrop(p, UnityEngine.Random.Range(0.06f, 0.12f), UnityEngine.Random.Range(0.25f, 0.5f));
                    float life = water.RainRingLifetime * UnityEngine.Random.Range(0.8f, 1.2f);
                    float grow = water.RainRingSize * UnityEngine.Random.Range(0.75f, 1.2f) / Mathf.Max(0.05f, life);
                    PushRing(p, 0.01f, grow, life, UnityEngine.Random.Range(0.55f, 0.9f), 0f, 2f);
                }
            }
            rainCarry = Mathf.Min(rainCarry, 4f);
        }

        private static Vector3 ViewGroundPoint(Camera camera)
        {
            float groundY = PixelWater.All.Count > 0 && PixelWater.All[0] != null ? PixelWater.All[0].SurfaceHeight : 0f;
            Ray ray = ObliqueProjection.ViewportPointToRay(camera, new Vector3(0.5f, 0.5f, 0f));
            Plane plane = new(Vector3.up, new Vector3(0f, groundY, 0f));
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : camera.transform.position;
        }

        private static bool EnsureResources()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                if (!warned) Debug.LogWarning("[Water] Compute shaders unsupported: no ripples.");
                warned = true;
                return false;
            }
            if (compute == null)
            {
                compute = Resources.Load<ComputeShader>(ComputePath);
                kernel = compute != null ? compute.FindKernel("Step") : -1;
                if (kernel < 0)
                {
                    if (!warned) Debug.LogWarning("[Water] Missing Resources/" + ComputePath + ".compute.");
                    warned = true;
                    compute = null;
                    return false;
                }
            }
            int resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Resolution), 64, 1024);
            if (targets[0] != null && targets[0].width == resolution) return true;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    targets[i].Release();
                    UnityEngine.Object.DestroyImmediate(targets[i]);
                }
                targets[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
                {
                    name = "Water Ripples " + i,
                    enableRandomWrite = true,
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

        public static void Release()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                targets[i].Release();
                UnityEngine.Object.DestroyImmediate(targets[i]);
                targets[i] = null;
            }
            compute = null;
            kernel = -1;
            hasOrigin = false;
            contacts.Clear();
            pending.Clear();
            moverCount = 0;
            rings.Clear();
            rainRings = 0;
            Shader.SetGlobalVector(GlobalParamsId, Vector4.zero);
            Shader.SetGlobalFloat(MoverCountId, 0f);
            Shader.SetGlobalFloat(RingCountId, 0f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            hasOrigin = false;
            contacts.Clear();
            pending.Clear();
            rings.Clear();
            rainRings = 0;
            lastFrame = -1;
            accumulator = 0f;
            pendingShift = Vector2.zero;
            pendingReset = false;
            warned = false;
        }
    }
}
