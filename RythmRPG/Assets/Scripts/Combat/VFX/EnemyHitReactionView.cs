using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class EnemyHitReactionView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Material whiteFlashMaterial;

        private Material originalMaterial;
        private Material runtimeMaterial;
        private Vector3 restingPosition;
        private bool canMoveVisualRoot;
        private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (visualRoot == null && spriteRenderer != null) visualRoot = spriteRenderer.transform;
            canMoveVisualRoot = visualRoot != null && visualRoot != transform;
            if (visualRoot != null) restingPosition = visualRoot.localPosition;
            if (whiteFlashMaterial == null) whiteFlashMaterial = Resources.Load<Material>("Combat/VFX/EnemyWhiteFlash");
            originalMaterial = spriteRenderer != null ? spriteRenderer.sharedMaterial : null;
        }

        public void Play(float duration, float strength)
        {
            if (spriteRenderer == null) return;
            StopAndRestore();
            if (whiteFlashMaterial != null)
            {
                runtimeMaterial = new Material(whiteFlashMaterial);
                spriteRenderer.sharedMaterial = runtimeMaterial;
                Tween.Custom(this, 0f, 1f, Mathf.Max(0.01f, duration * 0.5f),
                        (target, value) => target.SetFlash(value), Ease.OutQuad, cycles: 2, cycleMode: CycleMode.Yoyo)
                    .OnComplete(this, target => target.RestoreMaterial());
            }
            if (canMoveVisualRoot)
                Tween.ShakeLocalPosition(visualRoot, Vector3.one * Mathf.Max(0f, strength), Mathf.Max(0.01f, duration));
        }

        public void StopAndRestore()
        {
            Tween.StopAll(this);
            if (canMoveVisualRoot)
            {
                Tween.StopAll(visualRoot);
                visualRoot.localPosition = restingPosition;
            }
            RestoreMaterial();
        }

        private void SetFlash(float value)
        {
            if (runtimeMaterial != null) runtimeMaterial.SetFloat(FlashAmount, value);
        }

        private void RestoreMaterial()
        {
            if (spriteRenderer != null) spriteRenderer.sharedMaterial = originalMaterial;
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }

        private void OnDisable() => StopAndRestore();
    }
}
