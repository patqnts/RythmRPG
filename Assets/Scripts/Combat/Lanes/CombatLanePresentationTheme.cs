using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Persistent visual settings for the runtime-generated rhythm lane presentation.
    /// Edit the asset in Resources/Combat/UI to restyle the controls without changing code.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatLanePresentationTheme", menuName = "Rythm RPG/Combat/Lane Presentation Theme")]
    public sealed class CombatLanePresentationTheme : ScriptableObject
    {
        [Header("Button UI")]
        [Tooltip("Show the key buttons under the lanes. Off: they are hidden (they still hold the layout and the ability " +
                 "icons) and the Hit Line Key Markers show the keys instead.")]
        public bool ShowButtons;
        [FormerlySerializedAs("ButtonSprite")]
        public Sprite ButtonUnpressedSprite;
        public Sprite ButtonPressedSprite;
        public Image.Type ButtonImageType = Image.Type.Sliced;
        public Color ButtonColor = new(0.06f, 0.07f, 0.09f, 0.94f);
        public Vector2 ButtonSize = new(72f, 58f);
        [Min(0f)] public float ButtonSpacing = 24f;
        [Min(0f)] public float BottomOffset = 8f;
        [Tooltip("TextMeshPro font for the key labels. Empty = generated from Button Font (legacy), else TMP's default font.")]
        public TMP_FontAsset ButtonFontAsset;
        [Tooltip("Legacy uGUI font, only used to generate a TMP font when Button Font Asset is empty.")]
        public Font ButtonFont;
        [Min(1)] public int ButtonFontSize = 28;
        public FontStyle ButtonFontStyle = FontStyle.Bold;
        public Color ButtonTextColor = Color.white;
        [Tooltip("Key label outline (alpha 0 = none).")]
        public Color ButtonTextOutline = new(0f, 0f, 0f, 0.75f);

        [Header("Perfect Hit Line")]
        [Tooltip("Optional sprite. Leave empty to use the LineRenderer fallback.")]
        public Sprite LineSprite;
        public Material LineMaterial;
        public Color LineColor = Color.white;
        [Tooltip("Hit line thickness in pixels of a 270-row screen (1 = one pixel-art pixel = 4 screen pixels at 1080p). " +
                 "Applied live to the hit line the combat builds. Drawn crisp (Crisp World UI camera) any value works, e.g. " +
                 "0.25 = 1 screen pixel; inside the pixel render it is at least 3 so it does not flicker.")]
        [Min(0.05f)] public float LineThickness = 2f;
        [Min(0f)] public float LineGapAboveButtons = 8f;
        [Min(0f)] public float LineHorizontalPadding = 8f;
        public bool SnapToRenderTexturePixels = true;

        [Header("Hit Line Key Markers")]
        [Tooltip("An outline shape on the hit line for each lane, where its notes land. Lights up while the key is held " +
                 "and flashes in the judgement colour on a hit.")]
        public bool ShowKeyMarkers = true;
        [Tooltip("Draw the hit line in pieces between the markers, so it runs up to each outline instead of through it.")]
        public bool KeyMarkersBreakLine = true;
        public KeyMarkerShape KeyMarkerShape = KeyMarkerShape.Square;
        [Tooltip("Optional outline art. Overrides the shape (the pressed glow uses the same silhouette).")]
        public Sprite KeyMarkerSprite;
        [Tooltip("Marker size in pixels of a 270-row screen (same unit as Line Thickness). Shrinks automatically if the " +
                 "lanes are closer together.")]
        [Min(2f)] public float KeyMarkerSize = 20f;
        [Tooltip("Outline thickness in pixels of a 270-row screen. Square / Diamond only. Rounded to whole pixel-art " +
                 "pixels inside the pixel render; drawn crisp, fractions work (0.25 = 1 screen pixel at 1080p).")]
        [Min(0.05f)] public float KeyMarkerOutline = 1f;
        public Color KeyMarkerColor = new(1f, 1f, 1f, 0.85f);
        public Color KeyMarkerPressedColor = new(1f, 0.85f, 0.35f, 1f);
        [Tooltip("Fill shown inside the outline while the key is held.")]
        public Color KeyMarkerPressedFill = new(1f, 0.85f, 0.35f, 0.35f);
        [Range(1f, 1.6f)] public float KeyMarkerPressedScale = 1.15f;
        [Tooltip("Show the lane's key (A, S, D...) inside the marker. Follows rebinding.")]
        public bool ShowKeyMarkerLabels = true;
        public Color KeyMarkerLabelColor = new(1f, 1f, 1f, 0.9f);
        [Tooltip("Key label size relative to the marker.")]
        [Range(0.2f, 1f)] public float KeyMarkerLabelScale = 0.55f;

        [Header("Iridescence (Hit Line and Key Markers)")]
        [Tooltip("The white / grey parts of the hit line and key marker outlines shimmer through the colours below (the " +
                 "title logo's burn colours). Pressed, judgement flash and hold colours keep their own colour. The " +
                 "character's morph into the line uses the same colours.")]
        public bool Iridescent = true;
        [Range(0f, 1f)] public float IridescentStrength = 1f;
        [Tooltip("Ember colour.")]
        public Color IridescentEmber = new(0.9696f, 1f, 0.1255f, 1f);
        [Tooltip("Ember hot colour.")]
        public Color IridescentEmberHot = new(0.0613f, 1f, 0.1251f, 1f);
        [Tooltip("Ash colour.")]
        public Color IridescentAsh = new(1f, 0.7594f, 0.9603f, 1f);
        [Tooltip("Fourth colour of the cycle (the accent ramp's highlight).")]
        public Color IridescentAccent = new(0.42f, 1f, 0.51f, 1f);
        [Tooltip("How many times the colour cycle repeats across the screen.")]
        [Range(0f, 8f)] public float IridescentScale = 1.5f;
        [Tooltip("Cycles per second (negative = the other way).")]
        [Range(-4f, 4f)] public float IridescentSpeed = 0.35f;
        [Tooltip("Posterise the cycle into this many colour steps. 0 = smooth.")]
        [Range(0, 16)] public int IridescentSteps = 0;
        [Tooltip("Colours more saturated than this keep their own colour instead of shimmering (0.3 keeps the pressed " +
                 "yellow and the judgement flashes).")]
        [Range(0.05f, 1f)] public float IridescentKeepSaturated = 0.3f;

        [Header("Key Marker Charge (Stationary Notes)")]
        [Tooltip("Show a Stationary note's charge on its lane's key marker, in the marker's own shape: an outline " +
                 "shrinks onto the marker (Stationary), or the marker fills up from the centre (Stationary Hold). " +
                 "Both reach the marker exactly at the perfect hit time.")]
        public bool ShowStationaryCharge = true;
        [Tooltip("Size of the shrinking outline when the charge starts, relative to the marker.")]
        [Range(1.1f, 4f)] public float ChargeOutlineStartScale = 2.2f;
        public Color ChargeOutlineColor = new(1f, 1f, 1f, 0.9f);
        [Tooltip("Colour of the fill that grows inside the marker (Stationary Hold).")]
        public Color ChargeFillColor = new(0.55f, 0.85f, 1f, 0.65f);
        [Tooltip("Part of the charge (0-0.5) over which the outline and the fill fade in.")]
        [Range(0f, 0.5f)] public float ChargeFadeIn = 0.15f;

        [Header("Key Marker Hold (Hold / Stationary Hold notes)")]
        [Tooltip("While a hold note is held, its lane's key marker fills with the note's colour (the note's Marker Fill " +
                 "Color, or automatically the projectile's own colour).")]
        public bool ShowHoldFill = true;
        [Range(0f, 1f)] public float HoldFillAlpha = 0.8f;
        [Tooltip("How much the fill breathes while held (scale).")]
        [Range(0f, 0.3f)] public float HoldFillPulse = 0.06f;
        [Tooltip("How much the marker outline takes on the hold colour while held.")]
        [Range(0f, 1f)] public float HoldOutlineTint = 0.6f;

        [Header("Perfect Hit Line End Caps")]
        [Tooltip("Art added past both ends of the hit line (the line itself is unchanged). Drawn for the LEFT end, facing outward; the right end uses a mirrored copy. The sprite's pivot row is lined up with the middle of the line, and its right edge touches the line's end. Empty = no caps.")]
        public Sprite LineCapSprite;
        [Tooltip("How many sprite pixels tall the bar is where the cap meets the line. The cap is scaled so that bar is exactly as thick as the line (3 for the default dragon cap, so 1 sprite pixel = 1 screen pixel).")]
        [Min(0.01f)] public float LineCapJoinHeight = 3f;
        [Tooltip("Tint of the caps. Alpha 0 = same color as the line.")]
        public Color LineCapColor = new(1f, 1f, 1f, 0f);
        [Tooltip("Sprite pixels the caps are pulled in over the line ends (negative leaves a gap).")]
        public float LineCapOverlap;
    }
}
