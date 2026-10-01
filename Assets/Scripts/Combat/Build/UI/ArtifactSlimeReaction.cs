using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RythmRPG.Combat
{
    /// <summary>A short, damped poke response. Layout and hit areas stay still beneath the shared panel mask.</summary>
    [DisallowMultipleComponent]
    public sealed class ArtifactSlimeReaction : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        [SerializeField] private ArtifactInterfaceStyle style;
        [SerializeField] private RectTransform motion;
        [SerializeField] private ArtifactGeometry[] outlines;
        [SerializeField] private ArtifactComponentMask componentMask;
        private bool focused;
        private bool reacting;
        private float startedAt;
        private int? noiseSeed;
        // UI variation must not consume the gameplay RNG. Stay in the exact integer range of shader floats.
        private static readonly System.Random noiseRandom = new();
        private const int SeedRange = 1 << 24;

        public RectTransform Motion => motion;
        public bool IsReacting => reacting;

        public static ArtifactSlimeReaction Ensure(RectTransform target)
        {
            if (target.TryGetComponent<ArtifactSlimeReaction>(out var existing)) return existing;
            var frames = new List<ArtifactGeometry>();
            foreach (var geometry in target.GetComponentsInChildren<ArtifactGeometry>(true))
                if (geometry.Form == ArtifactGeometry.Shape.Frame) frames.Add(geometry);
            var reaction = target.gameObject.AddComponent<ArtifactSlimeReaction>();
            reaction.style = ArtifactInterfaceStyle.Load();
            reaction.motion = target;
            reaction.outlines = frames.ToArray();
            foreach (var frame in frames) frame.EnableLivingOutline(reaction.style);
            reaction.componentMask = ArtifactComponentMask.Ensure(target);
            return reaction;
        }

        public void SetFocused(bool value)
        {
            if (focused == value) return;
            focused = value;
            if (value) Poke();
            else ResetMotion();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            var button = GetComponent<UnityEngine.UI.Selectable>();
            if (button == null || button.IsInteractable()) Poke();
        }

        public void OnSelect(BaseEventData eventData) => Poke();

        public void Poke()
        {
            if (style == null) style = ArtifactInterfaceStyle.Load();
            if (!style.slimeResponse || motion == null) return;
            if (style.pokeRandomizeSeed)
            {
                int next = noiseRandom.Next(SeedRange);
                if (next == (noiseSeed ?? style.pokeNoiseSeed)) next = (next + 1) % SeedRange;
                noiseSeed = next;
            }
            else noiseSeed = null;
            startedAt = Time.unscaledTime;
            reacting = true;
            Advance(0f);
        }

        private void LateUpdate()
        {
            if (!reacting) return;
            if (!style.slimeResponse) { ResetMotion(); return; }
            Advance(Time.unscaledTime - startedAt);
        }

        // Explicit elapsed time also supports deterministic preview and verification.
        public void Advance(float elapsed)
        {
            if (motion == null || style == null) return;
            float duration = Mathf.Max(.05f, style.wobbleSeconds);
            float t = Mathf.Clamp01(elapsed / duration);
            float envelope = (1f - t) * (1f - t);
            float breakSeconds = Mathf.Max(0f, style.pokeDisintegrateSeconds);
            float restoreSeconds = Mathf.Max(0f, style.pokeGlitchSeconds);
            float pulse = elapsed < breakSeconds ? Mathf.Clamp01(elapsed / breakSeconds)
                : restoreSeconds > 0f ? Mathf.Clamp01(1f - (elapsed - breakSeconds) / restoreSeconds) : 0f;
            pulse = pulse * pulse * (3f - 2f * pulse);
            float disintegrate = style.pokeEdgeDepthPixels > 0f ? pulse * style.pokeGlitchStrength : 0f;
            componentMask?.SetResponse(envelope, t * style.wobbleCycles, disintegrate, style, noiseSeed);
            foreach (var outline in outlines)
                if (outline != null) outline.SetEdgeResponse(envelope, t * style.wobbleCycles,
                    disintegrate, style, noiseSeed);
            if (elapsed >= Mathf.Max(duration, breakSeconds + restoreSeconds)) ResetMotion();
        }

        private void OnDisable() { focused = false; ResetMotion(); }

        private void ResetMotion()
        {
            reacting = false;
            componentMask?.SetResponse(0f, 0f, 0f, style, noiseSeed);
            if (outlines != null)
                foreach (var outline in outlines)
                    if (outline != null) outline.SetEdgeResponse(0f, 0f, 0f, style, noiseSeed);
        }
    }
}
