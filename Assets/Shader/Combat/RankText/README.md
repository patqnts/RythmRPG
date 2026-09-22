# Combat rank text materials

Shader: **RythmRPG/UI/Rank Text SDF (URP)**, in `../RankTextSDF.shader`.

These are standalone TextMeshPro assets. The combat grading rules, result-screen
scripts, and development scene were not changed or connected to these materials.
The current result screen still uses `UnityEngine.UI.Text`; importing TMP does not
convert that component. Integrating the presets there requires a separate rank-label
migration to TMP and selection of the appropriate material when the result is shown.

## Included presets

| Rank | Material finish | Motion |
| --- | --- | --- |
| SS | Iridescent pearl | Flowing rainbow bands, moving highlight, subtle edge glow |
| S | Gold | Metallic bands, moving highlight, subtle edge glow |
| A | Emerald | Green shading, moving highlight, subtle edge glow |
| B | Sapphire | Blue shading and a softer moving highlight |
| C | Violet | Violet gradient and a very subtle moving highlight |
| D | Crimson | Muted red gradient, static |

All six materials share **Combat Rank SDF**, a static 1024 x 1024 SDF16 font atlas
baked from the project's existing Book Antiqua font (`BKANT.TTF`). It includes
printable ASCII, including all six ranks. The source font and imported TMP assets
are unchanged. The preview uses Liberation Sans for its explanatory labels only.

## Use on a TMP label

1. Assign **Combat Rank SDF** to the TMP component's **Font Asset**.
2. Choose the matching **Combat Rank SDF - [rank] [finish]** in **Material Preset**.
3. In TMP's extra settings, set horizontal and vertical **Texture Mapping** to
   **Paragraph**. This makes both letters of SS share a continuous finish. The
   default Character mapping repeats the effect on each glyph instead.
4. Enable **Extra Padding** if increasing the outline or glow. Ensure the containing
   Canvas supplies **TexCoord1** under Additional Shader Channels (the preview does).

The material defaults ignore text vertex RGB so a previous grade's tint cannot
muddy the rainbow. Text alpha and CanvasGroup alpha still fade the entire effect.
Set **Use Text Vertex RGB** to 1 if intentional text/rich-text coloring should tint it.

Double-click **Rank Text Preview.prefab** to inspect the six samples in Prefab Mode.
For animation, place it in a separate test scene and enter Play mode. It contains
an overlay Canvas and no combat scripts or input handlers. It is not added to the
development scene or build scene list.

## Adjust the finish

- **Gradient Low / High** set the two base colors; **Metallic Bands** adds bands.
- **Iridescence**, **Iridescence Speed**, and **Iridescence Bands** control SS's spectrum.
- **Pearl Highlight** softens the spectrum toward white.
- **Effect Scale / Offset** control the texture mapping without depending on screen position.
- **Shine Strength / Speed / Width / Angle** control the sweeping highlight.
- **Outline Thickness** and **Enable Edge Glow** control the silhouette.

The iridescence is a stylized animated thin-film appearance designed for a flat
orthographic UI. It needs no lights, camera angle changes, bloom, or render feature.
Animation uses Unity shader time and pauses when game time is paused.

For a different TMP SDF font, first create a material preset from that font's own
material and choose this shader. Keep that font's atlas, Gradient Scale, Texture
Width/Height, and Normal/Bold weights; copy only the rank appearance settings.
Assigning these existing materials to an unrelated font atlas will produce wrong glyphs.
Do not use this SDF shader with TMP bitmap or color-font assets.

## Validation

Validated in the connected Unity 6000.6.2f1 Editor using this project's URP 17.6:

- Shader import and all rendered variants: no shader errors.
- Six presets rendered in an isolated preview scene with a screen-space camera Canvas.
- Frames at 0 and 2 seconds differ for SS/S/A/B/C; D remains unchanged.
- Fully transparent text and CanvasGroups leave no visible glow.
- Stencil Mask and RectMask2D both clip the text and glow, with no pixels outside
  the tested mask bounds.
- The development scene remained clean and unchanged by this work.

The shader uses the Unity 6 TMP vertex layout (SDF scale in TEXCOORD0.w), verified
against this project's imported TMP shader. Older TMP vertex layouts are not targeted.

References: [Unity's URP shader structure](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/writing-shaders-urp-basic-unlit-structure.html)
and the project's imported `TextMesh Pro/Shaders/TMP_SDF-Mobile.shader` and
`TMP_SDF.shader` for SDF edge, masking, and texture-mapping conventions.
