// These enums intentionally remain in the global namespace. Existing scene and
// prefab serialization stores their numeric values, and the legacy gameplay
// scripts already refer to these names without a namespace.

public enum KeyType
{
    DEFAULT,
    FREEZE,
    LIGHTNING,
    LANE_CLEAR,
    XXX
}

public enum HitEffect
{
    Default,
    Ghost,
    Cluster,
    DoubleHit,
    Pong
}

public enum PlayerState
{
    Default,
    Freeze,
    Nausea,
    None
}

public enum NoteInitializeMovementType
{
    None,
    SlowThenBurst,
    MissileSCurve
}
