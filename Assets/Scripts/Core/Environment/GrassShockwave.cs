using System;
using UnityEngine;

namespace RythmRPG.Core
{
    public enum GrassShockwaveShape
    {
        /// <summary>A circle growing in every direction (a slam, an explosion).</summary>
        Ring,
        /// <summary>Only a slice of the circle, opening along a direction (a blast, a roar, a shotgun, a swing).</summary>
        Cone,
        /// <summary>A straight wall moving along a direction (a charge, a sweeping gust, a beam front).</summary>
        Line
    }

    /// <summary>
    /// Everything about one shockwave: its shape, how it moves, and what it does to the grass it passes.
    /// <code>
    /// var wave = GrassShockwave.Cone(player.position, player.forward, 6f, 70f);
    /// wave.effects = GrassShockwaveEffect.Cut;
    /// wave.cutHeight = GrassCutHeight.AboveGround(0.1f);
    /// Grass.Shockwave(wave);
    /// </code>
    /// </summary>
    [Serializable]
    public struct GrassShockwave
    {
        public GrassShockwaveShape shape;
        /// <summary>Where it starts (ring / cone centre, the line's middle at the start).</summary>
        public Vector3 origin;
        /// <summary>Cone / Line: which way it travels (only X and Z count).</summary>
        public Vector3 direction;
        /// <summary>How far it travels before fading out (world units).</summary>
        public float distance;
        /// <summary>Units per second.</summary>
        public float speed;
        /// <summary>Push at the start (1 = like a footstep, up to 2).</summary>
        public float strength;
        /// <summary>Thickness of the wave front (world units).</summary>
        public float width;
        /// <summary>Cone: full opening angle in degrees.</summary>
        public float angle;
        /// <summary>Line: how long the wall is, side to side (world units).</summary>
        public float lineLength;
        /// <summary>Also cut, ignite or put out the grass as the wave passes.</summary>
        public GrassShockwaveEffect effects;
        /// <summary>How far the effects reach (less than 0 = half the distance).</summary>
        public float effectDistance;
        /// <summary>Where the wave cuts the grass (with <see cref="GrassShockwaveEffect.Cut"/>).</summary>
        public GrassCutHeight cutHeight;

        private static GrassShockwave Make(GrassShockwaveShape shape, Vector3 origin, Vector3 direction, float distance)
        {
            return new GrassShockwave
            {
                shape = shape,
                origin = origin,
                direction = direction,
                distance = distance,
                speed = 14f,
                strength = 1.2f,
                width = 0.9f,
                angle = 60f,
                lineLength = 4f,
                effects = GrassShockwaveEffect.None,
                effectDistance = -1f,
                cutHeight = GrassCutHeight.FieldDefault
            };
        }

        public static GrassShockwave Ring(Vector3 center, float radius) =>
            Make(GrassShockwaveShape.Ring, center, Vector3.forward, radius);

        public static GrassShockwave Cone(Vector3 origin, Vector3 direction, float distance, float angle)
        {
            GrassShockwave wave = Make(GrassShockwaveShape.Cone, origin, direction, distance);
            wave.angle = angle;
            return wave;
        }

        public static GrassShockwave Line(Vector3 origin, Vector3 direction, float distance, float length)
        {
            GrassShockwave wave = Make(GrassShockwaveShape.Line, origin, direction, distance);
            wave.lineLength = length;
            return wave;
        }

        /// <summary>The travel direction on the ground (unit XZ; +Z when unset).</summary>
        public Vector2 FlatDirection
        {
            get
            {
                Vector2 d = new(direction.x, direction.z);
                return d.sqrMagnitude > 1e-6f ? d.normalized : new Vector2(0f, 1f);
            }
        }

        /// <summary>How far the effects reach.</summary>
        public float EffectReach => effectDistance < 0f ? distance * 0.5f : Mathf.Min(effectDistance, distance);

        /// <summary>The ground the effects cover, as an area.</summary>
        public GrassArea EffectArea
        {
            get
            {
                float reach = EffectReach;
                Vector2 d = FlatDirection;
                Vector3 forward = new(d.x, 0f, d.y);
                switch (shape)
                {
                    case GrassShockwaveShape.Cone:
                        return GrassArea.Cone(origin, forward, reach, Mathf.Clamp(angle, 1f, 160f));
                    case GrassShockwaveShape.Line:
                        return GrassArea.Box(origin + forward * (reach * 0.5f), Quaternion.LookRotation(forward, Vector3.up),
                            new Vector3(lineLength, 1f, reach));
                    default:
                        return GrassArea.Sphere(origin, reach);
                }
            }
        }

        /// <summary>How far the wave has to travel to reach a point (it gets there at distance / speed).</summary>
        public float TravelTo(Vector3 point)
        {
            float dx = point.x - origin.x, dz = point.z - origin.z;
            if (shape != GrassShockwaveShape.Line) return Mathf.Sqrt(dx * dx + dz * dz);
            Vector2 d = FlatDirection;
            return Mathf.Max(0f, dx * d.x + dz * d.y);
        }

        /// <summary>The way grass at a point is thrown (radians, world XZ).</summary>
        public float PushAngle(Vector3 point)
        {
            if (shape == GrassShockwaveShape.Line)
            {
                Vector2 d = FlatDirection;
                return Mathf.Atan2(d.y, d.x);
            }
            return Mathf.Atan2(point.z - origin.z, point.x - origin.x);
        }

        /// <summary>Draws the wave's reach and its effect area with Gizmos.</summary>
        public void DrawGizmo(Color waveColor, Color effectColor)
        {
            Gizmos.color = waveColor;
            DrawOutline(distance);
            if (effects == GrassShockwaveEffect.None) return;
            Gizmos.color = effectColor;
            DrawOutline(EffectReach);
        }

        private void DrawOutline(float reach)
        {
            Vector2 d = FlatDirection;
            Vector3 forward = new(d.x, 0f, d.y);
            Vector3 right = new(d.y, 0f, -d.x);
            switch (shape)
            {
                case GrassShockwaveShape.Line:
                {
                    Vector3 half = right * (lineLength * 0.5f);
                    Vector3 end = origin + forward * reach;
                    Gizmos.DrawLine(origin - half, origin + half);
                    Gizmos.DrawLine(end - half, end + half);
                    Gizmos.DrawLine(origin - half, end - half);
                    Gizmos.DrawLine(origin + half, end + half);
                    break;
                }
                default:
                {
                    float halfAngle = shape == GrassShockwaveShape.Cone ? Mathf.Clamp(angle, 1f, 360f) * 0.5f * Mathf.Deg2Rad : Mathf.PI;
                    const int segments = 40;
                    Vector3 previous = Vector3.zero;
                    for (int i = 0; i <= segments; i++)
                    {
                        float a = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
                        Vector3 point = origin + (forward * Mathf.Cos(a) + right * Mathf.Sin(a)) * reach;
                        if (i > 0) Gizmos.DrawLine(previous, point);
                        else if (shape == GrassShockwaveShape.Cone) Gizmos.DrawLine(origin, point);
                        previous = point;
                    }
                    if (shape == GrassShockwaveShape.Cone) Gizmos.DrawLine(origin, previous);
                    break;
                }
            }
        }
    }
}
