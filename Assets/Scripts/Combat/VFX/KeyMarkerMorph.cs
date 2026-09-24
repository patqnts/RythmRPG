using System;
using System.Collections.Generic;
using RythmRPG.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// "Ghost" outlines that fly between a hit-line key marker and an ability frame above the player, warping from the
    /// marker's shape (square / diamond / circle) into the frame's circle on the way, so the lane key reads as the
    /// ability's slot. Owned and ticked by <see cref="AbilitySelectionStage"/>; drawn on its own world-space canvas.
    /// </summary>
    public sealed class KeyMarkerMorph
    {
        /// <summary>One end of a morph: where, how big, how thick the outline, how round, angle and colour.</summary>
        public struct Pose
        {
            public Vector3 Position;
            public float Size;       // world units, outer width
            public float Outline;    // world units
            public float Roundness;  // 0 = square, 1 = circle
            public float Angle;      // degrees around the view axis
            public Color Color;
        }

        /// <summary>How much the ghosts bend in flight (from the Combat VFX Theme). All zero = rigid.</summary>
        public struct Distortion
        {
            public float Stretch;      // squash & stretch strength
            public float Wobble;       // outline ripple, fraction of the radius
            public int Lobes;          // bulges around the outline
            public float WobbleSpeed;  // radians per second the ripple travels
            public bool Active => Stretch > 0f || Wobble > 0f;
        }

        private sealed class Ghost
        {
            public RectTransform Rect;
            public Image Image;
            public MorphDistortEffect Distort;
            public float Seed;
            public TMP_Text Label;
            public Func<Pose> From;
            public Func<Pose> To;
            public float Delay;
            public float Duration;
            public float Elapsed;
            public bool LabelFadesIn;
            public float LabelAlpha;
        }

        private const float CanvasScale = 0.01f;
        private const float EndFadeSeconds = 0.12f;

        private readonly Canvas canvas;
        private readonly RectTransform canvasRect;
        private readonly List<Ghost> ghosts = new();

        public bool IsActive => ghosts.Count > 0;

        public KeyMarkerMorph(Transform parent, int sortingOrder)
        {
            var go = new GameObject("Key Marker Morph Canvas", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = sortingOrder;
            canvasRect = (RectTransform)go.transform;
            canvasRect.sizeDelta = Vector2.one;
            canvasRect.localScale = Vector3.one * CanvasScale;
            CrispWorldUI.Apply(go);
        }

        /// <summary>Starts a ghost after <paramref name="delay"/> seconds. Poses are read every frame (ends may move).</summary>
        public void Add(Func<Pose> from, Func<Pose> to, float delay, float duration, string label, bool labelFadesIn)
        {
            var go = new GameObject("Morph Ghost", typeof(RectTransform));
            go.layer = canvasRect.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvasRect, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            MorphDistortEffect distort = go.AddComponent<MorphDistortEffect>();

            TMP_Text text = CombatText.CreateUGUI("Key", rect, null, 10f, Color.white, TextAlignmentOptions.Center,
                new Color(0f, 0f, 0f, 0.8f));
            text.fontStyle = FontStyles.Bold;
            text.text = label ?? string.Empty;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;

            var ghost = new Ghost
            {
                Rect = rect, Image = image, Distort = distort, Seed = ghosts.Count * 2.3f, Label = text, From = from, To = to,
                Delay = Mathf.Max(0f, delay), Duration = Mathf.Max(0.01f, duration), LabelFadesIn = labelFadesIn
            };
            ghosts.Add(ghost);
            Apply(ghost, 0f, 1f); // visible from the first frame, exactly on the start pose
            go.SetActive(ghost.Delay <= 0f);
        }

        public void Clear()
        {
            foreach (Ghost ghost in ghosts)
                if (ghost.Rect != null) UnityEngine.Object.Destroy(ghost.Rect.gameObject);
            ghosts.Clear();
        }

        /// <summary>Advances the ghosts. <paramref name="view"/> is the rendering camera (the canvas faces it).</summary>
        public void Tick(float deltaTime, Camera view, float arcHeight, Distortion distortion = default)
        {
            if (ghosts.Count == 0) return;
            if (view != null)
            {
                canvasRect.rotation = view.transform.rotation;
                if (canvas.worldCamera != view) canvas.worldCamera = view;
            }

            for (int i = ghosts.Count - 1; i >= 0; i--)
            {
                Ghost ghost = ghosts[i];
                if (ghost.Rect == null)
                {
                    ghosts.RemoveAt(i);
                    continue;
                }
                if (ghost.Delay > 0f)
                {
                    ghost.Delay -= deltaTime;
                    // Hold on the start pose until it is this ghost's turn (the marker it replaces is already hidden).
                    if (!ghost.Rect.gameObject.activeSelf) ghost.Rect.gameObject.SetActive(true);
                    Apply(ghost, 0f, 1f, view, arcHeight, default);
                    continue;
                }

                ghost.Elapsed += deltaTime;
                float t = Mathf.Clamp01(ghost.Elapsed / ghost.Duration);
                float fade = ghost.Elapsed <= ghost.Duration
                    ? 1f
                    : 1f - Mathf.Clamp01((ghost.Elapsed - ghost.Duration) / EndFadeSeconds);
                Apply(ghost, t, fade, view, arcHeight, distortion);
                if (fade <= 0f)
                {
                    UnityEngine.Object.Destroy(ghost.Rect.gameObject);
                    ghosts.RemoveAt(i);
                }
            }
        }

        private void Apply(Ghost ghost, float t, float alpha, Camera view = null, float arcHeight = 0f,
            Distortion distortion = default)
        {
            Pose from = ghost.From();
            Pose to = ghost.To();
            float move = EaseInOutCubic(t);
            // The shape warps a little later than it moves, so it leaves as the marker and arrives as the frame.
            float warp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.75f));

            Vector3 position = FlightPosition(from, to, t, view, arcHeight);
            float size = Mathf.Lerp(from.Size, to.Size, move);
            float outline = Mathf.Lerp(from.Outline, to.Outline, move);
            float roundness = Mathf.Lerp(from.Roundness, to.Roundness, warp);
            float angle = Mathf.LerpAngle(from.Angle, to.Angle, warp);
            Color color = Color.Lerp(from.Color, to.Color, warp);
            color.a *= alpha;

            ghost.Rect.localPosition = canvasRect.InverseTransformPoint(position);
            ghost.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            float canvasSize = size / Mathf.Max(0.000001f, Mathf.Abs(canvasRect.lossyScale.x));
            ghost.Rect.sizeDelta = new Vector2(canvasSize, canvasSize);

            float band = size > 0.0001f ? outline / (size * 0.5f) : 0.1f;
            ghost.Image.sprite = RoundedOutlineSprites.Get(roundness, band);
            ghost.Image.color = color;
            Distort(ghost, from, to, t, angle, view, arcHeight, distortion);

            if (ghost.Label != null)
            {
                // The key letter leaves with the marker (or arrives with it on the way back).
                float labelAlpha = ghost.LabelFadesIn ? Mathf.Clamp01((t - 0.5f) * 2f) : 1f - Mathf.Clamp01(t * 2f);
                Color labelColor = ghost.Label.color;
                labelColor.a = labelAlpha * alpha;
                ghost.Label.color = labelColor;
                ghost.Label.fontSize = canvasSize * 0.55f;
                ghost.Label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle); // keep the letter upright
            }
        }

        private static Vector3 FlightPosition(Pose from, Pose to, float t, Camera view, float arcHeight)
        {
            Vector3 position = Vector3.Lerp(from.Position, to.Position, EaseInOutCubic(t));
            if (view != null && arcHeight != 0f) position += view.transform.up * (arcHeight * Mathf.Sin(t * Mathf.PI));
            return position;
        }

        // Squash & stretch along the flight plus a rippling outline. Zero at both ends, so the ghost still leaves as
        // the exact marker and lands as the exact frame.
        private void Distort(Ghost ghost, Pose from, Pose to, float t, float angle, Camera view, float arcHeight,
            Distortion distortion)
        {
            if (ghost.Distort == null) return;
            if (!distortion.Active || t <= 0f || t >= 1f)
            {
                ghost.Distort.Clear();
                return;
            }

            // Flight direction in the ghost's own (rotated) space.
            Vector3 ahead = canvasRect.InverseTransformPoint(FlightPosition(from, to, Mathf.Min(1f, t + 0.02f), view, arcHeight));
            Vector3 behind = canvasRect.InverseTransformPoint(FlightPosition(from, to, Mathf.Max(0f, t - 0.02f), view, arcHeight));
            float dx = ahead.x - behind.x, dy = ahead.y - behind.y;
            float radians = -angle * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            var axis = new Vector2(cos * dx - sin * dy, sin * dx + cos * dy);

            // Stretches with speed; squashes as it springs off the line and again as it lands in the frame.
            float speed = EaseInOutCubicSlope(t) / 3f;
            float squash = 0.7f * (Bump(t, 0f, 0.18f) + Bump(t, 0.8f, 1f));
            float stretch = Mathf.Max(0.4f, 1f + distortion.Stretch * (speed - squash));

            float wobble = distortion.Wobble * Mathf.Sin(t * Mathf.PI);
            float phase = ghost.Elapsed * distortion.WobbleSpeed + ghost.Seed;
            ghost.Distort.Set(wobble, distortion.Lobes, phase, axis, stretch);
        }

        private static float Bump(float t, float start, float end) =>
            t <= start || t >= end ? 0f : Mathf.Sin((t - start) / (end - start) * Mathf.PI);

        private static float EaseInOutCubic(float t) =>
            t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;

        private static float EaseInOutCubicSlope(float t) =>
            t < 0.5f ? 12f * t * t : 3f * (-2f * t + 2f) * (-2f * t + 2f);
    }

    /// <summary>Generated outline sprites between a square (roundness 0) and a circle (roundness 1), cached.</summary>
    public static class RoundedOutlineSprites
    {
        private const int Size = 96;
        private const int RoundSteps = 16;
        private const int BandSteps = 40;
        private static readonly Dictionary<int, Sprite> cache = new();

        public static Sprite Get(float roundness, float band)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(roundness) * RoundSteps), 0, RoundSteps);
            int b = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(band) * BandSteps), 1, BandSteps);
            int key = r * 1000 + b;
            if (cache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            sprite = Make((float)r / RoundSteps, (float)b / BandSteps);
            cache[key] = sprite;
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => cache.Clear();

        // Signed distance to a rounded square; the outline is the band just inside its edge.
        private static Sprite Make(float roundness, float band)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = $"Rounded Outline {roundness:0.00}/{band:0.00}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float half = Size * 0.5f - 1f;
            float radius = roundness * half;
            float bandPixels = Mathf.Max(1f, band * half);
            float center = (Size - 1) * 0.5f;
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float qx = Mathf.Abs(x - center) - (half - radius);
                float qy = Mathf.Abs(y - center) - (half - radius);
                float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + bandPixels + 0.5f);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
