using RythmRPG.Combat;

// Kept as a compatibility wrapper for existing charts/prefabs.
// The underlying behavior is a stationary hold timing note; "hold laser" is only the current visual theme.
public class HoldLaserNote : StationaryHoldNote
{
    public override void Initialize(RhythmNoteSpawnContext context)
    {
        base.Initialize(context);
        MagicBeamNoteTargeting.ConfigureBeam(this, GetLaneTarget()?.transform);
    }
}
