using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>Square pixel bits that drift up and fade (or gather in, for a reappearance). Runtime only.</summary>
    public sealed class NoteFlakes : MonoBehaviour
    {
        private struct Flake
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 Start;
            public Vector3 End;
        }

        private static Sprite pixel;
        private readonly List<Flake> flakes = new();
        private Color color;
        private float lifetime;
        private float age;
        private bool gather;
        private Camera view;

        public static void Spawn(Bounds bounds, int count, Color color, float size, float rise, float lifetime, bool gather,
            Camera camera, SpriteRenderer sortLike, Transform parent)
        {
            if (count <= 0) return;
            if (pixel == null)
            {
                Texture2D white = Texture2D.whiteTexture;
                pixel = Sprite.Create(white, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
                pixel.name = "Note Flake Pixel";
            }
            var root = new GameObject("Note Flakes");
            root.transform.SetParent(parent, true);
            NoteFlakes owner = root.AddComponent<NoteFlakes>();
            owner.color = color;
            owner.lifetime = Mathf.Max(0.05f, lifetime);
            owner.gather = gather;
            owner.view = camera;
            Vector3 up = Vector3.up;
            Vector3 side = camera != null ? camera.transform.right : Vector3.right;
            for (int i = 0; i < count; i++)
            {
                var flake = new GameObject("Flake");
                flake.transform.SetParent(root.transform, false);
                flake.transform.localScale = Vector3.one * size;
                SpriteRenderer renderer = flake.AddComponent<SpriteRenderer>();
                renderer.sprite = pixel;
                renderer.color = color;
                if (sortLike != null)
                {
                    renderer.sortingLayerID = sortLike.sortingLayerID;
                    renderer.sortingOrder = sortLike.sortingOrder + 1;
                }
                Vector3 inside = new(
                    Random.Range(bounds.min.x, bounds.max.x),
                    Random.Range(bounds.min.y, bounds.max.y),
                    Random.Range(bounds.min.z, bounds.max.z));
                Vector3 drift = up * (rise * Random.Range(0.6f, 1.2f)) + side * Random.Range(-0.35f, 0.35f) * rise;
                Vector3 start = gather ? inside + drift : inside;
                Vector3 end = gather ? inside : inside + drift;
                flake.transform.position = start;
                owner.flakes.Add(new Flake { Transform = flake.transform, Renderer = renderer, Start = start, End = end });
            }
            owner.Step(0f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            Step(Mathf.Clamp01(age / lifetime));
            if (age >= lifetime) Destroy(gameObject);
        }

        private void Step(float t)
        {
            float eased = gather ? t * t : 1f - (1f - t) * (1f - t);
            float alpha = gather ? Mathf.Clamp01(t * 2f) : 1f - t;
            Quaternion facing = view != null ? view.transform.rotation : Quaternion.identity;
            foreach (Flake flake in flakes)
            {
                if (flake.Transform == null) continue;
                flake.Transform.position = Vector3.LerpUnclamped(flake.Start, flake.End, eased);
                flake.Transform.rotation = facing;
                Color c = color;
                c.a *= alpha;
                flake.Renderer.color = c;
            }
        }
    }
}
