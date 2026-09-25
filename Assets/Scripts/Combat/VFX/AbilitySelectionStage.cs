using System.Collections.Generic;
using RythmRPG.Core;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Presentation of the player's ability choice (created by <see cref="CombatVFXController"/>):
    /// <list type="bullet">
    /// <item>A world-space canvas that floats above the player's head with one slot per lane, left to right in lane
    /// order. The <see cref="AbilitySlotView"/> icons live on these slots, with the lane's key under each icon.</item>
    /// <item>A Cinemachine camera that zooms in on the player for the choice and blends back to the combat view once
    /// an ability is picked. While zoomed, the combat layout (hit line, lanes, player spot) is frozen, because it is
    /// normally re-projected from the camera every frame.</item>
    /// <item>A sustained camera shake that builds up while an ability is held, then a punch when it is picked.</item>
    /// </list>
    /// Tuned on the <see cref="CombatVFXTheme"/> (Ability Selection sections).
    /// </summary>
    public sealed class AbilitySelectionStage : MonoBehaviour
    {
        private const float IconCanvasUnits = 54f; // AbilitySlotView's UI icon size
        private const int SortingOrder = 510;

        private sealed class Slot
        {
            public int LaneId;
            public RectTransform Rect;
            public TMP_Text Key;
            public AbilitySlotView View;
            public CanvasGroup Group;
            public Vector2 Rest;   // place in the row
            public float Pull;     // 0 = in the row, 1 = drawn into the player
            public float KeyAlpha;
        }

        private CombatVFXTheme theme;
        private Canvas canvas;
        private RectTransform canvasRect;
        private readonly List<Slot> slots = new();
        private Transform player;
        private float headOffset = 1.5f;
        private float spriteHeight = 1f;

        private CinemachineBrain brain;
        private CinemachineCamera combatCamera;
        private CinemachineCamera zoomCamera;
        private CombatLanePresentation3D lanes;
        private bool zoomed;
        private float unfreezeAt = -1f;
        private float blendSeconds;
        private bool charging;
        private int chargeLane = -1;
        private float chargeProgress;
        private int committedLane = -1;
        private TMP_Text nameLabel;
        private float nameAlpha;
        private float nameHoldUntil = -1f;
        private float namePunch;
        private float rowTop = IconCanvasUnits + 10f;
        private KeyMarkerMorph morph;

        private static AbilitySelectionStage blendOwner;
        private static CinemachineCore.GetBlendOverrideDelegate previousBlendOverride;
        private static bool blendOverrideInstalled;

        public bool IsZoomed => zoomed;
        public float ZoomOutSeconds => theme != null && theme.ZoomOnPlayerTurn ? theme.ZoomOutSeconds : 0f;

        public void Configure(CombatVFXTheme vfxTheme) => theme = vfxTheme;

        // ---------- Slots above the head ----------

        /// <summary>Builds (or reuses) one slot per lane above <paramref name="playerTransform"/>. Returns them by lane.</summary>
        public IReadOnlyDictionary<int, RectTransform> BuildSlots(Transform playerTransform, IEnumerable<LaneKeyBinding> bindings)
        {
            player = playerTransform;
            EnsureCanvas();
            var result = new Dictionary<int, RectTransform>();
            var ordered = new List<LaneKeyBinding>(bindings);
            ordered.Sort((a, b) => a.LaneId.CompareTo(b.LaneId));

            foreach (Slot old in slots)
                if (old.Rect != null && !ordered.Exists(binding => binding.LaneId == old.LaneId)) old.Rect.gameObject.SetActive(false);

            foreach (LaneKeyBinding binding in ordered)
            {
                Slot slot = slots.Find(candidate => candidate.LaneId == binding.LaneId && candidate.Rect != null);
                if (slot == null)
                {
                    slot = new Slot { LaneId = binding.LaneId };
                    var go = new GameObject($"Lane {binding.LaneId} Ability Slot", typeof(RectTransform));
                    go.layer = canvas.gameObject.layer;
                    slot.Rect = (RectTransform)go.transform;
                    slot.Rect.SetParent(canvasRect, false);
                    slot.Rect.anchorMin = slot.Rect.anchorMax = new Vector2(0.5f, 0f);
                    slot.Rect.pivot = new Vector2(0.5f, 0f);
                    slot.Rect.sizeDelta = new Vector2(IconCanvasUnits, 0f);
                    slot.Group = go.AddComponent<CanvasGroup>();
                    slot.Group.interactable = false;
                    slot.Group.blocksRaycasts = false;

                    slot.Key = CombatText.CreateUGUI("Key", slot.Rect, null, 22f, Color.white, TextAlignmentOptions.Center,
                        new Color(0f, 0f, 0f, 0.8f));
                    slot.Key.fontStyle = FontStyles.Bold;
                    RectTransform keyRect = slot.Key.rectTransform;
                    keyRect.anchorMin = keyRect.anchorMax = new Vector2(0.5f, 0f);
                    keyRect.pivot = new Vector2(0.5f, 1f);
                    keyRect.sizeDelta = new Vector2(IconCanvasUnits, 26f);
                    keyRect.anchoredPosition = new Vector2(0f, 4f);
                    slots.Add(slot);
                }
                slot.Rect.gameObject.SetActive(true);
                slot.Key.text = binding.DisplayName;
                result[binding.LaneId] = slot.Rect;
            }

            slots.Sort((a, b) => a.LaneId.CompareTo(b.LaneId));
            LayoutSlots();
            return result;
        }

        public void RefreshKeyLabels(IEnumerable<LaneKeyBinding> bindings)
        {
            foreach (LaneKeyBinding binding in bindings)
            {
                Slot slot = slots.Find(candidate => candidate.LaneId == binding.LaneId);
                if (slot?.Key != null) slot.Key.text = binding.DisplayName;
            }
        }

        private void EnsureCanvas()
        {
            if (canvas != null) return;
            var go = new GameObject("Ability Selection Canvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            canvasRect = (RectTransform)go.transform;
            canvasRect.pivot = new Vector2(0.5f, 0f);
            canvasRect.sizeDelta = new Vector2(IconCanvasUnits, IconCanvasUnits);
            // Ability slots, key labels and name: crisp at screen resolution (children copy the canvas layer).
            CrispWorldUI.Apply(go);

            // Name of the ability being held, above the row.
            nameLabel = CombatText.CreateUGUI("Ability Name", canvasRect, null, 26f, Color.white,
                TextAlignmentOptions.Center, new Color(0f, 0f, 0f, 0.85f));
            nameLabel.fontStyle = FontStyles.Bold;
            RectTransform nameRect = nameLabel.rectTransform;
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.sizeDelta = new Vector2(IconCanvasUnits * 8f, 34f);
            nameLabel.alpha = 0f;
        }

        private void LayoutSlots()
        {
            int visible = 0;
            foreach (Slot slot in slots) if (slot.Rect != null && slot.Rect.gameObject.activeSelf) visible++;
            float spacing = IconCanvasUnits * (theme != null ? theme.IconSpacing : 1.35f);
            // Curvature: the outermost icons sit this many icon heights lower (+) or higher (-) than the middle one.
            float curvature = theme != null ? theme.RowCurvature : 0.25f;
            float half = (visible - 1) * 0.5f;
            float highest = float.MinValue;
            int index = 0;
            foreach (Slot slot in slots)
            {
                if (slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
                float u = index - half;
                float normalized = half > 0f ? u / half : 0f;
                slot.Rest = new Vector2(u * spacing, -curvature * IconCanvasUnits * normalized * normalized);
                slot.Rect.anchoredPosition = slot.Rest;
                highest = Mathf.Max(highest, slot.Rest.y);
                index++;
            }
            rowTop = (highest > float.MinValue ? highest : 0f) + IconCanvasUnits + 14f;
            canvasRect.sizeDelta = new Vector2(Mathf.Max(1, visible) * spacing, IconCanvasUnits * 2f);
        }

        /// <summary>Measures the player's sprite, so the row sits just above the head at a size that suits the sprite.</summary>
        private void MeasurePlayer()
        {
            if (player == null) return;
            if (TryGetBounds(player, out Bounds bounds))
            {
                spriteHeight = Mathf.Max(0.1f, bounds.size.y);
                headOffset = bounds.max.y - player.position.y;
            }
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            SpriteRenderer sprite = root.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null && sprite.sprite != null)
            {
                bounds = sprite.bounds;
                return true;
            }
            Renderer any = root.GetComponentInChildren<Renderer>();
            bounds = any != null ? any.bounds : default;
            return any != null;
        }

        private Camera ViewCamera
        {
            get
            {
                if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
                Camera output = brain != null ? brain.OutputCamera : null;
                return output != null ? output : Camera.main;
            }
        }

        private void LateUpdate()
        {
            if (unfreezeAt >= 0f && Time.time >= unfreezeAt && (brain == null || !brain.IsBlending))
            {
                unfreezeAt = -1f;
                if (lanes != null) lanes.LayoutFrozen = false;
            }
            if (zoomed) PlaceZoomCamera();
            UpdateCanvas();
            morph?.Tick(Time.deltaTime, ViewCamera, spriteHeight * (theme != null ? theme.MorphArc : 0.3f), MorphDistortion);
        }

        private void UpdateCanvas()
        {
            if (canvas == null) return;
            CrispWorldUI.ApplyIfNeeded(canvas.gameObject);
            float dt = Time.unscaledDeltaTime;
            bool anyShown = false;
            bool anyPulled = false;
            bool holding = chargeLane >= 0 || committedLane >= 0;
            float othersAlpha = theme != null ? theme.OtherIconsAlphaWhileHolding : 0.45f;
            foreach (Slot slot in slots)
            {
                if (slot.Rect == null) continue;
                if (slot.View == null) slot.View = slot.Rect.GetComponent<AbilitySlotView>();
                bool shown = slot.View != null && slot.View.IsShown;
                anyShown |= shown || slot.KeyAlpha > 0.01f;
                slot.KeyAlpha = Mathf.MoveTowards(slot.KeyAlpha, shown ? 1f : 0f, dt * 6f);
                if (slot.Key != null)
                {
                    bool showKeys = theme == null || theme.ShowKeysUnderIcons;
                    Color color = slot.Key.color;
                    color.a = showKeys ? slot.KeyAlpha * (1f - slot.Pull) : 0f;
                    slot.Key.color = color;
                }

                bool isHeld = slot.LaneId == chargeLane || slot.LaneId == committedLane;
                if (slot.Group != null)
                    slot.Group.alpha = Mathf.MoveTowards(slot.Group.alpha, holding && !isHeld ? othersAlpha : 1f, dt * 5f);
                anyPulled |= slot.Pull > 0.001f;
            }

            if (nameLabel != null)
            {
                bool showName = (theme == null || theme.ShowAbilityName)
                    && (chargeLane >= 0 || Time.unscaledTime < nameHoldUntil);
                nameAlpha = Mathf.MoveTowards(nameAlpha, showName ? 1f : 0f, dt * (showName ? 8f : 4f));
                namePunch = Mathf.MoveTowards(namePunch, 0f, dt * 4f);
                nameLabel.alpha = nameAlpha;
                nameLabel.rectTransform.anchoredPosition = new Vector2(0f, rowTop);
                nameLabel.rectTransform.localScale = Vector3.one * (theme != null ? theme.AbilityNameSize : 1f)
                    * (1f + 0.3f * namePunch * namePunch);
            }

            bool morphing = morph != null && morph.IsActive;
            if (player == null || (!anyShown && !charging && !anyPulled && !morphing && nameAlpha <= 0.01f)) return;

            Camera view = ViewCamera;
            float iconWorld = spriteHeight * (theme != null ? theme.IconSize : 0.4f);
            float scale = iconWorld / IconCanvasUnits;
            Vector3 top = player.position + Vector3.up * (headOffset + spriteHeight * (theme != null ? theme.IconsHeightAboveHead : 0.12f));
            Quaternion facing = view != null ? ObliqueProjection.BillboardRotation(view) : Quaternion.identity;
            // Pulled a little toward the camera so the player's own sprite never cuts through the icons
            // (along the view ray, so the icons keep their screen position).
            Vector3 towardCamera = view != null ? -ObliqueProjection.ViewDirection(view) * 0.5f : Vector3.zero;
            canvasRect.SetPositionAndRotation(top + towardCamera, facing);
            canvasRect.localScale = Vector3.one * scale;
            if (view != null && canvas.worldCamera != view) canvas.worldCamera = view;

            UpdatePull(dt);
        }

        // The held icon is drawn down into the player as the hold fills (slow, then faster), and swallowed on select.
        private void UpdatePull(float dt)
        {
            bool pullEnabled = theme == null || theme.PullIntoCharacter;
            Vector3 chest = player.position + Vector3.up * (headOffset - spriteHeight * 0.5f);
            Vector2 target = canvasRect.InverseTransformPoint(chest);
            target.y -= 10f + IconCanvasUnits * 0.5f; // the icon centre sits above the slot's origin
            float holdReach = theme != null ? theme.PullDistance : 0.55f;
            float curve = theme != null ? theme.PullCurve : 2f;
            float endScale = theme != null ? theme.PullScale : 0.35f;
            float absorbSpeed = 1f / Mathf.Max(0.01f, theme != null ? theme.AbsorbSeconds : 0.15f);

            foreach (Slot slot in slots)
            {
                if (slot.Rect == null) continue;
                float wanted = 0f;
                float speed = 6f;
                if (pullEnabled && slot.LaneId == committedLane)
                {
                    wanted = 1f;
                    speed = absorbSpeed;
                }
                else if (pullEnabled && slot.LaneId == chargeLane)
                {
                    wanted = Mathf.Pow(Mathf.Clamp01(chargeProgress), curve) * holdReach;
                    speed = 12f;
                }
                slot.Pull = Mathf.MoveTowards(slot.Pull, wanted, dt * speed);
                float t = slot.Pull * slot.Pull * (3f - 2f * slot.Pull); // smoothstep
                slot.Rect.anchoredPosition = Vector2.Lerp(slot.Rest, target, t);
                slot.Rect.localScale = Vector3.one * Mathf.Lerp(1f, endScale, t);
            }
        }

        // ---------- Key markers <-> ability frames ----------

        private bool MorphEnabled => theme == null || theme.MorphMarkersIntoSlots;
        private float MorphSeconds => theme != null ? theme.MorphSeconds : 0.45f;
        private float MorphStagger => theme != null ? theme.MorphStagger : 0.05f;
        private KeyMarkerMorph.Distortion MorphDistortion => new()
        {
            Stretch = theme != null ? theme.MorphStretch : 0.3f,
            Wobble = theme != null ? theme.MorphWobble : 0.08f,
            Lobes = theme != null ? theme.MorphWobbleLobes : 3,
            WobbleSpeed = theme != null ? theme.MorphWobbleSpeed : 18f
        };

        /// <summary>
        /// Each hit-line key marker flies up and warps into its lane's ability frame (lanes in row order). Returns
        /// true if any marker morphs; the frames should then appear at <see cref="MorphArrival"/>.
        /// </summary>
        public bool MorphIn()
        {
            if (!MorphEnabled || canvasRect == null) return false;
            lanes ??= FindAnyObjectByType<CombatLanePresentation3D>();
            if (lanes == null) return false;
            morph ??= new KeyMarkerMorph(transform, SortingOrder - 1);
            morph.Clear();

            bool any = false;
            int index = 0;
            foreach (Slot slot in slots)
            {
                if (slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
                if (lanes.TryGetKeyMarker(slot.LaneId, out LaneKeyMarker marker))
                {
                    Slot target = slot;
                    KeyMarkerMorph.Pose start = MarkerPose(marker);
                    morph.Add(() => start, () => FramePose(target), index * MorphStagger, MorphSeconds, marker.LabelText, false);
                    any = true;
                }
                index++;
            }
            return any;
        }

        /// <summary>When the frame of the <paramref name="rowIndex"/>-th lane should pop in (the ghost has just arrived).</summary>
        public float MorphArrival(int rowIndex) => rowIndex * MorphStagger + MorphSeconds * 0.9f;

        private static KeyMarkerMorph.Pose MarkerPose(LaneKeyMarker marker) => new()
        {
            Position = marker.WorldCenter,
            Size = marker.WorldShapeSize,
            Outline = Mathf.Max(0.0001f, marker.WorldOutline),
            Roundness = marker.Shape == KeyMarkerShape.Circle ? 1f : 0f,
            Angle = marker.Shape == KeyMarkerShape.Diamond ? 45f : 0f,
            Color = marker.IdleColor
        };

        // The frame ring around the icon, where AbilitySlotView draws it (icon centre = 37 units above the slot).
        private KeyMarkerMorph.Pose FramePose(Slot slot)
        {
            AbilityIconFrameStyle style = theme != null && theme.IconFrame != null ? theme.IconFrame : null;
            float scale = Mathf.Abs(canvasRect.lossyScale.x) * slot.Rect.localScale.x;
            float size = IconCanvasUnits * scale;
            Vector3 center = canvasRect.TransformPoint(slot.Rect.anchoredPosition + new Vector2(0f, 10f + IconCanvasUnits * 0.5f));
            return new KeyMarkerMorph.Pose
            {
                Position = center,
                Size = size,
                Outline = size * 0.5f * (style != null ? style.FrameThickness : 0.1f),
                Roundness = 1f,
                Angle = 0f,
                Color = style != null ? style.FrameColor : new Color(1f, 0.86f, 0.5f, 1f)
            };
        }

        // ---------- Camera zoom ----------

        /// <summary>Blends to a Cinemachine camera framing the player (see the theme's Ability Selection Camera).</summary>
        public void ZoomIn(Transform focus)
        {
            if (focus != null) player = focus;
            MeasurePlayer();
            ResetRow();
            if (theme != null && !theme.ZoomOnPlayerTurn) return;
            if (zoomed || player == null || !EnsureCameras()) return;

            lanes ??= FindAnyObjectByType<CombatLanePresentation3D>();
            if (lanes != null) lanes.LayoutFrozen = true; // the zoom must not move the hit line, lanes or player
            unfreezeAt = -1f;

            LensSettings lens = combatCamera.Lens;
            float zoom = theme != null ? theme.ZoomScale : 0.55f;
            lens.OrthographicSize = Mathf.Max(0.01f, lens.OrthographicSize * zoom);
            lens.FieldOfView = Mathf.Clamp(lens.FieldOfView * zoom, 1f, 179f);
            zoomCamera.Lens = lens;
            zoomCamera.Priority = combatCamera.Priority.Value + 50;
            blendSeconds = theme != null ? theme.ZoomInSeconds : 0.45f;
            zoomed = true;
            PlaceZoomCamera();
            zoomCamera.gameObject.SetActive(true);
        }

        /// <summary>Blends back to the combat view. The layout unfreezes once the blend has finished.</summary>
        public void ZoomOut(bool immediate = false)
        {
            if (!zoomed)
            {
                if (immediate && unfreezeAt >= 0f) Unfreeze();
                return;
            }
            zoomed = false;
            blendSeconds = immediate ? 0f : theme != null ? theme.ZoomOutSeconds : 0.35f;
            if (zoomCamera != null) zoomCamera.gameObject.SetActive(false);
            if (immediate) Unfreeze();
            else unfreezeAt = Time.time + blendSeconds + 0.02f;
        }

        private void Unfreeze()
        {
            unfreezeAt = -1f;
            if (lanes != null) lanes.LayoutFrozen = false;
        }

        private bool EnsureCameras()
        {
            if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
            if (combatCamera == null || combatCamera == zoomCamera)
            {
                combatCamera = brain != null ? brain.ActiveVirtualCamera as CinemachineCamera : null;
                if (combatCamera == null || combatCamera == zoomCamera)
                {
                    combatCamera = null;
                    foreach (CinemachineCamera candidate in FindObjectsByType<CinemachineCamera>())
                        if (candidate != zoomCamera && candidate.isActiveAndEnabled) { combatCamera = candidate; break; }
                }
            }
            if (combatCamera == null) return false;

            if (zoomCamera == null)
            {
                var go = new GameObject("Ability Selection Camera (Cinemachine)");
                go.SetActive(false);
                go.transform.SetParent(transform, false);
                zoomCamera = go.AddComponent<CinemachineCamera>();
            }
            InstallBlendOverride();
            blendOwner = this;
            return true;
        }

        // Same angle as the combat camera; the player's head/body sits at the theme's screen position.
        private void PlaceZoomCamera()
        {
            if (zoomCamera == null || combatCamera == null || player == null) return;
            Transform reference = combatCamera.transform;
            Quaternion rotation = reference.rotation;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;

            Vector3 focus = player.position + Vector3.up * (headOffset - spriteHeight * 0.5f);
            float depth = Vector3.Dot(focus - reference.position, forward);
            if (depth < 1f) depth = 10f;
            Camera view = ViewCamera;
            float aspect = view != null && view.aspect > 0f ? view.aspect : 16f / 9f;
            float size = zoomCamera.Lens.OrthographicSize;
            Vector2 offset = theme != null ? theme.ZoomScreenPosition - new Vector2(0.5f, 0.5f) : new Vector2(0f, -0.12f);
            // With an ObliqueProjection the focus' screen height is not simply its offset along the camera's up.
            float upOffset = ObliqueProjection.CameraUpOffsetFor(view, rotation, focus.y, offset.y * 2f * size);
            Vector3 position = focus - forward * depth - right * (offset.x * 2f * size * aspect) - up * upOffset;
            zoomCamera.transform.SetPositionAndRotation(position, rotation);
        }

        private static void InstallBlendOverride()
        {
            if (blendOverrideInstalled) return;
            previousBlendOverride = CinemachineCore.GetBlendOverride;
            CinemachineCore.GetBlendOverride = BlendOverride;
            blendOverrideInstalled = true;
        }

        private static CinemachineBlendDefinition BlendOverride(ICinemachineCamera from, ICinemachineCamera to,
            CinemachineBlendDefinition defaultBlend, Object owner)
        {
            AbilitySelectionStage stage = blendOwner;
            if (stage != null && stage.zoomCamera != null
                && (ReferenceEquals(from, stage.zoomCamera) || ReferenceEquals(to, stage.zoomCamera)))
            {
                return stage.blendSeconds <= 0f
                    ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                    : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, stage.blendSeconds);
            }
            return previousBlendOverride != null ? previousBlendOverride(from, to, defaultBlend, owner) : defaultBlend;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            blendOwner = null;
            previousBlendOverride = null;
            blendOverrideInstalled = false;
        }

        // ---------- Charge shake ----------

        /// <summary>A hold on <paramref name="lane"/> started: show the ability's name and start the build-up.</summary>
        public void BeginCharge(int lane, string abilityName)
        {
            chargeLane = lane;
            committedLane = -1;
            chargeProgress = 0f;
            if (nameLabel != null) nameLabel.text = string.IsNullOrEmpty(abilityName) ? string.Empty : abilityName;
            SetCharge(lane, 0f);
        }

        /// <summary>Hold progress (0-1): the camera shake builds up and the icon is drawn toward the player.</summary>
        public void SetCharge(int lane, float progress)
        {
            if (lane != chargeLane) return;
            charging = true;
            chargeProgress = Mathf.Clamp01(progress);
            float start = theme != null ? theme.ChargeShakeStart : 0.004f;
            float end = theme != null ? theme.ChargeShakeEnd : 0.045f;
            float curve = theme != null ? theme.ChargeShakeCurve : 2f;
            float amount = Mathf.Lerp(start, end, Mathf.Pow(chargeProgress, curve));
            CombatCameraShaker.SetSustained(ViewCamera, amount, theme != null ? theme.ChargeShakeFrequency : 28f);
        }

        /// <summary>The hold was released early: the icon floats back up and the name fades.</summary>
        public void StopCharge()
        {
            charging = false;
            chargeLane = -1;
            chargeProgress = 0f;
            CombatCameraShaker.SetSustained(ViewCamera, 0f);
        }

        /// <summary>The ability was picked: one strong kick, the icon is swallowed by the player, the name lingers.</summary>
        public void CommitCharge(int lane)
        {
            StopCharge();
            committedLane = lane;
            namePunch = 1f;
            nameHoldUntil = Time.unscaledTime + (theme != null ? theme.AbilityNameHoldSeconds : 0.6f);
            CombatCameraShaker.Shake(ViewCamera, theme != null ? theme.SelectShake : 0.08f,
                theme != null ? theme.SelectShakeSeconds : 0.25f);
        }

        // New turn: every icon back in its place, nothing held, no name.
        private void ResetRow()
        {
            chargeLane = -1;
            committedLane = -1;
            chargeProgress = 0f;
            nameHoldUntil = -1f;
            nameAlpha = 0f;
            if (nameLabel != null) nameLabel.alpha = 0f;
            if (canvasRect != null) LayoutSlots();
            foreach (Slot slot in slots)
            {
                slot.Pull = 0f;
                if (slot.Rect != null)
                {
                    slot.Rect.anchoredPosition = slot.Rest;
                    slot.Rect.localScale = Vector3.one;
                }
                if (slot.Group != null) slot.Group.alpha = 1f;
            }
        }

        /// <summary>Battle ended or was cancelled: no zoom, no shake, layout live again.</summary>
        public void ResetAll()
        {
            StopCharge();
            ZoomOut(true);
            ResetRow();
            morph?.Clear();
        }

        private void OnDisable()
        {
            if (zoomCamera != null) zoomCamera.gameObject.SetActive(false);
            zoomed = false;
            Unfreeze();
            CombatCameraShaker.SetSustained(Camera.main, 0f);
            charging = false;
        }

        private void OnDestroy()
        {
            if (blendOwner == this) blendOwner = null;
        }
    }
}
