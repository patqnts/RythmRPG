ORTHOGRAPHIC WATER BASE
Unity 6 / URP
=======================

This version intentionally contains ONLY:

1. Water depth/color
2. Refraction
3. Automatic intersection foam

No:
- ripples
- caustics
- vertex waves
- Fresnel
- specular
- pixel-grid logic
- interaction scripts


INSTALL
=======

1. Copy OrthographicWaterBase.shader into Assets/.

2. Create a Material:
   Shader > JonQuin > URP > OrthographicWaterBase

3. Assign it to your horizontal water plane.

4. In the ACTIVE URP Asset enable:
   - Depth Texture
   - Opaque Texture

5. If the Camera has explicit depth/opaque texture overrides, set them ON.


IMPORTANT: FOAM
===============

The foam is based on the camera depth buffer.

Anything that:
- is rendered before the water,
- writes to depth,
- and intersects the visible water surface

can automatically create the white intersection border.

Examples:
- rocks
- terrain
- walls
- bridge pillars
- opaque characters
- opaque props

No collision script is required.

Transparent objects normally DO NOT write to the opaque scene depth, so they
will not automatically produce this foam.


IF FOAM SHOWS IN SCENE VIEW BUT NOT GAME VIEW
=============================================

This normally means the Scene camera has a depth texture but the runtime Game
camera does not.

Check:

1. Project Settings > Quality
   Find the quality level currently active.

2. Verify which URP Asset that quality level uses.

3. On THAT URP Asset:
   Depth Texture = ON
   Opaque Texture = ON

4. Select the Game Camera.
   If Unity exposes depth/opaque texture requirement overrides, set them ON.

5. Verify your rock/terrain/prop material is Opaque and depth-writing.


CONTROLS
========

DEPTH COLOR

Shallow Color
    Color where the floor/object is immediately underneath the water.

Deep Color
    Color reached when there is sufficient visible water depth.

Depth Distance
    How far the scene must be behind the water before it becomes Deep Color.

    Smaller = deep color appears quickly.
    Larger  = shallow color extends farther.

Depth Contrast
    Shapes the shallow -> deep transition.

Water Opacity
    Overall transparency.


REFRACTION

Refraction Strength
    Amount of screen-space distortion.

    Recommended starting range:
    0.005 - 0.025

Refraction Scale
    Size/frequency of the moving distortion.

Refraction Speed XY
    Movement direction and speed.

Refraction Depth Influence
    0 = roughly equal distortion regardless of depth.
    1 = distortion grows with water depth.


FOAM

Foam Width
    Physical depth range around intersections.

    Start around:
    0.10 - 0.30

Foam Strength
    Overall amount.

Foam Noise Scale
    Size of irregularity along the foam border.

Foam Noise Amount
    0 = clean continuous border.
    Higher = more irregular thickness.

Foam Speed
    Animation speed of the irregular edge.


ORTHOGRAPHIC CAMERA
===================

This shader calculates water thickness from VIEW-SPACE depth rather than from
world Y distance.

That matters for an orthographic camera because Scene Depth represents
distance along the camera's viewing direction.

This also means it works when your orthographic camera is tilted rather than
being perfectly vertical.


480x270 RENDER TEXTURE
======================

Do not configure this shader specifically for 480x270.

Render the water normally into your low-resolution RenderTexture and keep the
RenderTexture Filter Mode = Point.

That lets the final render naturally pixelate:
- foam
- depth transition
- refraction

without forcing pixel-grid mathematics into the water shader itself.


TEST MATERIAL
=============

Try these values first:

Shallow Color:
    bright cyan/teal

Deep Color:
    dark blue

Depth Distance:
    3

Depth Contrast:
    1

Opacity:
    0.82

Refraction Strength:
    0.012

Refraction Scale:
    2

Refraction Depth Influence:
    0.65

Foam Width:
    0.18

Foam Strength:
    1

Foam Noise Scale:
    6

Foam Noise Amount:
    0.20


DEBUGGING ORDER
===============

If something is wrong, test in this order:

1. Set Refraction Strength = 0.
2. Set Foam Strength = 0.
3. Confirm shallow/deep colors respond to geometry underneath the water.
4. Turn Foam Strength back to 1.
5. Confirm intersection foam.
6. Turn Refraction Strength back up.

Do not add caustics/ripples/waves until these three systems behave correctly
in the GAME camera.
