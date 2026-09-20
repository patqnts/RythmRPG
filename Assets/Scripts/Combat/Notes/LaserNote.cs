using RythmRPG.Combat;

// Kept as a compatibility wrapper for existing charts/prefabs.
// The underlying behavior is a stationary timing note; "laser" is only the current visual theme.
public class LaserNote : StationaryNote
{
    public override void Initialize(RhythmNoteSpawnContext context)
    {
        base.Initialize(context);
        MagicBeamNoteTargeting.ConfigureBeam(this, GetLaneTarget()?.transform);
    }
}
