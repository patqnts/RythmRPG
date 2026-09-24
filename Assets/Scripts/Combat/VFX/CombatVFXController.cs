using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PrimeTween;
using RythmRPG.Core;
using TMPro;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class CombatVFXController : MonoBehaviour
    {
        [SerializeField] private CombatUITheme uiTheme;
        [SerializeField] private CombatVFXTheme vfxTheme;
        [SerializeField] private Transform abilityCenter;
        [SerializeField] private Transform cameraTransform;
        [SerializeField, Min(0f)] private float minimumAbilityPatternSpawnHeightAboveLanes = 2.5f;
        [Tooltip("Pop-up / float / charge motion of the ability icons. Empty = Resources/Combat/UI/AbilitySlotAnimation.")]
        [SerializeField] private AbilitySlotAnimationProfile slotAnimation;
        [Tooltip("World-space size of floating combat text (TextMeshPro font size; 10 = 1 world unit tall).")]
        [SerializeField, Min(0.1f)] private float floatingTextSize = 2.7f;

        private readonly Dictionary<int, AbilitySlotView> slotViews = new();
        private readonly Dictionary<int, KeyButton> keyViews = new();
        private AbilitySelectionStage selectionStage;
        private LaneInputRouter boundInput;
        private AbilitySlotController slots;
        private RhythmJudgementSystem judgement;
        private EnemyCombatant enemy;
        private PlayerCombatant player;
        private EnemyHitReactionView hitReaction;
        private Transform runtimeAbilityCenter;
        private Transform runtimeAbilitySelectionCenter;
        private Transform runtimeAbilityPatternSpawnOrigin;
        private bool abilitySlotsVisible;
        private AbilityChargeEffect activeCharge;
        private int chargeLane = -1;
        private bool chargeCommitted;
        private CombatLanePresentation3D lanePresentation;

        public float TerminalDelay => vfxTheme != null ? vfxTheme.TerminalStateDelay : 1f;
        public float SelectionHoldDuration => vfxTheme != null ? vfxTheme.SelectionHoldDuration : 0.75f;
        public Transform AbilitySpawnOrigin => abilityCenter != null ? abilityCenter : runtimeAbilityCenter;
        public Transform AbilitySelectionCenter => ResolveAbilitySelectionCenter();
        public Transform AbilityPatternSpawnOrigin => ResolveAbilityPatternSpawnOrigin();

        private void Awake()
        {
            uiTheme ??= Resources.Load<CombatUITheme>("Combat/UI/CombatUITheme");
            vfxTheme ??= Resources.Load<CombatVFXTheme>("Combat/VFX/CombatVFXTheme");
            if (slotAnimation == null) slotAnimation = AbilitySlotAnimationProfile.LoadOrDefault();
            ResolvePresentationReferences();
        }

        public void Bind(AbilitySlotController slotController, LaneInputRouter input,
            RhythmJudgementSystem judgementSystem, EnemyCombatant enemyCombatant, PlayerCombatant playerCombatant = null)
        {
            Unbind();
            slotViews.Clear();
            ResolvePresentationReferences();
            slots = slotController;
            judgement = judgementSystem;
            enemy = enemyCombatant;
            player = playerCombatant;
            // The player and the enemy stand in front of the crisp hit line and key markers.
            if (player != null) CrispWorldUIOccluder.Ensure(player.gameObject);
            if (enemy != null) CrispWorldUIOccluder.Ensure(enemy.gameObject);
            if (slotAnimation == null) slotAnimation = AbilitySlotAnimationProfile.LoadOrDefault();
            keyViews.Clear();
            boundInput = input;

            // Ability icons: in a row above the player's head (world-space canvas), or above the lane buttons.
            IReadOnlyDictionary<int, RectTransform> headSlots = null;
            if ((vfxTheme == null || vfxTheme.AbilityIconsAbovePlayer) && player != null)
                headSlots = EnsureSelectionStage().BuildSlots(player.transform, input.Bindings);

            foreach (LaneKeyBinding binding in input.Bindings)
            {
                if (binding.View != null) keyViews[binding.LaneId] = binding.View;
                Component host = headSlots != null && headSlots.TryGetValue(binding.LaneId, out RectTransform slotRect)
                    ? (Component)slotRect
                    : binding.View;
                if (host == null) continue;
                AbilitySlotView view = host.GetComponent<AbilitySlotView>();
                if (view == null) view = host.gameObject.AddComponent<AbilitySlotView>();
                view.SetProfile(slotAnimation);
                view.SetFrameStyle(vfxTheme != null ? vfxTheme.IconFrame : null);
                slotViews[binding.LaneId] = view;
            }
            GameInput.BindingsChanged -= RefreshSelectionKeys;
            GameInput.BindingsChanged += RefreshSelectionKeys;
            // The lane count (and so each lane's key) changes per chart.
            input.LanesChanged -= RefreshSelectionKeys;
            input.LanesChanged += RefreshSelectionKeys;
            if (slots != null)
            {
                slots.SlotsChanged += RefreshSlots;
                slots.SelectionStarted += HandleSelectionStarted;
                slots.SelectionProgressed += HandleSelectionProgressed;
                slots.SelectionCancelled += HandleSelectionCancelled;
                slots.SelectionCommitted += HandleSelectionCommitted;
                RefreshSlots(slots.Slots);
            }
            SetAbilitySlotsVisible(false, false);
            if (player != null) player.ManaChanged += HandlePlayerManaChanged;
            if (judgement != null) judgement.OnJudgementResolved += PlayJudgement;
            if (enemy != null)
            {
                hitReaction = enemy.GetComponent<EnemyHitReactionView>() ?? enemy.gameObject.AddComponent<EnemyHitReactionView>();
                enemy.Damaged += HandleEnemyDamaged;
            }
        }

        public IEnumerator PlayAbilitySelected(int laneId, AbilityRuntimeInstance ability) =>
            PlayAbilitySelected(laneId, ability, -1d);

        /// <summary>
        /// The chosen ability leaves the player: the charge is released, the ability forms into a wisp (from its icon),
        /// arcs to centre stage, floats there and pops at <paramref name="popAtDspTime"/> (the moment its rhythm
        /// pattern's first projectile appears; negative = as soon as it has arrived). The pattern then comes out of the
        /// same point (see <see cref="AbilityPatternSpawnOrigin"/>).
        /// </summary>
        public IEnumerator PlayAbilitySelected(int laneId, AbilityRuntimeInstance ability, double popAtDspTime)
        {
            AbilityDefinition definition = ability?.Definition;
            AbilityVFXProfile profile = definition != null ? definition.VFXProfile : null;
            Camera viewCamera = ResolveActiveCamera();
            Color accent = profile != null ? profile.AccentColor : Color.white;

            // The held icon finishes its "charged" punch, then drops away as the ability leaves as a wisp.
            if (slotViews.TryGetValue(laneId, out AbilitySlotView slot))
                slot.SetVisible(false, true, slotAnimation != null ? slotAnimation.ReadyPunchSeconds : 0.15f, 0f);

            Vector3 start = activeCharge != null ? activeCharge.CorePosition : PlayerChargeCenter(profile);
            if (activeCharge != null) activeCharge.Release();
            activeCharge = null;
            chargeLane = -1;
            chargeCommitted = false;
            CastVisuals.Burst(start, Pick(profile != null ? profile.ChargeReleasePrefab : null, vfxTheme != null ? vfxTheme.DefaultChargeReleasePrefab : null),
                accent, ChargeSize(profile) * 0.6f, 8, 0.5f, viewCamera);

            AbilityWisp wisp = AbilityWisp.Create(start,
                Pick(profile != null ? profile.WispPrefab : null, vfxTheme != null ? vfxTheme.DefaultWispPrefab : null),
                definition != null ? definition.Icon : null, profile == null || profile.IconMorphsIntoWisp, accent,
                profile != null ? profile.WispSize : 0.6f, viewCamera);
            Vector3 destination = CenterStagePoint();
            yield return wisp.Fly(destination, profile != null ? profile.WispTravelSeconds : 0.45f,
                profile != null ? profile.WispArcHeight : 0.8f);
            yield return wisp.HoverUntil(popAtDspTime, profile != null ? profile.WispMinHoverSeconds : 0.1f);
            wisp.Pop(Pick(profile != null ? profile.PopPrefab : null, vfxTheme != null ? vfxTheme.DefaultPopPrefab : null),
                profile != null ? profile.PopSeconds : 0.25f);
            CombatCameraShaker.Shake(viewCamera, profile != null ? profile.PopShake : 0.05f, 0.18f);
        }

        /// <summary>Removes a charge or wisp left over when a battle ends or is cancelled mid-cast.</summary>
        public void ClearCastEffects()
        {
            if (selectionStage != null) selectionStage.ResetAll();
            if (activeCharge != null) activeCharge.Cancel();
            activeCharge = null;
            chargeLane = -1;
            chargeCommitted = false;
            foreach (AbilityWisp wisp in FindObjectsByType<AbilityWisp>(FindObjectsInactive.Include))
                if (wisp != null) Destroy(wisp.gameObject);
        }

        /// <summary>
        /// Shortest time from selecting an ability to its wisp popping (flight + minimum hover), and never shorter than
        /// the camera's blend back from the ability-selection zoom, so no note appears before the combat view is back.
        /// </summary>
        public float MinimumCastSeconds(AbilityDefinition definition)
        {
            AbilityVFXProfile profile = definition != null ? definition.VFXProfile : null;
            float cast = profile != null ? profile.WispTravelSeconds + profile.WispMinHoverSeconds : 0.55f;
            float zoomOut = selectionStage != null ? selectionStage.ZoomOutSeconds + 0.05f : 0f;
            return Mathf.Max(cast, zoomOut);
        }

        private AbilitySelectionStage EnsureSelectionStage()
        {
            if (selectionStage == null) selectionStage = GetComponent<AbilitySelectionStage>();
            if (selectionStage == null) selectionStage = gameObject.AddComponent<AbilitySelectionStage>();
            selectionStage.Configure(vfxTheme);
            return selectionStage;
        }

        private void RefreshSelectionKeys()
        {
            if (selectionStage != null && boundInput != null) selectionStage.RefreshKeyLabels(boundInput.Bindings);
        }

        /// <summary>A small spark at <paramref name="position"/> as one of the ability's projectiles is thrown.</summary>
        public void PlayNoteSpark(Vector3 position, AbilityDefinition definition)
        {
            AbilityVFXProfile profile = definition != null ? definition.VFXProfile : null;
            if (profile != null && !profile.SparkOnEveryNote) return;
            GameObject prefab = Pick(profile != null ? profile.NoteSparkPrefab : null,
                vfxTheme != null ? vfxTheme.DefaultNoteSparkPrefab : null);
            Color accent = profile != null ? profile.AccentColor : Color.white;
            CastVisuals.Burst(position, prefab, accent, 0.35f, 6, 0.35f, ResolveActiveCamera());
        }

        /// <summary>Between the player and the enemy (theme: Centre Stage Bias / Height), on the note lanes' plane.</summary>
        public Vector3 CenterStagePoint()
        {
            if (player == null || enemy == null) return ResolveAbilitySelectionCenter().position;
            float bias = vfxTheme != null ? vfxTheme.CenterStageBias : 0.5f;
            float height = vfxTheme != null ? vfxTheme.CenterStageHeight : 0f;
            Vector3 point = Vector3.Lerp(player.transform.position, enemy.transform.position, bias);
            CombatLanePresentation3D lanes = ResolveLanePresentation();
            if (lanes != null && lanes.HorizontalGameplay) point.y = lanes.GameplayHeight + height;
            else point += Vector3.up * height;
            return point;
        }

        private void StartCharge(int lane, AbilityRuntimeInstance ability)
        {
            if (activeCharge != null) activeCharge.Cancel();
            AbilityVFXProfile profile = ability?.Definition != null ? ability.Definition.VFXProfile : null;
            Transform anchor = player != null ? player.transform : null;
            Vector3 center = PlayerChargeCenter(profile);
            activeCharge = AbilityChargeEffect.Create(anchor, anchor != null ? center - anchor.position : center,
                Pick(profile != null ? profile.ChargePrefab : null, vfxTheme != null ? vfxTheme.DefaultChargePrefab : null),
                profile != null ? profile.AccentColor : Color.white, ChargeSize(profile), ResolveActiveCamera());
            chargeLane = lane;
            chargeCommitted = false;
        }

        // Centre of the player's sprite (+ the profile's offset): the charge gathers "around the character".
        private Vector3 PlayerChargeCenter(AbilityVFXProfile profile)
        {
            Vector3 offset = profile != null ? profile.ChargeOffset : Vector3.zero;
            if (player == null) return ResolveAbilitySelectionCenter().position + offset;
            SpriteRenderer body = player.GetComponentInChildren<SpriteRenderer>();
            Vector3 center = body != null ? body.bounds.center : player.transform.position + Vector3.up;
            return center + offset;
        }

        private static float ChargeSize(AbilityVFXProfile profile) => profile != null ? profile.ChargeSize : 1f;

        private static GameObject Pick(GameObject preferred, GameObject fallback) => preferred != null ? preferred : fallback;

        private CombatLanePresentation3D ResolveLanePresentation()
        {
            if (lanePresentation == null) lanePresentation = FindAnyObjectByType<CombatLanePresentation3D>();
            return lanePresentation;
        }

        public IEnumerator PlayAbilityImpact(AbilityRuntimeInstance ability, Transform origin, Transform target)
        {
            if (ability?.Definition == null || target == null) yield break;
            if (ability.Definition.AbilityType != AbilityType.BasicAttack
                && ability.Definition.AbilityType != AbilityType.SpecialAttack) yield break;

            AbilityVFXProfile profile = ability.Definition.VFXProfile;
            Vector3 originOffset = profile != null ? profile.ImpactOriginOffset : Vector3.up * 1.05f;
            Vector3 targetOffset = profile != null ? profile.ImpactTargetOffset : Vector3.up * 0.35f;
            Vector3 start = origin != null ? origin.position + originOffset
                : AbilitySpawnOrigin != null ? AbilitySpawnOrigin.position : Vector3.zero;
            Vector3 end = target.position + targetOffset;
            CombatLanePresentation3D lanePresentation = FindAnyObjectByType<CombatLanePresentation3D>();
            bool horizontal = lanePresentation != null && lanePresentation.HorizontalGameplay;
            if (horizontal)
            {
                start.y = lanePresentation.GameplayHeight;
                end.y = lanePresentation.GameplayHeight;
            }
            GameObject projectile = profile?.ImpactProjectilePrefab != null
                ? Instantiate(profile.ImpactProjectilePrefab, start, profile.ImpactProjectilePrefab.transform.rotation)
                : new GameObject($"{ability.Definition.DisplayName} Impact Projectile");
            projectile.transform.position = start;
            projectile.transform.localScale = Vector3.one * (profile != null ? profile.ImpactProjectileScale : 0.55f);
            SpriteRenderer renderer = projectile.GetComponentInChildren<SpriteRenderer>();
            bool usingFallbackSprite = renderer == null;
            if (usingFallbackSprite) renderer = projectile.AddComponent<SpriteRenderer>();
            if (usingFallbackSprite) renderer.sprite = CreateFallbackProjectileSprite();
            renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, 520);
            Color accent = ability.Definition.VFXProfile != null ? ability.Definition.VFXProfile.AccentColor : Color.white;
            if (usingFallbackSprite) renderer.color = new Color(accent.r, accent.g, accent.b, 1f);

            float distance = Vector3.Distance(start, end);
            float speed = profile != null ? profile.ImpactProjectileSpeed : 14f;
            float duration = Mathf.Clamp(distance / Mathf.Max(0.01f, speed), 0.22f, 0.9f);
            Vector3 direction = end - start;
            if (horizontal)
            {
                RhythmNoteVisualLayer visualLayer = projectile.GetComponent<RhythmNoteVisualLayer>()
                    ?? projectile.AddComponent<RhythmNoteVisualLayer>();
                visualLayer.FaceSpritesToCamera(lanePresentation.RenderCamera);
                CrispWorldUIOccluder.Ensure(projectile);
            }
            else if (direction.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                projectile.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }

            yield return Tween.Position(projectile.transform, end, duration, Ease.InQuad).ToYieldInstruction();
            Tween.Scale(projectile.transform, Vector3.one * 1.7f, 0.08f, Ease.OutBack);
            yield return new WaitForSecondsRealtime(0.08f);
            Destroy(projectile);
        }

        public Transform GetCenterLaneViewTransform()
        {
            List<RhythmLaneTarget> lanes = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Exclude).Where(target => target != null).OrderBy(target => target.LaneId).ToList();
            if (lanes.Count > 0) return lanes[lanes.Count / 2].transform;
            List<KeyValuePair<int, AbilitySlotView>> ordered = slotViews
                .Where(pair => pair.Value != null).OrderBy(pair => pair.Key).ToList();
            return ordered.Count == 0 ? null : ordered[ordered.Count / 2].Value.transform;
        }

        /// <summary>
        /// Show/hide the ability icons. Animated shows pop up left to right after the profile's delay, each one
        /// bobbing a little behind its neighbour; animated hides drop them away in the same order.
        /// </summary>
        public void SetAbilitySlotsVisible(bool visible, bool animate)
        {
            abilitySlotsVisible = visible;
            // Choosing an ability: the camera zooms in on the player; hiding the choice blends back to the combat view.
            if (selectionStage != null)
            {
                if (visible && slotViews.Count > 0) selectionStage.ZoomIn(player != null ? player.transform : null);
                else if (!visible) selectionStage.ZoomOut(!animate);
            }
            if (visible) RefreshUsability();
            AbilitySlotAnimationProfile profile = slotAnimation != null ? slotAnimation : AbilitySlotAnimationProfile.LoadOrDefault();
            // The hit-line key markers fly up and warp into the frames: each icon pops in as its marker arrives.
            bool morphing = visible && animate && selectionStage != null && selectionStage.MorphIn();
            int index = 0;
            foreach (KeyValuePair<int, AbilitySlotView> pair in slotViews.OrderBy(pair => pair.Key))
            {
                if (pair.Value == null) continue;
                float delay = !visible ? index * profile.HideStagger
                    : morphing ? selectionStage.MorphArrival(index)
                    : profile.AppearDelay + index * profile.AppearStagger;
                pair.Value.SetVisible(visible, animate, delay, index * profile.FloatPhaseStep);
                index++;
            }
        }

        private void RefreshUsability()
        {
            if (slots == null) return;
            foreach ((int lane, AbilitySlotView view) in slotViews)
            {
                if (view == null) continue;
                slots.Slots.TryGetValue(lane, out AbilityRuntimeInstance ability);
                view.SetUsable(ability != null && (player == null || ability.CanUse(player)));
            }
        }

        private void HandlePlayerManaChanged(int current, int maximum) => RefreshUsability();

        public void PrepareEnemyDefeat() => hitReaction?.StopAndRestore();

        private void RefreshSlots(IReadOnlyDictionary<int, AbilityRuntimeInstance> runtimeSlots)
        {
            foreach ((int lane, AbilitySlotView view) in slotViews)
            {
                runtimeSlots.TryGetValue(lane, out AbilityRuntimeInstance ability);
                view.Configure(ability);
            }
            RefreshUsability();
        }

        private void HandleSelectionStarted(int lane, AbilityRuntimeInstance ability)
        {
            if (slotViews.TryGetValue(lane, out AbilitySlotView view)) view.SetCharging(true);
            StartCharge(lane, ability);
            // Rumble that builds while held, the icon is drawn toward the player, and the ability's name shows.
            if (selectionStage != null) selectionStage.BeginCharge(lane, ability?.Definition != null ? ability.Definition.DisplayName : null);
            else CombatCameraShaker.Shake(ResolveActiveCamera(), 0.025f, 0.15f);
        }

        private void HandleSelectionProgressed(int lane, float progress)
        {
            if (slotViews.TryGetValue(lane, out AbilitySlotView view)) view.SetProgress(progress);
            if (activeCharge != null && lane == chargeLane) activeCharge.SetProgress(progress);
            if (selectionStage != null) selectionStage.SetCharge(lane, progress);
        }

        // The hold completed: the charge stays up until PlayAbilitySelected turns it into the wisp.
        private void HandleSelectionCommitted(int lane, AbilityRuntimeInstance ability)
        {
            if (lane == chargeLane) chargeCommitted = true;
            if (selectionStage == null) return;
            // Picked: one kick, the other icons drop away and the camera heads back to the combat view while the
            // chosen ability's charge turns into its wisp.
            selectionStage.CommitCharge(lane);
            selectionStage.ZoomOut();
            // The other icons simply vanish (no animation); the key markers come back with the hit line.
            foreach (KeyValuePair<int, AbilitySlotView> pair in slotViews)
                if (pair.Value != null && pair.Key != lane) pair.Value.SetVisible(false, false);
        }

        private void HandleSelectionCancelled(int lane)
        {
            if (selectionStage != null && !chargeCommitted) selectionStage.StopCharge();
            if (activeCharge != null && lane == chargeLane && !chargeCommitted)
            {
                activeCharge.Cancel();
                activeCharge = null;
                chargeLane = -1;
            }
            if (!slotViews.TryGetValue(lane, out AbilitySlotView view)) return;
            view.SetCharging(false);
            view.SetProgress(0f);
        }

        private void HandleEnemyDamaged(int amount)
        {
            hitReaction?.Play(vfxTheme != null ? vfxTheme.HitFlashDuration : 0.12f,
                vfxTheme != null ? vfxTheme.HitShakeStrength : 0.12f);
            if (enemy != null) CreateFloatingText($"-{amount}", enemy.transform.position + Vector3.up, Color.white);
        }

        private void PlayJudgement(RhythmJudgementResult result)
        {
            string text = uiTheme != null ? uiTheme.GetText(result.Judgement) : result.Judgement.ToString().ToUpperInvariant();
            Color color = uiTheme != null ? uiTheme.GetColor(result.Judgement) : Color.white;
            CreateFloatingText(text, result.WorldPosition + Vector3.up * 0.65f, color);
            if (keyViews.TryGetValue(result.LaneId, out KeyButton key) && key != null)
                key.PlayJudgementFeedback(result.Judgement, color);
        }

        /// <summary>Floating combat text (heal numbers, WARD, GUARD...).</summary>
        public void ShowFloatingText(string value, Vector3 position, Color color) => CreateFloatingText(value, position, color);

        private void CreateFloatingText(string value, Vector3 position, Color color)
        {
            GameObject label = new(value);
            label.transform.position = position;
            // World-space TextMeshPro (3D). Font size is in world units x10: 2.7 ~ the old TextMesh size.
            TextMeshPro mesh = label.AddComponent<TextMeshPro>();
            // Drawn at screen resolution over the pixel render texture when the crisp camera is set up.
            CrispWorldUI.Apply(label);
            CombatHudStyle hud = CombatHudStyle.LoadOrDefault();
            TMP_FontAsset font = hud.FontAsset;
            if (font != null) mesh.font = font;
            mesh.text = value;
            mesh.alignment = TextAlignmentOptions.Center;
            mesh.textWrappingMode = TextWrappingModes.NoWrap;
            mesh.overflowMode = TextOverflowModes.Overflow;
            mesh.fontSize = floatingTextSize;
            mesh.fontStyle = FontStyles.Bold;
            mesh.color = color;
            mesh.rectTransform.sizeDelta = new Vector2(6f, 1.5f);
            // Judgement text must never be hidden by world geometry or by the hit line canvas: shared ZTest-Always
            // material with an outline for readability (one material per font, not one per popup).
            CombatText.ApplyOutline(mesh, hud.TextOutline, CombatText.DefaultOutlineWidth, alwaysOnTop: true);
            MeshRenderer textRenderer = label.GetComponent<MeshRenderer>();
            if (textRenderer != null) textRenderer.sortingOrder = 500;
            Tween.PositionY(label.transform, position.y + 0.8f, 0.65f, Ease.OutSine);
            Tween.Custom(mesh, 1f, 0f, 0.65f, (target, alpha) => target.alpha = alpha)
                .OnComplete(label, target => Destroy(target));
        }

        private void Unbind()
        {
            if (slots != null)
            {
                slots.SlotsChanged -= RefreshSlots;
                slots.SelectionStarted -= HandleSelectionStarted;
                slots.SelectionProgressed -= HandleSelectionProgressed;
                slots.SelectionCancelled -= HandleSelectionCancelled;
                slots.SelectionCommitted -= HandleSelectionCommitted;
            }
            if (judgement != null) judgement.OnJudgementResolved -= PlayJudgement;
            if (enemy != null) enemy.Damaged -= HandleEnemyDamaged;
            GameInput.BindingsChanged -= RefreshSelectionKeys;
            if (boundInput != null) boundInput.LanesChanged -= RefreshSelectionKeys;
            if (player != null) player.ManaChanged -= HandlePlayerManaChanged;
        }

        private void ResolvePresentationReferences()
        {
            Camera activeCamera = Camera.main;
            if (cameraTransform == null && activeCamera != null) cameraTransform = activeCamera.transform;
            if (abilityCenter != null || activeCamera == null) return;
            if (runtimeAbilityCenter == null)
            {
                GameObject center = new("Runtime Ability Center");
                center.transform.SetParent(transform, false);
                runtimeAbilityCenter = center.transform;
            }
            Vector3 worldCenter = activeCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.55f,
                Mathf.Abs(activeCamera.transform.position.z)));
            worldCenter.z = 0f;
            runtimeAbilityCenter.position = worldCenter;
        }

        private Transform ResolveAbilitySelectionCenter()
        {
            Camera activeCamera = ResolveActiveCamera();
            if (cameraTransform == null && activeCamera != null) cameraTransform = activeCamera.transform;
            if (runtimeAbilitySelectionCenter == null)
            {
                GameObject center = new("Runtime Ability Selection Center");
                center.transform.SetParent(transform, false);
                runtimeAbilitySelectionCenter = center.transform;
            }

            Vector3 position = Vector3.zero;
            if (activeCamera != null)
            {
                position = activeCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.55f,
                    Mathf.Abs(activeCamera.transform.position.z)));
            }

            position = ClampPresentationPointAboveLanes(position);
            runtimeAbilitySelectionCenter.position = position;
            return runtimeAbilitySelectionCenter;
        }

        private Transform ResolveAbilityPatternSpawnOrigin()
        {
            ResolvePresentationReferences();
            if (runtimeAbilityPatternSpawnOrigin == null)
            {
                GameObject origin = new("Runtime Ability Pattern Spawn Origin");
                origin.transform.SetParent(transform, false);
                runtimeAbilityPatternSpawnOrigin = origin.transform;
            }

            CombatLanePresentation3D lanes = ResolveLanePresentation();
            Vector3 position;
            if (lanes != null && lanes.HorizontalGameplay && player != null && enemy != null)
            {
                // 2.5D: the pattern bursts out of centre stage, where the ability's wisp pops.
                position = CenterStagePoint();
            }
            else
            {
                position = AbilitySpawnOrigin != null ? AbilitySpawnOrigin.position : Vector3.zero;
                position = ClampPresentationPointAboveLanes(position);
            }

            runtimeAbilityPatternSpawnOrigin.position = position;
            return runtimeAbilityPatternSpawnOrigin;
        }

        private Vector3 ClampPresentationPointAboveLanes(Vector3 position)
        {
            List<RhythmLaneTarget> lanes = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Exclude).Where(target => target != null).OrderBy(target => target.LaneId).ToList();
            if (lanes.Count > 0)
            {
                float highestLaneY = lanes.Select(target => target.transform.position.y).Max();
                position.x = lanes[lanes.Count / 2].transform.position.x;
                position.y = Mathf.Max(position.y, highestLaneY + minimumAbilityPatternSpawnHeightAboveLanes);
            }
            else if (slotViews.Count > 0)
            {
                List<AbilitySlotView> orderedSlots = slotViews.Values.Where(view => view != null)
                    .OrderBy(view => view.transform.position.x).ToList();
                if (orderedSlots.Count > 0)
                {
                    float highestLaneY = orderedSlots.Select(view => view.transform.position.y).Max();
                    position.x = orderedSlots[orderedSlots.Count / 2].transform.position.x;
                    position.y = Mathf.Max(position.y, highestLaneY + minimumAbilityPatternSpawnHeightAboveLanes);
                }
            }

            position.z = 0f;
            return position;
        }

        private Camera ResolveActiveCamera()
        {
            if (cameraTransform != null && cameraTransform.TryGetComponent(out Camera assignedCamera))
            {
                return assignedCamera;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null) return mainCamera;

            return FindObjectsByType<Camera>(FindObjectsInactive.Exclude)
                .Where(camera => camera != null && camera.enabled)
                .OrderByDescending(camera => camera.depth)
                .FirstOrDefault();
        }

        private void OnDisable()
        {
            Unbind();
            if (selectionStage != null) selectionStage.ResetAll();
            if (activeCharge != null) activeCharge.Cancel();
            activeCharge = null;
        }

        private static Sprite fallbackProjectileSprite;

        private static Sprite CreateFallbackProjectileSprite()
        {
            if (fallbackProjectileSprite != null) return fallbackProjectileSprite;
            Texture2D texture = new(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float point = Mathf.Abs(y - 15.5f) + Mathf.Max(0f, 15f - x) * 0.35f;
                    bool core = x > 6 && x < 29 && point < 8f;
                    bool tail = x <= 12 && Mathf.Abs(y - 15.5f) < Mathf.Lerp(1.5f, 5f, x / 12f);
                    texture.SetPixel(x, y, core || tail ? Color.white : Color.clear);
                }
            }
            texture.Apply();
            fallbackProjectileSprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
            return fallbackProjectileSprite;
        }
    }
}
