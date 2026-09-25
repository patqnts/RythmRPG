using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RythmRPG.UI.Title
{
    /// <summary>
    /// Shared core of <see cref="TitleLogoText"/> (TextMeshPro) and <see cref="TitleLogoImage"/> (UI Image):
    /// the look, the Disintegrate value and its tweens, the runtime material instances, and the flakes child.
    /// </summary>
    [ExecuteAlways]
    public abstract class TitleLogoBase : UIBehaviour, ITitleFlakeOwner
    {
        [Tooltip("The logo shader. Found by name automatically; keep it assigned so builds include it.")]
        [SerializeField] protected Shader shader;

        [SerializeField] protected TitleLogoLook look = new TitleLogoLook();

        [Header("Disintegrate")]
        [Tooltip("0 = intact, 1 = fully gone (flakes finished). Animate this with PrimeTween, an Animator or Timeline.")]
        [Range(0, 1)] [SerializeField] float disintegrate;
        [SerializeField] protected TitleDisintegrateSettings disintegration = new TitleDisintegrateSettings();

        [Header("Playback (Play Mode)")]
        [Tooltip("On enable, start fully disintegrated and assemble (the flakes fly back in).")]
        [SerializeField] bool assembleOnEnable;
        [SerializeField] float assembleDelay = 0.2f;
        [SerializeField] float assembleDuration = 1.8f;
        [SerializeField] Ease assembleEase = Ease.OutSine;
        [SerializeField] float disintegrateDuration = 2.4f;
        [SerializeField] Ease disintegrateEase = Ease.InSine;
        [SerializeField] bool useUnscaledTime = true;

        protected Material mainMaterial;
        protected Material flakeMaterial;
        protected TitleLogoFlakes flakes;
        protected readonly TitleFlakeSystem flakeSystem = new TitleFlakeSystem();
        /// <summary>Width / height of the logo in effect space.</summary>
        protected float aspect = 1f;
        /// <summary>Logo height in the graphic's local units (1 effect unit).</summary>
        protected float heightLocal = 100f;
        /// <summary>Burst focus in effect UV (0..1 over the logo).</summary>
        protected Vector2 focusUV = new Vector2(0.5f, 0.5f);

        bool layoutDirty = true;
        bool sourcesDirty = true;
        bool flakeMeshDirty = true;
        bool paddingDirty = true;
        bool warnedMissingShader;
        float lastFlakeProgress = -1f;
        Tween tween;

        // ------------------------------------------------------------------ hooks for the concrete logo

        protected abstract string DefaultShaderName { get; }
        /// <summary>Make sure the target graphic exists and renders with <see cref="mainMaterial"/>.</summary>
        protected abstract bool EnsureTarget();
        /// <summary>Texture the flakes sample (font atlas / sprite texture).</summary>
        protected abstract Texture FlakeTexture { get; }
        /// <summary>Pixelate grid density, in pixels per logo height.</summary>
        protected abstract float PixelDensity { get; }
        /// <summary>Update <see cref="aspect"/>, <see cref="heightLocal"/> and <see cref="focusUV"/>.</summary>
        protected abstract void RecomputeLayout();
        /// <summary>Feed the logo's quads to <see cref="flakeSystem"/> (skip when <c>disintegration.flakes</c> is off).</summary>
        protected abstract void BuildFlakeSources();
        /// <summary>Give the target graphic its original material back.</summary>
        protected abstract void RestoreTarget();

        protected virtual void OnLogoEnable() { }
        protected virtual void OnLogoDisable() { }
        protected virtual void ApplyExtra(Material m, bool isFlake, bool updateRatios) { }
        protected virtual void ApplyToExtraMaterials(float dissolve, bool updateRatios) { }
        protected virtual void OnPaddingChanged() { }
        protected virtual void OnFlakeMaterialCreated(Material m) { }

        // ------------------------------------------------------------------ public API

        /// <summary>0 = intact, 1 = fully disintegrated (dissolve done and every flake gone).</summary>
        public float Disintegrate
        {
            get => disintegrate;
            set
            {
                disintegrate = Mathf.Clamp01(value);
                float dissolve = DissolveProgress;
                if (mainMaterial) mainMaterial.SetFloat(TitleLogoLook.Ids.Disintegrate, dissolve);
                if (flakeMaterial) flakeMaterial.SetFloat(TitleLogoLook.Ids.Disintegrate, dissolve);
            }
        }

        /// <summary>Live look settings. Edits apply next frame; call <see cref="RefreshLayout"/> after changing Pixelate.</summary>
        public TitleLogoLook Look => look;

        public TitleDisintegrateSettings Disintegration => disintegration;

        public bool IsPlaying => tween.isAlive;

        public void SetInkColor(Color ink) => look.ink = ink;

        /// <summary>One color in, a full shadow/mid/highlight ramp out.</summary>
        public void SetAccent(Color accent)
        {
            look.accent = accent;
            look.deriveRampFromAccent = true;
        }

        public void SetAccentRamp(Color shadow, Color mid, Color highlight)
        {
            look.deriveRampFromAccent = false;
            look.accentShadow = shadow;
            look.accentMid = mid;
            look.accentHighlight = highlight;
        }

        public virtual void ApplyLook(TitleLogoLook preset)
        {
            if (preset == null) return;
            look = preset.Clone();
            RefreshLayout();
        }

        /// <summary>Recompute focus, flake grid and padding (call after changing layout-affecting settings in code).</summary>
        public void RefreshLayout()
        {
            layoutDirty = true;
            sourcesDirty = true;
            paddingDirty = true;
            flakeMeshDirty = true;
        }

        public Tween PlayDisintegrate() => PlayTo(1f, disintegrateDuration, disintegrateEase, 0f);
        public Tween PlayDisintegrate(float duration, float delay = 0f) => PlayTo(1f, duration, disintegrateEase, delay);
        public Tween PlayAssemble() => PlayTo(0f, assembleDuration, assembleEase, 0f);
        public Tween PlayAssemble(float duration, float delay = 0f) => PlayTo(0f, duration, assembleEase, delay);

        public void StopAnimation()
        {
            if (tween.isAlive) tween.Stop();
        }

        Tween PlayTo(float target, float duration, Ease ease, float delay)
        {
            StopAnimation();
            if (!Application.isPlaying || duration <= 0f)
            {
                Disintegrate = target;
                return default;
            }
            tween = Tween.Custom(this, disintegrate, target, duration, (self, v) => self.Disintegrate = v,
                ease, startDelay: delay, useUnscaledTime: useUnscaledTime);
            return tween;
        }

        // ------------------------------------------------------------------ helpers for the concrete logo

        protected void MarkLayoutDirty() => layoutDirty = true;

        protected void MarkSourcesDirty()
        {
            sourcesDirty = true;
            flakeMeshDirty = true;
        }

        protected Shader ResolveShader()
        {
            if (!shader) shader = Shader.Find(DefaultShaderName);
            if (!shader && !warnedMissingShader)
            {
                warnedMissingShader = true;
                Debug.LogWarning($"[{GetType().Name}] Shader '{DefaultShaderName}' not found. Assign it on {name}.", this);
            }
            return shader;
        }

        /// <summary>Reuse our own unsaved instance (e.g. after a domain reload) instead of leaking a new one.</summary>
        protected Material CreateOrReuseMaterial(Material current, string suffix)
        {
            if (current && current.shader == shader && (current.hideFlags & HideFlags.DontSave) != 0 &&
                current != mainMaterial && current != flakeMaterial)
                return current;
            return new Material(shader) { name = $"{name} ({suffix})", hideFlags = HideFlags.DontSave };
        }

        float DissolveProgress => disintegration.flakes
            ? Mathf.Min(1f, disintegrate * (1f + Mathf.Max(0.01f, disintegration.flakeLifetime)))
            : disintegrate;

        // ------------------------------------------------------------------ lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            OnLogoEnable();
            RefreshLayout();
            if (EnsureTarget())
            {
                EnsureFlakes();
                ApplyAll(true);
            }
            if (flakes) flakes.enabled = true;

            if (Application.isPlaying && assembleOnEnable)
            {
                Disintegrate = 1f;
                PlayTo(0f, assembleDuration, assembleEase, assembleDelay);
            }
        }

        protected override void OnDisable()
        {
            StopAnimation();
            if (flakes) flakes.enabled = false;
            OnLogoDisable();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            RestoreTarget();
            if (flakes)
            {
                GameObject go = flakes.gameObject;
                if (Application.isPlaying) Destroy(go);
#if UNITY_EDITOR
                else UnityEditor.EditorApplication.delayCall += () => { if (go) DestroyImmediate(go); };
#endif
            }
            DestroySafe(mainMaterial);
            DestroySafe(flakeMaterial);
            base.OnDestroy();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            RefreshLayout();
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        protected override void Reset()
        {
            base.Reset();
            shader = Shader.Find(DefaultShaderName);
        }
#endif

        protected virtual void LateUpdate()
        {
            if (!EnsureTarget() || !mainMaterial) return;
            EnsureFlakes();

            if (layoutDirty)
            {
                layoutDirty = false;
                RecomputeLayout();
            }
            if (sourcesDirty)
            {
                sourcesDirty = false;
                flakeMeshDirty = true;
                flakeSystem.Clear();
                BuildFlakeSources(); // implementations return early when flakes are off
            }
            if (paddingDirty)
            {
                paddingDirty = false;
                ApplyAll(true);
                OnPaddingChanged();
            }

            ApplyAll(false);

            if (flakes && flakes.isActiveAndEnabled &&
                (flakeMeshDirty || !Mathf.Approximately(lastFlakeProgress, disintegrate)))
            {
                flakeMeshDirty = false;
                lastFlakeProgress = disintegrate;
                flakes.SetVerticesDirty();
            }
        }

        // ------------------------------------------------------------------ materials

        protected void ApplyAll(bool updateRatios)
        {
            if (!mainMaterial) return;
            float dissolve = DissolveProgress;
            ApplyTo(mainMaterial, false, dissolve, updateRatios);
            if (flakeMaterial) ApplyTo(flakeMaterial, true, dissolve, updateRatios);
            ApplyToExtraMaterials(dissolve, updateRatios);
        }

        protected void ApplyTo(Material m, bool isFlake, float dissolve, bool updateRatios)
        {
            look.ApplyTo(m);
            m.SetFloat(TitleLogoLook.Ids.PixelDensity, PixelDensity);
            disintegration.ApplyTo(m, dissolve);
            m.SetFloat(TitleLogoLook.Ids.EffectAspect, aspect);
            m.SetVector(TitleLogoLook.Ids.FocusPoint, focusUV);
            m.SetFloat(TitleLogoLook.Ids.IsFlake, isFlake ? 1f : 0f);
            ApplyExtra(m, isFlake, updateRatios);
        }

        // ------------------------------------------------------------------ flakes

        void EnsureFlakes()
        {
            if (!disintegration.flakes)
            {
                if (flakes && flakes.enabled) flakes.enabled = false;
                return;
            }

            if (!flakes)
            {
                foreach (TitleLogoFlakes existing in GetComponentsInChildren<TitleLogoFlakes>(true))
                {
                    if (existing.transform.parent == transform) { flakes = existing; break; }
                }
                if (!flakes)
                {
                    var go = new GameObject("Title Logo Flakes", typeof(RectTransform), typeof(CanvasRenderer), typeof(TitleLogoFlakes))
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        layer = gameObject.layer,
                    };
                    go.transform.SetParent(transform, false);
                    flakes = go.GetComponent<TitleLogoFlakes>();
                }
                // Same local space as the logo mesh.
                var rt = (RectTransform)flakes.transform;
                var parentRt = (RectTransform)transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = parentRt.pivot;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
                rt.anchoredPosition3D = Vector3.zero;
                flakeMeshDirty = true;
            }

            if (!flakeMaterial && shader)
            {
                flakeMaterial = CreateOrReuseMaterial(flakes.material, "Title Logo Flakes");
                OnFlakeMaterialCreated(flakeMaterial);
                ApplyTo(flakeMaterial, true, DissolveProgress, true);
            }

            flakes.Bind(this, flakeMaterial, FlakeTexture);
            if (flakes.enabled != isActiveAndEnabled) flakes.enabled = isActiveAndEnabled;
        }

        void ITitleFlakeOwner.FillFlakeMesh(VertexHelper vh)
        {
            flakeSystem.Fill(vh, disintegrate, heightLocal, look, disintegration, PixelDensity);
        }

        protected static void DestroySafe(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
