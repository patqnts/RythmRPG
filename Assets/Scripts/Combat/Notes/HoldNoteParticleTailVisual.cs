using UnityEngine;

[DisallowMultipleComponent]
public class HoldNoteParticleTailVisual : HoldNoteTailVisual
{
    [SerializeField] private ParticleSystem tailParticles;
    [SerializeField] private bool createParticleSystemIfMissing = true;
    [SerializeField] private HoldNoteTailAxis lengthAxis = HoldNoteTailAxis.Y;
    [SerializeField] private HoldNoteTailDirection tailDirection = HoldNoteTailDirection.Up;
    [SerializeField, Min(0f)] private float lengthScale = 1f;
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

    private Vector3 initialLocalPosition;
    private bool initialized;

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

        if (configureAsPixelFireBurst)
        {
            ConfigurePixelFireBurst();
        }

        SetRemainingLength(0f, 0f);

        if (tailParticles != null && !tailParticles.isPlaying)
        {
            tailParticles.Play(true);
        }

        EmitInitialBurst(totalLength);
    }

    public override void SetRemainingLength(float remainingLength, float normalizedRemaining)
    {
        CacheInitialTransform();

        if (tailParticles == null)
        {
            return;
        }

        float visualLength = Mathf.Max(0f, remainingLength * lengthScale);
        Vector3 direction = GetDirectionVector(tailDirection);
        transform.localPosition = initialLocalPosition + direction * visualLength * 0.5f;
        ApplyParticleLength(visualLength, visualLength > 0.01f);

        if (visualLength <= 0.01f)
        {
            Hide();
        }
    }

    public override void Hide()
    {
        if (tailParticles != null)
        {
            tailParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        base.Hide();
    }

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
        EnsureParticleSystem();

        if (initialized)
        {
            return;
        }

        initialLocalPosition = transform.localPosition;
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
            particleRenderer.sortingOrder = 51;

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
