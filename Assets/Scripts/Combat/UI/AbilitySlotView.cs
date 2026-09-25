using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The ability icon above a lane button. All motion comes from an <see cref="AbilitySlotAnimationProfile"/>:
    /// staggered pop-up, idle float, charge shake while held, a punch when the hold completes, and hide.
    /// The icon and the radial hold fill are animated; the lane button itself is never moved or scaled.
    /// Works on UI buttons (Image) and world buttons (SpriteRenderer + LineRenderer radial).
    /// </summary>
    public sealed class AbilitySlotView : MonoBehaviour
    {
        private enum Phase { Hidden, Appearing, Shown, Hiding }

        private const int Segments = 40;

        [Tooltip("Empty = the profile the combat VFX controller passes in, or the default one from Resources.")]
        [SerializeField] private AbilitySlotAnimationProfile profile;

        private SpriteRenderer iconRenderer;
        private UnityEngine.UI.Image uiIcon;
        private UnityEngine.UI.Image uiRadialFill;
        // UI mode: "Ability Icon" is an animated root holding a backdrop disc, a circular mask with the icon, and a frame.
        private UnityEngine.UI.Image uiFrame;
        private UnityEngine.UI.Image uiBackdrop;
        private UnityEngine.UI.Image uiMaskImage;
        private UnityEngine.UI.Mask uiMask;
        private RectTransform uiMaskRect;
        private AbilityIconFrameStyle frameStyle;
        private Color accentColor = Color.white;
        private bool hasAccent;
        private Transform iconTransform;
        private Transform radialTransform;
        private LineRenderer radialFill;
        private Material radialMaterial;
        private Vector3 iconRestingLocalPosition;
        private Vector3 radialRestingLocalPosition;
        private Color iconBaseColor = Color.white;
        private Color radialBaseColor = new(1f, 1f, 1f, 0.45f);

        private Phase phase = Phase.Hidden;
        // appearT/hideT/floatLift/punchT are 0..1 (floatLift is -1..1) progress values driven by PrimeTween;
        // Apply() is still the single place that blends them into one final transform, every LateUpdate.
        private float appearT;
        private float hideT;
        private float floatLift;
        private float floatPhase;
        private Tween appearTween;
        private Tween hideTween;
        private Tween floatTween;
        private Tween punchTween;
        private bool punchActive;
        private float punchT;
        private bool usable = true;
        private bool charging;
        private float chargeProgress;
        private float chargeBlend;

        public bool IsShown => phase == Phase.Appearing || phase == Phase.Shown;

        private AbilitySlotAnimationProfile Profile
        {
            get
            {
                if (profile == null) profile = AbilitySlotAnimationProfile.LoadOrDefault();
                return profile;
            }
        }

        private void Awake()
        {
            EnsureVisuals();
            SetVisible(false, false);
        }

        public void SetProfile(AbilitySlotAnimationProfile animationProfile)
        {
            if (animationProfile != null) profile = animationProfile;
        }

        /// <summary>Circular frame / backdrop / hold-ring look (UI icons). Null = the defaults.</summary>
        public void SetFrameStyle(AbilityIconFrameStyle style)
        {
            frameStyle = style;
            EnsureVisuals();
            ApplyFrameStyle();
        }

        public void Configure(AbilityRuntimeInstance ability)
        {
            EnsureVisuals();
            AbilityVFXProfile vfx = ability?.Definition != null ? ability.Definition.VFXProfile : null;
            hasAccent = vfx != null;
            if (hasAccent)
            {
                accentColor = vfx.AccentColor;
                accentColor.a = 1f;
            }
            ApplyFrameStyle();
            Sprite icon = ability?.Definition?.Icon;
            iconBaseColor = new Color(1f, 1f, 1f, ability == null ? 0.25f : 1f);
            if (uiIcon != null) uiIcon.sprite = icon;
            if (iconRenderer != null) iconRenderer.sprite = icon;
            SetProgress(0f);
            Apply();
        }

        /// <summary>Dims the icon when the ability cannot be used right now (mana, cooldown).</summary>
        public void SetUsable(bool isUsable)
        {
            usable = isUsable;
            Apply();
        }

        public void SetVisible(bool isVisible, bool animate) => SetVisible(isVisible, animate, 0f, 0f);

        /// <summary>
        /// Show or hide. Calling it again with the same visibility does nothing, so an animation in progress is never
        /// restarted or cut. delay = seconds before this icon starts; floatPhase01 = where in the bob it starts.
        /// </summary>
        public void SetVisible(bool isVisible, bool animate, float delay, float floatPhase01)
        {
            EnsureVisuals();
            AbilitySlotAnimationProfile p = Profile;
            if (isVisible)
            {
                floatPhase = floatPhase01;
                RestartFloatTween(p);
                if (IsShown) return;
                StartAppear(p, animate, delay);
            }
            else
            {
                SetCharging(false);
                if (phase == Phase.Hidden) return;
                if (!animate)
                {
                    appearTween.Stop();
                    hideTween.Stop();
                    phase = Phase.Hidden;
                }
                else if (phase != Phase.Hiding) StartHide(p, delay);
            }
            Apply();
        }

        // Idle bob: a continuous infinite yoyo (-1..1) so the row bobs without any per-frame Time.unscaledTime
        // polling. startDelay approximates the old per-slot phase offset, so the row still bobs like a wave.
        private void RestartFloatTween(AbilitySlotAnimationProfile p)
        {
            floatTween.Stop();
            float half = Mathf.Max(0.01f, p.FloatPeriod * 0.5f);
            float phaseDelay = Mathf.Repeat(floatPhase, 1f) * p.FloatPeriod;
            floatTween = Tween.Custom(this, new TweenSettings<float>(-1f, 1f,
                new TweenSettings(half, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, startDelay: phaseDelay)),
                (view, v) => view.floatLift = v);
        }

        private void StartAppear(AbilitySlotAnimationProfile p, bool animate, float delay)
        {
            appearTween.Stop();
            hideTween.Stop();
            appearT = 0f;
            phase = animate ? Phase.Appearing : Phase.Shown;
            if (!animate) return;
            appearTween = Tween.Custom(this, new TweenSettings<float>(0f, 1f,
                new TweenSettings(Mathf.Max(0.0001f, p.AppearSeconds), Ease.Linear, startDelay: Mathf.Max(0f, delay))),
                (view, t) => view.appearT = t);
            appearTween.OnComplete(this, view => view.phase = Phase.Shown);
        }

        private void StartHide(AbilitySlotAnimationProfile p, float delay)
        {
            appearTween.Stop();
            hideTween.Stop();
            hideT = 0f;
            phase = Phase.Hiding;
            hideTween = Tween.Custom(this, new TweenSettings<float>(0f, 1f,
                new TweenSettings(Mathf.Max(0.0001f, p.HideSeconds), Ease.Linear, startDelay: Mathf.Max(0f, delay))),
                (view, t) => view.hideT = t);
            hideTween.OnComplete(this, view => view.phase = Phase.Hidden);
        }

        /// <summary>Hold on this ability started (true) or was released/cancelled (false).</summary>
        public void SetCharging(bool isCharging)
        {
            // Progress is kept on release so the charge look fades out instead of snapping back.
            if (isCharging && !charging) chargeProgress = 0f;
            charging = isCharging;
        }

        /// <summary>Kept for older callers: highlighted = charging.</summary>
        public void SetHighlighted(bool highlighted) => SetCharging(highlighted);

        public void SetProgress(float progress)
        {
            EnsureVisuals();
            float clamped = Mathf.Clamp01(progress);
            if (clamped >= 1f && chargeProgress < 1f && charging) TriggerReadyPunch();
            chargeProgress = clamped;

            bool showRadial = IsShown && clamped > 0f && (frameStyle == null || frameStyle.ShowHoldProgress);
            if (uiRadialFill != null)
            {
                uiRadialFill.enabled = showRadial;
                uiRadialFill.fillAmount = showRadial ? clamped : 0f;
                return;
            }
            if (radialFill == null) return;
            radialFill.enabled = showRadial;
            if (!showRadial)
            {
                radialFill.positionCount = 0;
                return;
            }
            int count = Mathf.Max(0, Mathf.RoundToInt(Segments * clamped));
            radialFill.positionCount = count + (count > 0 ? 1 : 0);
            for (int index = 0; index <= count && radialFill.positionCount > 0; index++)
            {
                float angle = Mathf.Lerp(90f, -270f, (float)index / Segments) * Mathf.Deg2Rad;
                radialFill.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.58f);
            }
        }

        private void TriggerReadyPunch()
        {
            AbilitySlotAnimationProfile p = Profile;
            punchTween.Stop();
            punchT = 0f;
            punchActive = true;
            punchTween = Tween.Custom(this, 0f, 1f, Mathf.Max(0.0001f, p.ReadyPunchSeconds), (view, t) => view.punchT = t, Ease.Linear);
            punchTween.OnComplete(this, view => view.punchActive = false);
        }

        private void LateUpdate() => Apply(true);

        private void Apply(bool advance = false)
        {
            if (iconTransform == null) return;
            AbilitySlotAnimationProfile p = Profile;
            float dt = advance ? Time.unscaledDeltaTime : 0f; // blend only advances once per frame

            float scale = 1f;
            float alpha = 1f;
            float lift = 0f; // icon heights, + = up
            bool active = true;

            switch (phase)
            {
                case Phase.Hidden:
                    active = false;
                    break;
                case Phase.Appearing:
                {
                    float t = appearT;
                    scale = p.EvaluateAppearScale(t);
                    alpha = Mathf.Clamp01(t * 3f);
                    float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                    lift = -p.AppearRise * (1f - eased);
                    break;
                }
                case Phase.Hiding:
                {
                    float t = hideT;
                    scale = 1f - t * t;
                    alpha = 1f - t;
                    lift = -p.HideDrop * t * t;
                    break;
                }
            }

            chargeBlend = Mathf.MoveTowards(chargeBlend, charging && active ? 1f : 0f, p.ChargeBlendSpeed * dt);
            float size = IconSize();

            // Idle float: vertical bob only (no sway/rotation - floatLift comes from an infinite PrimeTween yoyo).
            float floatKeep = Mathf.Lerp(1f, p.FloatWhileCharging, chargeBlend);
            lift += floatLift * p.FloatHeight * floatKeep;
            float tilt = 0f;

            // Charge: jitter that grows with the hold, a slight tilt and a swell. Driven by the live, open-ended
            // hold duration from SetProgress (no fixed end time), so - unlike the rest of this file - it stays a
            // per-frame procedural effect rather than a discrete PrimeTween tween (flagged and accepted up front).
            Vector2 shake = Vector2.zero;
            if (chargeBlend > 0f)
            {
                float now = Time.unscaledTime;
                float amount = Mathf.Lerp(p.ChargeShakeStart, p.ChargeShakeEnd, chargeProgress) * chargeBlend;
                float f = now * p.ChargeShakeFrequency;
                shake = new Vector2(Mathf.PerlinNoise(f, 0.37f) * 2f - 1f, Mathf.PerlinNoise(0.71f, f) * 2f - 1f) * amount;
                tilt += (Mathf.PerlinNoise(f * 0.6f, 5.3f) * 2f - 1f) * p.ChargeTiltDegrees * chargeProgress * chargeBlend;
                scale *= Mathf.Lerp(1f, p.ChargeScale, chargeProgress * chargeBlend);
            }

            if (punchActive) scale *= 1f + p.ReadyPunch * Mathf.Sin(punchT * Mathf.PI);

            Vector3 offset = new Vector3(shake.x, lift + shake.y, 0f) * size;
            Quaternion rotation = Quaternion.Euler(0f, 0f, tilt);
            Vector3 scaleVector = Vector3.one * Mathf.Max(0f, scale);

            iconTransform.localPosition = iconRestingLocalPosition + offset;
            iconTransform.localRotation = rotation;
            iconTransform.localScale = scaleVector;
            if (radialTransform != null)
            {
                radialTransform.localPosition = radialRestingLocalPosition + offset;
                radialTransform.localScale = scaleVector;
            }

            Color tint = usable ? Color.white : p.UnusableTint;
            float usableAlpha = usable ? 1f : p.UnusableAlpha;
            Color color = new(iconBaseColor.r * tint.r, iconBaseColor.g * tint.g, iconBaseColor.b * tint.b,
                iconBaseColor.a * usableAlpha * alpha);
            if (uiIcon != null)
            {
                uiIcon.enabled = active && uiIcon.sprite != null;
                uiIcon.color = color;
            }
            ApplyFrameColors(active, alpha * usableAlpha, tint);
            if (iconRenderer != null)
            {
                iconRenderer.enabled = active && iconRenderer.sprite != null;
                iconRenderer.color = color;
            }
            if (uiRadialFill != null)
            {
                if (!active) uiRadialFill.enabled = false;
                Color radial = radialBaseColor;
                radial.a *= alpha;
                uiRadialFill.color = radial;
            }
            if (radialFill != null && !active) radialFill.enabled = false;
        }

        private float IconSize()
        {
            if (iconTransform is RectTransform rect) return Mathf.Max(1f, rect.rect.height);
            if (iconRenderer != null && iconRenderer.sprite != null) return Mathf.Max(0.01f, iconRenderer.sprite.bounds.size.y);
            return 1f;
        }

        private void EnsureVisuals()
        {
            if (transform is RectTransform)
            {
                EnsureUIVisuals();
                return;
            }
            if (iconRenderer == null)
            {
                Transform icon = transform.Find("Ability Icon");
                if (icon == null)
                {
                    icon = new GameObject("Ability Icon").transform;
                    icon.gameObject.layer = gameObject.layer;
                    icon.SetParent(transform, false);
                    icon.localPosition = Vector3.up * 1.05f;
                }
                iconTransform = icon;
                iconRestingLocalPosition = icon.localPosition;
                iconRenderer = icon.GetComponent<SpriteRenderer>();
                if (iconRenderer == null) iconRenderer = icon.gameObject.AddComponent<SpriteRenderer>();
                SpriteRenderer owner = GetComponent<SpriteRenderer>();
                if (owner != null && iconRenderer != null)
                {
                    iconRenderer.sortingLayerID = owner.sortingLayerID;
                    iconRenderer.sortingOrder = owner.sortingOrder + 5;
                }
            }
            if (radialFill == null)
            {
                Transform radial = transform.Find("Selection Radial Fill");
                if (radial == null)
                {
                    radial = new GameObject("Selection Radial Fill").transform;
                    radial.gameObject.layer = gameObject.layer;
                    radial.SetParent(transform, false);
                    radial.localPosition = Vector3.up * 1.05f;
                }
                radialTransform = radial;
                radialRestingLocalPosition = radial.localPosition;
                radialFill = radial.GetComponent<LineRenderer>();
                if (radialFill == null) radialFill = radial.gameObject.AddComponent<LineRenderer>();
                if (radialFill == null) return;
                radialFill.useWorldSpace = false;
                radialFill.loop = false;
                radialFill.widthMultiplier = 0.06f;
                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader != null)
                {
                    radialMaterial = new Material(spriteShader);
                    radialFill.sharedMaterial = radialMaterial;
                }
                radialFill.startColor = radialFill.endColor = Color.white;
                radialFill.sortingOrder = iconRenderer != null ? iconRenderer.sortingOrder + 1 : 1;
                radialFill.enabled = false;
            }
        }

        private void EnsureUIVisuals()
        {
            if (uiIcon == null)
            {
                // Animated root (scale / shake / bob), with: backdrop disc, circular mask -> icon, frame ring.
                Transform root = transform.Find("Ability Icon");
                if (root == null)
                {
                    root = new GameObject("Ability Icon", typeof(RectTransform)).transform;
                    root.gameObject.layer = gameObject.layer;
                    root.SetParent(transform, false);
                }
                UnityEngine.UI.Image legacyImage = root.GetComponent<UnityEngine.UI.Image>();
                if (legacyImage != null) legacyImage.enabled = false; // older layout had the icon on the root
                RectTransform rect = root as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                // Centre pivot so scale, tilt and shake happen around the middle of the icon.
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 10f + 27f);
                rect.sizeDelta = new Vector2(54f, 54f);
                iconTransform = root;
                iconRestingLocalPosition = root.localPosition;

                uiBackdrop = UiChild(root, "Backdrop");
                uiBackdrop.sprite = UiCircleSprites.Disc;

                uiMaskImage = UiChild(root, "Mask");
                uiMaskImage.sprite = UiCircleSprites.Disc;
                uiMaskRect = uiMaskImage.rectTransform;
                uiMask = uiMaskImage.gameObject.AddComponent<UnityEngine.UI.Mask>();
                uiMask.showMaskGraphic = false;

                uiIcon = UiChild(uiMaskRect, "Icon");
                uiIcon.preserveAspect = true;

                uiFrame = UiChild(root, "Frame");
                ApplyFrameStyle();
            }
            if (uiRadialFill == null)
            {
                Transform radial = transform.Find("Selection Radial Fill");
                if (radial == null)
                {
                    GameObject radialObject = new("Selection Radial Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                    radial = radialObject.transform;
                    radialObject.layer = gameObject.layer;
                    radial.SetParent(transform, false);
                }
                RectTransform rect = radial as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 6f + 31f);
                rect.sizeDelta = new Vector2(62f, 62f);
                radialTransform = radial;
                radialRestingLocalPosition = radial.localPosition;
                uiRadialFill = radial.GetComponent<UnityEngine.UI.Image>();
                if (uiRadialFill == null)
                    uiRadialFill = radial.gameObject.AddComponent<UnityEngine.UI.Image>();
                uiRadialFill.type = UnityEngine.UI.Image.Type.Filled;
                uiRadialFill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
                uiRadialFill.fillOrigin = 2;
                uiRadialFill.fillClockwise = true;
                uiRadialFill.color = radialBaseColor;
                uiRadialFill.raycastTarget = false;
                uiRadialFill.enabled = false;
                ApplyFrameStyle();
            }
        }

        private static UnityEngine.UI.Image UiChild(Transform parent, string childName)
        {
            var go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var child = (RectTransform)go.transform;
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.offsetMin = child.offsetMax = Vector2.zero;
            var image = go.GetComponent<UnityEngine.UI.Image>();
            image.raycastTarget = false;
            return image;
        }

        private AbilityIconFrameStyle Style => frameStyle ?? DefaultFrameStyle;
        private static readonly AbilityIconFrameStyle DefaultFrameStyle = new();

        // Sprites, sizes and the hold-ring colour from the frame style.
        private void ApplyFrameStyle()
        {
            AbilityIconFrameStyle style = Style;
            bool framed = style.Enabled;
            if (uiFrame != null) uiFrame.sprite = UiCircleSprites.Ring(style.FrameThickness);
            if (uiMaskRect != null)
            {
                float inset = framed ? (1f - style.IconInset) * 0.5f : 0f;
                uiMaskRect.anchorMin = new Vector2(inset, inset);
                uiMaskRect.anchorMax = new Vector2(1f - inset, 1f - inset);
                uiMaskRect.offsetMin = uiMaskRect.offsetMax = Vector2.zero;
            }
            if (uiMask != null) uiMask.enabled = framed;
            if (uiMaskImage != null) uiMaskImage.enabled = framed;
            if (uiRadialFill != null)
            {
                // A ring that fills around the icon (replaces the old white square sweep).
                uiRadialFill.sprite = UiCircleSprites.Ring(style.ProgressThickness);
                radialBaseColor = style.UseAbilityAccent && hasAccent ? accentColor : style.ProgressColor;
            }
        }

        private void ApplyFrameColors(bool active, float alpha, Color tint)
        {
            if (uiFrame == null && uiBackdrop == null) return;
            AbilityIconFrameStyle style = Style;
            bool framed = style.Enabled && active;
            if (uiFrame != null)
            {
                uiFrame.enabled = framed;
                // The frame warms up toward the ability's colour as the hold fills.
                Color frame = Color.Lerp(style.FrameColor, radialBaseColor, chargeBlend * chargeProgress);
                uiFrame.color = new Color(frame.r * tint.r, frame.g * tint.g, frame.b * tint.b, frame.a * alpha);
            }
            if (uiBackdrop != null)
            {
                uiBackdrop.enabled = framed && style.BackdropColor.a > 0f;
                Color back = style.BackdropColor;
                back.a *= alpha;
                uiBackdrop.color = back;
            }
        }

        private void OnDisable()
        {
            Tween.StopAll(this);
            if (iconTransform != null)
            {
                iconTransform.localPosition = iconRestingLocalPosition;
                iconTransform.localRotation = Quaternion.identity;
                iconTransform.localScale = Vector3.one;
            }
            if (radialTransform != null)
            {
                radialTransform.localPosition = radialRestingLocalPosition;
                radialTransform.localScale = Vector3.one;
            }
        }

        private void OnDestroy()
        {
            if (radialMaterial != null) Destroy(radialMaterial);
        }
    }
}
