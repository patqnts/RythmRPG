using System;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>How the height of a cut is given (see <see cref="GrassCutHeight"/>).</summary>
    public enum GrassCutMode
    {
        /// <summary>The field's Stubble Height (with a little variation).</summary>
        FieldDefault,
        /// <summary>Value = world units above each tuft's root.</summary>
        AboveGround,
        /// <summary>Value = a world Y (e.g. the height of the blade that swings through the grass).</summary>
        WorldHeight,
        /// <summary>Value = a fraction of each tuft (0 = ground, 1 = tip).</summary>
        Fraction
    }

    /// <summary>
    /// Where a cut goes through the tufts. Tufts that are already shorter are not cut, and no cut goes below the
    /// field's Min Stubble.
    /// </summary>
    [Serializable]
    public struct GrassCutHeight
    {
        public GrassCutMode mode;
        public float value;

        public GrassCutHeight(GrassCutMode mode, float value)
        {
            this.mode = mode;
            this.value = value;
        }

        public static GrassCutHeight FieldDefault => default;
        public static GrassCutHeight AboveGround(float height) => new GrassCutHeight(GrassCutMode.AboveGround, height);
        public static GrassCutHeight AtWorldHeight(float y) => new GrassCutHeight(GrassCutMode.WorldHeight, y);
        public static GrassCutHeight AtFraction(float fraction) => new GrassCutHeight(GrassCutMode.Fraction, fraction);
    }

    /// <summary>What a shockwave does to the grass besides pushing it.</summary>
    [Flags]
    public enum GrassShockwaveEffect
    {
        None = 0,
        /// <summary>Cuts the grass as the ring passes.</summary>
        Cut = 1,
        /// <summary>Sets the grass on fire as the ring passes (an explosion).</summary>
        Ignite = 2,
        /// <summary>Blows out fire as the ring passes.</summary>
        Extinguish = 4
    }

    /// <summary>
    /// One place for gameplay to cut, burn and shake the grass of every active <see cref="GrassField"/>.
    /// <code>
    /// Grass.Cut(swordTip, 0.8f);                       // returns how many tufts were cut (drop loot, play a sound)
    /// Grass.CutAlong(swingStart, swingEnd, 0.5f);
    /// Grass.CutAlong(swingStart, swingEnd, 0.5f, GrassCutHeight.AtWorldHeight(blade.position.y), swingDirection);
    /// Grass.Cut(player.position, 1.2f, GrassCutHeight.AboveGround(0.1f)); // mow low
    /// Grass.Ignite(fireball.position, 0.6f);           // the fire then spreads on its own, pushed by the wind
    /// Grass.Extinguish(splash.position, 2f);
    /// Grass.Shockwave(slam.position, 6f);              // expanding ring that pushes the grass flat
    /// Grass.Shockwave(bomb.position, 5f, effects: GrassShockwaveEffect.Cut | GrassShockwaveEffect.Ignite);
    /// var blast = GrassShockwave.Cone(player.position, player.forward, 6f, 70f); // or .Ring / .Line
    /// blast.effects = GrassShockwaveEffect.Cut; blast.cutHeight = GrassCutHeight.AboveGround(0.1f);
    /// Grass.Shockwave(blast);
    /// if (Grass.IsBurningAt(player.position, 0.4f)) ... // stand in fire: take damage
    /// </code>
    /// Positions are world space; only X and Z are used to find the tufts. Everything is Play-mode only.
    /// </summary>
    public static class Grass
    {
        /// <summary>Raised once per frame per field with the average position and number of tufts cut.</summary>
        public static event Action<Vector3, int> BladesCut;

        internal static void RaiseBladesCut(Vector3 position, int count) => BladesCut?.Invoke(position, count);

        /// <summary>Cuts the grass within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static int Cut(Vector3 center, float radius)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.CutInRadius(center, radius);
            return count;
        }

        /// <summary>
        /// Cuts the grass within <paramref name="radius"/> of <paramref name="center"/> at <paramref name="height"/>;
        /// the severed tops fly off along <paramref name="direction"/> (zero = outward).
        /// </summary>
        public static int Cut(Vector3 center, float radius, GrassCutHeight height, Vector3 direction = default) =>
            CutAlong(center, center, radius, height, direction);

        /// <summary>Cuts the grass within <paramref name="radius"/> of the segment from - to (a swing, a dash).</summary>
        public static int CutAlong(Vector3 from, Vector3 to, float radius) =>
            CutAlong(from, to, radius, GrassCutHeight.FieldDefault, to - from);

        /// <summary>
        /// Cuts the grass within <paramref name="radius"/> of the segment from - to at <paramref name="height"/>;
        /// the severed tops fly off along <paramref name="direction"/> (zero = away from the segment).
        /// </summary>
        public static int CutAlong(Vector3 from, Vector3 to, float radius, GrassCutHeight height, Vector3 direction = default)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.CutAlong(from, to, radius, height, direction);
            return count;
        }

        /// <summary>Sets the grass within <paramref name="radius"/> on fire (after <paramref name="delay"/> seconds).</summary>
        public static int Ignite(Vector3 center, float radius, float delay = 0f)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.IgniteInRadius(center, radius, delay);
            return count;
        }

        /// <summary>
        /// Sets fire to the grass inside a shape, e.g. a flamethrower's cone:
        /// <c>Grass.Ignite(GrassArea.Cone(nozzle.position, nozzle.forward, 4f, 25f))</c>.
        /// </summary>
        public static int Ignite(GrassArea area, float delay = 0f)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.IgniteInArea(area, delay);
            return count;
        }

        /// <summary>Puts out the fire inside a shape (a water hose's cone, a splash's sphere).</summary>
        public static int Extinguish(GrassArea area)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.ExtinguishInArea(area);
            return count;
        }

        /// <summary>Cuts the grass inside a shape (a sword arc as a cone, a mower as a box).</summary>
        public static int Cut(GrassArea area, GrassCutHeight height, Vector3 direction = default)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.CutInArea(area, height, direction);
            return count;
        }

        /// <summary>True if grass inside the shape is in flames.</summary>
        public static bool IsBurningIn(GrassArea area)
        {
            foreach (GrassField field in GrassField.ActiveFields)
                if (field.IsBurningIn(area)) return true;
            return false;
        }

        /// <summary>Puts out the fire within <paramref name="radius"/>.</summary>
        public static int Extinguish(Vector3 center, float radius)
        {
            int count = 0;
            foreach (GrassField field in GrassField.ActiveFields) count += field.ExtinguishInRadius(center, radius);
            return count;
        }

        /// <summary>
        /// An expanding ring that pushes the grass outward (it springs back behind the ring).
        /// </summary>
        /// <param name="maxRadius">Where the ring fades out (world units).</param>
        /// <param name="speed">How fast the ring grows (units per second).</param>
        /// <param name="strength">Push at the start (1 = as hard as a character's step; up to 2).</param>
        /// <param name="effects">Also cut, ignite or extinguish the grass as the ring passes.</param>
        /// <param name="effectRadius">How far the effects reach (less than 0 = half of <paramref name="maxRadius"/>).</param>
        /// <param name="width">Thickness of the ring (units).</param>
        public static void Shockwave(Vector3 center, float maxRadius = 5f, float speed = 14f, float strength = 1.2f,
            GrassShockwaveEffect effects = GrassShockwaveEffect.None, float effectRadius = -1f, float width = 0.9f)
        {
            GrassShockwave wave = GrassShockwave.Ring(center, maxRadius);
            wave.speed = speed;
            wave.strength = strength;
            wave.effects = effects;
            wave.effectDistance = effectRadius;
            wave.width = width;
            Shockwave(wave);
        }

        /// <summary>
        /// A ring, cone or line shockwave (see <see cref="GrassShockwave"/>): pushes the grass as it passes and
        /// applies its effects (cut at its cut height, ignite, put out) in the same shape.
        /// </summary>
        public static void Shockwave(GrassShockwave wave)
        {
            if (!Application.isPlaying) return;
            GrassInteractionMap.AddShockwave(wave);
            if (wave.effects == GrassShockwaveEffect.None) return;
            wave.speed = Mathf.Max(0.1f, wave.speed);
            foreach (GrassField field in GrassField.ActiveFields) field.ApplyShockwave(wave);
        }

        /// <summary>True if grass within <paramref name="radius"/> of <paramref name="point"/> is in flames.</summary>
        public static bool IsBurningAt(Vector3 point, float radius = 0.3f)
        {
            foreach (GrassField field in GrassField.ActiveFields)
                if (field.IsBurningAt(point, radius)) return true;
            return false;
        }

        /// <summary>Tufts burning or glowing in every field.</summary>
        public static int BurningCount
        {
            get
            {
                int count = 0;
                foreach (GrassField field in GrassField.ActiveFields) count += field.BurningCount;
                return count;
            }
        }

        /// <summary>
        /// How high <paramref name="point"/> is above the grass it is over (float.PositiveInfinity when it is not over
        /// any field). Use it to keep flying things from cutting or burning the grass.
        /// </summary>
        public static float HeightAboveGrass(Vector3 point)
        {
            float best = float.PositiveInfinity;
            foreach (GrassField field in GrassField.ActiveFields)
            {
                if (!field.ContainsXZ(point)) continue;
                float height = point.y - field.GroundHeight;
                if (Mathf.Abs(height) < Mathf.Abs(best)) best = height;
            }
            return best;
        }

        /// <summary>Restores every tuft in every field (uncut, unburnt) and cancels the fire.</summary>
        public static void RestoreAll()
        {
            foreach (GrassField field in GrassField.ActiveFields) field.RestoreAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => BladesCut = null;
    }
}
