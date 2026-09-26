using System.Collections.Generic;
using RythmRPG.Core;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The protagonist (a slime, like Rimuru) is the hit line. Whenever the line appears - the enemy's turn, the
    /// player's ability chart - the character morphs into it:
    /// <list type="number">
    /// <item>it stretches up a little, then squashes flat onto the line, washing into the line's iridescent colours;</item>
    /// <item>the line spreads out of it to both ends;</item>
    /// <item>the key markers slide out of it along the line to their lanes.</item>
    /// </list>
    /// While the line is up the character itself is not drawn. When the line hides for the ability choice, the line
    /// pulls back into the character, which pops back up out of it (the markers fly up into the ability frames, as
    /// before). The line and markers keep their normal look; the pieces in motion are copies of them.
    /// Added by <see cref="CombatVFXController"/>; timing on the <see cref="CombatVFXTheme"/> (Character becomes the hit line).
    /// </summary>
    [DefaultExecutionOrder(31500)] // after the lane layout and the pixel snap: copies the sprite where it is drawn
    [DisallowMultipleComponent]
    public sealed class CharacterHitLineMorph : MonoBehaviour
    {
        private const string GhostLineName = "Character Morph Line";
        private static readonly int IridescenceId = Shader.PropertyToID("_Iridescence");

        private enum State { Character, BecomingLine, Line, BecomingCharacter }

        private CombatVFXTheme theme;
        private PlayerCombatant player;
        private CombatLanePresentation3D lanes;
        private SpriteRenderer body;
        private float bodyRescanAt;

        private State state = State.Character;
        private float elapsed;
        private float duration = 0.5f;

        // Real renderers of the character that this component switched off.
        private readonly List<Renderer> hidden = new();

        // Copy of the body sprite that does the squash / stretch.
        private SpriteRenderer ghostSprite;
        private Material ghostSpriteMaterial;

        // Copy of the line that grows out of (or back into) the character.
        private RectTransform ghostLine;
        private Image ghostLineImage;

        private KeyMarkerMorph markerMorph;
        private bool markersLaunched;

        public bool CharacterIsLine => state == State.Line || state == State.BecomingLine;
        /// <summary>A morph (either way) is still playing.</summary>
        public bool IsMorphing => state == State.BecomingLine || state == State.BecomingCharacter;

        // Set just before a hide that is not followed by the ability choice: the markers slide back into the character
        // (at the ability choice they fly up into the frames instead).
        private bool pullMarkersIn;

        /// <summary>
        /// The character steps out of the hit line (e.g. for its attack animation): the line and the key markers pull
        /// back into it and it pops up. Hides the hit line. Returns false if the character is not the line right now.
        /// </summary>
        public bool ReturnToCharacter()
        {
            if (lanes == null || !Enabled || !CharacterIsLine) return false;
            pullMarkersIn = true;
            lanes.HideHitLine(); // -> HandleHitLineShownChanged(false) -> StartBecomingCharacter
            pullMarkersIn = false;
            return true;
        }

        public void Configure(CombatVFXTheme vfxTheme) => theme = vfxTheme;

        /// <summary>Attach to the player for a battle; null detaches and shows the character again.</summary>
        public void Bind(PlayerCombatant playerCombatant, CombatLanePresentation3D lanePresentation)
        {
            Unhook();
            ShowCharacterNow();
            player = playerCombatant;
            lanes = lanePresentation;
            body = null;
            bodyRescanAt = 0f;
            if (player == null || lanes == null) return;
            lanes.HitLineShownChanged += HandleHitLineShownChanged;
            lanes.RevealHandledExternally = Enabled;
            // Already showing (bound mid-battle): become the line at once.
            if (lanes.HitLineRevealed && Enabled) BecomeLineNow();
        }

        private bool Enabled => theme == null || theme.CharacterBecomesHitLine;

        private void Unhook()
        {
            if (lanes == null) return;
            lanes.HitLineShownChanged -= HandleHitLineShownChanged;
            lanes.RevealHandledExternally = false;
        }

        private void OnDisable()
        {
            Unhook();
            ShowCharacterNow();
            if (lanes != null && lanes.HitLineRevealed) lanes.ShowHitLineNow();
        }

        private void OnDestroy()
        {
            if (ghostSprite != null) Destroy(ghostSprite.gameObject);
            if (ghostSpriteMaterial != null) Destroy(ghostSpriteMaterial);
            if (ghostLine != null) Destroy(ghostLine.gameObject);
            markerMorph?.Clear();
        }

        // ---------- triggers ----------

        private void HandleHitLineShownChanged(bool shown)
        {
            if (!Enabled || player == null)
            {
                if (shown && lanes != null) lanes.ShowHitLineNow();
                return;
            }
            if (shown) StartBecomingLine();
            else StartBecomingCharacter();
        }

        private void StartBecomingLine()
        {
            // Already the line (the hit line was revealed again, e.g. enemy turn after the ability chart).
            if (state == State.Line)
            {
                lanes.ShowHitLineNow();
                return;
            }
            if (state == State.BecomingLine) return;
            if (ResolveBody() == null)
            {
                // Nothing to morph from: just show the line.
                lanes.ShowHitLineNow();
                state = State.Line;
                return;
            }
            state = State.BecomingLine;
            elapsed = 0f;
            duration = theme != null ? theme.BecomeLineSeconds : 0.55f;
            markersLaunched = false;
            markerMorph?.Clear();
            HideCharacter();
        }

        private void StartBecomingCharacter()
        {
            if (state == State.Character || state == State.BecomingCharacter) return;
            markerMorph?.Clear();
            state = State.BecomingCharacter;
            elapsed = 0f;
            duration = theme != null ? theme.BecomeCharacterSeconds : 0.4f;
            if (pullMarkersIn)
                LaunchMarkers(lanes.RenderCamera != null ? lanes.RenderCamera : Camera.main, duration * 0.55f, true);
        }

        private void BecomeLineNow()
        {
            HideCharacter();
            state = State.Line;
            lanes.ShowHitLineNow();
        }

        // ---------- per frame ----------

        private void LateUpdate()
        {
            if (player == null || lanes == null) return;

            // The battle ended (lanes gone) while the character was the line: give the character back.
            if (state != State.Character && state != State.BecomingCharacter && !lanes.PresentationActive)
                StartBecomingCharacter();

            float dt = Time.deltaTime;
            Camera view = lanes.RenderCamera != null ? lanes.RenderCamera : Camera.main;

            switch (state)
            {
                case State.BecomingLine:
                    elapsed += dt;
                    TickBecomingLine(Mathf.Clamp01(elapsed / duration), view);
                    if (elapsed >= duration)
                    {
                        state = State.Line;
                        lanes.ShowHitLineNow();
                        SetGhostSpriteVisible(false);
                        SetGhostLineVisible(false);
                    }
                    break;
                case State.BecomingCharacter:
                    elapsed += dt;
                    TickBecomingCharacter(Mathf.Clamp01(elapsed / duration));
                    if (elapsed >= duration)
                    {
                        state = State.Character;
                        ShowCharacterNow();
                    }
                    break;
                case State.Line:
                    // Keep the character hidden even if something re-enabled a renderer (animation, hit flash).
                    if (Time.frameCount % 10 == 0) HideCharacter();
                    break;
            }

            if (markerMorph != null)
            {
                markerMorph.GhostMaterial = lanes.GhostMaterial;
                markerMorph.Tick(dt, view, 0f, MarkerDistortion);
            }
        }

        // Character -> line. 0-0.14 stretch up, 0.14-0.45 squash flat onto the line, then the copy fades into the
        // line, which spreads out (0.3-0.85); the markers slide out along it from 0.35.
        private void TickBecomingLine(float t, Camera view)
        {
            float flatW = theme != null ? theme.FlatWidth : 1.5f;
            float flatH = theme != null ? theme.FlatHeight : 0.1f;
            float stretch = theme != null ? theme.AnticipationStretch : 0.15f;

            float sx, sy;
            if (t < 0.14f)
            {
                float u = EaseOutCubic(t / 0.14f);
                sx = Mathf.Lerp(1f, 1f - stretch * 0.6f, u);
                sy = Mathf.Lerp(1f, 1f + stretch, u);
            }
            else
            {
                float u = EaseInCubic(Mathf.Clamp01((t - 0.14f) / 0.31f));
                sx = Mathf.Lerp(1f - stretch * 0.6f, flatW, u);
                sy = Mathf.Lerp(1f + stretch, flatH, u);
            }
            float iridescence = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.4f));
            float alpha = 1f - Mathf.Clamp01((t - 0.42f) / 0.12f);
            UpdateGhostSprite(sx, sy, iridescence, alpha);

            // The line spreads out from the flattened character.
            float spread = EaseOutCubic(Mathf.Clamp01((t - 0.3f) / 0.55f));
            UpdateGhostLine(t >= 0.3f ? spread : -1f, flatW);

            if (!markersLaunched && t >= 0.35f)
            {
                markersLaunched = true;
                LaunchMarkers(view, duration * 0.6f);
            }
        }

        // Line -> character: the line pulls in (0-0.5), the character pops up out of it (0.35-1) with an overshoot.
        private void TickBecomingCharacter(float t)
        {
            float flatW = theme != null ? theme.FlatWidth : 1.5f;
            float flatH = theme != null ? theme.FlatHeight : 0.1f;
            float overshoot = theme != null ? theme.ReformOvershoot : 1.7f;

            float pull = EaseInCubic(Mathf.Clamp01(t / 0.5f));
            UpdateGhostLine(lanes.PresentationActive && t < 0.5f ? 1f - pull : -1f, flatW);

            if (t < 0.35f)
            {
                UpdateGhostSprite(flatW, flatH, 1f, Mathf.Clamp01(t / 0.2f));
                return;
            }
            float u = Mathf.Clamp01((t - 0.35f) / 0.65f);
            float grow = EaseOutBack(u, overshoot);
            float sy = Mathf.LerpUnclamped(flatH, 1f, grow);
            float sx = Mathf.LerpUnclamped(flatW, 1f, EaseOutCubic(u));
            UpdateGhostSprite(sx, sy, 1f - Mathf.SmoothStep(0f, 1f, u), 1f);
        }

        // ---------- the character copy ----------

        private void UpdateGhostSprite(float scaleX, float scaleY, float iridescence, float alpha)
        {
            SpriteRenderer source = ResolveBody();
            if (source == null || alpha <= 0.001f)
            {
                SetGhostSpriteVisible(false);
                return;
            }
            EnsureGhostSprite();
            SetGhostSpriteVisible(true);

            Transform from = source.transform;
            ghostSprite.sprite = source.sprite;
            ghostSprite.flipX = source.flipX;
            ghostSprite.flipY = source.flipY;
            ghostSprite.sortingLayerID = source.sortingLayerID;
            ghostSprite.sortingOrder = source.sortingOrder;
            ghostSprite.gameObject.layer = source.gameObject.layer;
            Color color = source.color;
            color.a *= alpha;
            ghostSprite.color = color;
            ghostSpriteMaterial.SetFloat(IridescenceId, iridescence);

            // Scale about the feet, so it flattens onto the line the character stands on.
            Bounds bounds = source.bounds;
            Vector3 up = from.up;
            var feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float pivotAboveFeet = Vector3.Dot(from.position - feet, up);
            Vector3 position = from.position - up * (pivotAboveFeet * (1f - scaleY));
            Vector3 lossy = from.lossyScale;
            Transform ghost = ghostSprite.transform;
            ghost.SetPositionAndRotation(position, from.rotation);
            ghost.localScale = new Vector3(lossy.x * scaleX, lossy.y * scaleY, lossy.z);
        }

        private void EnsureGhostSprite()
        {
            if (ghostSprite != null) return;
            // Not parented, so its scale is exactly the one copied from the body.
            var go = new GameObject("Character Morph Sprite");
            go.AddComponent<SpaceBackdropKeepVisible>();
            ghostSprite = go.AddComponent<SpriteRenderer>();
            Shader shader = Resources.Load<Shader>("Combat/VFX/IridescentMorphSprite");
            if (shader == null) shader = Shader.Find("Rythm RPG/Combat/Iridescent Morph Sprite");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            ghostSpriteMaterial = new Material(shader) { name = "Character Morph Sprite (runtime)", hideFlags = HideFlags.DontSave };
            ghostSprite.sharedMaterial = ghostSpriteMaterial;
        }

        private void SetGhostSpriteVisible(bool visible)
        {
            if (ghostSprite != null && ghostSprite.enabled != visible) ghostSprite.enabled = visible;
        }

        // ---------- the line copy ----------

        // spread 0 = as wide as the flattened character, 1 = the whole line; negative = hidden.
        private void UpdateGhostLine(float spread, float flatWidth)
        {
            RectTransform anchor = lanes.HitLineAnchor;
            if (spread < 0f || anchor == null || !anchor.gameObject.activeInHierarchy)
            {
                SetGhostLineVisible(false);
                return;
            }
            EnsureGhostLine(anchor);
            SetGhostLineVisible(true);

            Rect rect = anchor.rect;
            float center = rect.center.x;
            float half = rect.width * 0.5f;
            float characterX = center;
            float characterHalf = half * 0.1f;
            SpriteRenderer source = ResolveBody();
            Camera view = lanes.RenderCamera;
            if (source != null)
            {
                Bounds bounds = source.bounds;
                Vector3 left = OnLinePlane(anchor, new Vector3(bounds.min.x, bounds.min.y, bounds.center.z), view);
                Vector3 right = OnLinePlane(anchor, new Vector3(bounds.max.x, bounds.min.y, bounds.center.z), view);
                characterX = (left.x + right.x) * 0.5f;
                characterHalf = Mathf.Abs(right.x - left.x) * 0.5f * flatWidth;
            }

            float from = Mathf.Lerp(characterX - characterHalf, rect.xMin, spread);
            float to = Mathf.Lerp(characterX + characterHalf, rect.xMax, spread);
            ghostLine.anchoredPosition = new Vector2((from + to) * 0.5f - center, 0f);
            ghostLine.sizeDelta = new Vector2(Mathf.Max(0f, to - from), 0f);

            // Same look as the real line.
            Image line = anchor.Find("Line") is Transform lineTransform ? lineTransform.GetComponent<Image>() : null;
            if (line != null)
            {
                if (ghostLineImage.sprite != line.sprite) ghostLineImage.sprite = line.sprite;
                ghostLineImage.type = line.type;
                ghostLineImage.color = line.color;
            }
            Material material = lanes.HitLineMaterial;
            if (material != null && ghostLineImage.material != material) ghostLineImage.material = material;
        }

        private void EnsureGhostLine(RectTransform anchor)
        {
            if (ghostLine != null && ghostLine.parent == anchor) return;
            if (ghostLine != null) Destroy(ghostLine.gameObject);
            var go = new GameObject(GhostLineName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.layer = anchor.gameObject.layer;
            ghostLine = (RectTransform)go.transform;
            ghostLine.SetParent(anchor, false);
            ghostLine.anchorMin = new Vector2(0.5f, 0f);
            ghostLine.anchorMax = new Vector2(0.5f, 1f);
            ghostLine.pivot = new Vector2(0.5f, 0.5f);
            ghostLine.localRotation = Quaternion.identity;
            ghostLine.localScale = Vector3.one;
            ghostLineImage = go.GetComponent<Image>();
            ghostLineImage.raycastTarget = false;
            // The real line is kept invisible (alpha 0) during the morph; this copy must not inherit that.
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 1f;
        }

        private void SetGhostLineVisible(bool visible)
        {
            if (ghostLine != null && ghostLine.gameObject.activeSelf != visible) ghostLine.gameObject.SetActive(visible);
        }

        // A world point carried onto the line's plane along the view ray, in the line's local (canvas) units.
        private static Vector3 OnLinePlane(RectTransform anchor, Vector3 world, Camera view)
        {
            Vector3 normal = anchor.forward;
            Vector3 direction = view != null ? ObliqueProjection.ViewDirection(view) : normal;
            float along = Vector3.Dot(direction, normal);
            if (Mathf.Abs(along) > 1e-4f) world -= direction * (Vector3.Dot(world - anchor.position, normal) / along);
            return anchor.InverseTransformPoint(world);
        }

        // ---------- the key markers ----------

        private KeyMarkerMorph.Distortion MarkerDistortion => new()
        {
            Stretch = theme != null ? theme.MorphStretch : 0.3f,
            Wobble = theme != null ? theme.MorphWobble : 0.08f,
            Lobes = theme != null ? theme.MorphWobbleLobes : 3,
            WobbleSpeed = theme != null ? theme.MorphWobbleSpeed : 18f
        };

        // Each marker starts as a small round blob where the character flattened and slides out along the line.
        // Each marker starts as a small round blob where the character flattened and slides out along the line;
        // inward = the reverse (markers slide back into the character and shrink away).
        private void LaunchMarkers(Camera view, float seconds, bool inward = false)
        {
            RectTransform anchor = lanes.HitLineAnchor;
            SpriteRenderer source = ResolveBody();
            if (anchor == null || source == null) return;
            markerMorph ??= new KeyMarkerMorph(transform, 60);
            markerMorph.Clear();
            markerMorph.GhostMaterial = lanes.GhostMaterial;

            Bounds bounds = source.bounds;
            Vector3 local = OnLinePlane(anchor, new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), view);
            Vector3 origin = anchor.TransformPoint(new Vector3(local.x, anchor.rect.center.y, 0f));
            float stagger = theme != null ? theme.MarkerSlideStagger : 0.04f;

            // The markers may already be hidden (inward); their positions are still valid.
            var markers = new List<LaneKeyMarker>();
            for (int laneId = 1; laneId <= GameInput.LaneCount; laneId++)
                if (lanes.TryGetKeyMarker(laneId, out LaneKeyMarker marker)) markers.Add(marker);
            markers.Sort((a, b) => Vector3.Distance(a.WorldCenter, origin).CompareTo(Vector3.Distance(b.WorldCenter, origin)));
            if (inward) markers.Reverse(); // farthest leaves first, so they arrive together

            for (int i = 0; i < markers.Count; i++)
            {
                LaneKeyMarker marker = markers[i];
                KeyMarkerMorph.Pose end = MarkerPose(marker);
                KeyMarkerMorph.Pose blob = end;
                blob.Position = origin;
                blob.Size = end.Size * 0.35f;
                blob.Outline = Mathf.Min(end.Outline, blob.Size * 0.5f);
                blob.Roundness = 1f;
                blob.Angle = 0f;
                float delay = i * stagger;
                float flight = Mathf.Max(0.05f, seconds - delay);
                if (inward) markerMorph.Add(() => end, () => blob, delay, flight, marker.LabelText, false);
                else markerMorph.Add(() => blob, () => MarkerPose(marker), delay, flight, marker.LabelText, true);
            }
        }

        private static KeyMarkerMorph.Pose MarkerPose(LaneKeyMarker marker) => new()
        {
            Position = marker.WorldCenter,
            Size = marker.WorldShapeSize,
            Outline = Mathf.Max(0.0001f, marker.WorldOutline),
            Roundness = marker.Shape == KeyMarkerShape.Circle ? 1f : 0f,
            Angle = marker.Shape == KeyMarkerShape.Diamond ? 45f : 0f,
            Color = marker.IdleColor
        };

        // ---------- showing / hiding the real character ----------

        private void HideCharacter()
        {
            if (player == null) return;
            foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || renderer.forceRenderingOff) continue;
                renderer.forceRenderingOff = true;
                hidden.Add(renderer);
            }
        }

        private void ShowCharacterNow()
        {
            foreach (Renderer renderer in hidden)
                if (renderer != null) renderer.forceRenderingOff = false;
            hidden.Clear();
            SetGhostSpriteVisible(false);
            SetGhostLineVisible(false);
            markerMorph?.Clear();
            state = State.Character;
        }

        // The body sprite: the biggest enabled SpriteRenderer under the player that is not a shadow.
        private SpriteRenderer ResolveBody()
        {
            if (player == null) return null;
            if (body != null && body.gameObject.activeInHierarchy && Time.time < bodyRescanAt) return body;
            bodyRescanAt = Time.time + 1f;
            SpriteRenderer best = null;
            float bestArea = 0f;
            foreach (SpriteRenderer candidate in player.GetComponentsInChildren<SpriteRenderer>())
            {
                if (candidate == null || candidate == ghostSprite || !candidate.enabled || candidate.sprite == null) continue;
                if (candidate.name.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                Vector3 size = candidate.bounds.size;
                float area = size.x * size.y;
                if (area > bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }
            body = best;
            return body;
        }

        private static float EaseInCubic(float t) => t * t * t;
        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        private static float EaseOutBack(float t, float overshoot)
        {
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }
    }
}
