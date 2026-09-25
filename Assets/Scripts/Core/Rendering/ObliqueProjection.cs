using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>
    /// Eastward-style oblique projection for a tilted orthographic camera. The 3D world is not changed; only the
    /// camera's projection matrix is.
    /// <para>
    /// A tilted ortho camera shrinks the ground by sin(pitch) and walls by cos(pitch) (at 35 degrees: ground 57%, walls
    /// 82%). This component shears and scales the projection so that 1 unit of ground depth covers
    /// <see cref="floorScale"/> units of screen height and 1 unit of wall height covers <see cref="wallScale"/> units,
    /// the same as 1 unit of X. Depth is left unchanged, so the depth test, sorting and culling keep working.
    /// </para>
    /// <para>
    /// Upright things (walls, grass, sprites with rotation 0) are then drawn at <see cref="wallScale"/>. Things tilted to
    /// face the camera would be drawn taller (1.39x at 35 degrees), so in Play mode camera-facing sprites are stood
    /// upright by <see cref="ObliqueBillboard"/> while this is active (see <see cref="autoUprightSprites"/>).
    /// </para>
    /// <para>Added by Tools > Rythm RPG > Rendering > Add Oblique Projection To Pixel Camera.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(9990)] // after CinemachineBrain / camera shake, before CrispWorldUICamera (10000)
    public sealed class ObliqueProjection : MonoBehaviour
    {
        [Tooltip("On-screen size of 1 unit of ground depth. 1 = same as X, no squash.")]
        [SerializeField, Range(0.25f, 2f)] private float floorScale = 1f;
        [Tooltip("On-screen size of 1 unit of wall height. 1 = same as X, no squash.")]
        [SerializeField, Range(0.25f, 2f)] private float wallScale = 1f;
        [Tooltip("World Y of the ground. The ground point at the centre of the view stays put, so Cinemachine framing is kept.")]
        [SerializeField] private float groundHeight;

        [Header("Play mode")]
        [Tooltip("Find sprites, meshes and local-space particle systems that are tilted to face the camera and give " +
                 "them an ObliqueBillboard, which stands them upright for rendering so they keep their size. " +
                 "Turning this off only stops new ones being added.")]
        [SerializeField] private bool autoUprightSprites = true;
        [Tooltip("Particle systems whose particles face the camera (Billboard or Mesh, Render Alignment View/Facing) " +
                 "are switched to face the camera's direction upright instead, so they are not stretched. Restored when " +
                 "this component is turned off.")]
        [SerializeField] private bool fixCameraFacingParticles = true;
        [Tooltip("An object counts as camera-facing when its forward is within this angle of the camera's forward.")]
        [SerializeField, Range(0.5f, 20f)] private float cameraFacingTolerance = 5f;

        private const float SeenResetInterval = 10f;
        private static readonly List<ObliqueProjection> Instances = new();

        private Camera cam;
        private float nextSeenReset;
        private int preparedFrame = -1;
        private readonly List<Renderer> scanBuffer = new();
        // Renderers already looked at (billboarded, fixed or left alone), so each frame only new ones cost anything.
        private readonly HashSet<Renderer> seenRenderers = new();
        private readonly Dictionary<ParticleSystemRenderer, ParticleSetup> fixedParticles = new();

        private struct ParticleSetup
        {
            public ParticleSystemRenderMode RenderMode;
            public ParticleSystemRenderSpace Alignment;
        }

        public float FloorScale => floorScale;
        public float WallScale => wallScale;
        /// <summary>World Y of the ground (the pixel grid of <see cref="PixelPerfectRig"/> is anchored on it).</summary>
        public float GroundHeight => groundHeight;

        // ---------- Static helpers (fall back to plain camera maths when no oblique projection is active) ----------

        /// <summary>The enabled oblique projection on <paramref name="camera"/>, if any.</summary>
        public static bool TryGet(Camera camera, out ObliqueProjection projection)
        {
            projection = null;
            if (camera == null) return false;
            foreach (ObliqueProjection candidate in Instances)
            {
                if (candidate == null || candidate.cam != camera || !candidate.isActiveAndEnabled) continue;
                projection = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Rotation for world-space quads (UI canvases, sprites) that should read as flat 2D on screen: upright and
        /// turned toward the camera when oblique (drawn at <see cref="WallScale"/>), otherwise the camera's rotation.
        /// </summary>
        public static Quaternion BillboardRotation(Camera camera)
        {
            if (camera == null) return Quaternion.identity;
            return TryGet(camera, out _) ? UprightRotation(camera.transform) : camera.transform.rotation;
        }

        /// <summary>
        /// World direction along which a point keeps the same screen position (the orthographic "view ray"). With an
        /// oblique projection this is not the camera's forward.
        /// </summary>
        public static Vector3 ViewDirection(Camera camera)
        {
            if (camera == null) return Vector3.forward;
            if (!camera.orthographic || !TryGet(camera, out ObliqueProjection projection))
                return camera.transform.forward;
            projection.Coefficients(out float alpha, out float beta);
            // View space: x' = x, y' = alpha*y + beta*z. Unchanged along (0, beta, -alpha); Unity view space looks down -z.
            Vector3 view = new Vector3(0f, beta, -alpha).normalized;
            return camera.cameraToWorldMatrix.MultiplyVector(view).normalized;
        }

        // ---------- Viewport <-> world (use these instead of Camera.ViewportPointToRay & co.) ----------
        // Unity's own Camera.ViewportPointToRay / ViewportToWorldPoint did not match the sheared matrix in play (combat
        // was laid out off screen), so these unproject through the camera's actual matrices instead. With no oblique
        // projection active they are the plain Unity calls.

        /// <summary>Ray through a viewport point (0-1), exact for the oblique projection.</summary>
        public static Ray ViewportPointToRay(Camera camera, Vector3 viewport)
        {
            if (!TryGet(camera, out _)) return camera.ViewportPointToRay(viewport);
            return RayThrough(camera.projectionMatrix * camera.worldToCameraMatrix, viewport);
        }

        /// <summary>Viewport position (0-1) of a world point; z is its distance in front of the camera.</summary>
        public static Vector3 WorldToViewportPoint(Camera camera, Vector3 world)
        {
            if (!TryGet(camera, out _)) return camera.WorldToViewportPoint(world);
            Vector3 ndc = (camera.projectionMatrix * camera.worldToCameraMatrix).MultiplyPoint(world);
            float depth = -camera.worldToCameraMatrix.MultiplyPoint(world).z;
            return new Vector3(ndc.x * 0.5f + 0.5f, ndc.y * 0.5f + 0.5f, depth);
        }

        /// <summary>World point at a viewport position (0-1), <paramref name="viewport"/>.z in front of the camera.</summary>
        public static Vector3 ViewportToWorldPoint(Camera camera, Vector3 viewport)
        {
            if (!TryGet(camera, out _)) return camera.ViewportToWorldPoint(viewport);
            Matrix4x4 projection = camera.projectionMatrix;
            float viewZ = -viewport.z;
            float ndcZ = (projection.m22 * viewZ + projection.m23) / (projection.m32 * viewZ + projection.m33);
            return (projection * camera.worldToCameraMatrix).inverse
                .MultiplyPoint(new Vector3(viewport.x * 2f - 1f, viewport.y * 2f - 1f, ndcZ));
        }

        /// <summary>
        /// Ray through a viewport point for <paramref name="camera"/> as if it stood at another pose (camera
        /// placement code that works out where the camera should go). Plain orthographic maths when not oblique.
        /// </summary>
        public static Ray ViewportPointToRayAt(Camera camera, Vector3 position, Quaternion rotation, Vector3 viewport)
        {
            Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f))
                                      * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            if (TryGet(camera, out ObliqueProjection projection) && camera.orthographic)
                return RayThrough(projection.BuildProjection(position, rotation) * worldToCamera, viewport);
            if (camera == null || !camera.orthographic)
                return new Ray(position, rotation * Vector3.forward);
            float halfHeight = camera.orthographicSize;
            float halfWidth = halfHeight * camera.aspect;
            Vector3 origin = position
                + rotation * new Vector3((viewport.x * 2f - 1f) * halfWidth, (viewport.y * 2f - 1f) * halfHeight, 0f);
            return new Ray(origin, rotation * Vector3.forward);
        }

        /// <summary>
        /// Camera placement: how far along the camera's up (in world units) a point at height
        /// <paramref name="pointHeight"/> must sit from the camera so that it shows <paramref name="screenOffset"/>
        /// world units above the centre of the view (orthographic size units). Plain cameras: the offset itself.
        /// </summary>
        public static float CameraUpOffsetFor(Camera camera, Quaternion rotation, float pointHeight, float screenOffset)
        {
            if (!TryGet(camera, out ObliqueProjection projection) || !camera.orthographic) return screenOffset;
            float pitch = PitchRadians(rotation * Vector3.forward);
            float s = Mathf.Sin(pitch);
            if (s < 0.0001f || projection.floorScale < 0.0001f) return screenOffset;
            projection.Coefficients(pitch, out _, out float beta);
            // Screen y of the point = alpha*Y + beta*(pivot - depth); with the camera height following from the
            // point's height, this reduces to Y*floorScale/sin + beta*(pointHeight - ground)/sin.
            return (screenOffset - beta * (pointHeight - projection.groundHeight) / s) * s / projection.floorScale;
        }

        private static Ray RayThrough(Matrix4x4 worldToClip, Vector3 viewport)
        {
            Matrix4x4 clipToWorld = worldToClip.inverse;
            float x = viewport.x * 2f - 1f, y = viewport.y * 2f - 1f;
            Vector3 near = clipToWorld.MultiplyPoint(new Vector3(x, y, -1f));
            Vector3 far = clipToWorld.MultiplyPoint(new Vector3(x, y, 1f));
            return new Ray(near, far - near);
        }

        /// <summary>
        /// For a camera-facing quad parented to <paramref name="camera"/> at local depth <paramref name="distance"/>
        /// (a full-screen backdrop): the local up offset and height factor that make it cover the same screen area it
        /// would without the oblique projection. The shear slides far planes up or down by beta * distance and
        /// stretches them by alpha. False (offset 0, factor 1) when no oblique projection is active.
        /// </summary>
        public static bool TryGetScreenPlane(Camera camera, float distance, out float localUpOffset, out float heightScale)
        {
            localUpOffset = 0f;
            heightScale = 1f;
            if (camera == null || !camera.orthographic || !TryGet(camera, out ObliqueProjection projection)) return false;
            projection.Coefficients(out float alpha, out float beta);
            if (alpha < 0.0001f) return false;
            // Screen y of camera-local (0, y, distance) = alpha * y + beta * (pivot - distance); solve for 0.
            localUpOffset = -beta * (projection.PivotDistance() - distance) / alpha;
            heightScale = 1f / alpha;
            return true;
        }

        /// <summary>Screen size of 1 unit of ground depth, relative to 1 unit of X (sin(pitch) for a plain ortho camera).</summary>
        public static float GroundForeshortening(Camera camera)
        {
            if (camera == null) return 1f;
            if (TryGet(camera, out ObliqueProjection projection)) return projection.floorScale;
            Vector3 up = camera.transform.up;
            return new Vector2(up.x, up.z).magnitude;
        }

        /// <summary>
        /// Makes sure <paramref name="camera"/>'s oblique matrix is current (and, when <paramref name="forRendering"/>,
        /// that camera-facing sprites are stood upright for this frame). Safe to call when there is none.
        /// Called by <see cref="CrispWorldUICamera"/> before it copies the projection.
        /// </summary>
        public static void Prepare(Camera camera, bool forRendering)
        {
            if (TryGet(camera, out ObliqueProjection projection)) projection.PrepareInternal(forRendering);
        }

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            cam = GetComponent<Camera>();
            if (!Instances.Contains(this)) Instances.Add(this);
            RenderPipelineManager.beginContextRendering += HandleBeginContextRendering;
            seenRenderers.Clear();
            ApplyMatrix();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= HandleBeginContextRendering;
            Instances.Remove(this);
            ObliqueBillboard.RestoreAll();
            RestoreParticles();
            seenRenderers.Clear();
            if (cam != null) cam.ResetProjectionMatrix();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && cam != null) ApplyMatrix();
        }

        // Update: current for gameplay code that raycasts through the camera in Update/LateUpdate.
        // LateUpdate + render callback: current after Cinemachine has moved the camera.
        private void Update() => ApplyMatrix();
        private void LateUpdate() => ApplyMatrix();

        // Runs after every LateUpdate (Cinemachine, camera shake) and before any camera renders.
        private void HandleBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) =>
            PrepareInternal(true);

        private void PrepareInternal(bool forRendering)
        {
            ApplyMatrix();
            if (!forRendering || !Application.isPlaying || preparedFrame == Time.frameCount) return;
            preparedFrame = Time.frameCount;
            if (autoUprightSprites || fixCameraFacingParticles) ScanNewRenderers();
            ObliqueBillboard.ApplyAll(cam);
        }

        // ---------- Projection ----------

        private void Coefficients(out float alpha, out float beta) =>
            Coefficients(PitchRadians(transform.forward), out alpha, out beta);

        private void Coefficients(float pitch, out float alpha, out float beta)
        {
            float s = Mathf.Sin(pitch), c = Mathf.Cos(pitch);
            // Screen up = floorScale * groundDepth + wallScale * height, written in the camera's view space.
            alpha = wallScale * c + floorScale * s;
            beta = wallScale * s - floorScale * c;
        }

        private void ApplyMatrix()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (!cam.orthographic)
            {
                cam.ResetProjectionMatrix();
                return;
            }

            cam.projectionMatrix = BuildProjection(transform.position, transform.rotation);
        }

        // The oblique projection matrix this camera has when it sits at the given pose.
        private Matrix4x4 BuildProjection(Vector3 position, Quaternion rotation)
        {
            float pitch = PitchRadians(rotation * Vector3.forward);
            Coefficients(pitch, out float alpha, out float beta);
            // Keep the ground point at the centre of the view fixed: a pure shear would slide the image by
            // beta * (distance to the ground), moving the Cinemachine target off its framing.
            float pivot = PivotDistance(position.y, pitch);

            Matrix4x4 shear = Matrix4x4.identity;
            shear.m11 = alpha;
            shear.m12 = beta;
            shear.m13 = beta * pivot;

            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            return Matrix4x4.Ortho(-halfWidth, halfWidth, -halfHeight, halfHeight,
                cam.nearClipPlane, cam.farClipPlane) * shear;
        }

        // Distance along the view from the camera to the ground point at the centre of the view.
        private float PivotDistance() => PivotDistance(transform.position.y, PitchRadians(transform.forward));

        private float PivotDistance(float cameraHeight, float pitch)
        {
            float s = Mathf.Sin(pitch);
            return s > 0.0001f ? (cameraHeight - groundHeight) / s : 0f;
        }

        // Downward tilt of a camera looking along `forward` (0 = at the horizon, 90 = straight down).
        private static float PitchRadians(Vector3 forward) =>
            Mathf.Atan2(-forward.y, new Vector2(forward.x, forward.z).magnitude);

        private static Quaternion UprightRotation(Transform camera)
        {
            Vector3 flat = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.000001f) flat = Vector3.ProjectOnPlane(camera.up, Vector3.up);
            if (flat.sqrMagnitude < 0.000001f) flat = Vector3.forward;
            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }

        // ---------- Camera-facing sprites ----------

        // Every rendered frame, before drawing: looks at renderers it has not seen yet (notes and effects spawned this
        // frame included), so nothing is ever drawn stretched.
        private void ScanNewRenderers()
        {
            if (Time.unscaledTime >= nextSeenReset)
            {
                // Forget destroyed objects now and then; survivors are just looked at again once.
                nextSeenReset = Time.unscaledTime + SeenResetInterval;
                seenRenderers.Clear();
            }

            Vector3 cameraForward = transform.forward;
            float minDot = Mathf.Cos(cameraFacingTolerance * Mathf.Deg2Rad);
            int crispLayer = CrispWorldUI.Layer;
            scanBuffer.Clear();
            foreach (Renderer candidate in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || !seenRenderers.Add(candidate)) continue;
                if (candidate.gameObject.layer == crispLayer && crispLayer >= 0) continue;
                Transform t = candidate.transform;
                // Camera children (the space backdrop, the occlusion quad) are laid out in screen space already.
                if (t.GetComponentInParent<Camera>(true) != null) continue;

                if (candidate is ParticleSystemRenderer particles)
                {
                    if (fixCameraFacingParticles) FixParticleFacing(particles);
                    // Local-aligned particles turn with their transform: treat like a mesh.
                    if (particles.alignment != ParticleSystemRenderSpace.Local) continue;
                }
                else if (!(candidate is SpriteRenderer || candidate is MeshRenderer || candidate is SkinnedMeshRenderer))
                {
                    continue; // trails and lines are built from world points
                }

                if (!autoUprightSprites) continue;
                // Already upright (grass, props) or lying flat (shadows): leave alone.
                if (Mathf.Abs(t.forward.y) < 0.02f) continue;
                if (Vector3.Dot(t.forward, cameraForward) < minDot) continue;
                if (t.GetComponentInParent<ObliqueBillboard>(true) != null) continue;
                scanBuffer.Add(candidate);
            }
            if (scanBuffer.Count == 0) return;

            // Parents first, so a child that just inherits its parent's tilt is not rotated twice.
            scanBuffer.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));
            foreach (Renderer candidate in scanBuffer)
            {
                if (candidate == null || candidate.transform.GetComponentInParent<ObliqueBillboard>(true) != null) continue;
                candidate.gameObject.AddComponent<ObliqueBillboard>();
            }
            scanBuffer.Clear();
        }

        // Billboard / mesh particles aligned to the view are built in the camera's tilted plane and would be drawn
        // 1.39x taller (at 35 degrees). World alignment builds them upright, facing +Z, which is "upright toward the
        // camera" when the camera is not turned sideways; otherwise vertical billboards do the same job.
        private void FixParticleFacing(ParticleSystemRenderer particles)
        {
            if (fixedParticles.ContainsKey(particles)) return;
            ParticleSystemRenderSpace alignment = particles.alignment;
            if (alignment != ParticleSystemRenderSpace.View && alignment != ParticleSystemRenderSpace.Facing) return;
            ParticleSystemRenderMode mode = particles.renderMode;
            if (mode != ParticleSystemRenderMode.Billboard && mode != ParticleSystemRenderMode.Mesh) return;

            Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            bool facingWorldZ = flat.sqrMagnitude > 0.000001f && Vector3.Angle(flat, Vector3.forward) < 1f;
            if (!facingWorldZ && mode != ParticleSystemRenderMode.Billboard) return;

            fixedParticles[particles] = new ParticleSetup { RenderMode = mode, Alignment = alignment };
            if (facingWorldZ) particles.alignment = ParticleSystemRenderSpace.World;
            else particles.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        }

        private void RestoreParticles()
        {
            foreach (KeyValuePair<ParticleSystemRenderer, ParticleSetup> entry in fixedParticles)
            {
                if (entry.Key == null) continue;
                entry.Key.renderMode = entry.Value.RenderMode;
                entry.Key.alignment = entry.Value.Alignment;
            }
            fixedParticles.Clear();
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null; p = p.parent) depth++;
            return depth;
        }
    }
}
