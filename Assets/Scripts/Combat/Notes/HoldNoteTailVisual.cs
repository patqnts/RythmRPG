using UnityEngine;

public enum HoldNoteTailAxis
{
    X,
    Y
}

public enum HoldNoteTailDirection
{
    Left,
    Right,
    Up,
    Down,
    /// <summary>
    /// Lies along the note's lane, back toward where the note came from, in world space (like the laser beam, which
    /// runs from the note to its lane target). The note's own rotation/tilt is ignored.
    /// </summary>
    [InspectorName("Along Lane (like the laser)")]
    AlongLane
}

public abstract class HoldNoteTailVisual : MonoBehaviour
{
    public abstract HoldNoteTailMode TailMode { get; }

    public virtual void Initialize(float totalLength, float noteSpeed)
    {
        SetRemainingLength(0f, 0f);
    }

    public abstract void SetRemainingLength(float remainingLength, float normalizedRemaining);

    private Vector3 laneDirection;
    private bool hasLaneDirection;

    /// <summary>
    /// World direction from the hit line back up the lane (toward the spawn), set every frame by the note. Used by
    /// <see cref="HoldNoteTailDirection.AlongLane"/>.
    /// </summary>
    public void SetLaneDirection(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude <= 0.000001f) return;
        laneDirection = worldDirection.normalized;
        hasLaneDirection = true;
    }

    /// <summary>
    /// World direction the tail extends in from the note. AlongLane uses the lane (falls back to the parent's Up until
    /// the note knows its lane); the fixed directions are in <paramref name="space"/> (the note), as before.
    /// </summary>
    protected Vector3 GetWorldDirection(HoldNoteTailDirection direction, Transform space)
    {
        if (direction == HoldNoteTailDirection.AlongLane && hasLaneDirection) return laneDirection;
        Vector3 local = GetDirectionVector(direction);
        return space != null ? space.TransformDirection(local).normalized : local;
    }

    /// <summary>
    /// The parent-space vector for a world vector. Tolerates a zero scale on an axis (e.g. a note flattened with Z
    /// scale 0), where the plain inverse would give infinities: that axis just gets no offset.
    /// </summary>
    protected static Vector3 WorldToParentVector(Transform parent, Vector3 world)
    {
        if (parent == null) return world;
        Vector3 rotated = Quaternion.Inverse(parent.rotation) * world;
        Vector3 scale = parent.lossyScale;
        return new Vector3(SafeDivide(rotated.x, scale.x), SafeDivide(rotated.y, scale.y), SafeDivide(rotated.z, scale.z));
    }

    /// <summary>
    /// <paramref name="baseRotation"/> turned just enough that its <paramref name="localAxis"/> points along
    /// <paramref name="worldDirection"/> (the smallest turn, so the sprite/particles keep facing the same way).
    /// </summary>
    protected static Quaternion AlignAxis(Quaternion baseRotation, Vector3 localAxis, Vector3 worldDirection)
    {
        Vector3 axis = baseRotation * localAxis;
        if (axis.sqrMagnitude <= 0.000001f || worldDirection.sqrMagnitude <= 0.000001f) return baseRotation;
        return Quaternion.FromToRotation(axis, worldDirection) * baseRotation;
    }

    private static float SafeDivide(float value, float divisor) =>
        Mathf.Abs(divisor) > 0.000001f ? value / divisor : 0f;

    public virtual void Hide()
    {
        gameObject.SetActive(false);
    }

    protected static Vector3 GetDirectionVector(HoldNoteTailDirection direction)
    {
        switch (direction)
        {
            case HoldNoteTailDirection.Left:
                return Vector3.left;
            case HoldNoteTailDirection.Right:
                return Vector3.right;
            case HoldNoteTailDirection.Up:
                return Vector3.up;
            case HoldNoteTailDirection.Down:
                return Vector3.down;
            case HoldNoteTailDirection.AlongLane:
                return Vector3.up; // until the lane is known
            default:
                return Vector3.left;
        }
    }
}
