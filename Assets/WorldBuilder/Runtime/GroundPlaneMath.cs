using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Camera-aware world/grid math shared by runtime and editor code. Every conversion here goes
    /// through a proper ray-plane intersection or an explicit camera-space projection -- Scene view
    /// screen coordinates are never assumed to map 1:1 onto XZ world coordinates, since the World
    /// Builder's camera is tilted rather than looking straight down.
    /// </summary>
    public static class GroundPlaneMath
    {
        /// <summary>
        /// Intersects a ray with the horizontal plane Y = planeY. Returns false for rays parallel to
        /// the plane or that only intersect it behind the ray origin.
        /// </summary>
        public static bool RayPlaneIntersect(Ray ray, float planeY, out Vector3 point)
        {
            float denom = ray.direction.y;
            if (Mathf.Abs(denom) < 1e-6f)
            {
                point = default;
                return false;
            }

            float t = (planeY - ray.origin.y) / denom;
            if (t < 0f)
            {
                point = default;
                return false;
            }

            point = ray.origin + ray.direction * t;
            return true;
        }

        public static TileCoord WorldToTileCoord(Vector3 worldPosition, float tileWorldSize, int elevationLevel)
        {
            int x = Mathf.FloorToInt(worldPosition.x / Mathf.Max(0.0001f, tileWorldSize));
            int z = Mathf.FloorToInt(worldPosition.z / Mathf.Max(0.0001f, tileWorldSize));
            return new TileCoord(x, z, elevationLevel);
        }

        public static Vector3 TileCoordToWorldCenter(TileCoord coord, float tileWorldSize, float elevationIncrement)
        {
            float x = (coord.x + 0.5f) * tileWorldSize;
            float z = (coord.z + 0.5f) * tileWorldSize;
            float y = coord.elevationLevel * elevationIncrement;
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Rounds a world position onto the logical tile grid (world-space units). This is a placement
        /// convenience, not a screen-pixel-accurate snap -- use <see cref="CameraPixelSnap"/> when true
        /// pixel-perfect alignment under the tilted camera is required.
        /// </summary>
        public static Vector3 GridSnap(Vector3 worldPosition, float tileWorldSize)
        {
            if (tileWorldSize <= 0f) return worldPosition;
            worldPosition.x = Mathf.Round(worldPosition.x / tileWorldSize) * tileWorldSize;
            worldPosition.z = Mathf.Round(worldPosition.z / tileWorldSize) * tileWorldSize;
            return worldPosition;
        }

        /// <summary>
        /// Snaps a world position onto the camera's own screen-space pixel grid rather than the world
        /// axes. This is the mathematically correct way to keep placement pixel-perfect under an
        /// arbitrarily rotated orthographic camera: world-space X/Y/Z snapping does not guarantee
        /// pixel-perfect screen-space results once the camera is tilted, because world axes and screen
        /// axes are no longer aligned. Only the camera's right/up screen axes are snapped; the
        /// depth-along-view component is left untouched so this never nudges an object's ground plane.
        /// </summary>
        public static Vector3 CameraPixelSnap(Vector3 worldPosition, Camera camera, int renderTargetHeightPixels)
        {
            if (camera == null || !camera.orthographic || renderTargetHeightPixels <= 0) return worldPosition;

            float unitsPerPixel = (2f * camera.orthographicSize) / renderTargetHeightPixels;
            if (unitsPerPixel <= 0f) return worldPosition;

            Transform camTransform = camera.transform;
            Vector3 relative = worldPosition - camTransform.position;

            float right = Vector3.Dot(relative, camTransform.right);
            float up = Vector3.Dot(relative, camTransform.up);
            float forward = Vector3.Dot(relative, camTransform.forward);

            right = Mathf.Round(right / unitsPerPixel) * unitsPerPixel;
            up = Mathf.Round(up / unitsPerPixel) * unitsPerPixel;

            return camTransform.position + camTransform.right * right + camTransform.up * up + camTransform.forward * forward;
        }
    }
}
