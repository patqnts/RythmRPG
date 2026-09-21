using RythmRPG.Combat;
using UnityEngine;

/// <summary>
/// Pong note: travels down its lane like a Default note (travel-time / audio-clock driven) and is hit on the line.
/// It is the shot used by the Ping-Pong sequence attack, which fires the next one back after each deflect.
/// </summary>
public class PongNote : NoteObject
{
    public override bool CanReceiveHit(KeyButton keyButton)
    {
        return base.CanReceiveHit(keyButton);
    }
}
