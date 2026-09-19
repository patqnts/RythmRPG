using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
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
        [SerializeField] private LineRenderer judgementLine;

        [Header("Screen UI")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform buttonRow;

        private readonly List<RhythmLaneTarget> targets = new();
        public IReadOnlyList<RhythmLaneTarget> Targets => targets;

        public void EnsurePresentation(LaneInputRouter input)
        {
            if (input == null || input.Bindings.Count == 0) return;
            theme ??= Resources.Load<CombatLanePresentationTheme>(DefaultThemePath);
            EnsureWorldTargets(input);
            EnsureButtonRow(input);
            AlignWorldTargetsAndLine(input);
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
                Transform presentationParent = input.Bindings
                    .Select(binding => binding.View != null ? binding.View.transform.parent : null)
                    .FirstOrDefault(parent => parent != null);
                worldRoot.SetParent(presentationParent != null ? presentationParent : transform, false);
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

            if (buttonRow == null)
            {
                Transform existing = canvas.transform.Find("Rhythm Button Row");
                GameObject rowObject = existing != null
                    ? existing.gameObject
                    : new GameObject("Rhythm Button Row", typeof(RectTransform));
                buttonRow = rowObject.GetComponent<RectTransform>();
                buttonRow.SetParent(canvas.transform, false);
            }

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
            Canvas.ForceUpdateCanvases();
        }

        private void ResolveCanvas()
        {
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
            image.sprite = theme != null ? theme.ButtonSprite : null;
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
            view.ConfigureUI(binding.LaneId, image, label);
            return view;
        }

        private void AlignWorldTargetsAndLine(LaneInputRouter input)
        {
            if (buttonRow == null || targets.Count == 0 || !ResolveWorldCamera())
            {
                ConfigureFallbackLine();
                return;
            }

            Plane plane = BuildCombatPlane();
            float canvasScale = Mathf.Max(0.0001f, canvas.scaleFactor);
            float gap = (theme != null ? theme.LineGapAboveButtons : 8f) * canvasScale;
            float padding = (theme != null ? theme.LineHorizontalPadding : 8f) * canvasScale;
            float thickness = (theme != null ? theme.LineThickness : 2f) * canvasScale;
            Vector3[] corners = new Vector3[4];
            buttonRow.GetWorldCorners(corners);
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
            float lineY = topRight.y + gap;

            if (!TryScreenToCombatPlane(new Vector2(bottomLeft.x - padding, lineY), plane, out Vector3 start)
                || !TryScreenToCombatPlane(new Vector2(topRight.x + padding, lineY), plane, out Vector3 end))
            {
                ConfigureFallbackLine();
                return;
            }

            Vector3 travel = ResolveTravelDirection(plane, (bottomLeft.x + topRight.x) * 0.5f, lineY, thickness);
            foreach (LaneKeyBinding binding in input.Bindings.OrderBy(binding => binding.LaneId))
            {
                RhythmLaneTarget target = targets.FirstOrDefault(candidate => candidate.LaneId == binding.LaneId);
                RectTransform buttonRect = buttonRow.Find($"Lane {binding.LaneId} Button") as RectTransform;
                if (target == null || buttonRect == null) continue;
                Vector2 buttonCenter = RectTransformUtility.WorldToScreenPoint(uiCamera, buttonRect.position);
                if (!TryScreenToCombatPlane(new Vector2(buttonCenter.x, lineY), plane, out Vector3 point)) continue;
                target.transform.position = point;
                target.Configure(binding.LaneId, travel);
            }

            float worldThickness = ResolveWorldThickness(plane,
                new Vector2((bottomLeft.x + topRight.x) * 0.5f, lineY), thickness);
            ConfigureJudgementLine(start, end, worldThickness);
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
            Vector3 point = targets.Count > 0
                ? targets.Aggregate(Vector3.zero, (sum, target) => sum + target.transform.position) / targets.Count
                : worldRoot.position;
            Vector3 normal = worldRoot != null ? worldRoot.forward : Vector3.forward;
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

        private void ConfigureJudgementLine(Vector3 start, Vector3 end, float width)
        {
            EnsureJudgementLineObject();
            Material material = theme != null && theme.LineMaterial != null
                ? theme.LineMaterial
                : Resources.Load<Material>("Combat/VFX/JudgementLineOverlay");
            Color color = theme != null ? theme.LineColor : Color.white;
            Sprite sprite = theme != null ? theme.LineSprite : null;
            SpriteRenderer spriteRenderer = judgementLine.GetComponent<SpriteRenderer>();

            if (sprite != null)
            {
                if (spriteRenderer == null) spriteRenderer = judgementLine.gameObject.AddComponent<SpriteRenderer>();
                judgementLine.enabled = false;
                spriteRenderer.enabled = true;
                spriteRenderer.sprite = sprite;
                spriteRenderer.color = color;
                spriteRenderer.sharedMaterial = material;
                spriteRenderer.sortingOrder = -100;
                Vector3 direction = end - start;
                spriteRenderer.transform.position = (start + end) * 0.5f;
                spriteRenderer.transform.rotation = Quaternion.FromToRotation(Vector3.right, direction.normalized);
                Vector2 spriteSize = sprite.bounds.size;
                spriteRenderer.transform.localScale = new Vector3(
                    direction.magnitude / Mathf.Max(0.0001f, spriteSize.x),
                    width / Mathf.Max(0.0001f, spriteSize.y), 1f);
                return;
            }

            if (spriteRenderer != null) spriteRenderer.enabled = false;
            judgementLine.enabled = true;
            judgementLine.useWorldSpace = true;
            judgementLine.positionCount = 2;
            judgementLine.SetPosition(0, start);
            judgementLine.SetPosition(1, end);
            judgementLine.startWidth = judgementLine.endWidth = width;
            judgementLine.startColor = judgementLine.endColor = color;
            if (material != null) judgementLine.sharedMaterial = material;
            judgementLine.numCapVertices = 2;
            judgementLine.sortingOrder = -100;
        }

        private void ConfigureFallbackLine()
        {
            List<RhythmLaneTarget> ordered = targets.OrderBy(target => target.LaneId).ToList();
            if (ordered.Count == 0) return;
            Vector3 axis = ordered.Count > 1
                ? (ordered[^1].transform.position - ordered[0].transform.position).normalized
                : Vector3.right;
            float extension = ordered.Count > 1
                ? Vector3.Distance(ordered[0].transform.position, ordered[^1].transform.position) * 0.08f
                : 1f;
            ConfigureJudgementLine(ordered[0].transform.position - axis * extension,
                ordered[^1].transform.position + axis * extension, 0.075f);
        }

        private void EnsureJudgementLineObject()
        {
            if (judgementLine != null) return;
            Transform existing = worldRoot != null ? worldRoot.Find("Perfect Hit Line") : null;
            GameObject lineObject = existing != null ? existing.gameObject : new GameObject("Perfect Hit Line");
            lineObject.transform.SetParent(worldRoot, false);
            judgementLine = lineObject.GetComponent<LineRenderer>();
            if (judgementLine == null) judgementLine = lineObject.AddComponent<LineRenderer>();
        }
    }
}
