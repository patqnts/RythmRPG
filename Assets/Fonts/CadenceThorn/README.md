# Cadence Thorn

A fantasy display font created for Rhythm RPG, inspired by the pointed terminals,
flared strokes, almond-shaped counters, and sweeping curves in the supplied lettering
references. The glyphs are original vector drawings, not extracted outlines from the
reference images or modifications of another font.

## Files

- **CadenceThorn-Regular.ttf** — installable, usable TrueType font.
- **Cadence Thorn SDF.asset** — ready-to-use static TextMeshPro font.
- **Materials/** — six matching rank presets: SS iridescent, S gold, A emerald,
  B sapphire, C violet, and D crimson.
- **Cadence Thorn Rank Preview.prefab** — separate sample Canvas showing the presets.
- **Source~/build_font.py** — editable glyph outlines and reproducible font builder.
- **Source~/specimen.py** — renders a specimen using the actual TrueType font.

The font supports A–Z, a–z, 0–9, all printable ASCII punctuation, a nonbreaking space,
en/em dashes, curly quotes, bullet, ellipsis, multiplication, and minus: 106 characters.
It includes 21 optical kerning pairs. Accented letters and other writing systems are
not included in this first character set.

## Unity

For ordinary text, assign **Cadence Thorn SDF** to the TMP component's Font Asset.
The default font material gives you plain, tintable text. Use Normal style: the
designed glyphs already have substantial display weight.

For rank effects, also choose a **Cadence Thorn SDF - [rank] [finish]** material.
Set horizontal and vertical Texture Mapping to Paragraph for a continuous effect
across multiple letters such as SS. Ensure the Canvas has TexCoord1 enabled under
Additional Shader Channels; the supplied preview is already configured.

Use Extra Padding when increasing the outline or glow. The six rank materials use
the existing **RythmRPG/UI/Rank Text SDF (URP)** shader and share this font's atlas.
The original Book Antiqua rank materials retain their original font atlas.

The TMP atlas is static SDF16, 90-point sampling, 9-pixel padding, and 1024 x 1024.
Best suited to titles, combat ranks, and short display labels. The font's spacing
supports ordinary typed words; the overlapping composition of the reference logos
is custom lettering and is not automatically reproduced in every word.

No scene, existing font, or combat result component is reassigned by this asset set.

## Rebuild

Use Python 3 with `fonttools` and `skia-pathops`, then run:

```text
python build_font.py
```

The source folder ends in `~` so Unity excludes the build tools from asset import.
The builder writes the TTF into the parent folder and verifies full printable ASCII
coverage. Rebuild the TMP atlas in Unity after changing the TTF outlines.

Pillow is required only for `specimen.py`.
