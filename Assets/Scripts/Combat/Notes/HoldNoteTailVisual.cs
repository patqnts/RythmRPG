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
    Down
}

public abstract class HoldNoteTailVisual : MonoBehaviour
{
    public abstract HoldNoteTailMode TailMode { get; }

    public virtual void Initialize(float totalLength, float noteSpeed)
    {
        SetRemainingLength(0f, 0f);
    }

    public abstract void SetRemainingLength(float remainingLength, float normalizedRemaining);

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
            default:
                return Vector3.left;
        }
    }
}
