namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// How a placed prop's quad is rotated relative to the ground it sits on. Mirrors the rotation
    /// presets this project already applies by hand via
    /// <c>Assets/Scripts/Editor/ScreenAlignmentTools.cs</c> ("Face Main Camera" / "Lay Flat on XZ" /
    /// "Level World Axes") so Phase 3 placement produces the same results that workflow did, just driven
    /// by data on a <see cref="PropDefinition"/> instead of a manual menu click per object.
    /// </summary>
    public enum PropOrientationMode
    {
        /// <summary>
        /// Standing upright, local rotation identity -- equivalent to ScreenAlignmentTools' "Level World
        /// Axes". The right orientation for a character-height prop authored front-facing (a torch, a
        /// sign, a standing barrel) that should read as a vertical silhouette from the tilted camera.
        /// </summary>
        Vertical,

        /// <summary>
        /// Lying flat on the XZ ground plane, local rotation Euler(-90, 0, 0) -- equivalent to
        /// ScreenAlignmentTools' "Lay Flat on XZ". For decals and ground decoration authored to be read
        /// from directly above, the same convention this project's existing ground-decoration prefabs
        /// already use.
        /// </summary>
        Horizontal,

        /// <summary>
        /// Rotated to directly face the world's reference camera -- equivalent to ScreenAlignmentTools'
        /// "Face Main Camera", just computed automatically at placement/rebuild time from
        /// <see cref="WorldBuilderSettings.referenceCamera"/> instead of a manual menu click. Because
        /// this project's presentation camera holds a fixed tilt while following the player (see
        /// world-builder-phase1.md), this rotation only needs recomputing when the prop rebuilds, not
        /// every frame -- there is no runtime per-frame billboard component.
        /// </summary>
        Billboard,

        /// <summary>An explicit Euler angle set on the prop (<see cref="PropDefinition.customEulerAngles"/>), for anything the three presets above don't cover.</summary>
        Custom
    }
}
