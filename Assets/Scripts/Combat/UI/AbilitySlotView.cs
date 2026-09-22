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
        private Transform iconTransform;
        private Transform radialTransform;
        private LineRenderer radialFill;
        private Material radialMaterial;
        private Vector3 iconRestingLocalPosition;
        private Vector3 radialRestingLocalPosition;
        private Color iconBaseColor = Color.white;
        private Color radialBaseColor = new(1f, 1f, 1f, 0.45f);

        private Phase phase = Phase.Hidden;
        private float phaseStart;
        private float floatPhase;
        private bool usable = true;
        private bool charging;
        private float chargeProgress;
        private float chargeBlend;
        private float punchStart = -1f;

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

        public void Configure(AbilityRuntimeInstance ability)
        {
            EnsureVisuals();
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
            float now = Time.unscaledTime;
            if (isVisible)
            {
                floatPhase = floatPhase01;
                if (IsShown) return;
                phase = animate ? Phase.Appearing : Phase.Shown;
                phaseStart = now + Mathf.Max(0f, delay);
            }
            else
            {
                SetCharging(false);
                if (phase == Phase.Hidden) return;
                if (!animate) phase = Phase.Hidden;
                else if (phase != Phase.Hiding)
                {
                    phase = Phase.Hiding;
                    phaseStart = now + Mathf.Max(0f, delay);
                }
            }
            Apply();
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
            if (clamped >= 1f && chargeProgress < 1f && charging) punchStart = Time.unscaledTime;
            chargeProgress = clamped;

            bool showRadial = IsShown && clamped > 0f;
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

        private void LateUpdate() => Apply(true);

        private void Apply(bool advance = false)
        {
            if (iconTransform == null) return;
            AbilitySlotAnimationProfile p = Profile;
            float now = Time.unscaledTime;
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
                    float t = (now - phaseStart) / p.AppearSeconds;
                    if (t >= 1f) phase = Phase.Shown;
                    else if (t <= 0f)
                    {
                        scale = 0f;
                        alpha = 0f;
                        lift = -p.AppearRise;
                    }
                    else
                    {
                        scale = p.EvaluateAppearScale(t);
                        alpha = Mathf.Clamp01(t * 3f);
                        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
                        lift = -p.AppearRise * (1f - eased);
                    }
                    break;
                }
                case Phase.Hiding:
                {
                    float t = (now - phaseStart) / p.HideSeconds;
                    if (t >= 1f)
                    {
                        phase = Phase.Hidden;
                        active = false;
                    }
                    else if (t > 0f)
                    {
                        scale = 1f - t * t;
                        alpha = 1f - t;
                        lift = -p.HideDrop * t * t;
                    }
                    break;
                }
            }

            chargeBlend = Mathf.MoveTowards(chargeBlend, charging && active ? 1f : 0f, p.ChargeBlendSpeed * dt);
            float size = IconSize();

            // Idle float: a slow bob plus a little sway, offset per slot so the row moves like a wave.
            float wave = (now / p.FloatPeriod + floatPhase) * Mathf.PI * 2f;
            float floatKeep = Mathf.Lerp(1f, p.FloatWhileCharging, chargeBlend);
            lift += Mathf.Sin(wave) * p.FloatHeight * floatKeep;
            float tilt = Mathf.Sin(wave * 0.5f) * p.FloatSwayDegrees * floatKeep;

            // Charge: jitter that grows with the hold, a slight tilt and a swell.
            Vector2 shake = Vector2.zero;
            if (chargeBlend > 0f)
            {
                float amount = Mathf.Lerp(p.ChargeShakeStart, p.ChargeShakeEnd, chargeProgress) * chargeBlend;
                float f = now * p.ChargeShakeFrequency;
                shake = new Vector2(Mathf.PerlinNoise(f, 0.37f) * 2f - 1f, Mathf.PerlinNoise(0.71f, f) * 2f - 1f) * amount;
                tilt += (Mathf.PerlinNoise(f * 0.6f, 5.3f) * 2f - 1f) * p.ChargeTiltDegrees * chargeProgress * chargeBlend;
                scale *= Mathf.Lerp(1f, p.ChargeScale, chargeProgress * chargeBlend);
            }

            if (punchStart >= 0f)
            {
                float t = (now - punchStart) / p.ReadyPunchSeconds;
                if (t >= 1f) punchStart = -1f;
                else scale *= 1f + p.ReadyPunch * Mathf.Sin(t * Mathf.PI);
            }

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
                Transform icon = transform.Find("Ability Icon");
                if (icon == null)
                {
                    GameObject iconObject = new("Ability Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                    icon = iconObject.transform;
                    icon.SetParent(transform, false);
                }
                RectTransform rect = icon as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                // Centre pivot so scale, tilt and shake happen around the middle of the icon.
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, 10f + 27f);
                rect.sizeDelta = new Vector2(54f, 54f);
                iconTransform = icon;
                iconRestingLocalPosition = icon.localPosition;
                uiIcon = icon.GetComponent<UnityEngine.UI.Image>();
                if (uiIcon == null)
                    uiIcon = icon.gameObject.AddComponent<UnityEngine.UI.Image>();
                uiIcon.preserveAspect = true;
                uiIcon.raycastTarget = false;
            }
            if (uiRadialFill == null)
            {
                Transform radial = transform.Find("Selection Radial Fill");
                if (radial == null)
                {
                    GameObject radialObject = new("Selection Radial Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                    radial = radialObject.transform;
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
            }
        }

        private void OnDisable()
        {
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
