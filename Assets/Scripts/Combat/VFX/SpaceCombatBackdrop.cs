using System.Collections;
using System.Collections.Generic;
using RythmRPG.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>How the space backdrop moves: into the screen (Forward) or down the screen (Upward).</summary>
    public enum BackdropMotion { Forward, Upward }

    /// <summary>
    /// Makes combat look like it happens in space, flying forward: when a battle starts, a flash covers the switch, the
    /// scene around the fight is hidden and a procedural space backdrop appears behind the fighters (gradient, nebula,
    /// three star layers streaming down the screen, the nearest with speed streaks). The fighters hover. When the
    /// battle ends it all switches back.
    /// <list type="bullet">
    /// <item>Motion Forward (default): flying into the screen. Stars come out of a vanishing point and rush outward
    /// past the viewer with radial streaks, over still distant stars. Motion Upward: the star layers stream down the
    /// screen instead (climbing).</item>
    /// <item>The backdrop is drawn inside the pixel render and laid out in render-texture pixels, so it matches the art.</item>
    /// <item>Speed follows the fight: a warp-in burst at the start, a surge on every beat of the combat music, faster
    /// while notes are in play, slower while choosing an ability.</item>
    /// <item>Hidden: every renderer in the scene except the player, the enemy, the combat systems, UI and anything
    /// under a <see cref="SpaceBackdropKeepVisible"/>. Things spawned during the fight (notes, effects) are not
    /// touched. Only renderers are switched off: colliders and scripts keep working.</item>
    /// </list>
    /// Add it with Tools > Rythm RPG > Combat > Add Space Backdrop To Scene (it goes on the CombatController).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SpaceCombatBackdrop : MonoBehaviour
    {
        private const string ShaderPath = "Rendering/SpaceBackdrop";
        private const float HashPeriodCells = 4096f;

        [SerializeField] private CombatController combat;
        [Tooltip("Show the backdrop in the Scene / Game view while not playing (the scene is not hidden in edit mode).")]
        [SerializeField] private bool previewInEditMode;

        [Header("Colours")]
        [SerializeField] private Color topColor = new(0.02f, 0.02f, 0.07f);
        [SerializeField] private Color bottomColor = new(0.06f, 0.02f, 0.11f);
        [SerializeField] private Color nebulaColorA = new(0.10f, 0.04f, 0.22f);
        [SerializeField] private Color nebulaColorB = new(0.30f, 0.10f, 0.40f);
        [Tooltip("Nebula size in render-texture pixels.")]
        [SerializeField, Min(4f)] private float nebulaScale = 70f;
        [SerializeField, Range(0f, 1f)] private float nebulaStrength = 0.55f;
        [Tooltip("Colour bands in the nebula (fewer = more pixel-art).")]
        [SerializeField, Range(1, 8)] private int nebulaLevels = 4;
        [Tooltip("Nebula scroll speed, relative to the far star layer.")]
        [SerializeField, Range(0f, 1f)] private float nebulaSpeed = 0.35f;

        [Header("Motion")]
        [Tooltip("Forward: flying into the screen (stars rush out of the vanishing point toward you). " +
                 "Upward: star layers stream down the screen (climbing).")]
        [SerializeField] private BackdropMotion motion = BackdropMotion.Forward;
        [Tooltip("Forward: where the stars come from, in screen space (0-1, 0.5 = centre).")]
        [SerializeField] private Vector2 vanishingPoint = new(0.5f, 0.6f);
        [Tooltip("Forward: depth slices of stars passing per second at cruise speed.")]
        [SerializeField, Min(0f)] private float forwardSpeed = 0.2f;
        [Tooltip("Forward: star grid at the far distance in render-texture pixels (smaller = more stars).")]
        [SerializeField, Min(2f)] private float forwardCell = 7f;
        [Tooltip("Forward: chance of a star per cell (0-1).")]
        [SerializeField, Range(0f, 1f)] private float forwardDensity = 0.35f;
        [Tooltip("Forward: streak length as a share of each star's distance from the vanishing point, at cruise speed " +
                 "(grows with speed).")]
        [SerializeField, Range(0f, 0.8f)] private float forwardStreak = 0.08f;
        [SerializeField] private Color forwardStarColor = new(0.92f, 0.96f, 1f, 1f);

        [Header("Stars (far, mid, near)")]
        [Tooltip("Scroll speed of each layer in render-texture pixels per second at cruise speed.")]
        [SerializeField] private Vector3 layerSpeed = new(5f, 16f, 55f);
        [Tooltip("Grid cell per layer in render-texture pixels: at most one star per cell.")]
        [SerializeField] private Vector3 layerCell = new(9f, 16f, 30f);
        [Tooltip("Chance of a star in each cell (0-1).")]
        [SerializeField] private Vector3 layerDensity = new(0.55f, 0.45f, 0.35f);
        [Tooltip("Star size in render-texture pixels.")]
        [SerializeField] private Vector3 layerSize = new(1f, 1f, 1f);
        [Tooltip("Streak length behind each star, in render-texture pixels per cruise speed (grows with speed).")]
        [SerializeField] private Vector3 layerStreak = new(0f, 1f, 6f);
        [SerializeField] private Color farStarColor = new(0.55f, 0.60f, 0.85f, 0.55f);
        [SerializeField] private Color midStarColor = new(0.80f, 0.85f, 1.00f, 0.80f);
        [SerializeField] private Color nearStarColor = new(1.00f, 1.00f, 1.00f, 1.00f);
        [SerializeField, Range(0f, 1f)] private float twinkle = 0.35f;

        [Header("Speed")]
        [Tooltip("Speed multiplier at the start of the battle, easing down to cruise over Warp In Seconds.")]
        [SerializeField, Min(1f)] private float warpInSpeed = 7f;
        [SerializeField, Min(0.01f)] private float warpInSeconds = 1.4f;
        [Tooltip("Extra speed on every beat of the combat music (0 = off), fading out over the beat.")]
        [SerializeField, Range(0f, 2f)] private float beatSurge = 0.45f;
        [Tooltip("Speed while notes are in play (enemy attack, ability).")]
        [SerializeField, Min(0f)] private float rhythmSpeed = 1.3f;
        [Tooltip("Speed while choosing an ability.")]
        [SerializeField, Min(0f)] private float selectionSpeed = 0.6f;
        [Tooltip("How fast the speed follows those changes (per second).")]
        [SerializeField, Min(0.1f)] private float speedResponse = 2.5f;

        [Header("Fighters")]
        [Tooltip("Hover height of the player and enemy sprites in world units (0 = off).")]
        [SerializeField, Min(0f)] private float hoverAmplitude = 0.06f;
        [SerializeField, Min(0.1f)] private float hoverPeriod = 2.2f;

        [Header("Transition")]
        [Tooltip("Hide the scene around the fight during combat.")]
        [SerializeField] private bool hideScene = true;
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField, Min(0f)] private float flashInSeconds = 0.12f;
        [SerializeField, Min(0f)] private float flashOutSeconds = 0.4f;

        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
        private static readonly int NebulaColorAId = Shader.PropertyToID("_NebulaColorA");
        private static readonly int NebulaColorBId = Shader.PropertyToID("_NebulaColorB");
        private static readonly int NebulaParamsId = Shader.PropertyToID("_NebulaParams");
        private static readonly int NebulaScrollId = Shader.PropertyToID("_NebulaScroll");
        private static readonly int LayerScrollId = Shader.PropertyToID("_LayerScroll");
        private static readonly int LayerCellId = Shader.PropertyToID("_LayerCell");
        private static readonly int LayerDensityId = Shader.PropertyToID("_LayerDensity");
        private static readonly int LayerSizeId = Shader.PropertyToID("_LayerSize");
        private static readonly int LayerStreakId = Shader.PropertyToID("_LayerStreak");
        private static readonly int StarColor0Id = Shader.PropertyToID("_StarColor0");
        private static readonly int StarColor1Id = Shader.PropertyToID("_StarColor1");
        private static readonly int StarColor2Id = Shader.PropertyToID("_StarColor2");
        private static readonly int TwinkleId = Shader.PropertyToID("_Twinkle");
        private static readonly int MotionId = Shader.PropertyToID("_Motion");
        private static readonly int ForwardId = Shader.PropertyToID("_Forward");
        private static readonly int ForwardStarId = Shader.PropertyToID("_ForwardStar");
        private static readonly int VanishId = Shader.PropertyToID("_Vanish");

        private GameObject quad;
        private MeshRenderer quadRenderer;
        private Material material;
        private MaterialPropertyBlock block;
        private bool missingShaderReported;

        private bool inSpace;
        private float warpStartTime = -100f;
        private float speed = 1f;
        private float targetSpeed = 1f;
        private Vector3 scroll;       // accumulated pixels per layer
        private Vector2 nebulaScroll;
        private float clock;
        private float forwardTravel;

        private readonly List<Renderer> hiddenRenderers = new();
        private readonly List<(Transform sprite, float phase)> hoverTargets = new();
        private readonly Dictionary<Transform, float> hoverApplied = new();

        private CombatMusicDirector music;
        private Image flash;
        private Coroutine transition;

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            if (combat == null) combat = GetComponent<CombatController>();
            if (combat == null) combat = FindAnyObjectByType<CombatController>();
            if (!Application.isPlaying) return;
            if (combat != null)
            {
                combat.BattleStarted += HandleBattleStarted;
                combat.BattleEnded += HandleBattleEnded;
                combat.StateChanged += HandleStateChanged;
                if (combat.IsBattleActive) EnterSpace(immediate: true);
            }
        }

        private void OnDisable()
        {
            if (combat != null)
            {
                combat.BattleStarted -= HandleBattleStarted;
                combat.BattleEnded -= HandleBattleEnded;
                combat.StateChanged -= HandleStateChanged;
            }
            if (transition != null) StopCoroutine(transition);
            transition = null;
            if (inSpace) LeaveSpace();
            if (flash != null) SetFlash(0f);
            DestroyQuad();
        }

        private void LateUpdate()
        {
            bool show = Application.isPlaying ? inSpace : previewInEditMode;
            if (!show)
            {
                if (quad != null && quad.activeSelf) quad.SetActive(false);
                return;
            }
            if (!EnsureQuad()) return;
            if (!quad.activeSelf) quad.SetActive(true);
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            UpdateSpeed(dt);
            UpdateScroll(dt);
            PlaceQuad();
            ApplyProperties();
            if (Application.isPlaying) UpdateHover();
        }

        // ---------- Battle events ----------

        private void HandleBattleStarted(CombatEncounterContext context)
        {
            CollectHoverTargets(context);
            if (inSpace)
            {
                warpStartTime = Time.time; // restart in place: warp in again
                return;
            }
            RunTransition(true);
        }

        private void HandleBattleEnded(CombatState state)
        {
            // Also when the battle ends while the switch to space is still running.
            if (!inSpace && transition == null) return;
            RunTransition(false);
        }

        private void HandleStateChanged(CombatState state)
        {
            targetSpeed = state switch
            {
                CombatState.EnemyTurnExecuting or CombatState.PlayerAbilityExecuting => rhythmSpeed,
                CombatState.PlayerAbilitySelection => selectionSpeed,
                _ => 1f
            };
        }

        private void RunTransition(bool enter)
        {
            if (transition != null) StopCoroutine(transition);
            transition = StartCoroutine(Transition(enter));
        }

        // Flash up, switch while the screen is covered, flash down. Unscaled time: hit-stop does not stretch it.
        private IEnumerator Transition(bool enter)
        {
            EnsureFlash();
            for (float t = 0f; t < flashInSeconds; t += Time.unscaledDeltaTime)
            {
                SetFlash(t / flashInSeconds);
                yield return null;
            }
            SetFlash(1f);
            if (enter) EnterSpace(false);
            else LeaveSpace();
            yield return null;
            for (float t = 0f; t < flashOutSeconds; t += Time.unscaledDeltaTime)
            {
                SetFlash(1f - t / flashOutSeconds);
                yield return null;
            }
            SetFlash(0f);
            transition = null;
        }

        private void EnterSpace(bool immediate)
        {
            inSpace = true;
            warpStartTime = immediate ? -100f : Time.time;
            speed = targetSpeed = 1f;
            if (hideScene) HideScene();
            // GPU grass and other Renderer-less effects hide through this.
            if (hideScene && Application.isPlaying) SceneVisibility.SetWorldHidden(true);
        }

        private void LeaveSpace()
        {
            inSpace = false;
            ClearHover();
            foreach (Renderer hidden in hiddenRenderers)
                if (hidden != null) hidden.enabled = true;
            hiddenRenderers.Clear();
            SceneVisibility.SetWorldHidden(false);
            if (quad != null) quad.SetActive(false);
        }

        // ---------- Scene hiding ----------

        private void HideScene()
        {
            hiddenRenderers.Clear();
            var keepRoots = new List<Transform> { transform };
            if (combat != null) keepRoots.Add(combat.transform);
            AddKeepRoots<CombatVFXController>(keepRoots);
            AddKeepRoots<CombatLanePresentation3D>(keepRoots);
            AddKeepRoots<RhythmPatternRunner>(keepRoots);
            AddKeepRoots<PlayerCombatant>(keepRoots);
            AddKeepRoots<EnemyCombatant>(keepRoots);
            AddKeepRoots<SpaceBackdropKeepVisible>(keepRoots);

            int uiLayer = LayerMask.NameToLayer("UI");
            int crispLayer = CrispWorldUI.Layer;
            foreach (Renderer candidate in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (candidate == null || !candidate.enabled || candidate == quadRenderer) continue;
                int layer = candidate.gameObject.layer;
                if (layer == uiLayer || layer == crispLayer) continue;
                if (candidate.GetComponentInParent<Canvas>() != null) continue;
                if (IsUnder(candidate.transform, keepRoots)) continue;
                candidate.enabled = false;
                hiddenRenderers.Add(candidate);
            }
        }

        private static void AddKeepRoots<T>(List<Transform> roots) where T : Component
        {
            foreach (T component in FindObjectsByType<T>(FindObjectsInactive.Include))
                if (component != null) roots.Add(component.transform);
        }

        private static bool IsUnder(Transform candidate, List<Transform> roots)
        {
            foreach (Transform root in roots)
                if (root != null && (candidate == root || candidate.IsChildOf(root))) return true;
            return false;
        }

        // ---------- Hover ----------

        private void CollectHoverTargets(CombatEncounterContext context)
        {
            ClearHover();
            hoverTargets.Clear();
            AddHoverTarget(context.Player != null ? context.Player.transform : null, 0f);
            AddHoverTarget(context.Enemy != null ? context.Enemy.transform : null, 0.5f);
        }

        private void AddHoverTarget(Transform combatant, float phase)
        {
            if (combatant == null) return;
            SpriteRenderer sprite = combatant.GetComponentInChildren<SpriteRenderer>();
            // Move the sprite, not the combatant: positions, colliders and the lanes stay where they are.
            if (sprite != null && sprite.transform != combatant) hoverTargets.Add((sprite.transform, phase));
        }

        // Applied as a change on top of whatever else moves the sprite (animation), and removed when leaving space.
        private void UpdateHover()
        {
            foreach ((Transform sprite, float phase) in hoverTargets)
            {
                if (sprite == null) continue;
                float offset = hoverAmplitude * Mathf.Sin((Time.time / hoverPeriod + phase) * Mathf.PI * 2f);
                hoverApplied.TryGetValue(sprite, out float previous);
                sprite.position += Vector3.up * (offset - previous);
                hoverApplied[sprite] = offset;
            }
        }

        private void ClearHover()
        {
            foreach (KeyValuePair<Transform, float> applied in hoverApplied)
                if (applied.Key != null) applied.Key.position -= Vector3.up * applied.Value;
            hoverApplied.Clear();
        }

        // ---------- Speed and scroll ----------

        private void UpdateSpeed(float dt)
        {
            speed = Mathf.MoveTowards(speed, targetSpeed, speedResponse * dt);
        }

        private float CurrentSpeed()
        {
            float warp = Application.isPlaying
                ? Mathf.Lerp(warpInSpeed, 1f, Mathf.SmoothStep(0f, 1f, (Time.time - warpStartTime) / warpInSeconds))
                : 1f;
            return speed * warp * (1f + beatSurge * BeatPulse());
        }

        // 1 on the beat, fading to 0 by the next one. 0 without combat music.
        private float BeatPulse()
        {
            if (!Application.isPlaying || beatSurge <= 0f) return 0f;
            if (music == null) music = FindAnyObjectByType<CombatMusicDirector>();
            if (music == null || !music.HasSong) return 0f;
            double now = GameAudioClock.Now;
            double next = music.NextGridDsp(now, RythmRPG.Rhythm.MusicSync.NextBeat);
            double after = music.NextGridDsp(next + 0.001, RythmRPG.Rhythm.MusicSync.NextBeat);
            double length = after - next;
            if (length <= 0.0001) return 0f;
            float sinceBeat = 1f - Mathf.Clamp01((float)((next - now) / length));
            return Mathf.Pow(1f - sinceBeat, 3f);
        }

        private void UpdateScroll(float dt)
        {
            float current = CurrentSpeed();
            clock += dt;
            scroll += layerSpeed * (current * dt);
            // Wrap where the star pattern repeats, so the offsets never grow large.
            scroll.x = Mathf.Repeat(scroll.x, Mathf.Max(2f, layerCell.x) * HashPeriodCells);
            scroll.y = Mathf.Repeat(scroll.y, Mathf.Max(2f, layerCell.y) * HashPeriodCells);
            scroll.z = Mathf.Repeat(scroll.z, Mathf.Max(2f, layerCell.z) * HashPeriodCells);
            // Flying forward, the nebula barely drifts; climbing, it scrolls with the far stars.
            float nebulaRate = motion == BackdropMotion.Forward ? 0.15f : 1f;
            nebulaScroll.y = Mathf.Repeat(nebulaScroll.y + layerSpeed.x * nebulaSpeed * nebulaRate * current * dt, 65536f);
            forwardTravel = Mathf.Repeat(forwardTravel + forwardSpeed * current * dt, 4096f);
        }

        // ---------- Quad ----------

        private bool EnsureQuad()
        {
            Camera view = Camera.main;
            if (view == null) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>(ShaderPath);
                if (shader == null)
                {
                    if (!missingShaderReported)
                        Debug.LogWarning("[Combat] Space backdrop shader missing (Assets/Resources/Rendering/SpaceBackdrop.shader).", this);
                    missingShaderReported = true;
                    return false;
                }
                material = new Material(shader) { name = "Space Backdrop (runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            if (quad == null)
            {
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Space Backdrop";
                quad.hideFlags = HideFlags.DontSave;
                if (quad.TryGetComponent(out Collider collider)) DestroySafe(collider);
                quadRenderer = quad.GetComponent<MeshRenderer>();
                quadRenderer.sharedMaterial = material;
                quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
                quadRenderer.receiveShadows = false;
                quadRenderer.lightProbeUsage = LightProbeUsage.Off;
                quadRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                quadRenderer.sortingOrder = short.MinValue;
            }
            if (quad.transform.parent != view.transform) quad.transform.SetParent(view.transform, false);
            return true;
        }

        // Fills the camera's view, just in front of its far plane (behind everything).
        private void PlaceQuad()
        {
            Camera view = quad.transform.parent != null ? quad.transform.parent.GetComponent<Camera>() : null;
            if (view == null) return;
            float distance = Mathf.Lerp(view.nearClipPlane, view.farClipPlane, 0.97f);
            float height = view.orthographic
                ? 2f * view.orthographicSize
                : 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * Mathf.Max(0.01f, view.aspect);
            // An oblique projection (ObliqueProjection) would slide this far plane off screen and stretch it; undo that.
            ObliqueProjection.TryGetScreenPlane(view, distance, out float upOffset, out float heightScale);
            quad.transform.localPosition = new Vector3(0f, upOffset, distance);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(width * 1.15f, height * heightScale * 1.15f, 1f);
        }

        private void ApplyProperties()
        {
            block ??= new MaterialPropertyBlock();
            float current = CurrentSpeed();
            block.SetColor(TopColorId, topColor);
            block.SetColor(BottomColorId, bottomColor);
            block.SetColor(NebulaColorAId, nebulaColorA);
            block.SetColor(NebulaColorBId, nebulaColorB);
            block.SetVector(NebulaParamsId, new Vector4(nebulaScale, nebulaStrength, nebulaLevels, 3.7f));
            block.SetVector(NebulaScrollId, new Vector4(0f, Mathf.Floor(nebulaScroll.y), 0f, 0f));
            // Whole pixels: stars step one pixel-art pixel at a time instead of shimmering.
            block.SetVector(LayerScrollId, new Vector4(Mathf.Floor(scroll.x), Mathf.Floor(scroll.y), Mathf.Floor(scroll.z), clock));
            block.SetVector(LayerCellId, layerCell);
            block.SetVector(LayerDensityId, layerDensity);
            block.SetVector(LayerSizeId, layerSize);
            // Streaks stretch with speed (warp-in, beats), capped so they stay inside their cell.
            block.SetVector(LayerStreakId, new Vector4(
                StreakPixels(layerStreak.x, current, layerCell.x, layerSize.x),
                StreakPixels(layerStreak.y, current, layerCell.y, layerSize.y),
                StreakPixels(layerStreak.z, current, layerCell.z, layerSize.z), 0f));
            block.SetColor(StarColor0Id, farStarColor);
            block.SetColor(StarColor1Id, midStarColor);
            block.SetColor(StarColor2Id, nearStarColor);
            block.SetFloat(TwinkleId, twinkle);
            block.SetFloat(MotionId, motion == BackdropMotion.Forward ? 1f : 0f);
            block.SetVector(ForwardId, new Vector4(forwardTravel, forwardCell, forwardDensity,
                Mathf.Clamp(forwardStreak * current, 0f, 0.85f)));
            block.SetColor(ForwardStarId, forwardStarColor);
            block.SetVector(VanishId, new Vector4(vanishingPoint.x, vanishingPoint.y, 0f, 0f));
            quadRenderer.SetPropertyBlock(block);
        }

        private static float StreakPixels(float streak, float speedFactor, float cell, float size) =>
            Mathf.Clamp(Mathf.Round(streak * speedFactor), 0f, Mathf.Max(0f, cell - size - 1f));

        private void DestroyQuad()
        {
            if (quad != null) DestroySafe(quad);
            if (material != null) DestroySafe(material);
            quad = null;
            quadRenderer = null;
            material = null;
        }

        private static void DestroySafe(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        // ---------- Flash ----------

        // A full-screen image over the pixel render (under the crisp UI and the HUD).
        private void EnsureFlash()
        {
            if (flash != null) return;
            Camera view = Camera.main;
            RawImage output = null;
            if (CrispWorldUICamera.Active != null) output = CrispWorldUICamera.Active.Output;
            if (output == null && view != null && view.targetTexture != null)
                foreach (RawImage candidate in FindObjectsByType<RawImage>(FindObjectsInactive.Exclude))
                    if (candidate.texture == view.targetTexture) { output = candidate; break; }

            Transform parent;
            if (output != null) parent = output.transform;
            else
            {
                var canvasObject = new GameObject("Space Backdrop Flash Canvas", typeof(RectTransform), typeof(Canvas));
                canvasObject.transform.SetParent(transform, false);
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -100;
                parent = canvasObject.transform;
            }

            var go = new GameObject("Space Backdrop Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            flash = go.GetComponent<Image>();
            flash.raycastTarget = false;
            SetFlash(0f);
        }

        private void SetFlash(float amount)
        {
            if (flash == null) return;
            Color color = flashColor;
            color.a *= Mathf.Clamp01(amount);
            flash.color = color;
            flash.enabled = color.a > 0.001f;
        }
    }
}
