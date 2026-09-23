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
    private Quaternion initialLocalRotation;
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

        tailTransform.localScale = scale;
        if (tailDirection == HoldNoteTailDirection.AlongLane)
        {
            // Along the lane in world space: turn the sprite's length axis onto the lane, ignoring the note's tilt.
            Transform parent = tailTransform.parent;
            Vector3 direction = GetWorldDirection(tailDirection, parent);
            Quaternion baseRotation = (parent != null ? parent.rotation : Quaternion.identity) * initialLocalRotation;
            tailTransform.rotation = AlignAxis(baseRotation, lengthAxis == HoldNoteTailAxis.X ? Vector3.right : Vector3.up, direction);
            tailTransform.localPosition = initialLocalPosition + WorldToParentVector(parent, direction * visualLength * 0.5f);
            return;
        }

        Vector3 localDirection = GetDirectionVector(tailDirection);
        tailTransform.localPosition = initialLocalPosition + localDirection * visualLength * 0.5f;
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
        initialLocalRotation = tailTransform.localRotation;
        initialLocalScale = tailTransform.localScale;
        initialized = true;
    }
}
