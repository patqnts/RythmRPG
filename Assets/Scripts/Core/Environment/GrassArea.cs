using UnityEngine;

namespace RythmRPG.Core
{
    public enum GrassAreaShape { Sphere, Capsule, Cone, Box }

    /// <summary>
    /// A shape of grass to cut, burn or put out: a sphere, a capsule, a cone (a flamethrower, a breath attack, a
    /// sword arc) or a box. Build one with the static methods and pass it to <see cref="Grass"/>.
    /// <para>
    /// <b>Footprint</b> (flat = true, the default): the shape is laid on the ground seen from above, and everything
    /// under it counts, whatever its height. A cone held at chest height and tilted down covers the strip of ground
    /// it points along, shortened by the tilt. With flat = false the shape must really touch the tuft in 3D (its
    /// root, middle or tip).
    /// </para>
    /// </summary>
    public struct GrassArea
    {
        private GrassAreaShape shape;
        private Vector3 start;      // sphere centre, capsule start, cone apex, box centre
        private Vector3 end;        // capsule end, cone end (on the axis)
        private Vector3 direction;  // cone axis (unit)
        private float radius;       // sphere / capsule radius, cone radius at the apex
        private float endRadius;    // cone radius at its end
        private float length;       // cone length
        private Quaternion rotation;
        private Quaternion inverseRotation;
        private Vector3 halfSize;
        private bool flat;

        public GrassAreaShape Shape => shape;
        public bool Flat => flat;
        /// <summary>Sphere / box centre, capsule start, cone apex.</summary>
        public Vector3 Origin => start;
        /// <summary>The way a cone points (zero for other shapes).</summary>
        public Vector3 Direction => direction;

        public static GrassArea Sphere(Vector3 center, float radius, bool flat = true)
        {
            var area = new GrassArea { shape = GrassAreaShape.Sphere, start = center, end = center, radius = Mathf.Max(0f, radius), flat = flat };
            return area;
        }

        public static GrassArea Capsule(Vector3 from, Vector3 to, float radius, bool flat = true)
        {
            var area = new GrassArea { shape = GrassAreaShape.Capsule, start = from, end = to, radius = Mathf.Max(0f, radius), flat = flat };
            return area;
        }

        /// <param name="apex">Where the cone starts (the nozzle).</param>
        /// <param name="forward">Which way it points.</param>
        /// <param name="length">How far it reaches.</param>
        /// <param name="angle">Full opening angle in degrees.</param>
        /// <param name="startRadius">Radius at the nozzle (0 = a point).</param>
        public static GrassArea Cone(Vector3 apex, Vector3 forward, float length, float angle, float startRadius = 0f, bool flat = true)
        {
            Vector3 axis = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
            length = Mathf.Max(0f, length);
            startRadius = Mathf.Max(0f, startRadius);
            float tangent = Mathf.Tan(Mathf.Clamp(angle, 0f, 170f) * 0.5f * Mathf.Deg2Rad);
            var area = new GrassArea
            {
                shape = GrassAreaShape.Cone,
                start = apex,
                end = apex + axis * length,
                direction = axis,
                radius = startRadius,
                endRadius = startRadius + length * tangent,
                length = length,
                flat = flat
            };
            return area;
        }

        /// <param name="size">Full size along the rotation's right / up / forward.</param>
        public static GrassArea Box(Vector3 center, Quaternion rotation, Vector3 size, bool flat = true)
        {
            if (flat) rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f); // footprint: only the heading matters
            var area = new GrassArea
            {
                shape = GrassAreaShape.Box,
                start = center,
                end = center,
                rotation = rotation,
                inverseRotation = Quaternion.Inverse(rotation),
                halfSize = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f,
                flat = flat
            };
            return area;
        }

        /// <summary>World XZ rectangle that holds the whole shape (for the grid search).</summary>
        public void GetBoundsXZ(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            float reach;
            switch (shape)
            {
                case GrassAreaShape.Cone:
                    reach = Mathf.Max(radius, endRadius);
                    break;
                case GrassAreaShape.Box:
                    reach = flat ? new Vector2(halfSize.x, halfSize.z).magnitude : halfSize.magnitude;
                    break;
                default:
                    reach = radius;
                    break;
            }
            minX = Mathf.Min(start.x, end.x) - reach;
            maxX = Mathf.Max(start.x, end.x) + reach;
            minZ = Mathf.Min(start.z, end.z) - reach;
            maxZ = Mathf.Max(start.z, end.z) + reach;
        }

        /// <summary>
        /// Whether a tuft rooted at <paramref name="root"/>, <paramref name="height"/> tall, is in the area.
        /// </summary>
        public bool ContainsTuft(Vector3 root, float height)
        {
            if (flat) return ContainsFlat(root);
            return Contains(root) || Contains(root + Vector3.up * (height * 0.5f)) || Contains(root + Vector3.up * height);
        }

        /// <summary>Whether a point is inside the shape (in 3D, whatever <see cref="Flat"/> says).</summary>
        public bool Contains(Vector3 p)
        {
            switch (shape)
            {
                case GrassAreaShape.Sphere:
                    return (p - start).sqrMagnitude <= radius * radius;
                case GrassAreaShape.Capsule:
                    return DistanceToSegmentSq(p, start, end) <= radius * radius;
                case GrassAreaShape.Cone:
                {
                    Vector3 v = p - start;
                    float along = Vector3.Dot(v, direction);
                    if (along < 0f || along > length) return false;
                    float r = Mathf.Lerp(radius, endRadius, length > 1e-5f ? along / length : 0f);
                    return (v - direction * along).sqrMagnitude <= r * r;
                }
                default:
                {
                    Vector3 local = inverseRotation * (p - start);
                    return Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.y) <= halfSize.y && Mathf.Abs(local.z) <= halfSize.z;
                }
            }
        }

        // Seen from above: the shape's footprint on the ground.
        private bool ContainsFlat(Vector3 p)
        {
            Vector2 q = new(p.x, p.z);
            Vector2 a = new(start.x, start.z);
            Vector2 b = new(end.x, end.z);
            switch (shape)
            {
                case GrassAreaShape.Sphere:
                    return (q - a).sqrMagnitude <= radius * radius;
                case GrassAreaShape.Capsule:
                    return DistanceToSegmentSq(q, a, b) <= radius * radius;
                case GrassAreaShape.Cone:
                {
                    // The axis laid on the ground: a tilted cone covers a shorter strip.
                    Vector2 axis = b - a;
                    float flatLength = axis.magnitude;
                    if (flatLength < 1e-4f) return (q - b).sqrMagnitude <= endRadius * endRadius; // pointing straight down
                    axis /= flatLength;
                    Vector2 v = q - a;
                    float along = Vector2.Dot(v, axis);
                    if (along < 0f || along > flatLength) return false;
                    float r = Mathf.Lerp(radius, endRadius, along / flatLength);
                    float across = v.x * axis.y - v.y * axis.x;
                    return across * across <= r * r;
                }
                default:
                {
                    Vector3 local = inverseRotation * (p - start);
                    return Mathf.Abs(local.x) <= halfSize.x && Mathf.Abs(local.z) <= halfSize.z;
                }
            }
        }

        private static float DistanceToSegmentSq(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            float t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / lengthSq) : 0f;
            return (p - (a + ab * t)).sqrMagnitude;
        }

        private static float DistanceToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            float t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq) : 0f;
            return (p - (a + ab * t)).sqrMagnitude;
        }

        /// <summary>Draws the shape with Gizmos (for components' OnDrawGizmosSelected).</summary>
        public void DrawGizmo()
        {
            switch (shape)
            {
                case GrassAreaShape.Sphere:
                    Gizmos.DrawWireSphere(start, radius);
                    break;
                case GrassAreaShape.Capsule:
                    Gizmos.DrawWireSphere(start, radius);
                    Gizmos.DrawWireSphere(end, radius);
                    Gizmos.DrawLine(start, end);
                    break;
                case GrassAreaShape.Cone:
                {
                    Vector3 side = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(side, direction).normalized;
                    const int segments = 24;
                    Vector3 previousStart = start + side * radius, previousEnd = end + side * endRadius;
                    for (int i = 1; i <= segments; i++)
                    {
                        float angle = i / (float)segments * Mathf.PI * 2f;
                        Vector3 offset = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                        Vector3 nextStart = start + offset * radius, nextEnd = end + offset * endRadius;
                        Gizmos.DrawLine(previousEnd, nextEnd);
                        if (radius > 0f) Gizmos.DrawLine(previousStart, nextStart);
                        if (i % 6 == 0) Gizmos.DrawLine(nextStart, nextEnd);
                        previousStart = nextStart;
                        previousEnd = nextEnd;
                    }
                    break;
                }
                default:
                {
                    Matrix4x4 previous = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(start, rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, halfSize * 2f);
                    Gizmos.matrix = previous;
                    break;
                }
            }
        }
    }
}
