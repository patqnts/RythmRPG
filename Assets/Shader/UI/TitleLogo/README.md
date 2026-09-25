# Title Logo (TMP) — ink + gold burst, disintegrate into flakes

| Target | Component | Shader |
| --- | --- | --- |
| TextMeshPro label | **Title Logo Text** (`TitleLogoText.cs`) | RythmRPG/UI/Title Logo SDF (URP) |
| UI Image / RawImage | **Title Logo Image** (`TitleLogoImage.cs`) | RythmRPG/UI/Title Logo Sprite (URP) |

Both shaders share `TitleLogoCommon.hlsl`; both components share `TitleLogoBase.cs` (look, Disintegrate,
tweens, flakes). Scripts live in `Assets/Scripts/UI/Title/`.

Inspired by anime title cards like *Mushoku Tensei*: heavy black letters with a grungy gold
fill bursting out of one character, a hot core, and a thin swirl around it. Font and colors
are yours to change; nothing is baked into textures.

## Quick start

1. **GameObject > Rythm RPG > Title Logo (TMP)**. You get a Title and a Subtitle label under
   your Canvas (or a new 1920x1080 overlay Canvas), already set up.
2. On the **Title** TMP component, set your text and **Font Asset** (any TMP SDF font: a heavy serif
   or CJK font gives the anime-logo look; the DRT pixel fonts work with the Pixel Gold preset).
3. On **Title Logo Text**:
   - **Focus Character**: the letter the burst centers on (0-based, spaces count). The default of 2
     is the third character. Use -1 plus **Manual Focus** to place it anywhere.
   - **Look**: Ink, the Accent ramp (or tick *Derive Ramp From Accent* and pick one color),
     burst radius and breakup, grunge, core, swirl, pulse, shine, Pixelate.
   - Preset buttons: Ink + Gold Burst, Gold Gradient (subtitle), Crimson Burst, Pixel Gold.
4. Drag **Disintegrate** from 0 to 1 to preview the break-up in Edit Mode.

To add it to an existing label, add **Title Logo Text** to any TextMeshProUGUI.

## On a UI Image (sprite logo)

1. Select your Image and add **Title Logo Image** (or right-click the Image component > *Add Title Logo
   Effect*, or **GameObject > Rythm RPG > Title Logo (Image)** for a new one).
2. **Focus** is the burst center on the image (0,0 bottom-left, 1,1 top-right).
3. **Sprite** settings:
   - **Ink Replace**: 0 keeps the sprite's own colors outside the burst; 1 turns them into the flat Ink color.
   - **Keep Shading**: how much of the sprite's light/dark detail shows through the gold. Use 0 for a flat
     one-color logo.
   - **Match Sprite Pixels**: with Pixelate on, the grunge, dissolve and flakes snap to the sprite's own pixels.
   - **Outline Texels**: pixel outline around the art. Needs transparent padding around it (and atlas padding
     if the sprite is packed).
4. Presets: **Sprite Gold Burst** (painted logos) and **Pixel Sprite** (pixel art), plus the text presets.
   Presets keep Pixelate on if you turned it on.

Sizes on an Image are measured in *image heights*, transparent margin included. If your PNG has a big empty
border, either trim it (Sprite Editor) or use a smaller Burst Radius / Swirl Radius and a bigger Grunge Scale.

Turn on **Read/Write** in the texture's import settings so flakes skip transparent areas. It works without it,
just with invisible extra flakes. All Image types work (Simple, Sliced, Tiled, Filled, Use Sprite Mesh), and
RawImage too. Removing or disabling the component gives the Image its old material back.

## Disintegrate

The **Disintegrate** value (0–1) is the only thing to animate. The letters char dark, an ember
edge eats through them, and each piece that burns off peels away as a flake, drifting on the
wind and cooling from ember to ash. At 1 everything is gone, flakes included. Played backwards
(1 → 0), the flakes fly back in and the logo assembles.

```csharp
[SerializeField] TitleLogoText title;

void Start() => title.PlayAssemble();          // 1 -> 0, flakes fly in
void OnStartPressed() => title.PlayDisintegrate(); // 0 -> 1, returns a PrimeTween Tween

// Or drive it yourself (Animator, Timeline, PrimeTween, beat sync...):
title.Disintegrate = beatProgress;
```

Tick **Assemble On Enable** to play the intro automatically in Play Mode.

Disintegrate settings: **Direction** (0 = starts at the left, 90 = bottom, 180 = right),
**Directional vs Noise** (clean sweep vs random holes), noise scale and seed, ember / char band
widths and colors, and the flake grid, chance, lifetime, wind, spread, turbulence, spin, shrink.

Flakes are real chunks of the glyphs (same atlas, same finish), so ink breaks off as ink and gold
as gold. With **Pixelate** on they snap to the pixel grid and do not rotate.

## Changing font or colors from code

```csharp
title.SetFont(myFontAsset);             // any TMP SDF font
title.SetInkColor(new Color(0.05f, 0.02f, 0.1f));
title.SetAccent(new Color(0.3f, 0.8f, 1f)); // one color -> full shadow/mid/highlight ramp
title.ApplyLook(TitleLogoLook.CrimsonBurst());
title.SetFocusCharacter(0);

// Image version: same API, plus
logoImage.SetFocus(new Vector2(0.6f, 0.6f));
logoImage.Sprite.inkReplace = 1f;
```

After editing `title.Look` fields that change layout (Pixelate, pixel density, outline, glow),
call `title.RefreshLayout()`.

## Notes

- The component creates its own material instance (not saved), copies the font's atlas settings
  into it, and assigns it to the label. Your font's material is never modified. Removing the
  component restores the font's default material.
- Text: texture mapping is forced to **Paragraph** so one burst spans the whole line.
- Image: the component writes its own effect UV into TEXCOORD1 and turns that channel on for the Canvas.
- Flakes are drawn by a hidden, unsaved child (`Title Logo Flakes`). Dynamic font assets let it
  skip empty cells. Static atlases that aren't readable still work, with extra invisible quads.
- Glyphs coming from a **fallback** font get the finish and the dissolve but no flakes.
- Don't put the logo under a stencil **Mask** (TMP copies the material for stencils, so live edits
  won't reach it). **RectMask2D** works.
- Core, swirl and ember colors are HDR. For real bloom, use a Screen Space – Camera canvas with URP
  post-processing. Screen Space – Overlay has no post-processing.
- The noise lives in both `TitleLogoCommon.hlsl` and `TitleLogoNoise.cs` so flakes detach exactly where the
  logo dissolves. If you edit one, update the other.
- Shader animation (grunge flow, swirl spin, pulse) uses shader time: it runs in Play Mode and
  pauses with game time. In the Scene view it animates only with "Always Refresh" on.
