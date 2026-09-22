using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// The root component for one constructed world/level. Holds the authoritative tile data (ground
    /// layers), the settings that describe this world's grid/camera/compensation configuration, and the
    /// palette it paints from. Chunk GameObjects are created as children on demand and their meshes are
    /// (re)generated from tile data -- saving a scene with a WorldBuilderWorld never destroys existing
    /// scene objects, and reopening the scene safely regenerates any chunk whose mesh did not survive
    /// the domain reload.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("World Builder/World Builder World")]
    public class WorldBuilderWorld : MonoBehaviour
    {
        public WorldBuilderSettings settings = new WorldBuilderSettings();
        public TilePalette palette;
        public List<TileLayerData> groundLayers = new List<TileLayerData>();

        [NonSerialized] public int activeLayerIndex;
        [NonSerialized] public int activeElevationLevel;

        private readonly Dictionary<TileCoord, GroundChunk> chunkLookup = new Dictionary<TileCoord, GroundChunk>();
        private readonly HashSet<TileCoord> dirtyChunks = new HashSet<TileCoord>();
        private Transform chunkRoot;

        public IReadOnlyList<TileLayerData> Layers => groundLayers;

        public TileLayerData ActiveLayer
        {
            get
            {
                EnsureLayers();
                activeLayerIndex = Mathf.Clamp(activeLayerIndex, 0, groundLayers.Count - 1);
                return groundLayers[activeLayerIndex];
            }
        }

        private void EnsureLayers()
        {
            if (groundLayers.Count == 0)
            {
                groundLayers.Add(new TileLayerData { layerName = "Ground" });
            }
        }

        public TileLayerData AddLayer(string layerName)
        {
            TileLayerData layer = new TileLayerData { layerName = layerName };
            groundLayers.Add(layer);
            return layer;
        }

        public void SetTile(TileLayerData layer, TileCoord coord, TileCellData cell)
        {
            layer.SetTile(coord, cell);
            MarkChunkDirty(coord);
        }

        public void EraseTile(TileLayerData layer, TileCoord coord)
        {
            layer.RemoveTile(coord);
            MarkChunkDirty(coord);
        }

        public void MarkChunkDirty(TileCoord tileCoord)
        {
            int chunkSize = Mathf.Max(1, settings.chunkSizeInTiles);
            dirtyChunks.Add(ChunkKeyOf(tileCoord, chunkSize));
        }

        public static TileCoord ChunkKeyOf(TileCoord tileCoord, int chunkSize)
        {
            int cx = FloorDiv(tileCoord.x, chunkSize);
            int cz = FloorDiv(tileCoord.z, chunkSize);
            return new TileCoord(cx, cz, tileCoord.elevationLevel);
        }

        private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        public void RebuildDirtyChunks()
        {
            if (dirtyChunks.Count == 0) return;
            foreach (TileCoord chunkKey in dirtyChunks)
            {
                RebuildChunk(chunkKey);
            }

            dirtyChunks.Clear();
        }

        public void RebuildAllChunks()
        {
            foreach (TileCoord key in CollectAllChunkKeys())
            {
                RebuildChunk(key);
            }

            dirtyChunks.Clear();
        }

        public void RebuildMissingChunks()
        {
            foreach (TileCoord key in CollectAllChunkKeys())
            {
                if (!chunkLookup.TryGetValue(key, out GroundChunk chunk) || chunk == null || chunk.IsEmpty)
                {
                    RebuildChunk(key);
                }
            }
        }

        private HashSet<TileCoord> CollectAllChunkKeys()
        {
            EnsureLayers();
            int chunkSize = Mathf.Max(1, settings.chunkSizeInTiles);
            HashSet<TileCoord> keys = new HashSet<TileCoord>();
            foreach (TileLayerData layer in groundLayers)
            {
                foreach (KeyValuePair<TileCoord, TileCellData> kvp in layer.AllTiles)
                {
                    keys.Add(ChunkKeyOf(kvp.Key, chunkSize));
                }
            }

            return keys;
        }

        private void RebuildChunk(TileCoord chunkKey)
        {
            EnsureLayers();
            GroundChunk chunk = GetOrCreateChunk(chunkKey);
            chunk.Rebuild(this, groundLayers);
        }

        private GroundChunk GetOrCreateChunk(TileCoord chunkKey)
        {
            if (chunkLookup.TryGetValue(chunkKey, out GroundChunk existing) && existing != null)
            {
                return existing;
            }

            EnsureChunkRoot();
            GameObject go = new GameObject($"Chunk_{chunkKey.x}_{chunkKey.z}_L{chunkKey.elevationLevel}");
#if UNITY_EDITOR
            // Painting creates this GameObject as a side effect of a tile edit that
            // Undo.RegisterCompleteObjectUndo already recorded on this component, in the same undo
            // group (this call happens synchronously within that same edit, before Unity opens a new
            // group). Registering the GameObject too keeps that group consistent: without this,
            // Unity's undo system has no record of the child ever being created, so undoing the tile
            // edit leaves it "dangling" in the hierarchy instead of removing it along with the data
            // that produced it -- exactly the warning this fixes.
            if (!Application.isPlaying)
            {
                Undo.RegisterCreatedObjectUndo(go, "Paint Tiles");
            }
#endif
            go.transform.SetParent(chunkRoot, false);
            GroundChunk chunk = go.AddComponent<GroundChunk>();
            chunk.Configure(chunkKey.x, chunkKey.z, chunkKey.elevationLevel);
            chunkLookup[chunkKey] = chunk;
            return chunk;
        }

        private void EnsureChunkRoot()
        {
            if (chunkRoot != null) return;

            Transform existing = transform.Find("Chunks");
            if (existing != null)
            {
                chunkRoot = existing;
                foreach (Transform child in chunkRoot)
                {
                    GroundChunk chunk = child.GetComponent<GroundChunk>();
                    if (chunk != null)
                    {
                        chunkLookup[new TileCoord(chunk.ChunkX, chunk.ChunkZ, chunk.ElevationLevel)] = chunk;
                    }
                }

                return;
            }

            GameObject rootGO = new GameObject("Chunks");
#if UNITY_EDITOR
            // Same reasoning as the per-chunk registration below: this is the first thing painting
            // ever creates for a freshly-added WorldBuilderWorld, so it needs to be tied into the
            // same undo group as the tile edit that triggered it, or it's left dangling on undo too.
            if (!Application.isPlaying)
            {
                Undo.RegisterCreatedObjectUndo(rootGO, "Paint Tiles");
            }
#endif
            rootGO.transform.SetParent(transform, false);
            chunkRoot = rootGO.transform;
        }

        private void OnEnable()
        {
            EnsureLayers();
            EnsureChunkRoot();
            RebuildMissingChunks();
#if UNITY_EDITOR
            Undo.undoRedoPerformed += HandleUndoRedo;
#endif
        }

#if UNITY_EDITOR
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        private void HandleUndoRedo()
        {
            // Undo/redo reverts groundLayers (a serialized field Undo.RegisterCompleteObjectUndo
            // snapshots) but has no idea the already-built chunk meshes exist -- they're procedurally
            // generated and were never part of that snapshot. Left alone, the Scene keeps showing
            // whatever was on screen right before the undo until the next paint stroke happens to
            // touch the same chunk, at which point a full chunk's worth of tiles (up to
            // chunkSizeInTiles^2) suddenly snaps to the correct data all at once -- which is what
            // reads as "a big random chunk" appearing when painting right after an undo. Rebuilding
            // everything the instant undo/redo happens keeps the Scene matched to the data instead.
            if (this == null) return;
            RebuildAllChunks();
        }
#endif
    }
}
