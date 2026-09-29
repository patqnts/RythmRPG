using System;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>Which side of the character a bubble sits on.</summary>
    public enum BubbleSide
    {
        /// <summary>Above the head, tail pointing down.</summary>
        Above,
        /// <summary>Below the feet, tail pointing up (talking to someone higher up the screen).</summary>
        Below,
    }

    /// <summary>How a panel picks the bubble's side.</summary>
    public enum BubblePlacement
    {
        /// <summary>Below when the listener is higher on screen than the speaker, otherwise above.</summary>
        Auto,
        AlwaysAbove,
        AlwaysBelow,
    }

    /// <summary>
    /// The shell of a speech bubble: body (9-sliced), tail, their drop shadows, placement above or below a character
    /// and the pop animation. Shared by <see cref="PixelBubbleSubtitlePanel"/> and <see cref="PixelChoiceMenuPanel"/>.
    /// <para>
    /// Layout (all in game pixels): <see cref="root"/> sits on the tail's tip. Above: the tail hangs below the body and
    /// its top row covers the body's bottom outline. Below: the tail sticks up from the body and its bottom row covers
    /// the body's top outline. The body's content is laid out by the panel that owns the frame.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class PixelBubbleFrame
    {
        [Tooltip("Moves with the speaker; its origin is the tail tip. Scaled for the pop animation.")]
        public RectTransform root;
        public RectTransform body;
        public RectTransform bodyShadow;
        public RectTransform tail;
        public RectTransform tailShadow;

        [Header("Tail (game pixels)")]
        [Tooltip("Size of the tail sprites.")]
        public Vector2Int tailSize = new(7, 6);
        [Tooltip("Tail used when the bubble is above the character (points down, tip = bottom-left pixel).")]
        public Sprite tailDownSprite;
        [Tooltip("Tail used when the bubble is below the character (points up, tip = top-left pixel).")]
        public Sprite tailUpSprite;
        [Tooltip("Preferred distance from the body's left edge to the tail.")]
        public int tailInset = 8;
        [Tooltip("The tail never gets closer than this to the body's corners.")]
        public int tailCornerClearance = 3;

        [Header("Placement (game pixels)")]
        [Tooltip("Gap between the character's head and the tail tip (bubble above).")]
        public Vector2Int headGap = new(0, 3);
        [Tooltip("Gap between the character's feet and the tail tip (bubble below).")]
        public Vector2Int feetGap = new(0, 2);
        [Tooltip("Auto: the listener must be at least this many game pixels higher on screen for the bubble to go below.")]
        [Min(0)] public int belowThreshold = 6;
        [Tooltip("Bubbles stay this far inside the screen edges.")]
        public int screenMargin = 4;
        [Tooltip("Drop shadow offset.")]
        public Vector2Int shadowOffset = new(0, -1);

        [Header("Pop")]
        [Min(0f)] public float popDuration = 0.16f;
        [Range(0.1f, 1f)] public float popFromScale = 0.6f;

        private Vector2Int bodySize;
        private float popStartTime = -100f;
        private Vector2 lastHead;
        private Vector2 lastFeet;
        private bool hasTarget;

        public Vector2Int BodySize => bodySize;

        /// <summary>The side the bubble was actually drawn on last (after the on-screen fallback).</summary>
        public BubbleSide LastSide { get; private set; }

        /// <summary>
        /// Picks a side for a line: below the speaker when the listener stands higher on screen (so the bubble points
        /// away from them and covers neither of the two), otherwise above.
        /// </summary>
        public BubbleSide ChooseSide(PixelDialogueSpace space, BubblePlacement placement, Transform speaker,
            Transform listener, float fallbackHeight)
        {
            switch (placement)
            {
                case BubblePlacement.AlwaysAbove: return BubbleSide.Above;
                case BubblePlacement.AlwaysBelow: return BubbleSide.Below;
            }
            if (space == null || speaker == null || listener == null || listener == speaker) return BubbleSide.Above;
            if (listener.IsChildOf(speaker) || speaker.IsChildOf(listener)) return BubbleSide.Above;
            PixelSpeechAnchor.Points a = PixelSpeechAnchor.Resolve(speaker, fallbackHeight);
            PixelSpeechAnchor.Points b = PixelSpeechAnchor.Resolve(listener, fallbackHeight);
            if (!space.TryWorldToLocal(a.Feet, out Vector2 speakerFeet)) return BubbleSide.Above;
            if (!space.TryWorldToLocal(b.Feet, out Vector2 listenerFeet)) return BubbleSide.Above;
            return listenerFeet.y > speakerFeet.y + belowThreshold ? BubbleSide.Below : BubbleSide.Above;
        }

        public void Pop() => popStartTime = Time.unscaledTime;

        /// <summary>Keeps the 9-slice borders at 1 texel per game pixel, whatever the canvas' reference PPU.</summary>
        public void FixSlicedPixelsPerUnit()
        {
            PixelDialogueStyle.FixSlicedPixelsPerUnit(body);
            PixelDialogueStyle.FixSlicedPixelsPerUnit(bodyShadow);
        }

        /// <summary>Sets the body's size (the panel has already laid out what is inside it).</summary>
        public void SetBodySize(Vector2Int size)
        {
            // Wide enough that the tail always fits, mirrored or not.
            bodySize = new Vector2Int(Mathf.Max(size.x, 2 * (tailSize.x + tailCornerClearance)), Mathf.Max(size.y, 8));
            if (body != null) body.sizeDelta = bodySize;
            if (bodyShadow != null) bodyShadow.sizeDelta = bodySize;
        }

        /// <summary>Forget the last tracked character position (next placement starts fresh).</summary>
        public void ForgetTarget() => hasTarget = false;

        /// <summary>
        /// Places the bubble on <paramref name="side"/> of <paramref name="character"/>. When the character cannot be
        /// projected this frame (or is null) the last known position is kept.
        /// </summary>
        public void PlaceOn(PixelDialogueSpace space, Transform character, float fallbackHeight, BubbleSide side)
        {
            if (space == null) return;
            if (character != null)
            {
                PixelSpeechAnchor.Points points = PixelSpeechAnchor.Resolve(character, fallbackHeight);
                if (space.TryWorldToLocal(points.Head, out Vector2 head) && space.TryWorldToLocal(points.Feet, out Vector2 feet))
                {
                    lastHead = head + points.HeadPixelOffset;
                    lastFeet = feet + points.FeetPixelOffset;
                    hasTarget = true;
                }
            }
            if (!hasTarget)
            {
                lastHead = lastFeet = new Vector2(space.Size.x * 0.5f, space.Size.y * 0.3f);
            }
            Place(space, lastHead, lastFeet, side);
        }

        /// <summary>
        /// Places the bubble on <paramref name="side"/> of a character whose head and feet are at
        /// <paramref name="head"/> / <paramref name="feet"/> (points in <paramref name="space"/>): kept on screen
        /// (switching sides when the wanted one does not fit), snapped to screen pixels, with the pop scale applied.
        /// </summary>
        public void Place(PixelDialogueSpace space, Vector2 head, Vector2 feet, BubbleSide side)
        {
            if (root == null || space == null) return;
            Vector2 size = space.Size;
            float margin = screenMargin;

            // Space the body + tail take above / below the tip.
            int heightAboveTip = tailSize.y - 1 + bodySize.y; // bubble above: tip at the bottom
            int depthBelowTip = tailSize.y - 2 + bodySize.y;  // bubble below: tip at the top
            Vector2 aboveTip = head + (Vector2)headGap;
            Vector2 belowTip = feet - (Vector2)feetGap;
            bool fitsAbove = aboveTip.y + heightAboveTip <= size.y - margin;
            bool fitsBelow = belowTip.y - depthBelowTip >= margin;
            if (side == BubbleSide.Above && !fitsAbove && fitsBelow) side = BubbleSide.Below;
            else if (side == BubbleSide.Below && !fitsBelow && fitsAbove) side = BubbleSide.Above;
            LastSide = side;

            Vector2 tip;
            if (side == BubbleSide.Above)
            {
                tip = aboveTip;
                tip.y = Mathf.Clamp(tip.y, margin, Mathf.Max(margin, size.y - margin - heightAboveTip));
            }
            else
            {
                tip = belowTip;
                tip.y = Mathf.Clamp(tip.y, Mathf.Min(margin + depthBelowTip, size.y - margin), size.y - margin);
            }
            // Horizontal: the tip itself must stay where a body can still be drawn around it.
            tip.x = Mathf.Clamp(tip.x, margin + tailCornerClearance,
                Mathf.Max(margin + tailCornerClearance, size.x - margin - tailCornerClearance - 1f));
            tip = space.SnapToScreenPixels(tip);

            // inset = body's left edge -> the tail tip pixel, in whole game pixels (so the body shares the tip's grid).
            int minInset = tailCornerClearance;
            int maxInset = bodySize.x - tailCornerClearance - 1;
            int inset = Mathf.Clamp(tailInset, minInset, bodySize.x - tailCornerClearance - tailSize.x);
            if (tip.x - inset + bodySize.x > size.x - margin) inset = Mathf.CeilToInt(tip.x + bodySize.x - (size.x - margin));
            if (tip.x - inset < margin) inset = Mathf.FloorToInt(tip.x - margin);
            inset = Mathf.Clamp(inset, minInset, maxInset);

            // The tail leans left; in the body's right half it is mirrored (leans right).
            bool mirrored = inset * 2 > bodySize.x;

            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.sizeDelta = Vector2.zero;
            root.anchoredPosition = tip;

            Vector2 bodyPos = side == BubbleSide.Above
                ? new Vector2(-inset, tailSize.y - 1)
                : new Vector2(-inset, -depthBelowTip);
            SetRect(body, bodyPos);
            SetRect(bodyShadow, bodyPos + shadowOffset);

            float tailY = side == BubbleSide.Above ? 0f : -(tailSize.y - 1);
            Vector2 tailPos = new(mirrored ? 1f : 0f, tailY);
            Vector3 tailScale = mirrored ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            Sprite tailSprite = side == BubbleSide.Above ? tailDownSprite : tailUpSprite;
            PlaceTail(tail, tailPos, tailScale, tailSprite);
            PlaceTail(tailShadow, tailPos + shadowOffset, tailScale, tailSprite);

            root.localScale = Vector3.one * PopScale();
        }

        private void PlaceTail(RectTransform rect, Vector2 position, Vector3 scale, Sprite sprite)
        {
            if (rect == null) return;
            SetRect(rect, position);
            rect.sizeDelta = tailSize;
            rect.localScale = scale;
            if (sprite != null && rect.TryGetComponent(out Image image) && image.sprite != sprite) image.sprite = sprite;
        }

        private float PopScale()
        {
            if (popDuration <= 0f) return 1f;
            float t = (Time.unscaledTime - popStartTime) / popDuration;
            if (t >= 1f) return 1f;
            if (t <= 0f) return popFromScale;
            // Ease out back: a small overshoot, like a bubble inflating.
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            float eased = 1f + c3 * u * u * u + c1 * u * u;
            return Mathf.LerpUnclamped(popFromScale, 1f, eased);
        }

        private static void SetRect(RectTransform rect, Vector2 position)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
        }
    }
}
