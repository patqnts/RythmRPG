using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    public enum HitLinePlacement
    {
        /// <summary>The hit line is re-projected every frame from a screen (viewport) position, so it always matches the camera framing. Edit "Hit Line Viewport".</summary>
        FollowCamera,
        /// <summary>The hit line stays where you put it in the world. Move / rotate / scale the object freely; notes land on it wherever it is.</summary>
        WorldFixed
    }

    public enum HitLaneSource
    {
        /// <summary>Notes land above their UI buttons (same X as the buttons, measured along the hit line).</summary>
        ButtonPositions,
        /// <summary>Lanes are spread evenly across the width of the hit line.</summary>
        EvenlyAcrossLine
    }

    /// <summary>
    /// Keeps 3D rhythm gameplay aligned with its screen-space controls. UI positions are projected
    /// through the render-texture camera onto the world combat plane used by notes and judgement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatLanePresentation3D : MonoBehaviour
    {
        private const string DefaultThemePath = "Combat/UI/CombatLanePresentationTheme";

        [Header("Editable Theme")]
        [SerializeField] private CombatLanePresentationTheme theme;

        [Header("World Gameplay")]
        [SerializeField] private Transform worldRoot;
        [SerializeField] private Camera worldCamera;
        [Tooltip("Keep the hit line and note paths on a level X/Z plane, independent of camera tilt.")]
        [SerializeField] private bool horizontalGameplay = true;
        [FormerlySerializedAs("heightAboveCombatants")]
        [Tooltip("Small vertical clearance above the combatants' ground contact level.")]
        [SerializeField, Min(0f)] private float heightAboveGround = 1f;
        [SerializeField] private float gameplayHeight;
        [Tooltip("Sorting order of the world-space hit line canvas. Must be above environment sprites/tilemaps and below notes (RhythmNoteVisualLayer lifts notes to 50+).")]
        [SerializeField] private int hitLineSortingOrder = 45;

        [Header("Hit Line (World-Space UI)")]
        [Tooltip("A world-space Canvas lying on the gameplay plane. It is rendered by the same camera as the notes, so it shares their framing, tilt and sorting. Design it like any UI (Image, sprite, children, animation). Auto-created when empty; use the context menu 'Create Hit Line In Scene' to place one you can style in the editor.")]
        [SerializeField] private RectTransform hitLineAnchor;
        [Tooltip("FollowCamera: the line is placed from Hit Line Viewport every frame. WorldFixed: you place it.")]
        [SerializeField] private HitLinePlacement hitLinePlacement = HitLinePlacement.FollowCamera;
        [Tooltip("FollowCamera only. Position of the line in the render-texture view (0-1). Y < 0 means 'seed from the button row on first run'.")]
        [SerializeField] private Vector2 hitLineViewport = new(0.5f, -1f);
        [SerializeField] private HitLaneSource laneSource = HitLaneSource.ButtonPositions;

        [Header("Screen UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform buttonRow;
        private RawImage cameraOutput;

        private const string LegacyUiAnchorName = "Perfect Hit Line UI";
        private const string HitLineName = "Perfect Hit Line (World UI)";
        private const float WorldUnitsPerCanvasUnit = 0.01f;

        private readonly List<RhythmLaneTarget> targets = new();
        private LaneInputRouter activeInput;
        private bool presentationVisible;
        public IReadOnlyList<RhythmLaneTarget> Targets => targets;
        public bool HorizontalGameplay => horizontalGameplay;
        public float GameplayHeight => gameplayHeight;
        public RectTransform HitLineAnchor => hitLineAnchor;
        public Camera RenderCamera => ResolveWorldCamera() ? worldCamera : null;

        private void LateUpdate()
        {
            if (presentationVisible && activeInput != null) AlignWorldTargetsAndLine(activeInput);
        }

        public void EnsurePresentation(LaneInputRouter input)
        {
            if (input == null || input.Bindings.Count == 0) return;
            activeInput = input;
            theme ??= Resources.Load<CombatLanePresentationTheme>(DefaultThemePath);
            EnsureWorldTargets(input);
            EnsureButtonRow(input);
            AlignWorldTargetsAndLine(input);
        }

        public void ConfigureEncounter(CombatEncounterContext context, Transform projectileHolder)
        {
            if (!horizontalGameplay) return;
            float playerGroundHeight = ResolveGroundHeight(context.Player);
            float enemyGroundHeight = ResolveGroundHeight(context.Enemy);
            gameplayHeight = HorizontalCombatGeometry.ResolveGameplayHeight(
                playerGroundHeight, enemyGroundHeight, heightAboveGround);
            if (projectileHolder == null) return;

            projectileHolder.position = HorizontalCombatGeometry.AtHeight(projectileHolder.position, gameplayHeight);
            projectileHolder.rotation = Quaternion.identity;
            gameplayHeight = projectileHolder.position.y;
        }

        public static float ResolveGroundHeight(Component combatant)
        {
            Collider[] colliders = combatant.GetComponentsInChildren<Collider>(false)
                .Where(collider => collider.enabled && !collider.isTrigger && collider.bounds.size.sqrMagnitude > 0f)
                .ToArray();
            if (colliders.Length > 0) return colliders.Min(collider => collider.bounds.min.y);

            Renderer[] renderers = combatant.GetComponentsInChildren<Renderer>(false)
                .Where(renderer => renderer.enabled && renderer.bounds.size.sqrMagnitude > 0f)
                .ToArray();
            if (renderers.Length > 0) return renderers.Min(renderer => renderer.bounds.min.y);
            return combatant.transform.position.y;
        }

        public Vector3 ProjectToGameplayPlane(Vector3 position) => horizontalGameplay
            ? HorizontalCombatGeometry.AtHeight(position, gameplayHeight)
            : position;

        public void RefreshPresentation()
        {
            if (activeInput != null) AlignWorldTargetsAndLine(activeInput);
        }

        public bool TryGetPlayerPosition(float rootHeight, float footOffset, float gap, out Vector3 position)
        {
            position = default;
            if (!ResolveWorldCamera() || !TryGetHitLineFrame(out Vector3 center, out _)) return false;
            // Project the elevated line onto the feet plane through the actual output camera.
            Ray ray = worldCamera.ViewportPointToRay(worldCamera.WorldToViewportPoint(center));
            Plane feetPlane = new(Vector3.up, Vector3.up * (rootHeight + footOffset));
            if (!feetPlane.Raycast(ray, out float distance)) return false;
            Vector3 screenUp = Vector3.ProjectOnPlane(worldCamera.transform.up, Vector3.up);
            position = HorizontalCombatGeometry.PositionBehindLine(ray.GetPoint(distance),
                screenUp, gap, rootHeight);
            return true;
        }

        public bool TryGetHitLineFrame(out Vector3 center, out Vector3 towardPlayer)
        {
            List<RhythmLaneTarget> activeTargets = targets.Where(target => target != null).ToList();
            if (activeTargets.Count == 0)
            {
                center = default;
                towardPlayer = default;
                return false;
            }

            center = activeTargets.Aggregate(Vector3.zero,
                (sum, target) => sum + target.transform.position) / activeTargets.Count;
            towardPlayer = activeTargets.Aggregate(Vector3.zero,
                (sum, target) => sum + target.WorldTravelDirection).normalized;
            if (horizontalGameplay)
            {
                center.y = gameplayHeight;
                towardPlayer = Vector3.ProjectOnPlane(towardPlayer, Vector3.up).normalized;
            }
            if (towardPlayer.sqrMagnitude <= 0.0001f) towardPlayer = Vector3.back;
            return true;
        }

        public void SetPresentationVisible(bool visible)
        {
            presentationVisible = visible;
            if (worldRoot != null) worldRoot.gameObject.SetActive(visible);
            if (buttonRow != null) buttonRow.gameObject.SetActive(visible);
            if (hitLineAnchor != null) hitLineAnchor.gameObject.SetActive(visible);
        }

        private void EnsureWorldTargets(LaneInputRouter input)
        {
            targets.Clear();
            targets.AddRange(FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Where(target => target != null));

            if (worldRoot == null)
            {
                GameObject root = new("Rhythm World Lanes");
                worldRoot = root.transform;
                worldRoot.SetParent(transform, false);
            }

            foreach (LaneKeyBinding binding in input.Bindings)
            {
                RhythmLaneTarget target = targets.FirstOrDefault(candidate => candidate.LaneId == binding.LaneId);
                if (target != null) continue;
                GameObject targetObject = new($"Lane {binding.LaneId} Target");
                targetObject.transform.SetParent(worldRoot, true);
                targetObject.transform.position = binding.View != null
                    ? binding.View.transform.position
                    : worldRoot.position + Vector3.right * (binding.LaneId - 3);
                target = targetObject.AddComponent<RhythmLaneTarget>();
                target.Configure(binding.LaneId, Vector3.down);
                targets.Add(target);
            }
        }

        private void EnsureButtonRow(LaneInputRouter input)
        {
            ResolveCanvas();
            if (canvas == null) return;
            Transform rowParent = cameraOutput != null ? cameraOutput.transform : canvas.transform;

            if (buttonRow == null)
            {
                Transform existing = rowParent.Find("Rhythm Button Row");
                GameObject rowObject = existing != null
                    ? existing.gameObject
                    : new GameObject("Rhythm Button Row", typeof(RectTransform));
                buttonRow = rowObject.GetComponent<RectTransform>();
            }
            buttonRow.SetParent(rowParent, false);

            Vector2 size = theme != null ? theme.ButtonSize : new Vector2(72f, 58f);
            float spacing = theme != null ? theme.ButtonSpacing : 24f;
            float bottom = theme != null ? theme.BottomOffset : 36f;
            buttonRow.anchorMin = buttonRow.anchorMax = new Vector2(0.5f, 0f);
            buttonRow.pivot = new Vector2(0.5f, 0f);
            buttonRow.anchoredPosition = new Vector2(0f, bottom);

            List<LaneKeyBinding> ordered = input.Bindings.OrderBy(binding => binding.LaneId).ToList();
            float totalWidth = ordered.Count * size.x + Mathf.Max(0, ordered.Count - 1) * spacing;
            buttonRow.sizeDelta = new Vector2(totalWidth, size.y);

            for (int index = 0; index < ordered.Count; index++)
            {
                LaneKeyBinding binding = ordered[index];
                KeyButton oldView = binding.View;
                KeyButton uiView = FindOrCreateUIButton(binding, index, size, spacing);
                input.SetView(binding.LaneId, uiView);
                if (oldView == null || oldView == uiView) continue;
                if (oldView.spriteRenderer != null) oldView.spriteRenderer.enabled = false;
                foreach (Collider2D collider in oldView.GetComponents<Collider2D>()) collider.enabled = false;
            }
            RetireLegacyUiAnchor(rowParent);
            Canvas.ForceUpdateCanvases();
        }

        private void ResolveCanvas()
        {
            if (ResolveWorldCamera() && worldCamera.targetTexture != null)
            {
                cameraOutput = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.texture == worldCamera.targetTexture
                        && candidate.gameObject.activeInHierarchy);
                if (cameraOutput != null) canvas = cameraOutput.GetComponentInParent<Canvas>();
            }
            if (canvas == null)
            {
                canvas = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(candidate => candidate.renderMode == RenderMode.ScreenSpaceOverlay)
                    .OrderByDescending(candidate => candidate.gameObject.activeInHierarchy)
                    .ThenByDescending(candidate => candidate.transform.parent == null)
                    .FirstOrDefault();
            }
            if (canvas != null) return;

            GameObject canvasObject = new("Combat Rhythm Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(480f, 270f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private KeyButton FindOrCreateUIButton(LaneKeyBinding binding, int index, Vector2 size, float spacing)
        {
            string objectName = $"Lane {binding.LaneId} Button";
            Transform existing = buttonRow.Find(objectName);
            GameObject buttonObject = existing != null
                ? existing.gameObject
                : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                    typeof(Button), typeof(KeyButton));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(buttonRow, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(size.x * 0.5f + index * (size.x + spacing), 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.sprite = theme != null ? theme.ButtonUnpressedSprite : null;
            image.type = image.sprite != null && theme != null ? theme.ButtonImageType : Image.Type.Simple;
            image.color = theme != null ? theme.ButtonColor : new Color(0.06f, 0.07f, 0.09f, 0.94f);
            buttonObject.GetComponent<Button>().interactable = false;

            Text label = buttonObject.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                GameObject labelObject = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                labelObject.transform.SetParent(buttonObject.transform, false);
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
                label = labelObject.GetComponent<Text>();
                label.alignment = TextAnchor.MiddleCenter;
                label.raycastTarget = false;
            }

            label.font = theme != null && theme.ButtonFont != null
                ? theme.ButtonFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = theme != null ? theme.ButtonFontSize : 28;
            label.fontStyle = theme != null ? theme.ButtonFontStyle : FontStyle.Bold;
            label.color = theme != null ? theme.ButtonTextColor : Color.white;
            label.text = binding.KeyCode == KeyCode.None ? binding.LaneId.ToString() : binding.KeyCode.ToString();
            KeyButton view = buttonObject.GetComponent<KeyButton>();
            view.ConfigureUI(binding.LaneId, image, label,
                theme != null ? theme.ButtonUnpressedSprite : null,
                theme != null ? theme.ButtonPressedSprite : null);
            return view;
        }

        private void AlignWorldTargetsAndLine(LaneInputRouter input)
        {
            if (buttonRow == null || targets.Count == 0 || !ResolveWorldCamera()) return;

            Plane plane = BuildCombatPlane();
            float canvasScale = Mathf.Max(0.0001f, canvas.scaleFactor);
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            EnsureHitLine(plane, uiCamera, canvasScale);
            if (hitLineAnchor == null) return;

            if (Time.frameCount % 120 == 0) PrepareHitLineMaterials(hitLineAnchor);
            Canvas lineCanvas = hitLineAnchor.GetComponent<Canvas>();
            if (lineCanvas != null)
            {
                if (lineCanvas.worldCamera != worldCamera) lineCanvas.worldCamera = worldCamera;
                lineCanvas.sortingOrder = hitLineSortingOrder;
            }
            if (hitLinePlacement == HitLinePlacement.FollowCamera)
                PlaceHitLineFollowingCamera(plane, uiCamera, canvasScale);

            // The world-space line is the single source of truth for landing position and direction.
            Vector3 origin = hitLineAnchor.position;
            Vector3 right = hitLineAnchor.right;
            Vector3 travel = -hitLineAnchor.up;
            Plane linePlane = plane;
            if (horizontalGameplay)
            {
                right = Vector3.ProjectOnPlane(right, Vector3.up);
                travel = Vector3.ProjectOnPlane(travel, Vector3.up);
                linePlane = new Plane(Vector3.up, origin);
            }
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
            travel = travel.sqrMagnitude > 0.0001f ? travel.normalized : Vector3.back;

            List<LaneKeyBinding> ordered = input.Bindings.OrderBy(binding => binding.LaneId).ToList();
            float lineWidth = hitLineAnchor.rect.width * Mathf.Abs(hitLineAnchor.lossyScale.x);
            for (int index = 0; index < ordered.Count; index++)
            {
                LaneKeyBinding binding = ordered[index];
                RhythmLaneTarget target = targets.FirstOrDefault(candidate => candidate.LaneId == binding.LaneId);
                if (target == null) continue;

                float along;
                if (laneSource == HitLaneSource.ButtonPositions)
                {
                    RectTransform buttonRect = buttonRow.Find($"Lane {binding.LaneId} Button") as RectTransform;
                    if (buttonRect == null) continue;
                    Vector2 buttonCenter = RectTransformUtility.WorldToScreenPoint(uiCamera, buttonRect.position);
                    if (!TryScreenToCombatPlane(buttonCenter, linePlane, out Vector3 hit)) continue;
                    along = Vector3.Dot(hit - origin, right);
                }
                else
                {
                    float spacing = lineWidth / Mathf.Max(1, ordered.Count);
                    along = (index + 0.5f - ordered.Count * 0.5f) * spacing;
                }

                target.transform.position = origin + right * along;
                target.Configure(binding.LaneId, travel);
            }
        }

        private void PlaceHitLineFollowingCamera(Plane plane, Camera uiCamera, float canvasScale)
        {
            if (hitLineViewport.y < 0f) SeedHitLineViewport(uiCamera, canvasScale);
            Vector2 viewport = new(Mathf.Clamp01(hitLineViewport.x), Mathf.Clamp01(hitLineViewport.y));
            Ray ray = worldCamera.ViewportPointToRay(viewport);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 point = ray.GetPoint(distance);

            Vector3 travel = Vector3.back;
            Ray below = worldCamera.ViewportPointToRay(new Vector2(viewport.x, Mathf.Clamp01(viewport.y - 0.02f)));
            if (plane.Raycast(below, out float belowDistance))
            {
                Vector3 direction = below.GetPoint(belowDistance) - point;
                if (horizontalGameplay) direction = Vector3.ProjectOnPlane(direction, Vector3.up);
                if (direction.sqrMagnitude > 0.000001f) travel = direction.normalized;
            }

            // Canvas +Z points away from the viewer (into the plane); canvas up points against note travel.
            Vector3 forward = horizontalGameplay ? Vector3.down : worldCamera.transform.forward;
            hitLineAnchor.SetPositionAndRotation(point, Quaternion.LookRotation(forward, -travel));
        }

        private void SeedHitLineViewport(Camera uiCamera, float canvasScale)
        {
            Vector3[] corners = new Vector3[4];
            buttonRow.GetWorldCorners(corners);
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
            float gap = (theme != null ? theme.LineGapAboveButtons : 8f) * canvasScale;
            hitLineViewport = ScreenPointToWorldCameraViewport(
                new Vector2((bottomLeft.x + topRight.x) * 0.5f, topRight.y + gap));
        }

        private static bool IsWorldSpace(RectTransform rect)
        {
            Canvas root = rect != null ? rect.GetComponentInParent<Canvas>() : null;
            return root != null && root.rootCanvas.renderMode == RenderMode.WorldSpace;
        }

        private void RetireLegacyUiAnchor(Transform parent)
        {
            Transform legacy = parent != null ? parent.Find(LegacyUiAnchorName) : null;
            if (legacy != null) legacy.gameObject.SetActive(false);
        }

        private bool ResolveWorldCamera()
        {
            if (worldCamera != null && worldCamera.isActiveAndEnabled) return true;
            worldCamera = Camera.main;
            if (worldCamera != null && worldCamera.isActiveAndEnabled) return true;
            worldCamera = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.targetTexture != null)
                ?? FindFirstObjectByType<Camera>();
            return worldCamera != null;
        }

        private Plane BuildCombatPlane()
        {
            if (horizontalGameplay)
                return new Plane(Vector3.up, Vector3.up * gameplayHeight);

            Vector3 point = targets.Count > 0
                ? targets.Aggregate(Vector3.zero, (sum, target) => sum + target.transform.position) / targets.Count
                : worldRoot.position;
            Vector3 normal = worldCamera != null
                ? worldCamera.transform.forward
                : worldRoot != null ? worldRoot.forward : Vector3.forward;
            return new Plane(normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.forward, point);
        }

        private bool TryScreenToCombatPlane(Vector2 screenPoint, Plane plane, out Vector3 worldPoint)
        {
            Vector2 viewport = ScreenPointToWorldCameraViewport(screenPoint);
            if (theme == null || theme.SnapToRenderTexturePixels)
            {
                int width = worldCamera.targetTexture != null ? worldCamera.targetTexture.width : worldCamera.pixelWidth;
                int height = worldCamera.targetTexture != null ? worldCamera.targetTexture.height : worldCamera.pixelHeight;
                if (width > 0) viewport.x = (Mathf.Floor(viewport.x * width) + 0.5f) / width;
                if (height > 0) viewport.y = (Mathf.Floor(viewport.y * height) + 0.5f) / height;
            }
            viewport.x = Mathf.Clamp01(viewport.x);
            viewport.y = Mathf.Clamp01(viewport.y);
            Ray ray = worldCamera.ViewportPointToRay(viewport);
            if (plane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }
            worldPoint = default;
            return false;
        }

        private Vector2 ScreenPointToWorldCameraViewport(Vector2 screenPoint)
        {
            RawImage output = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.texture != null && candidate.texture == worldCamera.targetTexture);
            if (output == null)
                return new Vector2(screenPoint.x / Mathf.Max(1f, Screen.width), screenPoint.y / Mathf.Max(1f, Screen.height));

            Canvas outputCanvas = output.GetComponentInParent<Canvas>();
            Camera outputCamera = outputCanvas == null || outputCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : outputCanvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            output.rectTransform.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(outputCamera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(outputCamera, corners[2]);
            float u = Mathf.InverseLerp(min.x, max.x, screenPoint.x);
            float v = Mathf.InverseLerp(min.y, max.y, screenPoint.y);
            Rect uv = output.uvRect;
            return new Vector2(uv.x + u * uv.width, uv.y + v * uv.height);
        }

        private Vector3 ResolveTravelDirection(Plane plane, float x, float y, float thickness)
        {
            float offset = Mathf.Max(2f, thickness);
            if (TryScreenToCombatPlane(new Vector2(x, y + offset), plane, out Vector3 above)
                && TryScreenToCombatPlane(new Vector2(x, y), plane, out Vector3 line))
                return (line - above).normalized;
            return Vector3.down;
        }

        private float ResolveWorldThickness(Plane plane, Vector2 center, float screenThickness)
        {
            Vector2 half = Vector2.up * Mathf.Max(0.25f, screenThickness) * 0.5f;
            if (TryScreenToCombatPlane(center - half, plane, out Vector3 bottom)
                && TryScreenToCombatPlane(center + half, plane, out Vector3 top))
                return Mathf.Max(0.001f, Vector3.Distance(bottom, top));
            return 0.075f;
        }

        private void EnsureHitLine(Plane plane, Camera uiCamera, float canvasScale)
        {
            if (hitLineAnchor != null && !IsWorldSpace(hitLineAnchor))
            {
                Debug.LogWarning("[Combat] Hit Line Anchor is a screen-space UI element. The hit line is now a world-space Canvas; " +
                    "assign a world-space one or clear the field.", this);
                hitLineAnchor = null;
            }
            if (hitLineAnchor != null)
            {
                if (hitLineAnchor != materialPreparedFor) PrepareHitLineMaterials(hitLineAnchor);
                return;
            }

            Transform parent = worldRoot != null ? worldRoot : transform;
            Transform existing = parent.Find(HitLineName);
            if (existing is RectTransform existingRect && IsWorldSpace(existingRect))
            {
                hitLineAnchor = existingRect;
                PrepareHitLineMaterials(hitLineAnchor);
                return;
            }
            hitLineAnchor = CreateHitLine(parent, plane, uiCamera, canvasScale);
            PrepareHitLineMaterials(hitLineAnchor);
        }

        // Seeds a sensible default size; afterwards the object is yours to restyle and resize.
        private RectTransform CreateHitLine(Transform parent, Plane plane, Camera uiCamera, float canvasScale)
        {
            float widthWorld = 6f;
            float thicknessWorld = 0.06f;
            if (buttonRow != null && ResolveWorldCamera())
            {
                Vector3[] corners = new Vector3[4];
                buttonRow.GetWorldCorners(corners);
                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
                float padding = (theme != null ? theme.LineHorizontalPadding : 8f) * canvasScale;
                float midY = (bottomLeft.y + topRight.y) * 0.5f;
                if (TryScreenToCombatPlane(new Vector2(bottomLeft.x - padding, midY), plane, out Vector3 left)
                    && TryScreenToCombatPlane(new Vector2(topRight.x + padding, midY), plane, out Vector3 rightPoint))
                    widthWorld = Mathf.Max(0.5f, Vector3.Distance(left, rightPoint));
                thicknessWorld = ResolvePixelThickness(theme != null ? theme.LineThickness : 2f);
            }
            return BuildHitLineObject(parent, widthWorld, Mathf.Max(0.02f, thicknessWorld));
        }

        // Thickness (on the gameplay plane) that covers `pixels` render-texture rows on screen. A line thinner than
        // ~2 render-texture pixels misses pixel centres as the camera moves and flickers or vanishes.
        private float ResolvePixelThickness(float pixels)
        {
            float rtHeight = worldCamera.targetTexture != null ? worldCamera.targetTexture.height : worldCamera.pixelHeight;
            // Theme thickness is in canvas pixels of a 270-row reference; convert to render-texture rows.
            pixels = Mathf.Max(3f, pixels * Mathf.Max(1f, rtHeight) / 270f);
            float unitsPerPixel = worldCamera.orthographic && rtHeight > 0f
                ? 2f * worldCamera.orthographicSize / rtHeight
                : 0.03f;
            float facing = Mathf.Abs(Vector3.Dot(worldCamera.transform.up, Vector3.up));
            float planar = horizontalGameplay ? Mathf.Max(0.2f, Mathf.Sqrt(Mathf.Max(0f, 1f - facing * facing))) : 1f;
            return pixels * unitsPerPixel / planar;
        }

        private RectTransform materialPreparedFor;
        private Material hitLineMaterial;

        // The Canvas system controls ZTest for graphics using the default UI material, so world geometry (ground, props)
        // sitting on the gameplay plane can swallow a thin line. Graphics still on the default material are switched to
        // "Hit Line UI", a UI shader with ZTest Always (Resources/Combat/VFX/HitLineUI.shader). Graphics with a
        // material of your own are left alone.
        private void PrepareHitLineMaterials(RectTransform root)
        {
            materialPreparedFor = root;
            if (hitLineMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("Combat/VFX/HitLineUI");
                if (shader == null) shader = Shader.Find("Rythm RPG/Combat/Hit Line UI");
                if (shader != null) hitLineMaterial = new Material(shader) { name = "Hit Line UI (runtime)" };
                else Debug.LogWarning("[Combat] Hit Line UI shader not found (Assets/Resources/Combat/VFX/HitLineUI.shader). The hit line may be hidden by world geometry.", this);
            }
            if (hitLineMaterial == null) return;

            Material defaultMaterial = Canvas.GetDefaultCanvasMaterial();
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                if (graphic.material == defaultMaterial) graphic.material = hitLineMaterial;
        }

        private RectTransform BuildHitLineObject(Transform parent, float widthWorld, float thicknessWorld)
        {
            GameObject lineObject = new(HitLineName, typeof(RectTransform), typeof(Canvas));
            lineObject.transform.SetParent(parent, false);
            RectTransform rect = lineObject.GetComponent<RectTransform>();
            Canvas lineCanvas = lineObject.GetComponent<Canvas>();
            lineCanvas.renderMode = RenderMode.WorldSpace;
            lineCanvas.worldCamera = worldCamera;
            lineCanvas.sortingOrder = hitLineSortingOrder;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one * WorldUnitsPerCanvasUnit;
            rect.sizeDelta = new Vector2(widthWorld / WorldUnitsPerCanvasUnit, thicknessWorld / WorldUnitsPerCanvasUnit);
            rect.rotation = Quaternion.Euler(90f, 0f, 0f);

            GameObject visual = new("Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            visual.transform.SetParent(rect, false);
            RectTransform visualRect = visual.GetComponent<RectTransform>();
            visualRect.anchorMin = Vector2.zero;
            visualRect.anchorMax = Vector2.one;
            visualRect.offsetMin = visualRect.offsetMax = Vector2.zero;
            Image image = visual.GetComponent<Image>();
            image.sprite = theme != null ? theme.LineSprite : null;
            image.color = theme != null ? theme.LineColor : Color.white;
            image.raycastTarget = false;
            return rect;
        }

#if UNITY_EDITOR
        [ContextMenu("Create Hit Line In Scene")]
        private void CreateHitLineInEditor()
        {
            if (hitLineAnchor != null)
            {
                UnityEditor.Selection.activeObject = hitLineAnchor.gameObject;
                return;
            }

            RectTransform rect = BuildHitLineObject(transform, 6f, 0.06f);
            UnityEditor.Undo.RegisterCreatedObjectUndo(rect.gameObject, "Create Hit Line");
            UnityEditor.Undo.RecordObject(this, "Assign Hit Line");
            hitLineAnchor = rect;
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.Selection.activeObject = rect.gameObject;
        }

        private void OnDrawGizmos()
        {
            if (hitLineAnchor == null) return;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.matrix = hitLineAnchor.localToWorldMatrix;
            Rect r = hitLineAnchor.rect;
            Gizmos.DrawWireCube(r.center, new Vector3(r.width, r.height, 0f));
        }
#endif
    }
}
