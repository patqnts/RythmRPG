using PixelCrushers.DialogueSystem;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Pixel "you can talk to this" arrow: a small bobbing triangle over the head of whatever the player's
    /// Proximity Selector (or raycast Selector) has selected. Hidden during conversations, pauses and when nothing
    /// is selected. Drawn in the <see cref="PixelDialogueSpace"/>, so it is as crisp as the bubbles.
    /// <para>
    /// Replaces the Dialogue System's "(spacebar to interact)" text: Tools > Rythm RPG > Dialogue > Use Pixel Bubble
    /// UI On Dialogue Manager turns that text off on the scene's selectors.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(30500)]
    public sealed class PixelUsableIndicator : MonoBehaviour
    {
        [Tooltip("The arrow image (its tip is the bottom-centre pixel).")]
        public RectTransform arrow;
        public RectTransform arrowShadow;
        public Vector2Int shadowOffset = new(0, -1);

        [Header("Placement (game pixels)")]
        [Tooltip("Gap between the head and the arrow's tip.")]
        [Min(0)] public int headGap = 4;
        [Tooltip("Height above the target's pivot when it has no renderers and no PixelSpeechAnchor.")]
        public float fallbackHeadHeight = 1.5f;

        [Header("Motion")]
        [Tooltip("Bob height in game pixels.")]
        [Min(0)] public int bobPixels = 2;
        [Tooltip("Bobs per second.")]
        [Min(0f)] public float bobSpeed = 1.4f;
        [Tooltip("When a new target is selected the arrow drops in from this many pixels higher.")]
        [Min(0)] public int dropInPixels = 5;
        [Min(0.01f)] public float dropInDuration = 0.14f;

        [Header("Selectors")]
        [Tooltip("Empty = found automatically (the scene's Proximity Selector / Selector).")]
        public ProximitySelector proximitySelector;
        public Selector selector;

        private PixelDialogueSpace space;
        private Usable shownTarget;
        private float shownSince;
        private float nextSearch;

        private void LateUpdate()
        {
            if (space == null) space = PixelDialogueSpace.For(this);
            Usable target = CurrentTarget();
            bool show = target != null && space != null && !GamePause.IsPaused
                        && !(DialogueManager.hasInstance && DialogueManager.isConversationActive);

            if (show && target != shownTarget)
            {
                shownTarget = target;
                shownSince = Time.unscaledTime;
            }
            if (!show) shownTarget = null;

            Vector2 local = default;
            if (show)
            {
                PixelSpeechAnchor.Points points = PixelSpeechAnchor.Resolve(target.transform, fallbackHeadHeight);
                show = space.TryWorldToLocal(points.Head, out local);
                local += points.HeadPixelOffset;
            }

            SetVisible(show);
            if (!show) return;

            float t = Time.unscaledTime - shownSince;
            float drop = t < dropInDuration ? Mathf.Round(dropInPixels * (1f - t / dropInDuration)) : 0f;
            float bob = bobSpeed > 0f ? Mathf.Round(bobPixels * 0.5f * (1f + Mathf.Sin(t * bobSpeed * Mathf.PI * 2f))) : 0f;

            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            // The arrow's tip is its bottom-centre pixel; keep the sprite on whole game pixels around it.
            int halfWidth = arrow != null ? Mathf.FloorToInt(arrow.sizeDelta.x * 0.5f) : 0;
            Vector2 tip = new(local.x, local.y + headGap + bob + drop);
            rect.anchoredPosition = space.SnapToScreenPixels(tip - new Vector2(halfWidth, 0f));
            Place(arrow, Vector2.zero);
            Place(arrowShadow, shadowOffset);
        }

        private Usable CurrentTarget()
        {
            if (proximitySelector == null && selector == null && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 1f;
                proximitySelector = FindAnyObjectByType<ProximitySelector>();
                if (proximitySelector == null) selector = FindAnyObjectByType<Selector>();
            }

            Usable usable = null;
            if (proximitySelector != null && proximitySelector.isActiveAndEnabled) usable = proximitySelector.CurrentUsable;
            else if (selector != null && selector.isActiveAndEnabled) usable = selector.CurrentUsable;
            if (usable == null || !usable.enabled || !usable.gameObject.activeInHierarchy) return null;
            return usable;
        }

        private void SetVisible(bool visible)
        {
            if (arrow != null && arrow.gameObject.activeSelf != visible) arrow.gameObject.SetActive(visible);
            if (arrowShadow != null && arrowShadow.gameObject.activeSelf != visible) arrowShadow.gameObject.SetActive(visible);
        }

        private static void Place(RectTransform target, Vector2 position)
        {
            if (target == null) return;
            target.anchorMin = target.anchorMax = target.pivot = Vector2.zero;
            target.anchoredPosition = position;
        }
    }
}
