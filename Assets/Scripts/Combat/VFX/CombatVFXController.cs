using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PrimeTween;
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

        private readonly Dictionary<int, AbilitySlotView> slotViews = new();
        private AbilitySlotController slots;
        private RhythmJudgementSystem judgement;
        private EnemyCombatant enemy;
        private EnemyHitReactionView hitReaction;
        private Transform runtimeAbilityCenter;
        private Transform runtimeAbilitySelectionCenter;
        private Transform runtimeAbilityPatternSpawnOrigin;
        private bool abilitySlotsVisible;

        public float TerminalDelay => vfxTheme != null ? vfxTheme.TerminalStateDelay : 1f;
        public float SelectionHoldDuration => vfxTheme != null ? vfxTheme.SelectionHoldDuration : 0.75f;
        public Transform AbilitySpawnOrigin => abilityCenter != null ? abilityCenter : runtimeAbilityCenter;
        public Transform AbilitySelectionCenter => ResolveAbilitySelectionCenter();
        public Transform AbilityPatternSpawnOrigin => ResolveAbilityPatternSpawnOrigin();

        private void Awake()
        {
            uiTheme ??= Resources.Load<CombatUITheme>("Combat/UI/CombatUITheme");
            vfxTheme ??= Resources.Load<CombatVFXTheme>("Combat/VFX/CombatVFXTheme");
            ResolvePresentationReferences();
        }

        public void Bind(AbilitySlotController slotController, LaneInputRouter input,
            RhythmJudgementSystem judgementSystem, EnemyCombatant enemyCombatant)
        {
            Unbind();
            slotViews.Clear();
            ResolvePresentationReferences();
            slots = slotController;
            judgement = judgementSystem;
            enemy = enemyCombatant;
            foreach (LaneKeyBinding binding in input.Bindings)
            {
                if (binding.View == null) continue;
                AbilitySlotView view = binding.View.GetComponent<AbilitySlotView>() ?? binding.View.gameObject.AddComponent<AbilitySlotView>();
                slotViews[binding.LaneId] = view;
            }
            if (slots != null)
            {
                slots.SlotsChanged += RefreshSlots;
                slots.SelectionStarted += HandleSelectionStarted;
                slots.SelectionProgressed += HandleSelectionProgressed;
                slots.SelectionCancelled += HandleSelectionCancelled;
                RefreshSlots(slots.Slots);
            }
            SetAbilitySlotsVisible(false, false);
            if (judgement != null) judgement.OnJudgementResolved += PlayJudgement;
            if (enemy != null)
            {
                hitReaction = enemy.GetComponent<EnemyHitReactionView>() ?? enemy.gameObject.AddComponent<EnemyHitReactionView>();
                enemy.Damaged += HandleEnemyDamaged;
            }
        }

        public IEnumerator PlayAbilitySelected(int laneId, AbilityRuntimeInstance ability)
        {
            if (!slotViews.TryGetValue(laneId, out AbilitySlotView slot) || ability?.Definition?.Icon == null) yield break;
            GameObject iconObject = new("Selected Ability Icon");
            RhythmLaneTarget laneTarget = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).FirstOrDefault(target => target.LaneId == laneId);
            iconObject.transform.position = laneTarget != null
                ? laneTarget.transform.position + Vector3.up
                : AbilitySelectionCenter.position;
            SpriteRenderer renderer = iconObject.AddComponent<SpriteRenderer>();
            renderer.sprite = ability.Definition.Icon;
            renderer.sortingOrder = 500;
            Vector3 destination = ResolveAbilitySelectionCenter().position;
            float duration = ability.Definition.VFXProfile != null ? ability.Definition.VFXProfile.IconTravelDuration : 0.35f;
            yield return Tween.Position(iconObject.transform, destination, Mathf.Max(0.01f, duration), Ease.InOutSine).ToYieldInstruction();
            GameObject burstPrefab = ability.Definition.VFXProfile?.BurstPrefab;
            if (burstPrefab != null) Instantiate(burstPrefab, destination, Quaternion.identity);
            Tween.Scale(iconObject.transform, Vector3.one * 1.8f, 0.12f, Ease.OutBack);
            yield return new WaitForSecondsRealtime(0.12f);
            Destroy(iconObject);
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
            List<RhythmLaneTarget> lanes = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Where(target => target != null).OrderBy(target => target.LaneId).ToList();
            if (lanes.Count > 0) return lanes[lanes.Count / 2].transform;
            List<KeyValuePair<int, AbilitySlotView>> ordered = slotViews
                .Where(pair => pair.Value != null).OrderBy(pair => pair.Key).ToList();
            return ordered.Count == 0 ? null : ordered[ordered.Count / 2].Value.transform;
        }

        public void SetAbilitySlotsVisible(bool visible, bool animate)
        {
            abilitySlotsVisible = visible;
            foreach (AbilitySlotView view in slotViews.Values)
                if (view != null) view.SetVisible(visible, animate);
        }

        public void PrepareEnemyDefeat() => hitReaction?.StopAndRestore();

        private void RefreshSlots(IReadOnlyDictionary<int, AbilityRuntimeInstance> runtimeSlots)
        {
            foreach ((int lane, AbilitySlotView view) in slotViews)
            {
                runtimeSlots.TryGetValue(lane, out AbilityRuntimeInstance ability);
                view.Configure(ability);
                view.SetVisible(abilitySlotsVisible, false);
            }
        }

        private void HandleSelectionStarted(int lane, AbilityRuntimeInstance ability)
        {
            if (slotViews.TryGetValue(lane, out AbilitySlotView view)) view.SetHighlighted(true);
            CombatCameraShaker.Shake(ResolveActiveCamera(), 0.025f, 0.15f);
        }

        private void HandleSelectionProgressed(int lane, float progress)
        {
            if (slotViews.TryGetValue(lane, out AbilitySlotView view)) view.SetProgress(progress);
        }

        private void HandleSelectionCancelled(int lane)
        {
            if (!slotViews.TryGetValue(lane, out AbilitySlotView view)) return;
            view.SetProgress(0f);
            view.SetHighlighted(false);
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
            if (slotViews.TryGetValue(result.LaneId, out AbilitySlotView slot))
                slot.GetComponent<KeyButton>()?.PlayJudgementFeedback(result.Judgement, color);
        }

        /// <summary>Floating combat text (heal numbers, WARD, GUARD...).</summary>
        public void ShowFloatingText(string value, Vector3 position, Color color) => CreateFloatingText(value, position, color);

        private void CreateFloatingText(string value, Vector3 position, Color color)
        {
            GameObject label = new(value);
            label.transform.position = position;
            TextMesh mesh = label.AddComponent<TextMesh>();
            mesh.text = value;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontSize = 36;
            mesh.characterSize = 0.075f;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;
            MeshRenderer textRenderer = label.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 500;
            // Judgement text must never be hidden by world geometry or by the hit line canvas.
            textRenderer.material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            Tween.PositionY(label.transform, position.y + 0.8f, 0.65f, Ease.OutSine);
            Tween.Custom(mesh, 1f, 0f, 0.65f, (target, alpha) =>
            {
                Color current = target.color;
                current.a = alpha;
                target.color = current;
            }).OnComplete(label, target => Destroy(target));
        }

        private void Unbind()
        {
            if (slots != null)
            {
                slots.SlotsChanged -= RefreshSlots;
                slots.SelectionStarted -= HandleSelectionStarted;
                slots.SelectionProgressed -= HandleSelectionProgressed;
                slots.SelectionCancelled -= HandleSelectionCancelled;
            }
            if (judgement != null) judgement.OnJudgementResolved -= PlayJudgement;
            if (enemy != null) enemy.Damaged -= HandleEnemyDamaged;
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

            Vector3 position = AbilitySpawnOrigin != null ? AbilitySpawnOrigin.position : Vector3.zero;
            position = ClampPresentationPointAboveLanes(position);

            runtimeAbilityPatternSpawnOrigin.position = position;
            return runtimeAbilityPatternSpawnOrigin;
        }

        private Vector3 ClampPresentationPointAboveLanes(Vector3 position)
        {
            List<RhythmLaneTarget> lanes = FindObjectsByType<RhythmLaneTarget>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Where(target => target != null).OrderBy(target => target.LaneId).ToList();
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

            return FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(camera => camera != null && camera.enabled)
                .OrderByDescending(camera => camera.depth)
                .FirstOrDefault();
        }

        private void OnDisable() => Unbind();

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
