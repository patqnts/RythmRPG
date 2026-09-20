using UnityEngine;

[DisallowMultipleComponent]
public class HoldNoteSpriteTailVisual : HoldNoteTailVisual
{
    [SerializeField] private Transform tailTransform;
    [SerializeField] private SpriteRenderer tailRenderer;
    [SerializeField] private HoldNoteTailAxis lengthAxis = HoldNoteTailAxis.X;
    [SerializeField] private HoldNoteTailDirection tailDirection = HoldNoteTailDirection.Left;
    [SerializeField, Min(0f)] private float lengthScale = 1f;
    [SerializeField] private bool preserveInitialThickness = true;
    [SerializeField, Min(0f)] private float thickness = 1f;

    private Vector3 initialLocalPosition;
    private Vector3 initialLocalScale;
    private bool initialized;

    public override HoldNoteTailMode TailMode => HoldNoteTailMode.Sprite;

    private void Awake()
    {
        CacheInitialTransform();
    }

    private void Reset()
    {
        tailTransform = transform;
        tailRenderer = GetComponent<SpriteRenderer>();
    }

    public override void Initialize(float totalLength, float noteSpeed)
    {
        CacheInitialTransform();
        gameObject.SetActive(true);
        if (tailRenderer != null)
        {
            tailRenderer.enabled = true;
        }

        SetRemainingLength(0f, 0f);
    }

    public override void SetRemainingLength(float remainingLength, float normalizedRemaining)
    {
        CacheInitialTransform();

        float visualLength = Mathf.Max(0f, remainingLength * lengthScale);
        Vector3 scale = initialLocalScale;

        if (lengthAxis == HoldNoteTailAxis.X)
        {
            scale.x = visualLength;
            if (!preserveInitialThickness)
            {
                scale.y = thickness;
            }
        }
        else
        {
            scale.y = visualLength;
            if (!preserveInitialThickness)
            {
                scale.x = thickness;
            }
        }

        Vector3 direction = GetDirectionVector(tailDirection);
        tailTransform.localScale = scale;
        tailTransform.localPosition = initialLocalPosition + direction * visualLength * 0.5f;
    }

    public override void Hide()
    {
        if (tailRenderer != null)
        {
            tailRenderer.enabled = false;
            return;
        }

        base.Hide();
    }

    private void CacheInitialTransform()
    {
        if (initialized)
        {
            return;
        }

        if (tailTransform == null)
        {
            tailTransform = transform;
        }

        if (tailRenderer == null)
        {
            tailRenderer = GetComponent<SpriteRenderer>();
        }

        initialLocalPosition = tailTransform.localPosition;
        initialLocalScale = tailTransform.localScale;
        initialized = true;
    }
}
