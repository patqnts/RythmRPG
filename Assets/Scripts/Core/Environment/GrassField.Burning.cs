using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    // Cutting, burning and regrowth for GrassField.
    //
    // Every drawn tuft has one float4 of state on the GPU (_GrassStates, same order as _GrassBlades):
    //   x = height left (0..1 of the tuft), y = time it catches fire (< 0 = never), z = time it starts to regrow
    //   (< 0 = never), w = how charred it is (0..1).
    // The shader animates burning and regrowth from those times on its own, so a tuft only costs CPU time when its
    // state changes: when it is cut, catches fire (and lights its neighbours), burns out or finishes regrowing.
    // Those moments are kept in one time-ordered queue. Fire spreads through a spatial grid of the tuft roots.
    //
    // A second float4 per tuft (_GrassCuts) records its last cut: x = time, y / z = bottom / top of the severed
    // piece (0..1 of the tuft), w = the direction it is thrown (radians). The chunks with a recent cut are drawn
    // again with _GRASS_PIECES, which animates those pieces flying off and lying on the ground.
    public sealed partial class GrassField
    {
        private enum EventKind : byte { Ignite, BurnOut, Cut, Extinguish, RegrowDone }

        private struct StateEvent
        {
            public float Time;
            public int Blade;
            public EventKind Kind;
            public float Stamp; // the state time the event belongs to; stale events are skipped (Cut: flight angle)
            public float Value; // Cut: where it cuts (fraction of the tuft)
        }

        private const int StateStride = sizeof(float) * 4;
        private const int MaxEventsPerFrame = 20000;
        private static readonly Vector4 FreshState = new(1f, -1f, -1f, 0f);

        private GraphicsBuffer stateBuffer;
        private Vector4[] states;
        private Vector3[] roots;
        private float[] bladeTips; // world height of each tuft's visible tip above its root, uncut
        private float[] bladeBases; // world height of each tuft's lowest visible row above its root
        private int[] bladeGroups; // draw group of each tuft
        private float[] groupPieceUntil; // per draw group: until when cut pieces are in flight / on the ground
        private GraphicsBuffer cutBuffer;
        private Vector4[] cuts;
        private int cutDirtyMin = int.MaxValue;
        private int cutDirtyMax = -1;
        private static readonly Vector4 NoCut = new(-1f, 0f, 0f, 0f);
        private Material pieceMaterial;
        private Material flameMaterial;
        private GraphicsBuffer flameBuffer;
        private struct GpuFlame
        {
            public Vector4 PositionSeed; // xyz = world position, w = random
            public Vector4 Data;         // x = size, y = brightness
        }

        private GpuFlame[] flameData = new GpuFlame[0];
        private int flameCount;
        private Bounds flameBounds;
        private bool flameFramesReported;
        private readonly Vector4[] flameFrameRects = new Vector4[32];
        private static readonly int FlamesId = Shader.PropertyToID("_GrassFlames");
        private static readonly int FlameFramesId = Shader.PropertyToID("_FlameFrames");
        private static readonly int FlameInfoId = Shader.PropertyToID("_FlameInfo");
        private static readonly int FlameSizeId = Shader.PropertyToID("_FlameSize");
        private static readonly int FlameTintId = Shader.PropertyToID("_FlameTint");
        private int[] stateSource; // index in `blades` of each GPU slot (to keep the fire going across rebuilds)
        private int stateDirtyMin = int.MaxValue;
        private int stateDirtyMax = -1;
        private readonly Dictionary<long, List<int>> cells = new();
        private float cellSize = 1f;
        private Bounds fieldBounds;
        private readonly List<StateEvent> events = new(); // binary min-heap on Time
        private readonly HashSet<int> burning = new();
        private readonly List<int> query = new();
        private readonly List<int> spreadQuery = new();
        private readonly Dictionary<Sprite, Vector2> visibleRowsCache = new();
        private Light fireLight;
        private int cutsThisFrame;
        private Vector3 cutSumThisFrame;

        /// <summary>Tufts burning or still glowing right now.</summary>
        public int BurningCount => burning.Count;

        /// <summary>World bounds of the drawn tufts (empty before the first build).</summary>
        public Bounds FieldBounds => fieldBounds;

        // ---------- Public API (see also the Grass facade) ----------

        /// <summary>Cuts every tuft within <paramref name="radius"/> (XZ) of <paramref name="center"/>.</summary>
        public int CutInRadius(Vector3 center, float radius) =>
            CutAlong(center, center, radius, GrassCutHeight.FieldDefault, Vector3.zero);

        /// <summary>Cuts every tuft within <paramref name="radius"/> of the segment (a sword swing, a mower's path).</summary>
        public int CutAlong(Vector3 from, Vector3 to, float radius) =>
            CutAlong(from, to, radius, GrassCutHeight.FieldDefault, Vector3.zero);

        /// <summary>
        /// Cuts every tuft within <paramref name="radius"/> of the segment at <paramref name="height"/>. Tufts already
        /// shorter than that are left alone. The severed tops fly off along <paramref name="direction"/> (world;
        /// zero = away from the segment).
        /// </summary>
        public int CutAlong(Vector3 from, Vector3 to, float radius, GrassCutHeight height, Vector3 direction)
        {
            if (!PrepareStates() || !cuttable) return 0;
            float now = Time.time;
            QueryCapsule(from, to, radius, query);
            Vector2 push = new(direction.x, direction.z);
            if (push.sqrMagnitude > 0.0001f) push.Normalize();
            float segX = to.x - from.x, segZ = to.z - from.z;
            float lengthSq = segX * segX + segZ * segZ;
            int cut = 0;
            foreach (int i in query)
            {
                // Away from the nearest point of the cut, leaning along the given direction.
                Vector3 p = roots[i];
                float t = lengthSq > 1e-8f ? Mathf.Clamp01(((p.x - from.x) * segX + (p.z - from.z) * segZ) / lengthSq) : 0f;
                Vector2 away = new(p.x - (from.x + segX * t), p.z - (from.z + segZ * t));
                away = away.sqrMagnitude > 0.0001f ? away.normalized : Random.insideUnitCircle.normalized;
                Vector2 flight = push.sqrMagnitude > 0f ? push * 0.75f + away * 0.5f : away;
                if (CutBlade(i, now, CutTarget(i, height), Mathf.Atan2(flight.y, flight.x) + Random.Range(-0.45f, 0.45f)))
                    cut++;
            }
            return cut;
        }

        /// <summary>Sets fire to the tufts within <paramref name="radius"/>; it then spreads on its own.</summary>
        public int IgniteInRadius(Vector3 center, float radius, float delay = 0f)
        {
            if (!PrepareStates() || !burnable) return 0;
            float now = Time.time;
            QueryCapsule(center, center, radius, query);
            int lit = 0;
            foreach (int i in query)
                if (TryIgnite(i, now + Mathf.Max(0f, delay) + Random.Range(0f, 0.12f), now)) lit++;
            return lit;
        }

        /// <summary>Puts out fire (burning and not yet burning) within <paramref name="radius"/>.</summary>
        public int ExtinguishInRadius(Vector3 center, float radius)
        {
            if (!PrepareStates()) return 0;
            float now = Time.time;
            QueryCapsule(center, center, radius, query);
            int count = 0;
            foreach (int i in query)
                if (ExtinguishBlade(i, now)) count++;
            return count;
        }

        /// <summary>Sets fire to the tufts inside <paramref name="area"/> (a cone for a flamethrower, a box, ...).</summary>
        public int IgniteInArea(GrassArea area, float delay = 0f)
        {
            if (!PrepareStates() || !burnable) return 0;
            float now = Time.time;
            QueryArea(area, query);
            int lit = 0;
            foreach (int i in query)
                if (TryIgnite(i, now + Mathf.Max(0f, delay) + Random.Range(0f, 0.12f), now)) lit++;
            return lit;
        }

        /// <summary>Puts out fire inside <paramref name="area"/>.</summary>
        public int ExtinguishInArea(GrassArea area)
        {
            if (!PrepareStates()) return 0;
            float now = Time.time;
            QueryArea(area, query);
            int count = 0;
            foreach (int i in query)
                if (ExtinguishBlade(i, now)) count++;
            return count;
        }

        /// <summary>
        /// Cuts the tufts inside <paramref name="area"/> at <paramref name="height"/>. The tops fly along
        /// <paramref name="direction"/>; zero = along a cone's axis, or away from the shape's origin.
        /// </summary>
        public int CutInArea(GrassArea area, GrassCutHeight height, Vector3 direction = default)
        {
            if (!PrepareStates() || !cuttable) return 0;
            float now = Time.time;
            QueryArea(area, query);
            Vector3 lean = direction.sqrMagnitude > 1e-6f ? direction : area.Direction;
            Vector2 push = new(lean.x, lean.z);
            if (push.sqrMagnitude > 1e-6f) push.Normalize();
            Vector3 origin = area.Origin;
            int cut = 0;
            foreach (int i in query)
            {
                Vector2 away = new(roots[i].x - origin.x, roots[i].z - origin.z);
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Random.insideUnitCircle.normalized;
                Vector2 flight = push.sqrMagnitude > 0f ? push * 0.75f + away * 0.5f : away;
                if (CutBlade(i, now, CutTarget(i, height), Mathf.Atan2(flight.y, flight.x) + Random.Range(-0.45f, 0.45f)))
                    cut++;
            }
            return cut;
        }

        /// <summary>True if a tuft inside <paramref name="area"/> is in flames right now.</summary>
        public bool IsBurningIn(GrassArea area)
        {
            if (states == null || burning.Count == 0) return false;
            float now = Time.time;
            QueryArea(area, query);
            foreach (int i in query)
            {
                Vector4 s = states[i];
                if (s.y >= 0f && now >= s.y && now < s.y + burnDuration) return true;
            }
            return false;
        }

        /// <summary>
        /// Applies a shockwave's effects to the tufts within <paramref name="radius"/>, each when the ring
        /// (moving at <paramref name="speed"/>) reaches it. The push itself is drawn by the interaction map.
        /// </summary>
        public void ApplyShockwave(Vector3 center, float radius, float speed, GrassShockwaveEffect effects)
        {
            GrassShockwave wave = GrassShockwave.Ring(center, radius);
            wave.speed = speed;
            wave.effects = effects;
            wave.effectDistance = radius;
            ApplyShockwave(wave);
        }

        /// <summary>
        /// Applies a shockwave's effects (cut at its cut height, ignite, put out) to the tufts in its effect area,
        /// each when the wave front reaches it. The push itself is drawn by the interaction map.
        /// </summary>
        public void ApplyShockwave(GrassShockwave wave)
        {
            GrassShockwaveEffect effects = wave.effects;
            if (effects == GrassShockwaveEffect.None || !PrepareStates()) return;
            float now = Time.time;
            QueryArea(wave.EffectArea, query);
            float inverseSpeed = 1f / Mathf.Max(0.01f, wave.speed);
            foreach (int i in query)
            {
                float time = now + wave.TravelTo(roots[i]) * inverseSpeed;
                if ((effects & GrassShockwaveEffect.Extinguish) != 0) Push(time, i, EventKind.Extinguish, 0f);
                if ((effects & GrassShockwaveEffect.Cut) != 0 && cuttable)
                    Push(time, i, EventKind.Cut, wave.PushAngle(roots[i]) + Random.Range(-0.3f, 0.3f), CutTarget(i, wave.cutHeight));
                if ((effects & GrassShockwaveEffect.Ignite) != 0 && burnable) TryIgnite(i, time + 0.05f, now);
            }
        }

        /// <summary>True if a tuft within <paramref name="radius"/> (XZ) is in flames right now.</summary>
        public bool IsBurningAt(Vector3 point, float radius)
        {
            if (states == null || burning.Count == 0) return false;
            float now = Time.time;
            QueryCapsule(point, point, radius, query);
            foreach (int i in query)
            {
                Vector4 s = states[i];
                if (s.y >= 0f && now >= s.y && now < s.y + burnDuration) return true;
            }
            return false;
        }

        /// <summary>True if <paramref name="point"/> is over this field (XZ, within its tufts' bounds).</summary>
        public bool ContainsXZ(Vector3 point, float margin = 0.5f)
        {
            if (states == null) return false;
            Vector3 min = fieldBounds.min, max = fieldBounds.max;
            return point.x >= min.x - margin && point.x <= max.x + margin && point.z >= min.z - margin && point.z <= max.z + margin;
        }

        /// <summary>Instantly restores every tuft (uncut, unburnt) and cancels pending fire.</summary>
        public void RestoreAll()
        {
            if (states == null) return;
            for (int i = 0; i < states.Length; i++) states[i] = FreshState;
            for (int i = 0; i < cuts.Length; i++) cuts[i] = NoCut;
            events.Clear();
            burning.Clear();
            MarkState(0);
            MarkState(states.Length - 1);
            MarkCut(0);
            MarkCut(cuts.Length - 1);
            UploadStates();
        }

        // ---------- Build / upload ----------

        private bool PrepareStates()
        {
            if (!Application.isPlaying) return false;
            if (dirty) Rebuild();
            return states != null;
        }

        private void ReleaseStateBuffer()
        {
            stateBuffer?.Release();
            stateBuffer = null;
            cutBuffer?.Release();
            cutBuffer = null;
            flameBuffer?.Release();
            flameBuffer = null;
            flameCount = 0;
        }

        private void ResetStates()
        {
            ReleaseStateBuffer();
            states = null;
            roots = null;
            bladeTips = null;
            bladeBases = null;
            bladeGroups = null;
            groupPieceUntil = null;
            cuts = null;
            stateSource = null;
            events.Clear();
            burning.Clear();
            cells.Clear();
            visibleRowsCache.Clear();
            fieldBounds = default;
            stateDirtyMin = int.MaxValue;
            stateDirtyMax = -1;
        }

        private void ReleaseStates()
        {
            ResetStates();
            if (fireLight == null) return;
            if (Application.isPlaying) Destroy(fireLight.gameObject);
            else DestroyImmediate(fireLight.gameObject);
            fireLight = null;
        }

        private void BuildStates(GpuBlade[] data, int[] source)
        {
            int count = data.Length;
            // Same tufts in the same order (e.g. a setting was tweaked in Play mode): keep the cuts and the fire.
            bool keep = states != null && stateSource != null && stateSource.Length == count
                        && System.Linq.Enumerable.SequenceEqual(stateSource, source);
            if (!keep || cuts == null || cuts.Length != count)
            {
                states = new Vector4[count];
                cuts = new Vector4[count];
                for (int i = 0; i < count; i++)
                {
                    states[i] = FreshState;
                    cuts[i] = NoCut;
                }
                events.Clear();
                burning.Clear();
            }
            groupPieceUntil = new float[groups.Count];
            if (keep)
            {
                // Pieces still in flight keep being drawn.
                float now = Application.isPlaying ? Time.time : 0f;
                for (int i = 0; i < count; i++)
                    if (cuts[i].x >= 0f && now - cuts[i].x < pieceLifetime)
                        groupPieceUntil[bladeGroups[i]] = Mathf.Max(groupPieceUntil[bladeGroups[i]], cuts[i].x + pieceLifetime);
            }
            stateSource = source;
            roots = new Vector3[count];
            for (int i = 0; i < count; i++) roots[i] = data[i].PositionScale;
            stateBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, StateStride);
            stateBuffer.SetData(states);
            stateDirtyMin = int.MaxValue;
            stateDirtyMax = -1;
            cutBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, StateStride);
            cutBuffer.SetData(cuts);
            cutDirtyMin = int.MaxValue;
            cutDirtyMax = -1;

            // Spatial grid of the roots, one cell about the fire's reach.
            cells.Clear();
            cellSize = Mathf.Max(0.5f, spreadRadius);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = roots[i];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                long key = CellKey(Cell(p.x), Cell(p.z));
                if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>(8);
                list.Add(i);
            }
            fieldBounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private void MarkState(int index)
        {
            if (index < stateDirtyMin) stateDirtyMin = index;
            if (index > stateDirtyMax) stateDirtyMax = index;
        }

        private void MarkCut(int index)
        {
            if (index < cutDirtyMin) cutDirtyMin = index;
            if (index > cutDirtyMax) cutDirtyMax = index;
        }

        private void UploadStates()
        {
            if (stateBuffer != null && stateDirtyMax >= 0)
            {
                int start = Mathf.Max(0, stateDirtyMin);
                int end = Mathf.Min(states.Length - 1, stateDirtyMax);
                if (end >= start) stateBuffer.SetData(states, start, start, end - start + 1);
            }
            stateDirtyMin = int.MaxValue;
            stateDirtyMax = -1;
            if (cutBuffer != null && cutDirtyMax >= 0)
            {
                int start = Mathf.Max(0, cutDirtyMin);
                int end = Mathf.Min(cuts.Length - 1, cutDirtyMax);
                if (end >= start) cutBuffer.SetData(cuts, start, start, end - start + 1);
            }
            cutDirtyMin = int.MaxValue;
            cutDirtyMax = -1;
        }

        // Quad rows (v, 0..1) the sprite's grass actually covers: x = lowest, y = highest.
        private Vector2 VisibleRows(Sprite sprite)
        {
            if (visibleRowsCache.TryGetValue(sprite, out Vector2 cached)) return cached;
            Vector2 result = new(0f, 1f);
            Rect rect = sprite.textureRect;
            Texture2D texture = sprite.texture;
            bool found = false;
            if (texture != null && texture.isReadable)
            {
                try
                {
                    int w = Mathf.Max(1, (int)rect.width), h = Mathf.Max(1, (int)rect.height);
                    Color[] pixels = texture.GetPixels((int)rect.x, (int)rect.y, w, h);
                    int low = h, high = -1;
                    for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (pixels[y * w + x].a < 0.5f) continue;
                        low = Mathf.Min(low, y);
                        high = Mathf.Max(high, y);
                        break;
                    }
                    if (high >= 0)
                    {
                        result = new Vector2(low / (float)h, (high + 1) / (float)h);
                        found = true;
                    }
                }
                catch (System.Exception) { } // compressed or packed textures: fall back to the mesh
            }
            if (!found)
            {
                // Sprite mesh vertices (a Tight sprite hugs its opaque pixels), relative to the pivot, in units.
                Vector2[] vertices = sprite.vertices;
                float height = rect.height / Mathf.Max(0.0001f, sprite.pixelsPerUnit);
                float pivot = sprite.pivot.y / Mathf.Max(1f, sprite.rect.height);
                if (vertices != null && vertices.Length > 0 && height > 0f)
                {
                    float low = float.MaxValue, high = float.MinValue;
                    foreach (Vector2 v in vertices)
                    {
                        low = Mathf.Min(low, v.y);
                        high = Mathf.Max(high, v.y);
                    }
                    result = new Vector2(Mathf.Clamp01(pivot + low / height), Mathf.Clamp01(pivot + high / height));
                }
            }
            if (result.y - result.x < 0.01f) result = new Vector2(0f, 1f);
            visibleRowsCache[sprite] = result;
            return result;
        }

        // ---------- Simulation ----------

        private void SimulateStates(float now, float deltaTime)
        {
            if (states == null) return;
            int handled = 0;
            while (events.Count > 0 && events[0].Time <= now && handled++ < MaxEventsPerFrame)
            {
                StateEvent e = Pop();
                switch (e.Kind)
                {
                    case EventKind.Ignite: HandleIgnite(e, now); break;
                    case EventKind.BurnOut: HandleBurnOut(e, now); break;
                    case EventKind.Cut: CutBlade(e.Blade, now, e.Value, e.Stamp); break;
                    case EventKind.Extinguish: ExtinguishBlade(e.Blade, now); break;
                    case EventKind.RegrowDone: HandleRegrowDone(e); break;
                }
            }
            if (cutsThisFrame > 0)
            {
                Grass.RaiseBladesCut(cutSumThisFrame / cutsThisFrame, cutsThisFrame);
                cutsThisFrame = 0;
                cutSumThisFrame = Vector3.zero;
            }
            UpdateFire(now, deltaTime);
            UploadStates();
        }

        // Height left and char of a tuft right now (the same maths as GrassBurnData in PixelGrass.shader).
        private void Evaluate(int i, float now, out float height, out float charAmount)
        {
            Vector4 s = states[i];
            height = s.x;
            charAmount = s.w;
            if (s.y >= 0f && now >= s.y)
            {
                float p = Mathf.Clamp01((now - s.y) / Mathf.Max(0.001f, burnDuration));
                height = Mathf.Lerp(s.x, Mathf.Min(s.x, ashHeight), p);
                charAmount = Mathf.Max(charAmount, Mathf.Clamp01(p * 3f));
            }
            if (s.z >= 0f)
            {
                float r = Mathf.Clamp01((now - s.z) / Mathf.Max(0.001f, regrowDuration));
                height = Mathf.Lerp(height, 1f, r);
                charAmount *= 1f - r;
            }
        }

        // Where a cut at `height` goes through tuft i, as a fraction of it (>= 1 = above it: no cut).
        private float CutTarget(int i, GrassCutHeight height)
        {
            float span = Mathf.Max(0.001f, bladeTips[i] - bladeBases[i]);
            float fraction;
            switch (height.mode)
            {
                case GrassCutMode.AboveGround: fraction = (height.value - bladeBases[i]) / span; break;
                case GrassCutMode.WorldHeight: fraction = (height.value - roots[i].y - bladeBases[i]) / span; break;
                case GrassCutMode.Fraction: fraction = height.value; break;
                default: fraction = stubbleHeight * Random.Range(0.85f, 1.15f); break;
            }
            return Mathf.Max(minStubble, fraction);
        }

        // World height (above the root) of a fraction of tuft i.
        private float HeightOf(int i, float fraction) =>
            bladeTips == null ? fraction * 0.5f : Mathf.Lerp(bladeBases[i], bladeTips[i], fraction);

        private bool CutBlade(int i, float now, float target, float flightAngle)
        {
            if (!cuttable) return false;
            Vector4 s = states[i];
            if (s.y >= 0f && now >= s.y) return false; // burning or burnt: nothing to cut
            Evaluate(i, now, out float height, out float charAmount);
            target = Mathf.Clamp01(target);
            if (height <= target + 0.03f) return false; // the cut passes above what is left

            bool pendingFire = s.y > now;
            float regrowStart = regrow && !pendingFire ? now + regrowDelay : -1f;
            states[i] = new Vector4(target, pendingFire ? s.y : -1f, regrowStart, charAmount);
            MarkState(i);
            if (regrowStart >= 0f) Push(regrowStart + regrowDuration, i, EventKind.RegrowDone, regrowStart);

            if (cutPieces)
            {
                cuts[i] = new Vector4(now, target, height, flightAngle);
                MarkCut(i);
                int group = bladeGroups[i];
                groupPieceUntil[group] = Mathf.Max(groupPieceUntil[group], now + pieceLifetime);
            }

            cutsThisFrame++;
            cutSumThisFrame += roots[i];
            Vector3 cutPoint = roots[i] + Vector3.up * HeightOf(i, target);
            if (cutEffect != null) GrassEffects.EmitCustom(cutEffect, cutPoint, burstCount);
            else if (particleEffects && clippingSpecks > 0) EmitClippings(i, target, height);
            return true;
        }

        private bool TryIgnite(int i, float time, float now)
        {
            if (!burnable) return false;
            Vector4 s = states[i];
            if (s.y >= 0f)
            {
                // Already burning / burnt, or already due to catch fire sooner.
                if (s.y <= now || time >= s.y) return false;
                s.y = time;
                states[i] = s;
                MarkState(i);
                Push(time, i, EventKind.Ignite, time);
                return true;
            }
            Evaluate(i, now, out float height, out float charAmount);
            if (charAmount >= 0.5f || height <= ashHeight + 0.01f) return false; // nothing left to burn
            if (burning.Count >= maxBurning) return false;
            states[i] = new Vector4(height, time, -1f, charAmount);
            MarkState(i);
            Push(time, i, EventKind.Ignite, time);
            return true;
        }

        private void HandleIgnite(StateEvent e, float now)
        {
            int i = e.Blade;
            if (states[i].y != e.Stamp) return;
            burning.Add(i);
            Push(e.Time + burnDuration + emberTime, i, EventKind.BurnOut, e.Stamp);
            Spread(i, e.Time, now);
            if (igniteEffect != null) GrassEffects.EmitCustom(igniteEffect, FlamePoint(i, now), burstCount);
            else if (particleEffects && burningEffect == null) EmitFireParticle(i, now, true);
        }

        private void Spread(int source, float time, float now)
        {
            if (spreadChance <= 0f || burning.Count >= maxBurning) return;
            Vector3 p = roots[source];
            Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized : Vector2.zero;
            QueryCapsule(p, p, spreadRadius, spreadQuery);
            foreach (int j in spreadQuery)
            {
                if (j == source || Random.value > spreadChance) continue;
                float dx = roots[j].x - p.x, dz = roots[j].z - p.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float along = distance > 0.0001f ? (dx * wind.x + dz * wind.y) / distance : 0f;
                float speed = spreadSpeed * Mathf.Max(0.2f, 1f + windSpread * along * 1.5f);
                float delay = Mathf.Max(0.05f, distance / speed * Random.Range(0.75f, 1.35f));
                TryIgnite(j, time + delay, now);
            }
        }

        private void HandleBurnOut(StateEvent e, float now)
        {
            int i = e.Blade;
            if (states[i].y != e.Stamp) return;
            burning.Remove(i);
            Evaluate(i, now, out float height, out _);
            Settle(i, now, height, 1f);
            if (burnOutEffect != null) GrassEffects.EmitCustom(burnOutEffect, roots[i] + Vector3.up * HeightOf(i, height), burstCount);
            else if (particleEffects && Random.value < 0.35f) EmitSmoke(i, height);
        }

        private bool ExtinguishBlade(int i, float now)
        {
            Vector4 s = states[i];
            if (s.y < 0f) return false;
            Evaluate(i, now, out float height, out float charAmount);
            burning.Remove(i);
            Settle(i, now, height, charAmount);
            if (particleEffects && s.y <= now && Random.value < 0.5f) EmitSmoke(i, height);
            return true;
        }

        // Leaves a tuft at rest (not burning) and, if it is damaged, schedules its regrowth.
        private void Settle(int i, float now, float height, float charAmount)
        {
            bool damaged = height < 0.999f || charAmount > 0.001f;
            float regrowStart = regrow && damaged ? now + regrowDelay : -1f;
            states[i] = damaged ? new Vector4(height, -1f, regrowStart, charAmount) : FreshState;
            MarkState(i);
            if (regrowStart >= 0f) Push(regrowStart + regrowDuration, i, EventKind.RegrowDone, regrowStart);
        }

        private void HandleRegrowDone(StateEvent e)
        {
            int i = e.Blade;
            Vector4 s = states[i];
            if (s.z != e.Stamp || s.y >= 0f) return;
            states[i] = FreshState;
            MarkState(i);
        }

        // ---------- Fire: light, flame sprites, particles ----------

        private void UpdateFire(float now, float deltaTime)
        {
            int flames = 0;
            Vector3 sum = Vector3.zero;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            bool effects = !SceneVisibility.WorldHidden;
            bool builtIn = effects && particleEffects && burningEffect == null;
            bool custom = effects && burningEffect != null && burningEffectRate > 0f;
            bool sprites = HasFlameFrames();
            flameCount = 0;
            foreach (int i in burning)
            {
                Vector4 s = states[i];
                bool flaming = now < s.y + burnDuration;
                if (flaming)
                {
                    flames++;
                    sum += roots[i];
                    min = Vector3.Min(min, roots[i]);
                    max = Vector3.Max(max, roots[i]);
                    if (sprites) AddFlame(i, now, s);
                    if (custom && Random.value < deltaTime * burningEffectRate)
                        GrassEffects.EmitCustom(burningEffect, FlamePoint(i, now), 1);
                }
                if (!builtIn) continue;
                if (flaming && Random.value < deltaTime * 5f) EmitFireParticle(i, now, false);
                else if (!flaming && Random.value < deltaTime * 0.6f) EmitSmoke(i, ashHeight);
            }
            UpdateFireLight(flames, flames > 0 ? sum / flames : Vector3.zero, min, max, now);
        }

        // Top of what is still standing of tuft i (where the flames are).
        private Vector3 FlamePoint(int i, float now)
        {
            Evaluate(i, now, out float height, out _);
            return roots[i] + Vector3.up * HeightOf(i, height);
        }

        private bool HasFlameFrames() =>
            flameFrames != null && flameFrames.Length > 0 && flameFrames[0] != null && flameFrames[0].texture != null;

        // One flipbook flame standing on the burning edge; it flares up, then dies down as the tuft is consumed.
        private void AddFlame(int i, float now, Vector4 s)
        {
            float progress = Mathf.Clamp01((now - s.y) / Mathf.Max(0.001f, burnDuration));
            float size = flameScale * Mathf.SmoothStep(0.35f, 1f, Mathf.Clamp01(progress / 0.15f))
                         * (1f - 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, progress)));
            Vector3 p = FlamePoint(i, now) - Vector3.up * (1f / Mathf.Max(1f, pixelsPerUnit));
            if (flameData.Length <= flameCount) System.Array.Resize(ref flameData, Mathf.Max(64, flameData.Length * 2));
            float seed = Mathf.Repeat(i * 0.6180339f, 1f);
            flameData[flameCount] = new GpuFlame
            {
                PositionSeed = new Vector4(p.x, p.y, p.z, seed),
                Data = new Vector4(size, 1f, 0f, 0f)
            };
            if (flameCount == 0) flameBounds = new Bounds(p, Vector3.zero);
            else flameBounds.Encapsulate(p);
            flameCount++;
        }

        private void DrawFlames(Vector3 right)
        {
            if (flameCount == 0 || !HasFlameFrames() || bladeMesh == null) return;
            if (flameMaterial == null)
            {
                Shader shader = Resources.Load<Shader>(FlameShaderPath);
                if (shader == null) return;
                flameMaterial = new Material(shader) { name = "Grass Flames (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            if (flameBuffer == null || flameBuffer.count < flameData.Length)
            {
                flameBuffer?.Release();
                flameBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(64, flameData.Length), StateStride * 2);
            }
            flameBuffer.SetData(flameData, 0, 0, flameCount);

            // Frames: every sprite from the same texture as the first one (a sprite sheet).
            Sprite first = flameFrames[0];
            Texture2D texture = first.texture;
            int frames = 0;
            foreach (Sprite frame in flameFrames)
            {
                if (frame == null || frames >= flameFrameRects.Length) continue;
                if (frame.texture != texture)
                {
                    if (!flameFramesReported)
                        Debug.LogWarning("[Grass] Flame Frames must all come from one texture (sprite sheet); the others are skipped.", this);
                    flameFramesReported = true;
                    continue;
                }
                Rect r = frame.textureRect;
                flameFrameRects[frames++] = new Vector4(r.x / texture.width, r.y / texture.height, r.width / texture.width, r.height / texture.height);
            }
            Rect rect = first.textureRect;
            float ppu = Mathf.Max(0.0001f, first.pixelsPerUnit);
            Vector2 size = rect.size / ppu;
            Vector2 pivot = new(first.pivot.x / Mathf.Max(1f, first.rect.width), first.pivot.y / Mathf.Max(1f, first.rect.height));

            flameMaterial.SetBuffer(FlamesId, flameBuffer);
            flameMaterial.SetTexture(BaseMapId, texture);
            flameMaterial.SetVectorArray(FlameFramesId, flameFrameRects);
            flameMaterial.SetVector(FlameInfoId, new Vector4(frames, flameFrameRate, flameCutoff, snapBendToPixels ? pixelsPerUnit : 0f));
            flameMaterial.SetVector(FlameSizeId, new Vector4(size.x, size.y, pivot.x, pivot.y));
            flameMaterial.SetColor(FlameTintId, flameTint);
            flameMaterial.SetVector(RightId, right);
            flameMaterial.SetVector(GrassTimeId, new Vector4(Time.time, 0f, 0f, 0f));

            Bounds bounds = flameBounds;
            bounds.Expand(Mathf.Max(size.x, size.y) * flameScale * 2f + 0.5f);
            var rp = new RenderParams(flameMaterial)
            {
                layer = gameObject.layer,
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off,
                reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off,
                worldBounds = bounds
            };
            Graphics.RenderMeshPrimitives(rp, bladeMesh, 0, flameCount);
        }

        private void UpdateFireLight(int flames, Vector3 center, Vector3 min, Vector3 max, float now)
        {
            bool wanted = flames > 0 && fireLightIntensity > 0f && !SceneVisibility.WorldHidden;
            if (!wanted)
            {
                if (fireLight != null) fireLight.enabled = false;
                return;
            }
            if (fireLight == null)
            {
                var go = new GameObject("Grass Fire Light") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                fireLight = go.AddComponent<Light>();
                fireLight.type = LightType.Point;
                fireLight.shadows = LightShadows.None;
            }
            float extent = Mathf.Max(max.x - min.x, max.z - min.z) * 0.5f;
            float flicker = 0.82f + 0.18f * Mathf.PerlinNoise(now * 7f, 0.37f);
            Color color = flameColor;
            float peak = Mathf.Max(0.0001f, color.maxColorComponent);
            fireLight.enabled = true;
            fireLight.transform.position = center + Vector3.up * 0.7f;
            fireLight.range = extent + 3f;
            fireLight.color = new Color(color.r / peak, color.g / peak, color.b / peak, 1f);
            fireLight.intensity = fireLightIntensity * Mathf.Clamp01(0.3f + flames / 15f) * flicker;
        }

        private float PixelSize => 1f / Mathf.Max(1f, pixelsPerUnit);

        private void EmitClippings(int i, float fromHeight, float toHeight)
        {
            for (int n = 0; n < clippingSpecks; n++)
            {
                Vector3 position = roots[i] + Vector3.up * HeightOf(i, Random.Range(fromHeight, toHeight))
                    + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.05f, 0.05f));
                Vector2 side = Random.insideUnitCircle.normalized * Random.Range(0.5f, 1.6f);
                Color color = Color.Lerp(clippingColor, clippingColor * 0.7f, Random.value);
                color.a = 1f;
                if (!GrassEffects.Emit(GrassEffects.Kind.Clipping, position, new Vector3(side.x, Random.Range(1.4f, 2.6f), side.y),
                        PixelSize * Random.Range(1f, 2.2f), Random.Range(0.4f, 0.75f), color, effectMaterial)) return;
            }
        }

        private void EmitFireParticle(int i, float now, bool withSmoke)
        {
            Evaluate(i, now, out float height, out _);
            Vector3 position = roots[i] + Vector3.up * (HeightOf(i, height) + PixelSize * 2f)
                + new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.04f, 0.04f));
            Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized * (windStrength * 4f) : Vector2.zero;
            Color flame = flameColor;
            float peak = Mathf.Max(1f, flame.maxColorComponent);
            Color ember = Color.Lerp(flame / peak, (Color)flameTipColor / Mathf.Max(1f, flameTipColor.maxColorComponent), Random.value);
            ember.a = 1f;
            GrassEffects.Emit(GrassEffects.Kind.Ember, position,
                new Vector3(wind.x + Random.Range(-0.3f, 0.3f), Random.Range(0.7f, 1.6f), wind.y + Random.Range(-0.3f, 0.3f)),
                PixelSize * Random.Range(1f, 1.6f), Random.Range(0.4f, 1.1f), ember, effectMaterial);
            if (withSmoke || Random.value < 0.3f) EmitSmoke(i, height);
        }

        private void EmitSmoke(int i, float height)
        {
            Vector2 wind = windDirection.sqrMagnitude > 0.0001f ? windDirection.normalized * (windStrength * 3f) : Vector2.zero;
            float grey = Random.Range(0.16f, 0.3f);
            GrassEffects.Emit(GrassEffects.Kind.Smoke, roots[i] + Vector3.up * (HeightOf(i, height) + PixelSize * 3f),
                new Vector3(wind.x + Random.Range(-0.1f, 0.1f), Random.Range(0.45f, 0.9f), wind.y + Random.Range(-0.1f, 0.1f)),
                PixelSize * Random.Range(2f, 3.5f), Random.Range(0.9f, 1.8f), new Color(grey, grey, grey * 0.95f, 1f), effectMaterial);
        }

        // ---------- Spatial grid ----------

        private int Cell(float value) => Mathf.FloorToInt(value / cellSize);

        private static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        // Tufts inside an area (grid cells under its XZ bounds, then the exact shape test).
        private void QueryArea(GrassArea area, List<int> result)
        {
            result.Clear();
            if (states == null) return;
            area.GetBoundsXZ(out float minX, out float minZ, out float maxX, out float maxZ);
            int x0 = Cell(minX), x1 = Cell(maxX), z0 = Cell(minZ), z1 = Cell(maxZ);
            long cellCount = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            if (cellCount > cells.Count)
            {
                for (int i = 0; i < roots.Length; i++)
                    if (area.ContainsTuft(roots[i], bladeTips[i])) result.Add(i);
                return;
            }
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!cells.TryGetValue(CellKey(x, z), out List<int> list)) continue;
                foreach (int i in list)
                    if (area.ContainsTuft(roots[i], bladeTips[i])) result.Add(i);
            }
        }

        // Tufts within radius (XZ) of the segment a-b (a point when a == b).
        private void QueryCapsule(Vector3 a, Vector3 b, float radius, List<int> result)
        {
            result.Clear();
            if (states == null || radius <= 0f) return;
            int x0 = Cell(Mathf.Min(a.x, b.x) - radius), x1 = Cell(Mathf.Max(a.x, b.x) + radius);
            int z0 = Cell(Mathf.Min(a.z, b.z) - radius), z1 = Cell(Mathf.Max(a.z, b.z) + radius);
            float segX = b.x - a.x, segZ = b.z - a.z;
            float lengthSq = segX * segX + segZ * segZ;
            float radiusSq = radius * radius;

            long cellCount = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            if (cellCount > cells.Count)
            {
                // A huge area: faster to test every tuft than to walk empty cells.
                for (int i = 0; i < roots.Length; i++)
                    if (InCapsule(roots[i], a, segX, segZ, lengthSq, radiusSq)) result.Add(i);
                return;
            }
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!cells.TryGetValue(CellKey(x, z), out List<int> list)) continue;
                foreach (int i in list)
                    if (InCapsule(roots[i], a, segX, segZ, lengthSq, radiusSq)) result.Add(i);
            }
        }

        private static bool InCapsule(Vector3 p, Vector3 a, float segX, float segZ, float lengthSq, float radiusSq)
        {
            float px = p.x - a.x, pz = p.z - a.z;
            float t = lengthSq > 1e-8f ? Mathf.Clamp01((px * segX + pz * segZ) / lengthSq) : 0f;
            float dx = px - segX * t, dz = pz - segZ * t;
            return dx * dx + dz * dz <= radiusSq;
        }

        // ---------- Event queue (binary min-heap) ----------

        private void Push(float time, int blade, EventKind kind, float stamp, float value = 0f)
        {
            events.Add(new StateEvent { Time = time, Blade = blade, Kind = kind, Stamp = stamp, Value = value });
            int child = events.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) >> 1;
                if (events[parent].Time <= events[child].Time) break;
                (events[parent], events[child]) = (events[child], events[parent]);
                child = parent;
            }
        }

        private StateEvent Pop()
        {
            StateEvent top = events[0];
            int last = events.Count - 1;
            events[0] = events[last];
            events.RemoveAt(last);
            int parent = 0;
            int count = events.Count;
            while (true)
            {
                int left = parent * 2 + 1;
                if (left >= count) break;
                int right = left + 1;
                int smallest = right < count && events[right].Time < events[left].Time ? right : left;
                if (events[parent].Time <= events[smallest].Time) break;
                (events[parent], events[smallest]) = (events[smallest], events[parent]);
                parent = smallest;
            }
            return top;
        }
    }
}
