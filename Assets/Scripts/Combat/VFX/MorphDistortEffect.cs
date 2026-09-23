using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Warps a simple UI graphic (a one-quad <see cref="Image"/>) through a grid: squash &amp; stretch along an axis and a
    /// rippling outline. Used by <see cref="KeyMarkerMorph"/> so the flying key-marker outlines bend and jiggle on their
    /// way into the ability frames. With no distortion set the quad is left untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MorphDistortEffect : BaseMeshEffect
    {
        private const int Grid = 12;

        private float wobble;
        private int lobes = 3;
        private float phase;
        private float axisX = 1f, axisY;
        private float stretch = 1f;

        private bool Idle => wobble <= 0.0001f && Mathf.Abs(stretch - 1f) <= 0.0001f;

        /// <param name="wobbleAmount">Ripple of the outline, as a fraction of its radius.</param>
        /// <param name="lobeCount">Number of bulges around the outline.</param>
        /// <param name="ripplePhase">Radians; animate it to make the ripple travel.</param>
        /// <param name="axis">Stretch direction in the graphic's local space.</param>
        /// <param name="stretchAmount">Scale along <paramref name="axis"/> (1 = none); the other axis gets the inverse.</param>
        public void Set(float wobbleAmount, int lobeCount, float ripplePhase, Vector2 axis, float stretchAmount)
        {
            bool wasIdle = Idle;
            float length = Mathf.Sqrt(axis.x * axis.x + axis.y * axis.y);
            wobble = Mathf.Max(0f, wobbleAmount);
            lobes = Mathf.Max(1, lobeCount);
            phase = ripplePhase;
            axisX = length > 0.0001f ? axis.x / length : 1f;
            axisY = length > 0.0001f ? axis.y / length : 0f;
            stretch = Mathf.Max(0.05f, stretchAmount);
            if ((!wasIdle || !Idle) && graphic != null) graphic.SetVerticesDirty();
        }

        public void Clear() => Set(0f, lobes, 0f, new Vector2(1f, 0f), 1f);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Idle || vh.currentVertCount != 4) return;

            // A simple Image quad: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right.
            UIVertex bottomLeft = default, topRight = default;
            vh.PopulateUIVertex(ref bottomLeft, 0);
            vh.PopulateUIVertex(ref topRight, 2);

            float x0 = bottomLeft.position.x, y0 = bottomLeft.position.y;
            float x1 = topRight.position.x, y1 = topRight.position.y;
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;

            vh.Clear();
            for (int j = 0; j <= Grid; j++)
            {
                float v = (float)j / Grid;
                for (int i = 0; i <= Grid; i++)
                {
                    float u = (float)i / Grid;
                    UIVertex vertex = bottomLeft;
                    Warp(Mathf.Lerp(x0, x1, u) - cx, Mathf.Lerp(y0, y1, v) - cy, out float dx, out float dy);
                    vertex.position = new Vector3(cx + dx, cy + dy, bottomLeft.position.z);
                    vertex.uv0 = new Vector4(Mathf.Lerp(bottomLeft.uv0.x, topRight.uv0.x, u),
                        Mathf.Lerp(bottomLeft.uv0.y, topRight.uv0.y, v), bottomLeft.uv0.z, bottomLeft.uv0.w);
                    vh.AddVert(vertex);
                }
            }

            int row = Grid + 1;
            for (int j = 0; j < Grid; j++)
            for (int i = 0; i < Grid; i++)
            {
                int a = j * row + i;
                vh.AddTriangle(a, a + row, a + row + 1);
                vh.AddTriangle(a + row + 1, a + 1, a);
            }
        }

        private void Warp(float x, float y, out float dx, out float dy)
        {
            dx = x;
            dy = y;
            if (wobble > 0.0001f && (x != 0f || y != 0f))
            {
                // Two travelling waves around the outline, so the ripple never looks like a plain rotation.
                float angle = Mathf.Atan2(y, x);
                float k = 1f + wobble * (Mathf.Sin(lobes * angle + phase) + 0.35f * Mathf.Sin((lobes + 2) * angle - phase * 1.7f));
                dx *= k;
                dy *= k;
            }
            if (Mathf.Abs(stretch - 1f) > 0.0001f)
            {
                float along = dx * axisX + dy * axisY;
                float across = -dx * axisY + dy * axisX;
                along *= stretch;
                across /= stretch; // keep the area, like squash & stretch
                dx = along * axisX - across * axisY;
                dy = along * axisY + across * axisX;
            }
        }
    }
}
