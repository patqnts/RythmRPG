using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// One-shot slime burst (the pop, the charge release): turns its particle systems to face the camera on spawn,
    /// so droplets splash out in the screen plane, and takes the ability's colour. Destroys itself when done.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlimeBurst : MonoBehaviour, ICastEffectTint
    {
        [SerializeField] private ParticleSystem[] systems = new ParticleSystem[0];
        [Tooltip("How much the ability's accent colour replaces the authored colours.")]
        [SerializeField, Range(0f, 1f)] private float tintAmount = 0.6f;
        [SerializeField, Min(0.1f)] private float lifetime = 1.5f;

        private Color[] authored;

        private void Awake()
        {
            transform.rotation = SlimeVfx.Facing(SlimeVfx.ViewCamera());
            authored = SlimeVfx.CaptureStartColors(systems);
            Destroy(gameObject, lifetime);
        }

        public void ApplyTint(Color accent)
        {
            if (authored == null) authored = SlimeVfx.CaptureStartColors(systems);
            SlimeVfx.Tint(systems, authored, accent, tintAmount);
        }
    }
}
