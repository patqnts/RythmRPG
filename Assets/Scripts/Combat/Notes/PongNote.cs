using PrimeTween;
using RythmRPG.Combat;
using UnityEngine;

/// <summary>
/// Pong note: travels down its lane like a Default note (travel-time / audio-clock driven) and is hit on the line.
/// In a Ping-Pong sequence the same object is used for the whole rally: a successful deflect sends this object back
/// to the enemy, and the next volley re-arms it there instead of spawning a new one.
/// </summary>
public class PongNote : NoteObject
{
    private bool deflecting;
    private bool keepForNextVolley;

    /// <summary>Set by the runner for Ping-Pong sequence shots.</summary>
    public bool IsSequenceShot { get; set; }
    /// <summary>Time the deflected shot takes to fly back to <see cref="ReturnDestination"/>.</summary>
    public float ReturnSeconds { get; set; } = 0.5f;
    /// <summary>Where a deflected shot flies back to (the enemy's spawn point).</summary>
    public Vector3 ReturnDestination { get; set; }
    public bool IsDeflecting => deflecting;
    /// <summary>Deflected, back at (or flying to) the enemy, and waiting to be fired again.</summary>
    public bool IsWaitingForNextVolley => deflecting && keepForNextVolley;

    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return !deflecting && base.CanReceiveHit(keyButton);
    }

    /// <summary>Called by the runner when this shot is deflected; the following DestroyObject flies it back instead.</summary>
    public void BeginDeflect() => deflecting = true;

    /// <summary>Called by the Ping-Pong host when another volley follows, so the shot is kept after it returns.</summary>
    public void KeepForNextVolley() => keepForNextVolley = true;

    public override void DestroyObject()
    {
        if (!deflecting)
        {
            base.DestroyObject();
            return;
        }

        isMoving = false;
        canBePressed = false;
        ResetMovementTween();
        Tween.Position(transform, ReturnDestination, Mathf.Max(0.05f, ReturnSeconds), Ease.OutQuad)
            .OnComplete(this, shot => shot.OnReturned());
    }

    private void OnReturned()
    {
        // Last volley (or the rally ended): the shot is gone once it reaches the enemy.
        if (!keepForNextVolley) Destroy(gameObject);
    }

    /// <summary>Fires this same object again as the next incoming volley.</summary>
    public void Rearm(RhythmNoteSpawnContext context, Vector3 spawnPosition)
    {
        Tween.StopAll(transform);
        deflecting = false;
        keepForNextVolley = false;
        transform.position = spawnPosition;
        Initialize(context);
        ResetMovementTween();
        isMoving = true;
    }
}
