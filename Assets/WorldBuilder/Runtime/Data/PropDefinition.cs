using System;
using UnityEngine;

namespace RythmRPG.WorldBuilder
{
    /// <summary>
    /// One placeable prop: a sprite plus how it should be built and behave when placed -- its default
    /// orientation preset, its footprint for collision, and whether it blocks movement. Mirrors
    /// <see cref="TileDefinition"/>'s stable-id pattern (props are referenced by <see cref="PropId"/>,
    /// not asset index or path, so palettes can be reordered without breaking already-placed instances)
    /// and its sprite-driven creation flow (see
    /// <c>WorldBuilderAssetFactory.CreatePropDefinitionFromSprite</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "World Builder/Prop Definition", fileName = "NewPropDefinition")]
    public class PropDefinition : ScriptableObject
    {
        [SerializeField] private string propId;

        public string displayName = "Prop";
        public string category = "Uncategorized";
        public Sprite sprite;

        [Header("Orientation")]
        public PropOrientationMode defaultOrientation = PropOrientationMode.Vertical;
        [Tooltip("Used only when Default Orientation is Custom.")]
        public Vector3 customEulerAngles;

        [Header("Placement")]
        [Tooltip("World units per sprite pixel. 0 = derive it from the world's own Tile Pixel Size / " +
                 "Tile World Size ratio, so props sit at the same scale as the ground grid by default.")]
        public float pixelsPerWorldUnitOverride;
        [Tooltip("Lifts the prop's pivot this many world units above the ground plane it was placed on -- " +
                 "for a sprite whose pivot isn't already at its visual base (most Vertical/Billboard props " +
                 "want their sprite's pivot set to bottom-center; this is a manual escape hatch, not a " +
                 "substitute for that).")]
        public float verticalPivotOffset;

        [Header("Gameplay")]
        [Tooltip("Adds a BoxCollider sized from Footprint Size so this prop blocks movement/other physics.")]
        public bool collisionEnabled = true;
        [Tooltip("XZ footprint size in world units for the generated BoxCollider.")]
        public Vector2 footprintSize = Vector2.one;
        [Tooltip("XZ offset of the footprint center from the placement point, in world units.")]
        public Vector2 footprintOffset;
        [Tooltip("Footprint collider height in world units.")]
        public float footprintHeight = 1f;

        public string[] tags = Array.Empty<string>();

        public string PropId
        {
            get
            {
                if (string.IsNullOrEmpty(propId)) propId = Guid.NewGuid().ToString("N");
                return propId;
            }
        }

        private void Reset()
        {
            if (string.IsNullOrEmpty(propId)) propId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(displayName)) displayName = name;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(propId)) propId = Guid.NewGuid().ToString("N");
        }
    }
}
