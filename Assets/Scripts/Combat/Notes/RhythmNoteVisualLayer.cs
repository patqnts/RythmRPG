using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Keeps both sprite and mesh note visuals above the world judgement line. Materials already
    /// in the transparent queue are reused; opaque materials get per-note runtime copies.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(12000)]
    public sealed class RhythmNoteVisualLayer : MonoBehaviour
    {
        private const int MinimumRenderQueue = 3000;
        private const int MinimumSortingOrder = 0;
        // Lifts notes above the Perfect Hit Line (sorting order 1) while keeping their relative layering.
        private const int SortingOrderOffset = 50;
        private readonly List<Material> ownedMaterials = new();
        private readonly List<(SpriteRenderer source, SpriteRenderer display, bool hidden)> sprites = new();
        private Camera visualCamera;
        private MaterialPropertyBlock spriteProperties;

        public void FaceSpritesToCamera(Camera camera)
        {
            visualCamera = camera;
            if (sprites.Count > 0) return;
            // Keep original renderers as animation/script targets, even when they share the movement root.
            // Only their display copies rotate; colliders, emitters and meshes retain their transforms.
            foreach (SpriteRenderer source in GetComponentsInChildren<SpriteRenderer>(true))
            {
                GameObject displayObject = new(source.name + " Camera Visual");
                displayObject.layer = source.gameObject.layer;
                displayObject.transform.SetParent(source.transform, false);
                SpriteRenderer display = displayObject.AddComponent<SpriteRenderer>();
                sprites.Add((source, display, source.forceRenderingOff));
                source.forceRenderingOff = true;
            }
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (visualCamera == null) visualCamera = Camera.main;
            if (visualCamera == null) return;
            foreach (var pair in sprites)
            {
                SpriteRenderer source = pair.source;
                SpriteRenderer display = pair.display;
                if (source == null || display == null) continue;
                source.forceRenderingOff = true;
                display.enabled = source.enabled && !pair.hidden;
                display.sprite = source.sprite;
                display.color = source.color;
                display.flipX = source.flipX;
                display.flipY = source.flipY;
                display.drawMode = source.drawMode;
                display.size = source.size;
                display.tileMode = source.tileMode;
                display.maskInteraction = source.maskInteraction;
                display.spriteSortPoint = source.spriteSortPoint;
                display.sortingLayerID = source.sortingLayerID;
                display.sortingOrder = source.sortingOrder;
                display.sharedMaterials = source.sharedMaterials;
                spriteProperties ??= new MaterialPropertyBlock();
                source.GetPropertyBlock(spriteProperties);
                display.SetPropertyBlock(spriteProperties);
                // Retain authored/animated roll in the sprite plane, independently of camera pitch.
                display.transform.rotation = visualCamera.transform.rotation
                    * Quaternion.Euler(0f, 0f, source.transform.eulerAngles.z);
            }
        }

        private void OnDisable()
        {
            foreach (var pair in sprites)
            {
                if (pair.source != null) pair.source.forceRenderingOff = pair.hidden;
                if (pair.display != null) pair.display.enabled = false;
            }
        }

        public void Configure()
        {
            foreach (Renderer visualRenderer in GetComponentsInChildren<Renderer>(true))
            {
                visualRenderer.sortingOrder = Mathf.Max(visualRenderer.sortingOrder, MinimumSortingOrder) + SortingOrderOffset;
                Material[] materials = visualRenderer.sharedMaterials;
                bool changed = false;

                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null || source.renderQueue >= MinimumRenderQueue) continue;

                    Material runtimeMaterial = new(source)
                    {
                        name = $"{source.name} (Rhythm Note Runtime)",
                        hideFlags = HideFlags.DontSave
                    };
                    runtimeMaterial.renderQueue = MinimumRenderQueue;
                    materials[index] = runtimeMaterial;
                    ownedMaterials.Add(runtimeMaterial);
                    changed = true;
                }

                if (changed) visualRenderer.sharedMaterials = materials;
            }
        }

        private void OnDestroy()
        {
            OnDisable();
            foreach (var pair in sprites)
            {
                if (pair.display == null) continue;
                if (Application.isPlaying) Destroy(pair.display.gameObject);
                else DestroyImmediate(pair.display.gameObject);
            }
            foreach (Material material in ownedMaterials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            ownedMaterials.Clear();
        }
    }
}
