using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// A short-lived jagged lightning line between two points (zaps, Chain Spark arcs). Code-made LineRenderer, so it
    /// needs no prefab; replace it with a proper effect when the art exists.
    /// </summary>
    public sealed class LightningArcVfx : MonoBehaviour
    {
        private static Material sharedMaterial;

        private LineRenderer line;
        private Vector3 from;
        private Vector3 to;
        private Color color;
        private float lifetime;
        private float age;
        private float nextJitter;
        private int segments;
        private float jitter;

        /// <summary>Spawns an arc that flickers and fades over <paramref name="seconds"/>.</summary>
        public static LightningArcVfx Spawn(Vector3 from, Vector3 to, Color color, float width = 0.06f, float seconds = 0.25f)
        {
            var go = new GameObject("Lightning Arc");
            LightningArcVfx arc = go.AddComponent<LightningArcVfx>();
            arc.Setup(from, to, color, width, seconds);
            return arc;
        }

        private void Setup(Vector3 start, Vector3 end, Color tint, float width, float seconds)
        {
            from = start;
            to = end;
            color = tint;
            lifetime = Mathf.Max(0.05f, seconds);
            float length = Vector3.Distance(start, end);
            segments = Mathf.Clamp(Mathf.RoundToInt(length * 4f), 4, 24);
            jitter = Mathf.Clamp(length * 0.08f, 0.05f, 0.4f);
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = segments + 1;
            line.startWidth = width;
            line.endWidth = width * 0.6f;
            line.numCapVertices = 0;
            line.sortingOrder = 500;
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            if (sharedMaterial != null) line.sharedMaterial = sharedMaterial;
            Rebuild();
            ApplyColor(1f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (age >= nextJitter)
            {
                nextJitter = age + 0.04f;
                Rebuild();
            }
            ApplyColor(1f - age / lifetime);
        }

        private void Rebuild()
        {
            if (line == null) return;
            Vector3 direction = to - from;
            Vector3 side = Vector3.Cross(direction.normalized, Vector3.forward);
            if (side.sqrMagnitude < 0.001f) side = Vector3.Cross(direction.normalized, Vector3.up);
            side.Normalize();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = from + direction * t;
                if (i > 0 && i < segments) point += side * Random.Range(-jitter, jitter) + Vector3.up * Random.Range(-jitter, jitter) * 0.5f;
                line.SetPosition(i, point);
            }
        }

        private void ApplyColor(float alpha)
        {
            if (line == null) return;
            Color start = new(1f, 1f, 1f, alpha);
            Color end = new(color.r, color.g, color.b, alpha);
            line.startColor = start;
            line.endColor = end;
        }
    }
}
