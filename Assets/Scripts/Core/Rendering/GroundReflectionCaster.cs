using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>
    /// Explicitly reflects this GameObject's MeshRenderer or SpriteRenderer onto one selected receiver.
    /// </summary>
    [AddComponentMenu("RythmRPG/Rendering/Ground Reflection Caster")]
    [DisallowMultipleComponent]
    public sealed class GroundReflectionCaster : MonoBehaviour
    {
        [Tooltip("The exact MeshRenderer or SpriteRenderer to reflect. A child Renderer is allowed.")]
        [SerializeField] private Renderer sourceRenderer;

        [Tooltip("The specific plane or mesh that receives this reflection. No assignment means no reflection.")]
        [SerializeField] private GroundReflectionReceiver receiver;

        [Range(0f, 2f)] public float strength = 1f;
        [Min(0f)] public float maxLengthOverride;

        public Renderer SourceRenderer
        {
            get
            {
                if (sourceRenderer == null) sourceRenderer = GetComponent<Renderer>();
                return sourceRenderer;
            }
        }

        public GroundReflectionReceiver Receiver => receiver;

        private void OnValidate()
        {
            if (sourceRenderer == null) sourceRenderer = GetComponent<Renderer>();
        }
    }
}
