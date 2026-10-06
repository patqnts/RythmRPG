using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Core
{
    /// <summary>Explicitly marks this GameObject's renderer as a ground-reflection receiving plane or mesh.</summary>
    [AddComponentMenu("RythmRPG/Rendering/Ground Reflection Receiver")]
    [DisallowMultipleComponent]
    public sealed class GroundReflectionReceiver : MonoBehaviour
    {
        private static readonly HashSet<GroundReflectionReceiver> Active = new();
        private static GroundReflectionReceiver[] snapshot = System.Array.Empty<GroundReflectionReceiver>();
        private static bool snapshotDirty = true;

        [Tooltip("The exact floor Plane or mesh Renderer that receives reflections. A child Renderer is allowed.")]
        [SerializeField] private Renderer targetRenderer;

        public Renderer TargetRenderer
        {
            get
            {
                if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
                return targetRenderer;
            }
        }

        public float PlaneHeight => TargetRenderer != null ? TargetRenderer.transform.position.y : transform.position.y;

        public int SubmeshCount
        {
            get
            {
                Renderer target = TargetRenderer;
                if (target != null && target.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    return Mathf.Max(1, filter.sharedMesh.subMeshCount);
                if (target is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                    return Mathf.Max(1, skinned.sharedMesh.subMeshCount);
                return 1;
            }
        }

        private void OnEnable()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                Debug.LogWarning(
                    "Ground Reflection Receiver needs its Target Renderer assigned. Child Renderers are supported.",
                    this);
            }

            Active.Add(this);
            snapshotDirty = true;
        }

        private void OnValidate()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            snapshotDirty = true;
        }

        private void OnDisable()
        {
            Active.Remove(this);
            snapshotDirty = true;
        }

        public static GroundReflectionReceiver[] ActiveSnapshot()
        {
            if (!snapshotDirty) return snapshot;
            Active.RemoveWhere(receiver => receiver == null);
            snapshot = new GroundReflectionReceiver[Active.Count];
            Active.CopyTo(snapshot);
            snapshotDirty = false;
            return snapshot;
        }
    }
}
