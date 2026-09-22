using UnityEditor;
using UnityEngine;

namespace RythmRPG.WorldBuilder.Editor
{
    /// <summary>
    /// Scene view grid/chunk/brush overlays. Everything here draws in world space on the active
    /// elevation plane, so it stays correctly aligned however the Scene camera is currently oriented.
    /// </summary>
    public static class WorldBuilderGizmos
    {
        public static void DrawGrid(WorldBuilderWorld world, Vector3 focusPoint, int radiusInTiles)
        {
            WorldBuilderSettings settings = world.settings;
            float tileSize = Mathf.Max(0.01f, settings.tileWorldSize);
            float y = world.activeElevationLevel * settings.elevationIncrement + 0.002f;

            int centerX = Mathf.FloorToInt(focusPoint.x / tileSize);
            int centerZ = Mathf.FloorToInt(focusPoint.z / tileSize);

            Color previous = Handles.color;
            Handles.color = new Color(1f, 1f, 1f, 0.35f);

            for (int i = -radiusInTiles; i <= radiusInTiles; i++)
            {
                float x = (centerX + i) * tileSize;
                Handles.DrawLine(
                    new Vector3(x, y, (centerZ - radiusInTiles) * tileSize),
                    new Vector3(x, y, (centerZ + radiusInTiles) * tileSize));

                float z = (centerZ + i) * tileSize;
                Handles.DrawLine(
                    new Vector3((centerX - radiusInTiles) * tileSize, y, z),
                    new Vector3((centerX + radiusInTiles) * tileSize, y, z));
            }

            Handles.color = previous;
        }

        public static void DrawChunkBounds(WorldBuilderWorld world, Vector3 focusPoint, int radiusInChunks)
        {
            WorldBuilderSettings settings = world.settings;
            float tileSize = Mathf.Max(0.01f, settings.tileWorldSize);
            int chunkSize = Mathf.Max(1, settings.chunkSizeInTiles);
            float chunkWorldSize = tileSize * chunkSize;
            float y = world.activeElevationLevel * settings.elevationIncrement + 0.004f;

            int centerChunkX = Mathf.FloorToInt(focusPoint.x / chunkWorldSize);
            int centerChunkZ = Mathf.FloorToInt(focusPoint.z / chunkWorldSize);

            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.65f, 0.1f, 0.8f);

            for (int cx = centerChunkX - radiusInChunks; cx <= centerChunkX + radiusInChunks; cx++)
            {
                for (int cz = centerChunkZ - radiusInChunks; cz <= centerChunkZ + radiusInChunks; cz++)
                {
                    Vector3 min = new Vector3(cx * chunkWorldSize, y, cz * chunkWorldSize);
                    Vector3 max = new Vector3((cx + 1) * chunkWorldSize, y, (cz + 1) * chunkWorldSize);
                    Handles.DrawLine(new Vector3(min.x, y, min.z), new Vector3(max.x, y, min.z));
                    Handles.DrawLine(new Vector3(max.x, y, min.z), new Vector3(max.x, y, max.z));
                    Handles.DrawLine(new Vector3(max.x, y, max.z), new Vector3(min.x, y, max.z));
                    Handles.DrawLine(new Vector3(min.x, y, max.z), new Vector3(min.x, y, min.z));
                }
            }

            Handles.color = previous;
        }

        public static void DrawBrushPreview(TileCoord coord, float tileSize, int brushSize, float elevationY)
        {
            int half = brushSize / 2;
            float minX = (coord.x - half) * tileSize;
            float minZ = (coord.z - half) * tileSize;
            float maxX = minX + brushSize * tileSize;
            float maxZ = minZ + brushSize * tileSize;
            float y = elevationY + 0.006f;

            Vector3[] quad =
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ)
            };

            Handles.DrawSolidRectangleWithOutline(quad, new Color(0.3f, 0.85f, 1f, 0.25f), new Color(0.3f, 0.85f, 1f, 0.9f));
        }

        private static Vector3[] TileFootprintQuad(TileCoord coord, float tileSize, float y)
        {
            float minX = coord.x * tileSize;
            float minZ = coord.z * tileSize;
            return new[]
            {
                new Vector3(minX, y, minZ),
                new Vector3(minX + tileSize, y, minZ),
                new Vector3(minX + tileSize, y, minZ + tileSize),
                new Vector3(minX, y, minZ + tileSize)
            };
        }

        private static Vector3[] BoundsFootprintQuad(TileCoord min, TileCoord max, float tileSize, float y)
        {
            float minX = min.x * tileSize;
            float minZ = min.z * tileSize;
            float maxX = (max.x + 1) * tileSize;
            float maxZ = (max.z + 1) * tileSize;
            return new[]
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ)
            };
        }

        /// <summary>Rectangle tool drag preview: the exact footprint <see cref="WorldBuilderPaintController.ApplyRectangle"/> will paint.</summary>
        public static void DrawRectPreview(TileCoord a, TileCoord b, float tileSize, float elevationY)
        {
            TileCoord min = new TileCoord(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), a.elevationLevel);
            TileCoord max = new TileCoord(Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z), a.elevationLevel);
            Vector3[] quad = BoundsFootprintQuad(min, max, tileSize, elevationY + 0.006f);
            Handles.DrawSolidRectangleWithOutline(quad, new Color(1f, 0.85f, 0.2f, 0.22f), new Color(1f, 0.85f, 0.2f, 0.9f));
        }

        /// <summary>Line tool drag preview: draws every individual tile the Bresenham line will paint, so the preview matches the commit exactly.</summary>
        public static void DrawLinePreview(TileCoord a, TileCoord b, float tileSize, float elevationY)
        {
            float y = elevationY + 0.006f;
            Color fill = new Color(1f, 0.85f, 0.2f, 0.3f);
            Color outline = new Color(1f, 0.85f, 0.2f, 0.9f);

            foreach (TileCoord coord in TileGridUtility.Line(a, b))
            {
                Handles.DrawSolidRectangleWithOutline(TileFootprintQuad(coord, tileSize, y), fill, outline);
            }
        }

        /// <summary>Select tool's current or in-progress selection rectangle.</summary>
        public static void DrawSelectionOverlay(TileCoord min, TileCoord max, float tileSize, float elevationY)
        {
            Vector3[] quad = BoundsFootprintQuad(min, max, tileSize, elevationY + 0.008f);
            Handles.DrawSolidRectangleWithOutline(quad, new Color(0.35f, 1f, 0.4f, 0.16f), new Color(0.35f, 1f, 0.4f, 0.95f));
        }

        /// <summary>Stamp tool hover preview: every cell the stamp will place at anchor, with the current placement rotation/flip applied.</summary>
        public static void DrawStampPreview(TileStamp stamp, TileCoord anchor, int rotationSteps, TileFlip flip, float tileSize, float elevationY)
        {
            if (stamp == null) return;

            float y = elevationY + 0.006f;
            Color fill = new Color(0.7f, 0.4f, 1f, 0.28f);
            Color outline = new Color(0.7f, 0.4f, 1f, 0.9f);

            foreach (StampCell stampCell in stamp.cells)
            {
                (int dx, int dz) offset = TileTransformUtility.FlipOffset(stampCell.dx, stampCell.dz, flip);
                offset = TileTransformUtility.RotateOffset(offset.dx, offset.dz, rotationSteps);
                TileCoord coord = new TileCoord(anchor.x + offset.dx, anchor.z + offset.dz, anchor.elevationLevel);
                Handles.DrawSolidRectangleWithOutline(TileFootprintQuad(coord, tileSize, y), fill, outline);
            }
        }

        /// <summary>
        /// Object Placement tool hover preview (Phase 3): the prop's collision footprint (or a 1x1 world
        /// unit default if it has none/is disabled) centered on the raycast hit point, plus a vertical
        /// tick so its ground position reads clearly even though the actual prop quad may stand upright.
        /// </summary>
        public static void DrawPropPreview(PropDefinition definition, Vector3 worldPosition, float elevationY)
        {
            Vector2 footprint = definition != null && definition.collisionEnabled ? definition.footprintSize : Vector2.one;
            Vector2 offset = definition != null && definition.collisionEnabled ? definition.footprintOffset : Vector2.zero;
            float y = elevationY + 0.006f;

            float minX = worldPosition.x + offset.x - footprint.x * 0.5f;
            float maxX = worldPosition.x + offset.x + footprint.x * 0.5f;
            float minZ = worldPosition.z + offset.y - footprint.y * 0.5f;
            float maxZ = worldPosition.z + offset.y + footprint.y * 0.5f;

            Vector3[] quad =
            {
                new Vector3(minX, y, minZ),
                new Vector3(maxX, y, minZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(minX, y, maxZ)
            };

            Handles.DrawSolidRectangleWithOutline(quad, new Color(1f, 0.55f, 0.2f, 0.25f), new Color(1f, 0.55f, 0.2f, 0.9f));

            Color previous = Handles.color;
            Handles.color = new Color(1f, 0.55f, 0.2f, 0.9f);
            Handles.DrawLine(new Vector3(worldPosition.x, y, worldPosition.z), new Vector3(worldPosition.x, y + 1f, worldPosition.z));
            Handles.color = previous;
        }
    }
}
