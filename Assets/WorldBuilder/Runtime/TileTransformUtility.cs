namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Shared rotation/flip composition math used everywhere a whole arrangement of tiles is
    /// placed/rotated as a block: the Rectangle/Line/Stamp placement transform, Select-tool
    /// rotate/flip, and clipboard paste all go through this, so "rotate a group of tiles" behaves
    /// identically no matter which feature triggered it.
    /// </summary>
    public static class TileTransformUtility
    {
        /// <summary>Rotates a relative (dx, dz) offset by 90-degree steps (positive = clockwise looking down -Y).</summary>
        public static (int dx, int dz) RotateOffset(int dx, int dz, int steps)
        {
            steps = ((steps % 4) + 4) % 4;
            for (int i = 0; i < steps; i++)
            {
                int nextDx = -dz;
                int nextDz = dx;
                dx = nextDx;
                dz = nextDz;
            }

            return (dx, dz);
        }

        /// <summary>Mirrors a relative (dx, dz) offset across the axes indicated by flip.</summary>
        public static (int dx, int dz) FlipOffset(int dx, int dz, TileFlip flip)
        {
            if ((flip & TileFlip.Horizontal) != 0) dx = -dx;
            if ((flip & TileFlip.Vertical) != 0) dz = -dz;
            return (dx, dz);
        }

        public static int CombineRotation(int a, int b) => ((a + b) % 4 + 4) % 4;

        /// <summary>XOR composition: flipping twice on the same axis cancels out, matching how two mirrors compose.</summary>
        public static TileFlip CombineFlip(TileFlip a, TileFlip b) => a ^ b;
    }
}
