using UnityEngine;

/// <summary>
/// Hold-note tail drawn as a beam (LineRenderer, like the laser note's MagicBeamStatic) with optional pixel-fire
/// particles along it.
///
/// Beam: assign Beam Line Prefab (e.g. the same line prefab the laser note's MagicBeamStatic uses) or leave it
/// empty for a generated pixel beam. Like the laser, the texture is tiled along the length, can scroll, and the
/// width pulses. Optional Head / End effect prefabs sit at the note end and the tail end.
/// Particles: the previous look; keep them on for sparks around the beam or turn them off for a clean beam.
/// </summary>
[DisallowMultipleComponent]
public class HoldNoteParticleTailVisual : HoldNoteTailVisual
{
    [Header("Shape")]
    [SerializeField] private HoldNoteTailAxis lengthAxis = HoldNoteTailAxis.Y;
    [Tooltip("Which way the tail extends from the note. Along Lane lays it on the lane in world space, like the laser " +
             "beam, so the note's rotation/tilt doesn't turn it. Left/Right/Up/Down are in the note's own (rotated) space.")]
    [SerializeField] private HoldNoteTailDirection tailDirection = HoldNoteTailDirection.Up;
    [SerializeField, Min(0f)] private float lengthScale = 1f;

    [Header("Beam (line renderer)")]
    [SerializeField] private bool useBeam = true;
    [Tooltip("A prefab with a LineRenderer, e.g. the beam line prefab the laser note uses. Empty = generated pixel beam.")]
    [SerializeField] private GameObject beamLinePrefab;
    [Tooltip("Spawned at the note end of the tail (optional).")]
    [SerializeField] private GameObject beamHeadPrefab;
    [Tooltip("Spawned at the far end of the tail (optional).")]
    [SerializeField] private GameObject beamEndPrefab;
    [SerializeField, Min(0f)] private float beamWidth = 0.3f;
    [Tooltip("Width along the tail: 0 = note end, 1 = far end.")]
    [SerializeField] private AnimationCurve beamWidthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.75f);
    [Tooltip("Beam colors along the tail (0 = note end). Used by the generated beam, and by a prefab beam when Override Prefab Colors is on.")]
    [SerializeField] private Gradient beamColor = DefaultBeamGradient();
    [Tooltip("Replace the Beam Line Prefab's own colors with Beam Color.")]
    [SerializeField] private bool overridePrefabColors;
    [Tooltip("Tile the texture along the length instead of stretching it (like the laser).")]
    [SerializeField] private bool tileTexture = true;
    [Tooltip("Texture length relative to its height (a 600x200 texture = 3).")]
    [SerializeField, Min(0.01f)] private float textureLengthScale = 1f;
    [Tooltip("Texture scroll along the beam (units per second, sign = direction).")]
    [SerializeField] private float textureScrollSpeed = 2f;
    [Tooltip("Width grows to this multiple at the top of each pulse (1 = no pulse).")]
    [SerializeField, Min(0f)] private float widthPulseMultiplier = 1.35f;
    [SerializeField, Min(0f)] private float widthPulseSpeed = 6f;
    [SerializeField] private int beamSortingOrder = 50;
    [Tooltip("Width grows while the note is being held (1 = no change).")]
    [SerializeField, Min(0f)] private float heldWidthBoost = 1.25f;

    [Header("Particles (optional sparks)")]
    [SerializeField] private bool useParticles = true;
    [SerializeField] private ParticleSystem tailParticles;
    [SerializeField] private bool createParticleSystemIfMissing = true;
    [SerializeField, Min(0f)] private float tailThickness = 0.35f;
    [SerializeField, Min(0f)] private float emissionRatePerUnit = 18f;
    [SerializeField, Min(0)] private int initialBurstParticles;
    [SerializeField, Min(0f)] private float burstParticlesPerUnit;
    [SerializeField] private bool configureAsPixelFireBurst = true;
    [SerializeField] private bool useGeneratedPixelMaterial = true;
    [SerializeField] private Color fireColorMin = new Color(1f, 0.25f, 0.05f, 0.85f);
    [SerializeField] private Color fireColorMax = new Color(1f, 0.9f, 0.15f, 0.95f);

    private const string RuntimePixelMaterialName = "Runtime Hold Note Pixel Fire Particle";
    private static Material sharedPixelMaterial;
    private static Texture2D generatedBeamTexture;

    private Vector3 initialLocalPosition;
    private Quaternion initialLocalRotation;
    private bool initialized;

    private GameObject beamObject;
    private LineRenderer beamLine;
    private Material beamMaterial;
    private GameObject beamHead;
    private GameObject beamEnd;
    // Along Lane: the head/end effects live outside the note, so its tilt and flattened (Z = 0) scale can't turn or
    // squash them, the same as the laser's start/end effects.
    private Transform beamEffectsRoot;
    private float currentLength;
    private float pulseTime;
    private float holdBlend;
    private float lastNormalized = 1f;

    public override HoldNoteTailMode TailMode => HoldNoteTailMode.Particle;

    private void Awake()
    {
        CacheInitialTransform();
    }

    private void Reset()
    {
        EnsureParticleSystem();
        ConfigurePixelFireBurst();
    }

    public override void Initialize(float totalLength, float noteSpeed)
    {
        CacheInitialTransform();
        gameObject.SetActive(true);
        pulseTime = 0f;
        holdBlend = 0f;
        lastNormalized = 1f;

        if (useBeam) EnsureBeam();
        if (useParticles && configureAsPixelFireBurst) ConfigurePixelFireBurst();

        SetRemainingLength(0f, 0f);

        if (useParticles && tailParticles != null && !tailParticles.isPlaying) tailParticles.Play(true);
        if (useParticles) EmitInitialBurst(totalLength);
    }

    public override void SetRemainingLength(float remainingLength, float normalizedRemaining)
    {
        CacheInitialTransform();

        float visualLength = Mathf.Max(0f, remainingLength * lengthScale);
        bool visible = visualLength > 0.01f;
        // The tail shrinks from the far end while held: normalized goes down over the hold.
        if (normalizedRemaining < lastNormalized - 0.0001f) holdBlend = 1f;
        lastNormalized = normalizedRemaining;
        currentLength = visualLength;

        if (useParticles && tailParticles != null)
        {
            if (tailDirection == HoldNoteTailDirection.AlongLane)
            {
                Transform parent = transform.parent;
                Vector3 direction = GetWorldDirection(tailDirection, parent);
                Quaternion baseRotation = (parent != null ? parent.rotation : Quaternion.identity) * initialLocalRotation;
                transform.rotation = AlignAxis(baseRotation, lengthAxis == HoldNoteTailAxis.X ? Vector3.right : Vector3.up, direction);
                transform.localPosition = initialLocalPosition + WorldToParentVector(parent, direction * visualLength * 0.5f);
            }
            else
            {
                Vector3 direction = GetDirectionVector(tailDirection);
                transform.localPosition = initialLocalPosition + direction * visualLength * 0.5f;
            }
            ApplyParticleLength(visualLength, visible);
        }
        else if (tailParticles != null && tailParticles.isPlaying)
        {
            tailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (useBeam)
        {
            EnsureBeam();
            SetBeamVisible(visible);
            UpdateBeam(0f);
        }

        if (!visible) Hide();
    }

    public override void Hide()
    {
        SetBeamVisible(false);
        if (tailParticles != null)
        {
            tailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }
        // Keep the object alive when a beam exists so it can be shown again (and keep updating).
        if (beamLine != null) return;
        base.Hide();
    }

    private void LateUpdate()
    {
        // The note moves every frame; a world-space beam must follow it even when the length does not change.
        if (useBeam && beamLine != null && beamLine.enabled) UpdateBeam(Time.deltaTime);
    }

    private void OnEnable()
    {
        if (beamEffectsRoot != null) beamEffectsRoot.gameObject.SetActive(true);
    }

    private void OnDisable()
    {
        if (beamEffectsRoot != null) beamEffectsRoot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (beamEffectsRoot != null) Destroy(beamEffectsRoot.gameObject);
        if (beamMaterial != null) Destroy(beamMaterial);
    }

    // ---------- beam ----------

    private void EnsureBeam()
    {
        if (beamLine != null) return;

        if (beamLinePrefab != null)
        {
            beamObject = Instantiate(beamLinePrefab, transform);
            beamObject.name = "Hold Tail Beam";
            beamLine = beamObject.GetComponentInChildren<LineRenderer>(true);
            if (beamLine == null)
            {
                Debug.LogWarning($"{name}: Beam Line Prefab has no LineRenderer; using the generated beam.", this);
                Destroy(beamObject);
                beamObject = null;
            }
        }

        bool generated = beamLine == null;
        if (generated)
        {
            beamObject = new GameObject("Hold Tail Beam");
            beamObject.transform.SetParent(transform, false);
            beamLine = beamObject.AddComponent<LineRenderer>();
            beamLine.sharedMaterial = CreateGeneratedBeamMaterial();
            beamLine.textureMode = LineTextureMode.Stretch;
            beamLine.numCapVertices = 0;
        }

        // Laser-style: two points in world space, own material instance so the texture can tile and scroll.
        beamLine.useWorldSpace = true;
        beamLine.positionCount = 2;
        beamLine.alignment = LineAlignment.View;
        beamLine.sortingOrder = beamSortingOrder;
        if (generated || overridePrefabColors) beamLine.colorGradient = beamColor ?? DefaultBeamGradient();
        if (beamMaterial == null && beamLine.sharedMaterial != null)
        {
            beamMaterial = new Material(beamLine.sharedMaterial) { name = beamLine.sharedMaterial.name + " (Hold Tail)" };
            beamLine.sharedMaterial = beamMaterial;
        }

        Transform effectsParent = beamObject.transform;
        if (tailDirection == HoldNoteTailDirection.AlongLane && (beamHeadPrefab != null || beamEndPrefab != null))
        {
            // Scene root, identity rotation and scale: the effects keep their prefab look and only face along the beam.
            if (beamEffectsRoot == null) beamEffectsRoot = new GameObject($"{name} Beam Ends").transform;
            effectsParent = beamEffectsRoot;
        }
        if (beamHeadPrefab != null) beamHead = Instantiate(beamHeadPrefab, effectsParent);
        if (beamEndPrefab != null) beamEnd = Instantiate(beamEndPrefab, effectsParent);
    }

    private void SetBeamVisible(bool visible)
    {
        if (beamLine != null) beamLine.enabled = visible;
        if (beamHead != null) beamHead.SetActive(visible);
        if (beamEnd != null) beamEnd.SetActive(visible);
    }

    private void UpdateBeam(float deltaTime)
    {
        if (beamLine == null) return;

        Transform parent = transform.parent;
        Vector3 localStart = initialLocalPosition;
        Vector3 start = parent != null ? parent.TransformPoint(localStart) : localStart;
        Vector3 end;
        if (tailDirection == HoldNoteTailDirection.AlongLane)
        {
            // World space along the lane, like the laser (note -> target), so the note's tilt doesn't turn it.
            end = start + GetWorldDirection(tailDirection, parent) * currentLength;
        }
        else
        {
            Vector3 localEnd = initialLocalPosition + GetDirectionVector(tailDirection) * currentLength;
            end = parent != null ? parent.TransformPoint(localEnd) : localEnd;
        }
        beamLine.SetPosition(0, start);
        beamLine.SetPosition(1, end);

        // Width: base x pulse x held boost, shaped by the curve along the tail.
        pulseTime += deltaTime * widthPulseSpeed;
        float pulse = Mathf.Lerp(1f, Mathf.Max(0.01f, widthPulseMultiplier), 0.5f + 0.5f * Mathf.Sin(pulseTime));
        float held = Mathf.Lerp(1f, heldWidthBoost, holdBlend);
        beamLine.widthCurve = beamWidthCurve != null && beamWidthCurve.length > 0 ? beamWidthCurve : AnimationCurve.Constant(0f, 1f, 1f);
        beamLine.widthMultiplier = beamWidth * pulse * held;

        if (beamMaterial != null)
        {
            float distance = Vector3.Distance(start, end);
            if (tileTexture) beamMaterial.mainTextureScale = new Vector2(Mathf.Max(0.01f, distance / textureLengthScale), 1f);
            if (!Mathf.Approximately(textureScrollSpeed, 0f))
                beamMaterial.mainTextureOffset -= new Vector2(deltaTime * textureScrollSpeed, 0f);
        }

        // Head faces down the beam, end faces back at the head (like the laser's LookAt). Along Lane uses the lane
        // direction even when the tail is still too short to have one of its own.
        Vector3 along = end - start;
        if (along.sqrMagnitude <= 0.0001f && tailDirection == HoldNoteTailDirection.AlongLane)
            along = GetWorldDirection(tailDirection, parent);
        if (beamHead != null)
        {
            beamHead.transform.position = start;
            if (along.sqrMagnitude > 0.0001f) beamHead.transform.rotation = Quaternion.LookRotation(along);
        }
        if (beamEnd != null)
        {
            beamEnd.transform.position = end;
            if (along.sqrMagnitude > 0.0001f) beamEnd.transform.rotation = Quaternion.LookRotation(-along);
        }
    }

    private static Gradient DefaultBeamGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.55f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.12f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 1f)
            });
        return gradient;
    }

    /// <summary>A pixel beam: bright white core with soft edges across the width, point-filtered.</summary>
    private static Material CreateGeneratedBeamMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;

        if (generatedBeamTexture == null)
        {
            // Width runs along the texture's V axis on a LineRenderer.
            float[] profile = { 0.15f, 0.45f, 0.85f, 1f, 1f, 0.85f, 0.45f, 0.15f };
            generatedBeamTexture = new Texture2D(2, profile.Length, TextureFormat.RGBA32, false)
            {
                name = "Runtime Hold Tail Beam Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < profile.Length; y++)
            {
                float core = profile[y];
                Color color = Color.Lerp(new Color(1f, 1f, 1f, core * 0.6f), Color.white, core >= 1f ? 1f : 0f);
                color.a = core;
                // Two columns with slightly different brightness give the scroll something to move.
                generatedBeamTexture.SetPixel(0, y, color);
                generatedBeamTexture.SetPixel(1, y, new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f, color.a));
            }
            generatedBeamTexture.Apply();
        }

        var material = new Material(shader) { name = "Runtime Hold Tail Beam", mainTexture = generatedBeamTexture };
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", generatedBeamTexture);
        return material;
    }

    // ---------- particles (previous look) ----------

    private Vector3 GetShapeScale(float visualLength)
    {
        if (lengthAxis == HoldNoteTailAxis.X)
        {
            return new Vector3(visualLength, tailThickness, 0.01f);
        }

        return new Vector3(tailThickness, visualLength, 0.01f);
    }

    private void CacheInitialTransform()
    {
        if (useParticles) EnsureParticleSystem();

        if (initialized)
        {
            return;
        }

        initialLocalPosition = transform.localPosition;
        initialLocalRotation = transform.localRotation;
        initialized = true;
    }

    private void ConfigurePixelFireBurst()
    {
        EnsureParticleSystem();

        if (tailParticles == null)
        {
            return;
        }

        ParticleSystem.MainModule main = tailParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(fireColorMin, fireColorMax);
        main.maxParticles = 140;

        ParticleSystem.EmissionModule emission = tailParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = emissionRatePerUnit;

        ParticleSystem.ShapeModule shape = tailParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = GetShapeScale(1f);

        ParticleSystem.VelocityOverLifetimeModule velocity = tailParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
        // Unity requires all three Velocity over Lifetime axes to use the same
        // MinMaxCurve mode. Keep Z as a zero-valued TwoConstants curve rather
        // than a Constant curve so it matches the randomized X and Y axes.
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = tailParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(fireColorMax, 0f),
                new GradientColorKey(fireColorMin, 0.65f),
                new GradientColorKey(new Color(0.35f, 0.05f, 0.02f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.65f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer particleRenderer = tailParticles.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer != null)
        {
            particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            particleRenderer.sortingOrder = beamSortingOrder + 1;

            if (useGeneratedPixelMaterial)
            {
                Material pixelMaterial = GetOrCreatePixelMaterial();
                if (pixelMaterial != null)
                {
                    particleRenderer.material = pixelMaterial;
                }
            }
        }
    }

    private void EnsureParticleSystem()
    {
        if (tailParticles == null)
        {
            tailParticles = GetComponent<ParticleSystem>();
        }

        if (tailParticles == null && createParticleSystemIfMissing)
        {
            tailParticles = gameObject.AddComponent<ParticleSystem>();
            tailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (tailParticles != null && tailParticles.GetComponent<ParticleSystemRenderer>() == null)
        {
            tailParticles.gameObject.AddComponent<ParticleSystemRenderer>();
        }
    }

    private static Material GetOrCreatePixelMaterial()
    {
        if (sharedPixelMaterial != null)
        {
            return sharedPixelMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Transparent");
        }

        if (shader == null)
        {
            return null;
        }

        Texture2D pixelTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = "Runtime Hold Note Pixel Fire Texture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        Color[] pixels =
        {
            Color.white,
            Color.white,
            Color.white,
            Color.white
        };
        pixelTexture.SetPixels(pixels);
        pixelTexture.Apply();

        sharedPixelMaterial = new Material(shader)
        {
            name = RuntimePixelMaterialName,
            mainTexture = pixelTexture,
            hideFlags = HideFlags.HideAndDontSave
        };

        if (sharedPixelMaterial.HasProperty("_BaseMap"))
        {
            sharedPixelMaterial.SetTexture("_BaseMap", pixelTexture);
        }

        return sharedPixelMaterial;
    }

    private void ApplyParticleLength(float visualLength, bool emit)
    {
        if (tailParticles == null)
        {
            return;
        }

        ParticleSystem.ShapeModule shape = tailParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = GetShapeScale(visualLength);

        ParticleSystem.EmissionModule emission = tailParticles.emission;
        emission.enabled = emit;
        emission.rateOverTime = emit ? visualLength * emissionRatePerUnit : 0f;

        if (emit && !tailParticles.isPlaying)
        {
            tailParticles.Play(true);
        }
    }

    private void EmitInitialBurst(float totalLength)
    {
        if (tailParticles == null)
        {
            return;
        }

        int burstCount = Mathf.RoundToInt(initialBurstParticles + Mathf.Max(0f, totalLength) * burstParticlesPerUnit);
        if (burstCount > 0)
        {
            tailParticles.Emit(burstCount);
        }
    }
}
