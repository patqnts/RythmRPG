using System;
using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One chunk's rendering representation: a generated mesh, not a source of truth. Tile data in
    /// <see cref="TileLayerData"/> is authoritative; this component (and the meshes it owns) can always
    /// be safely discarded and regenerated from that data, which is what happens automatically after a
    /// domain reload (generated Mesh objects are not themselves serialized into the scene file).
    /// Contains no UnityEditor references so it works identically in builds and at runtime.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class GroundChunk : MonoBehaviour
    {
        [SerializeField] private int chunkX;
        [SerializeField] private int chunkZ;
        [SerializeField] private int elevationLevel;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        public int ChunkX => chunkX;
        public int ChunkZ => chunkZ;
        public int ElevationLevel => elevationLevel;

        public bool IsEmpty => meshFilter == null || meshFilter.sharedMesh == null;

        public void Configure(int x, int z, int elevation)
        {
            chunkX = x;
            chunkZ = z;
            elevationLevel = elevation;
            name = $"Chunk_{x}_{z}_L{elevation}";
        }

        public void Rebuild(WorldBuilderWorld world, IReadOnlyList<TileLayerData> layers)
        {
            EnsureComponents();

            TileMeshBuilder.ChunkMeshResult result = TileMeshBuilder.BuildChunk(world, layers, chunkX, chunkZ, elevationLevel);

            ReleaseMesh(meshFilter.sharedMesh);
            meshFilter.sharedMesh = result.visualMesh;
            meshRenderer.sharedMaterials = result.visualMaterials ?? Array.Empty<Material>();
            meshRenderer.enabled = result.visualMesh != null;

            if (result.hasCollision)
            {
                if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
                ReleaseMesh(meshCollider.sharedMesh);
                meshCollider.sharedMesh = result.collisionMesh;
                meshCollider.enabled = true;
            }
            else if (meshCollider != null)
            {
                ReleaseMesh(meshCollider.sharedMesh);
                meshCollider.sharedMesh = null;
                meshCollider.enabled = false;
            }
        }

        private void EnsureComponents()
        {
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
        }

        private static void ReleaseMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        private void OnDestroy()
        {
            EnsureComponents();
            if (meshFilter != null) ReleaseMesh(meshFilter.sharedMesh);
            if (meshCollider != null) ReleaseMesh(meshCollider.sharedMesh);
        }
    }
}
