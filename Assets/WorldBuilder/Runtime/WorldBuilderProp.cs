using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One placed prop instance: a generated quad (mesh + alpha-cutout material, see
    /// <see cref="PropMeshBuilder"/> / <see cref="PropMaterialCache"/>) plus an optional box collider,
    /// built from a <see cref="PropDefinition"/> reference. Like <see cref="GroundChunk"/>, this is not a
    /// source of truth -- its generated mesh can always be safely discarded and regenerated from the
    /// definition, which is what happens automatically after a domain reload (generated Mesh objects are
    /// not themselves serialized into the scene file). Contains no UnityEditor references so it works
    /// identically in builds and at runtime.
    ///
    /// Rotation is fully re-derived from <see cref="PropDefinition.defaultOrientation"/> on every
    /// rebuild, the same rebuild-safety philosophy as the mesh itself -- so there is currently no
    /// per-instance rotation override (e.g. randomizing a rock prop's facing per placement). That's a
    /// known, deliberately deferred limitation, not an oversight; see world-builder-phase3.md.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WorldBuilderProp : MonoBehaviour
    {
        [SerializeField] private PropDefinition definition;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private BoxCollider boxCollider;

        public PropDefinition Definition => definition;

        /// <summary>Sets which PropDefinition this instance represents. Does not rebuild by itself -- call Rebuild afterward.</summary>
        public void Configure(PropDefinition propDefinition)
        {
            definition = propDefinition;
            if (string.IsNullOrEmpty(name) || name.StartsWith("GameObject"))
            {
                name = definition != null ? $"Prop_{definition.displayName}" : "Prop";
            }
        }

        public void Rebuild(WorldBuilderSettings settings, Camera referenceCamera)
        {
            EnsureComponents();

            if (definition == null || definition.sprite == null)
            {
                ReleaseMesh(meshFilter.sharedMesh);
                meshFilter.sharedMesh = null;
                meshRenderer.enabled = false;
                if (boxCollider != null) boxCollider.enabled = false;
                return;
            }

            Mesh mesh = PropMeshBuilder.BuildQuadMesh(definition, settings);
            ReleaseMesh(meshFilter.sharedMesh);
            meshFilter.sharedMesh = mesh;

            Material material = PropMaterialCache.GetMaterial(definition.sprite.texture, null);
            meshRenderer.sharedMaterial = material;
            meshRenderer.enabled = mesh != null && material != null;

            transform.localRotation = PropOrientationUtility.ResolveRotation(definition, referenceCamera, transform.position);

            ApplyCollision();
        }

        private void ApplyCollision()
        {
            if (definition.collisionEnabled)
            {
                if (boxCollider == null) boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.enabled = true;
                boxCollider.center = new Vector3(definition.footprintOffset.x, definition.footprintHeight * 0.5f, definition.footprintOffset.y);
                boxCollider.size = new Vector3(
                    Mathf.Max(0.0001f, definition.footprintSize.x),
                    Mathf.Max(0.0001f, definition.footprintHeight),
                    Mathf.Max(0.0001f, definition.footprintSize.y));
            }
            else if (boxCollider != null)
            {
                boxCollider.enabled = false;
            }
        }

        private void EnsureComponents()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

            if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        }

        private static void ReleaseMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        private void OnEnable()
        {
            WorldBuilderWorld world = GetComponentInParent<WorldBuilderWorld>();
            Rebuild(world != null ? world.settings : null, world != null ? world.settings.referenceCamera : null);
        }

        private void OnDestroy()
        {
            EnsureComponents();
            if (meshFilter != null) ReleaseMesh(meshFilter.sharedMesh);
        }
    }
}
