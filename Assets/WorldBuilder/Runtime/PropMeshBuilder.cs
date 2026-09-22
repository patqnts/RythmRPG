using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// Builds one prop's flat quad mesh (vertices in the local XY plane, Z = 0) and resolves the local
    /// rotation for its <see cref="PropOrientationMode"/>. Deliberately does not bake orientation into
    /// the mesh itself -- rotation is a plain transform rotation, the same as this project's existing
    /// by-hand convention in <c>Assets/Scripts/Editor/ScreenAlignmentTools.cs</c> ("Face Main Camera" /
    /// "Lay Flat on XZ" / "Level World Axes"), just computed automatically from a
    /// <see cref="PropDefinition"/> instead of a manual menu click per object. Keeping the two separate
    /// also means re-orienting an already-placed prop (e.g. switching Vertical to Horizontal) never
    /// requires rebuilding its mesh or UVs, only its transform.
    /// </summary>
    public static class PropMeshBuilder
    {
        /// <summary>
        /// Builds the quad mesh for one prop instance. The quad's pivot (from <see cref="Sprite.pivot"/>)
        /// sits at local origin, with <paramref name="definition"/>'s vertical pivot offset applied on
        /// the local Y axis -- the same axis that becomes world Y once the resolved orientation rotation
        /// is applied to a Vertical or Billboard prop (Horizontal instead lays that axis onto world Z,
        /// matching "Lay Flat on XZ"'s own Euler(-90,0,0) convention exactly).
        /// </summary>
        public static Mesh BuildQuadMesh(PropDefinition definition, WorldBuilderSettings settings)
        {
            if (definition == null || definition.sprite == null) return null;

            Sprite sprite = definition.sprite;
            float pixelsPerUnit = ResolvePixelsPerWorldUnit(definition, settings);
            if (pixelsPerUnit <= 0.0001f) return null;

            Rect rect = sprite.rect;
            float worldWidth = rect.width / pixelsPerUnit;
            float worldHeight = rect.height / pixelsPerUnit;

            // Sprite.pivot is in pixel units relative to the sprite's own rect; normalize it to a 0..1
            // fraction so it scales correctly regardless of pixelsPerUnit.
            float pivotX = rect.width > 0.0001f ? sprite.pivot.x / rect.width : 0.5f;
            float pivotY = rect.height > 0.0001f ? sprite.pivot.y / rect.height : 0f;

            float x0 = -pivotX * worldWidth;
            float x1 = (1f - pivotX) * worldWidth;
            float y0 = (-pivotY * worldHeight) + definition.verticalPivotOffset;
            float y1 = ((1f - pivotY) * worldHeight) + definition.verticalPivotOffset;

            Vector3 v00 = new Vector3(x0, y0, 0f);
            Vector3 v10 = new Vector3(x1, y0, 0f);
            Vector3 v11 = new Vector3(x1, y1, 0f);
            Vector3 v01 = new Vector3(x0, y1, 0f);

            GetSpriteUV(sprite, out Vector2 uvMin, out Vector2 uvMax);
            Vector2 uv00 = new Vector2(uvMin.x, uvMin.y);
            Vector2 uv10 = new Vector2(uvMax.x, uvMin.y);
            Vector2 uv11 = new Vector2(uvMax.x, uvMax.y);
            Vector2 uv01 = new Vector2(uvMin.x, uvMax.y);

            Mesh mesh = new Mesh { indexFormat = IndexFormat.UInt16, name = $"Prop_{sprite.name}" };
            mesh.SetVertices(new[] { v00, v10, v11, v01 });
            mesh.SetUVs(0, new[] { uv00, uv10, uv11, uv01 });
            // Same winding as TileMeshBuilder.SubmeshBuilder.AddQuad: (0,2,1)/(0,3,2), which is the one
            // that produces an outward (+Z, before orientation is applied) normal for corners ordered
            // 00,10,11,01 in this coordinate convention -- see that file's comment for the full
            // winding-order rationale. Getting this backwards means the prop face-culls invisible from
            // the front, the exact bug that made ground chunks disappear before it was fixed there.
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Pixels-per-world-unit to size this prop's quad by: the definition's own override if set,
        /// otherwise derived from the world's ground-tile scale (EffectiveTilePixelSize / tileWorldSize)
        /// so props default to sitting at the same authored scale as the ground grid.
        /// </summary>
        public static float ResolvePixelsPerWorldUnit(PropDefinition definition, WorldBuilderSettings settings)
        {
            if (definition.pixelsPerWorldUnitOverride > 0.0001f) return definition.pixelsPerWorldUnitOverride;
            if (settings == null) return 1f;
            return settings.EffectiveTilePixelSize / Mathf.Max(0.0001f, settings.tileWorldSize);
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
    }
}
