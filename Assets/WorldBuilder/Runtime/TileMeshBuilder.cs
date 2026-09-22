using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Builds the visual mesh (multi-submesh, one per unique source texture) and a separate collision
    /// mesh for one chunk, from tile data. This is the only place that applies projection compensation,
    /// and it applies the same per-tile depth (Z) axis factor to both meshes: the collision mesh's
    /// footprint always matches whatever is actually drawn on screen, so a character standing on visible
    /// ground is always standing on a real collider, whether or not that tile is compensated. (An
    /// earlier version kept collision at the uncompensated logical size for "clean" grid math, but this
    /// project's ground movement is a real physics-driven CharacterController that stands directly on
    /// these colliders, not tile-locked movement -- so a smaller collider than the art it sits under is
    /// a real gameplay bug, not just a cosmetic mismatch.)
    ///
    /// Compensation is applied as a per-tile depth (Z) axis scale measured from the world origin (not
    /// each tile's own center), so that a run of adjacently-compensated tiles remains seamless; mixing
    /// compensated and uncompensated tiles next to each other will show a seam -- now in the collider
    /// too, matching the visual seam -- which is an inherent and expected consequence of the two texture
    /// conventions having different intended footprints (see WorldBuilderWindow's World Preview section
    /// for a way to compare the two).
    /// </summary>
    public static class TileMeshBuilder
    {
        public struct ChunkMeshResult
        {
            public Mesh visualMesh;
            public Material[] visualMaterials;
            public Mesh collisionMesh;
            public bool hasCollision;
        }

        private sealed class SubmeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();

            /// <summary>
            /// Winds the two triangles as (a, c, b) and (a, d, c) rather than the more common (a, b, c) /
            /// (a, c, d) -- for a quad laid flat on the XZ plane in Unity's left-handed coordinate system
            /// with a,b,c,d going around in the order used everywhere in this file (00, 10, 11, 01), that
            /// "reversed" order is the one that produces an upward-facing (+Y) normal. Getting this backwards
            /// produces a mesh that is geometrically identical but renders back-face-culled (invisible) from
            /// any camera looking down at the ground, which is every camera this tool is built for -- so
            /// this winding is load-bearing, not stylistic.
            /// </summary>
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);
                uvs.Add(uvA);
                uvs.Add(uvB);
                uvs.Add(uvC);
                uvs.Add(uvD);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }

        public static ChunkMeshResult BuildChunk(
            WorldBuilderWorld world,
            IReadOnlyList<TileLayerData> layers,
            int chunkX,
            int chunkZ,
            int elevationLevel)
        {
            WorldBuilderSettings settings = world.settings;
            TilePalette palette = world.palette;
            int chunkSize = Mathf.Max(1, settings.chunkSizeInTiles);
            float tileSize = Mathf.Max(0.0001f, settings.tileWorldSize);
            float baseElevationY = elevationLevel * settings.elevationIncrement;
            float defaultFactor = settings.projectionCompensationEnabled ? settings.DefaultCompensationFactor : 1f;

            Dictionary<Material, SubmeshBuilder> visualBuilders = new Dictionary<Material, SubmeshBuilder>();
            SubmeshBuilder collisionBuilder = new SubmeshBuilder();
            bool anyCollision = false;

            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                TileLayerData layer = layers[layerIndex];
                if (layer == null || !layer.visible) continue;

                // Tiny per-layer Y offset avoids Z-fighting when two layers paint the same footprint
                // at the same elevation (e.g. a decoration layer over a ground layer). Purely visual.
                float layerVisualOffset = layerIndex * 0.0005f;

                foreach (TileCoord coord in layer.CoordsInChunk(chunkX, chunkZ, chunkSize, elevationLevel))
                {
                    if (!layer.TryGetTile(coord, out TileCellData cell) || cell.IsEmpty) continue;

                    TileDefinition tile = palette != null ? palette.FindById(cell.tileId) : null;
                    if (tile == null || tile.sprite == null) continue;

                    float factor = settings.projectionCompensationEnabled
                        ? tile.ResolveCompensationFactor(defaultFactor)
                        : 1f;

                    float logicalX0 = coord.x * tileSize;
                    float logicalX1 = logicalX0 + tileSize;
                    float logicalZ0 = coord.z * tileSize;
                    float logicalZ1 = logicalZ0 + tileSize;

                    float visualZ0 = logicalZ0 * factor;
                    float visualZ1 = logicalZ1 * factor;
                    float visualY = baseElevationY + layerVisualOffset;

                    Vector3 v00 = new Vector3(logicalX0, visualY, visualZ0);
                    Vector3 v10 = new Vector3(logicalX1, visualY, visualZ0);
                    Vector3 v11 = new Vector3(logicalX1, visualY, visualZ1);
                    Vector3 v01 = new Vector3(logicalX0, visualY, visualZ1);

                    GetSpriteUV(tile.sprite, out Vector2 uvMin, out Vector2 uvMax);
                    Vector2 uv00 = new Vector2(uvMin.x, uvMin.y);
                    Vector2 uv10 = new Vector2(uvMax.x, uvMin.y);
                    Vector2 uv11 = new Vector2(uvMax.x, uvMax.y);
                    Vector2 uv01 = new Vector2(uvMin.x, uvMax.y);

                    ApplyFlip(cell.flip, ref uv00, ref uv10, ref uv11, ref uv01);
                    RotateUVs(cell.rotationSteps, ref uv00, ref uv10, ref uv11, ref uv01);

                    Material material = TileMaterialCache.GetMaterial(tile.sprite.texture, settings.tileShaderOverride);
                    if (material == null) continue;

                    if (!visualBuilders.TryGetValue(material, out SubmeshBuilder builder))
                    {
                        builder = new SubmeshBuilder();
                        visualBuilders[material] = builder;
                    }

                    builder.AddQuad(v00, v10, v11, v01, uv00, uv10, uv11, uv01);

                    if (tile.collisionEnabled)
                    {
                        anyCollision = true;
                        // Same X/Z footprint as the visual quad above (visualZ0/visualZ1, the
                        // compensation-scaled depth), at the tile's true elevation with no per-layer
                        // visual offset -- the collider should sit exactly under the art, not under the
                        // art's tiny anti-z-fighting nudge.
                        Vector3 c00 = new Vector3(logicalX0, baseElevationY, visualZ0);
                        Vector3 c10 = new Vector3(logicalX1, baseElevationY, visualZ0);
                        Vector3 c11 = new Vector3(logicalX1, baseElevationY, visualZ1);
                        Vector3 c01 = new Vector3(logicalX0, baseElevationY, visualZ1);
                        collisionBuilder.AddQuad(c00, c10, c11, c01, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                    }
                }
            }

            ChunkMeshResult result = new ChunkMeshResult();

            if (visualBuilders.Count > 0)
            {
                result.visualMesh = AssembleMesh(visualBuilders, out Material[] materials);
                result.visualMaterials = materials;
            }

            if (anyCollision && settings.generateGroundCollision)
            {
                result.collisionMesh = AssembleSingleMesh(collisionBuilder);
                result.hasCollision = true;
            }

            return result;
        }

        private static Mesh AssembleMesh(Dictionary<Material, SubmeshBuilder> builders, out Material[] materials)
        {
            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };

            List<Vector3> allVertices = new List<Vector3>();
            List<Vector2> allUVs = new List<Vector2>();
            List<Material> materialList = new List<Material>();
            List<int[]> submeshTriangles = new List<int[]>();

            foreach (KeyValuePair<Material, SubmeshBuilder> kvp in builders)
            {
                int offset = allVertices.Count;
                allVertices.AddRange(kvp.Value.vertices);
                allUVs.AddRange(kvp.Value.uvs);

                int[] tris = new int[kvp.Value.triangles.Count];
                for (int i = 0; i < tris.Length; i++) tris[i] = kvp.Value.triangles[i] + offset;

                submeshTriangles.Add(tris);
                materialList.Add(kvp.Key);
            }

            mesh.SetVertices(allVertices);
            mesh.SetUVs(0, allUVs);
            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(submeshTriangles[i], i);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            materials = materialList.ToArray();
            return mesh;
        }

        private static Mesh AssembleSingleMesh(SubmeshBuilder builder)
        {
            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(builder.vertices);
            mesh.SetTriangles(builder.triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // This mesh is built procedurally (never goes through the asset import pipeline), so Unity
            // does not automatically pre-bake its triangle collision data the way an imported mesh asset
            // would. A MeshCollider referencing an unbaked mesh still works today via an implicit
            // just-in-time bake, but logs the "missing pre-baked triangle collision data" warning and
            // Unity's own docs say that implicit fallback goes away in a future version -- so bake
            // explicitly right after the mesh data is finalized, once per rebuild, non-convex (this is a
            // flat, open ground surface, not a closed convex shape).
            // Physics.BakeMesh(int, bool) and Object.GetInstanceID() are both hard-obsoleted (CS0619,
            // a compile error not just a warning) on this project's Unity version in favor of the
            // EntityId-based overload -- GetEntityId() takes GetInstanceID()'s place here.
            Physics.BakeMesh(mesh.GetEntityId(), false);

            return mesh;
        }

        private static void GetSpriteUV(Sprite sprite, out Vector2 uvMin, out Vector2 uvMax)
        {
            Rect textureRect = sprite.textureRect;
            Texture texture = sprite.texture;
            float texWidth = Mathf.Max(1, texture.width);
            float texHeight = Mathf.Max(1, texture.height);
            uvMin = new Vector2(textureRect.xMin / texWidth, textureRect.yMin / texHeight);
            uvMax = new Vector2(textureRect.xMax / texWidth, textureRect.yMax / texHeight);
        }

        private static void ApplyFlip(TileFlip flip, ref Vector2 uv00, ref Vector2 uv10, ref Vector2 uv11, ref Vector2 uv01)
        {
            if ((flip & TileFlip.Horizontal) != 0)
            {
                (uv00, uv10) = (uv10, uv00);
                (uv01, uv11) = (uv11, uv01);
            }

            if ((flip & TileFlip.Vertical) != 0)
            {
                (uv00, uv01) = (uv01, uv00);
                (uv10, uv11) = (uv11, uv10);
            }
        }

        private static void RotateUVs(int steps, ref Vector2 uv00, ref Vector2 uv10, ref Vector2 uv11, ref Vector2 uv01)
        {
            steps = ((steps % 4) + 4) % 4;
            for (int i = 0; i < steps; i++)
            {
                Vector2 temp = uv00;
                uv00 = uv01;
                uv01 = uv11;
                uv11 = uv10;
                uv10 = temp;
            }
        }
    }
}
