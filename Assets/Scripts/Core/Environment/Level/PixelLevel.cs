using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    public enum PixelCellShape : byte
    {
        Flat,
        /// <summary>A smooth slope up one level across the cell.</summary>
        Ramp,
        /// <summary>Steps up one level across the cell (walks like a ramp).</summary>
        Stairs
    }

    /// <summary>One grid cell of a <see cref="PixelLevel"/>.</summary>
    [Serializable]
    public struct PixelCell
    {
        public int x, z;
        /// <summary>Height in levels (0 = ground, 1 = one wall tile up...).</summary>
        public int height;
        /// <summary>Tile on top (index into the tileset).</summary>
        public int floor;
        /// <summary>Tile on the walls below this cell.</summary>
        public int wall;
        /// <summary>Tile for the top row of those walls (-1 = same as <see cref="wall"/>).</summary>
        public int wallTop;
        public PixelCellShape shape;
        /// <summary>Ramps / stairs: the way they go up. 0 = north (+Z), 1 = east (+X), 2 = south (-Z), 3 = west (-X).</summary>
        public byte direction;
        /// <summary>Floor tile rotation in quarter turns.</summary>
        public byte rotation;
    }

    /// <summary>Result of <see cref="PixelLevel.Raycast"/>.</summary>
    public struct PixelLevelHit
    {
        public Vector2Int cell;
        /// <summary>False when the ray hit empty ground (the paint plane) where no cell exists yet.</summary>
        public bool exists;
        /// <summary>-1 = the top of the cell; 0..3 = one of its walls (north, east, south, west).</summary>
        public int side;
        /// <summary>Hit point in the level's local space.</summary>
        public Vector3 localPoint;
    }

    /// <summary>
    /// A pixel-art level built on a height grid, painted in the Scene view (select it, press Edit Level):
    /// ground tiles on top of each cell, walls / cliffs made automatically wherever a cell is higher than its
    /// neighbour (or the level's edge), ramps and stairs, all textured from one <see cref="PixelTileset"/>.
    /// With 32 px tiles and 1 unit cells, one tile is exactly 32×32 screen pixels on the ground and on walls
    /// (oblique projection). The meshes and colliders are generated from the painted data (split into chunks),
    /// so the scene only stores the cells.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rythm RPG/Environment/Pixel Level")]
    public sealed class PixelLevel : MonoBehaviour, ISerializationCallbackReceiver
    {
        public const string ChunkPrefix = "[Pixel Level Chunk]";

        [SerializeField] private PixelTileset tileset;
        [Tooltip("World size of one cell (1 = one 32 px tile per unit, pixel-perfect).")]
        [SerializeField, Min(0.05f)] private float cellSize = 1f;
        [Tooltip("World height of one level (one wall tile).")]
        [SerializeField, Min(0.05f)] private float stepHeight = 1f;
        [Tooltip("Walls at the edge of the painted area go down to this level.")]
        [SerializeField] private int baseHeight;
        [Tooltip("Number of steps in a stairs cell.")]
        [SerializeField, Range(2, 8)] private int stairSteps = 4;
        [Tooltip("Generate colliders (walls block, ramps and stairs can be walked up).")]
        [SerializeField] private bool colliders = true;
        [SerializeField] private ShadowCastingMode castShadows = ShadowCastingMode.On;
        [SerializeField] private bool receiveShadows = true;
        [Tooltip("Cells per chunk side (only the chunks you edit are rebuilt).")]
        [SerializeField, Range(4, 64)] private int chunkSize = 16;
        [SerializeField, HideInInspector] private List<PixelCell> cells = new();

        private sealed class Chunk
        {
            public GameObject gameObject;
            public Mesh mesh;
            public Mesh collisionMesh;
        }

        private readonly Dictionary<Vector2Int, int> lookup = new();
        private readonly HashSet<Vector2Int> dirtyChunks = new();
        private readonly Dictionary<Vector2Int, Chunk> chunks = new();
        private bool lookupDirty = true;
        private bool allDirty = true;

        public PixelTileset Tileset { get => tileset; set { tileset = value; MarkAllDirty(); } }
        public float CellSize => cellSize;
        public float StepHeight => stepHeight;
        public int BaseHeight => baseHeight;
        public int StairSteps => stairSteps;
        public IReadOnlyList<PixelCell> Cells => cells;
        public int CellCount => cells.Count;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            RemoveStrayChunks();
            lookupDirty = true;
            MarkAllDirty();
            RebuildDirty();
        }

        private void OnDisable() => DestroyChunks();

        private void OnValidate()
        {
            MarkAllDirty();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        private void Update()
        {
            if (allDirty || dirtyChunks.Count > 0) RebuildDirty();
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            // Undo / redo / inspector edits replace the list: rebuild everything.
            lookupDirty = true;
            allDirty = true;
        }

        // ------------------------------------------------------------------ cells

        private void EnsureLookup()
        {
            if (!lookupDirty) return;
            lookup.Clear();
            for (int i = 0; i < cells.Count; i++) lookup[new Vector2Int(cells[i].x, cells[i].z)] = i;
            lookupDirty = false;
        }

        public bool TryGetCell(int x, int z, out PixelCell cell)
        {
            EnsureLookup();
            if (lookup.TryGetValue(new Vector2Int(x, z), out int index))
            {
                cell = cells[index];
                return true;
            }
            cell = default;
            return false;
        }

        public bool HasCell(int x, int z)
        {
            EnsureLookup();
            return lookup.ContainsKey(new Vector2Int(x, z));
        }

        /// <summary>Adds the cell or replaces the one at its x, z.</summary>
        public void SetCell(PixelCell cell)
        {
            EnsureLookup();
            var key = new Vector2Int(cell.x, cell.z);
            if (lookup.TryGetValue(key, out int index)) cells[index] = cell;
            else
            {
                lookup[key] = cells.Count;
                cells.Add(cell);
            }
            MarkCellDirty(cell.x, cell.z);
        }

        public bool RemoveCell(int x, int z)
        {
            EnsureLookup();
            var key = new Vector2Int(x, z);
            if (!lookup.TryGetValue(key, out int index)) return false;
            int last = cells.Count - 1;
            if (index != last)
            {
                cells[index] = cells[last];
                lookup[new Vector2Int(cells[index].x, cells[index].z)] = index;
            }
            cells.RemoveAt(last);
            lookup.Remove(key);
            MarkCellDirty(x, z);
            return true;
        }

        public void ClearAll()
        {
            cells.Clear();
            lookupDirty = true;
            MarkAllDirty();
        }

        public void MarkAllDirty() => allDirty = true;

        private Vector2Int ChunkOf(int x, int z) =>
            new(Mathf.FloorToInt(x / (float)chunkSize), Mathf.FloorToInt(z / (float)chunkSize));

        private void MarkCellDirty(int x, int z)
        {
            // The cell's own chunk plus any chunk a neighbour sits in (their walls depend on this cell).
            dirtyChunks.Add(ChunkOf(x, z));
            dirtyChunks.Add(ChunkOf(x + 1, z));
            dirtyChunks.Add(ChunkOf(x - 1, z));
            dirtyChunks.Add(ChunkOf(x, z + 1));
            dirtyChunks.Add(ChunkOf(x, z - 1));
        }

        // ------------------------------------------------------------------ surface

        /// <summary>Height of the top of a cell at (u, v) inside it (0..1 each), in levels.</summary>
        public float SurfaceHeight(in PixelCell cell, float u, float v)
        {
            if (cell.shape == PixelCellShape.Flat) return cell.height;
            float p = RiseAmount(cell.direction, u, v);
            if (cell.shape == PixelCellShape.Ramp) return cell.height + p;
            int n = Mathf.Max(2, stairSteps);
            return cell.height + Mathf.Min(n, Mathf.FloorToInt(p * n) + 1) / (float)n;
        }

        /// <summary>0 at the low edge of a ramp / stairs, 1 at the high edge.</summary>
        public static float RiseAmount(int direction, float u, float v) => direction switch
        {
            0 => v,
            1 => u,
            2 => 1f - v,
            _ => 1f - u
        };

        public static Vector2Int SideOffset(int side) => side switch
        {
            0 => new Vector2Int(0, 1),
            1 => new Vector2Int(1, 0),
            2 => new Vector2Int(0, -1),
            _ => new Vector2Int(-1, 0)
        };

        /// <summary>Local position of a point on a cell's top.</summary>
        public Vector3 CellTopLocal(in PixelCell cell, float u, float v) =>
            new((cell.x + u) * cellSize, SurfaceHeight(cell, u, v) * stepHeight, (cell.z + v) * cellSize);

        // ------------------------------------------------------------------ raycast

        /// <summary>
        /// First cell the ray hits (its top or one of its walls). Where there are no cells, the ray hits a flat
        /// plane at <paramref name="emptyHeight"/> (levels) and returns that empty cell.
        /// </summary>
        public bool Raycast(Ray worldRay, int emptyHeight, out PixelLevelHit hit, float maxDistance = 1000f)
        {
            hit = default;
            Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = transform.InverseTransformDirection(worldRay.direction).normalized;
            if (direction.sqrMagnitude < 1e-8f) return false;

            int minHeight = emptyHeight, maxHeight = emptyHeight;
            foreach (PixelCell c in cells)
            {
                minHeight = Mathf.Min(minHeight, Mathf.Min(c.height, baseHeight));
                maxHeight = Mathf.Max(maxHeight, c.height + 1);
            }
            float top = (maxHeight + 1) * stepHeight, bottom = (minHeight - 1) * stepHeight;

            // Start where the ray comes down to the top of the level.
            float start = 0f;
            if (origin.y > top && direction.y < 0f) start = (origin.y - top) / -direction.y;
            float step = Mathf.Min(cellSize, stepHeight) / 16f;
            float planeY = emptyHeight * stepHeight;

            Vector3 previous = origin + direction * start;
            Vector2Int previousCell = CellAt(previous);
            int maxSteps = Mathf.Min(40000, Mathf.CeilToInt(maxDistance / step));
            for (int i = 1; i < maxSteps; i++)
            {
                Vector3 p = origin + direction * (start + step * i);
                Vector2Int c = CellAt(p);
                if (TryGetCell(c.x, c.y, out PixelCell cell))
                {
                    float u = p.x / cellSize - c.x, v = p.z / cellSize - c.y;
                    if (p.y <= SurfaceHeight(cell, u, v) * stepHeight)
                    {
                        hit = new PixelLevelHit
                        {
                            cell = c,
                            exists = true,
                            side = previousCell == c ? -1 : SideTowards(c, previousCell, previous, cellSize),
                            localPoint = p
                        };
                        return true;
                    }
                }
                else if (previous.y > planeY && p.y <= planeY)
                {
                    hit = new PixelLevelHit { cell = c, exists = false, side = -1, localPoint = p };
                    return true;
                }
                if (p.y < bottom && direction.y <= 0f) break;
                previous = p;
                previousCell = c;
            }
            return false;
        }

        private Vector2Int CellAt(Vector3 local) =>
            new(Mathf.FloorToInt(local.x / cellSize), Mathf.FloorToInt(local.z / cellSize));

        private static int SideTowards(Vector2Int cell, Vector2Int from, Vector3 fromPoint, float size)
        {
            int dx = from.x - cell.x, dz = from.y - cell.y;
            if (dx != 0 && dz != 0)
            {
                // Diagonal step: pick the axis whose boundary is nearer to where the ray came from.
                float fx = fromPoint.x / size - (dx > 0 ? cell.x + 1 : cell.x);
                float fz = fromPoint.z / size - (dz > 0 ? cell.y + 1 : cell.y);
                if (Mathf.Abs(fx) > Mathf.Abs(fz)) dz = 0; else dx = 0;
            }
            if (dz > 0) return 0;
            if (dx > 0) return 1;
            if (dz < 0) return 2;
            return 3;
        }

        // ------------------------------------------------------------------ building

        public void RebuildDirty()
        {
            EnsureLookup();
            bool everything = allDirty;
            allDirty = false;
            var keys = new HashSet<Vector2Int>(dirtyChunks);
            dirtyChunks.Clear();

            // Group the cells of the chunks to build in one pass over the list.
            var groups = new Dictionary<Vector2Int, List<PixelCell>>();
            foreach (PixelCell c in cells)
            {
                Vector2Int key = ChunkOf(c.x, c.z);
                if (!everything && !keys.Contains(key)) continue;
                if (!groups.TryGetValue(key, out List<PixelCell> list)) groups[key] = list = new List<PixelCell>();
                list.Add(c);
            }
            if (everything)
            {
                keys = new HashSet<Vector2Int>(groups.Keys);
                foreach (Vector2Int key in chunks.Keys) keys.Add(key);
            }
            foreach (Vector2Int key in keys)
                BuildChunk(key, groups.TryGetValue(key, out List<PixelCell> list) ? list : null);
        }

        private void BuildChunk(Vector2Int key, List<PixelCell> inChunk)
        {
            if (inChunk == null || inChunk.Count == 0 || tileset == null || tileset.texture == null)
            {
                DestroyChunk(key);
                return;
            }

            var visual = new PixelMeshBuffer();
            var collision = colliders ? new PixelMeshBuffer() : null;
            PixelLevelMesher.Build(this, inChunk, visual, collision);

            if (!chunks.TryGetValue(key, out Chunk chunk) || chunk.gameObject == null)
            {
                chunk = new Chunk();
                chunks[key] = chunk;
                chunk.gameObject = new GameObject($"{ChunkPrefix} {key.x},{key.y}")
                {
                    hideFlags = HideFlags.DontSave | HideFlags.NotEditable
                };
                chunk.gameObject.transform.SetParent(transform, false);
                chunk.gameObject.AddComponent<MeshFilter>();
                chunk.gameObject.AddComponent<MeshRenderer>();
            }
            GameObject go = chunk.gameObject;
            go.layer = gameObject.layer;
            go.isStatic = false;

            if (chunk.mesh == null) chunk.mesh = new Mesh { name = "Pixel Level Chunk", hideFlags = HideFlags.DontSave };
            visual.ToMesh(chunk.mesh);
            go.GetComponent<MeshFilter>().sharedMesh = chunk.mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = tileset.GetMaterial();
            renderer.shadowCastingMode = castShadows;
            renderer.receiveShadows = receiveShadows;

            var meshCollider = go.GetComponent<MeshCollider>();
            if (collision != null && collision.VertexCount > 0)
            {
                if (chunk.collisionMesh == null)
                    chunk.collisionMesh = new Mesh { name = "Pixel Level Chunk Collision", hideFlags = HideFlags.DontSave };
                collision.ToMesh(chunk.collisionMesh);
                if (meshCollider == null) meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = chunk.collisionMesh;
            }
            else if (meshCollider != null)
            {
                DestroySafe(meshCollider);
            }
        }

        private void DestroyChunk(Vector2Int key)
        {
            if (!chunks.TryGetValue(key, out Chunk chunk)) return;
            DestroySafe(chunk.gameObject);
            DestroySafe(chunk.mesh);
            DestroySafe(chunk.collisionMesh);
            chunks.Remove(key);
        }

        private void DestroyChunks()
        {
            var keys = new List<Vector2Int>(chunks.Keys);
            foreach (Vector2Int key in keys) DestroyChunk(key);
            chunks.Clear();
        }

        // Chunks copied along when the level object was duplicated, or left from an older session.
        private void RemoveStrayChunks()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith(ChunkPrefix, StringComparison.Ordinal)) DestroySafe(child.gameObject);
            }
            chunks.Clear();
        }

        private static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void OnDrawGizmosSelected()
        {
            if (cells.Count > 0) return;
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0.5f, 0f, 0.5f) * cellSize, new Vector3(cellSize, 0f, cellSize));
        }
    }
}
