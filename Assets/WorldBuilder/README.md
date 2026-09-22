# World Builder (Phase 1-2 -- Functional Core + Advanced Tile Editing)

A custom Unity Editor tool for building this game's tilted-orthographic, pixel-art, 2D-in-3D ground
environments, purpose-built for the ~35 degree presentation camera rather than behaving like a generic
terrain editor or a standard 2D Tilemap.

Phase 1 delivered world configuration, the camera-aware grid, tile data, the tile palette, ground
painting (Brush / Eraser / Eyedropper), chunked mesh generation, and saving. Phase 2 builds directly on
that same architecture -- no rewrite -- and adds every remaining tool from the spec's tool table:
Rectangle, Fill (flood fill), Line, Select (with Move / Copy / Cut / Paste / Rotate / Flip), and Stamp,
plus automatic terrain transitions (auto-tiling). Phases 3-6 (object/prop placement, elevation/walls/
water, full collision & navigation editing, camera-accurate preview, and further optimization) still
extend this same foundation without requiring a rewrite -- see "What's next" below.

## Folder structure

```
Assets/WorldBuilder/
  Runtime/                                  -- no UnityEditor references; ships in builds
    RythmRPG.WorldBuilder.Runtime.asmdef
    Data/
      TileCoord.cs             -- grid/chunk coordinate struct
      TileCellData.cs          -- what is painted in one cell (tile id, rotation, flip, terrain set, manual override)
      TileLayerData.cs         -- one ground layer's sparse tile map (serializable)
      TileDefinition.cs        -- ScriptableObject: one paintable tile (sprite + gameplay/compensation flags)
      TilePalette.cs           -- ScriptableObject: a reusable collection of TileDefinitions, TileStamps and TerrainTransitionSets
      TileStamp.cs             -- ScriptableObject: a reusable multi-tile arrangement (Phase 2)
      TerrainTransitionSet.cs  -- ScriptableObject: an auto-tiling rule set (Phase 2)
      WorldBuilderSettings.cs  -- per-world grid/camera/compensation/rendering configuration
    GroundPlaneMath.cs         -- ray-plane intersection, grid snap, camera-space pixel snap
    TileMaterialCache.cs       -- one shared Material per source texture
    TileMeshBuilder.cs         -- builds a chunk's visual mesh (compensated) + collision mesh (uncompensated)
    TileGridUtility.cs         -- Rectangle iteration, Bresenham line, iterative capped flood fill (Phase 2)
    TileTransformUtility.cs    -- shared rotate/flip offset math for Stamp/Paste/Select (Phase 2)
    TerrainTransitionResolver.cs -- computes and applies auto-tiling neighbor masks (Phase 2)
    GroundChunk.cs              -- MonoBehaviour: one chunk's generated mesh/collider
    WorldBuilderWorld.cs         -- MonoBehaviour: the world root (layers, settings, chunk management)
  Editor/                                    -- Editor-only; excluded from builds
    RythmRPG.WorldBuilder.Editor.asmdef
    WorldBuilderWindow.cs      -- Tools > World Builder window (the 9 spec'd sections)
    WorldBuilderSceneTool.cs   -- Scene view raycasting, per-tool preview, mouse/keyboard routing for all 9 tools
    WorldBuilderPaintController.cs -- every tool's edit logic (Brush/Rectangle/Fill/Line/Stamp/Select/Move/
                                      Copy/Paste/Rotate/Flip/terrain-aware placement) with Undo integration
    WorldBuilderSelection.cs   -- the Select tool's current rectangular selection (Phase 2)
    WorldBuilderClipboard.cs   -- in-memory copy/paste buffer (Phase 2)
    WorldBuilderPaletteView.cs -- searchable thumbnail palette grid (favorites/recents/categories)
    WorldBuilderAssetFactory.cs -- "New World" / "New Palette" / sprite-to-tile / stamp / terrain set creation
    WorldBuilderGizmos.cs      -- grid / chunk bounds / brush / rectangle / line / selection / stamp preview drawing
    WorldBuilderDiagnostics.cs -- Optimization & Debugging warnings (missing palette, non-point filtering,
                                  unconfigured terrain sets/stamps, etc.)
    WorldBuilderPrefs.cs       -- EditorPrefs-backed tool state (current tool, brush size, placement transform,
                                  terrain mode, favorites...)
  Generated/                                  -- created on demand by the tool
    Tiles/       -- TileDefinition assets created via sprite drag-and-drop
    Stamps/      -- TileStamp assets created via "Save as Stamp" / "New Stamp From Selection"
    TerrainSets/ -- TerrainTransitionSet assets created via "New Terrain Set"
```

Nothing outside `Assets/WorldBuilder/` was modified. This does not touch the existing camera, the
`RhythmSystem` composer tool, `OrthographicWaterBase.shader`, or any combat/gameplay script.

## Installing / opening the tool

1. Let Unity import the new files (it will generate `.meta` files and compile the two new assemblies
   automatically the next time the Editor has focus).
2. Open it from **Tools > World Builder**.

## Setting up a world (matches this project's actual camera)

1. In the World Builder window toolbar, click **New World**. This creates a `World` GameObject in the
   active scene with a `WorldBuilderWorld` component and selects it.
2. Go to the **World Settings** tab:
   - **Reference Camera**: drag in your scene's presentation camera (in the `2.5D Scene` scene this is
     the `Main Camera` object, which is orthographic, size 4.2, tilted 35 degrees on X, rendering into the
     480x270 `PixelRender` render texture -- the World Builder does not require or assume Cinemachine,
     it only reads the plain `Camera` component).
   - Click **Sync Camera Tilt From Reference Camera** to pull the 35 degree angle in automatically
     (or leave the default, which is already 35).
   - **Tile Pixel Size**: 32 (this project's default tile pixel size), or 16/64/Custom.
   - **Tile World Size**: world units per tile (1 is a reasonable default; match whatever scale your
     ground props already use).
   - Leave **Projection Compensation** on if you are painting with square, top-down-authored tile art;
     turn it off (or turn it off per-tile in the Tile Palette) if your art was drawn specifically for the
     tilted camera.
3. Go to **Tile Palette**: click **New** to create a `TilePalette` asset, then drag Sprites (or whole
   spritesheet textures -- every sub-sprite is imported) from the Project window onto the "Drag Sprites
   here" box. Each drop creates a `TileDefinition` asset under `Assets/WorldBuilder/Generated/Tiles/` and
   adds it to the palette. Right-click a tile thumbnail to favorite it; use the search field to filter by
   name/category/tag.
4. Go to **Terrain & Ground**, tick **Enable Ground Painting in Scene View**, pick a tile in the palette,
   and paint directly in the Scene view. Undo (Ctrl/Cmd+Z) works normally for every tool below.

## The tools (Terrain & Ground tab)

The tool toolbar matches the spec's table order exactly: **Brush, Rectangle, Fill, Eraser, Eyedropper,
Line, Select, Move, Stamp**.

- **Brush / Eraser**: click or drag to continuously paint/erase, honoring **Brush Size**.
- **Rectangle**: drag out a rectangle; releasing the mouse fills its exact footprint with the selected
  tile (preview shown live while dragging).
- **Line**: drag from a start to an end tile; releasing paints a Bresenham line between them (the preview
  draws the exact same tiles the commit will paint).
- **Fill**: click a cell to flood-fill every 4-connected cell that currently matches it (same tile id, or
  empty space). Capped by **Fill Max Tiles** (default 4096) so it can't run away on an effectively
  infinite grid; a warning appears if the cap was hit.
- **Eyedropper**: click a painted cell to pick up its tile as the active selection.
- **Select**: drag out a rectangular selection. With an active selection and the Select tool current:
  - **Copy / Cut / Delete / Clear Selection**, **Rotate CW / CCW**, **Flip H / V**, **Toggle Manual
    Override** (locks/unlocks cells from automatic terrain re-resolution), **Paste At Cursor**, and
    **Save as Stamp** are all available as buttons in the window, and Ctrl/Cmd+C, Ctrl/Cmd+V and
    Delete/Backspace work directly in the Scene view.
  - Rotate/Flip apply to the selection's tile *arrangement* (positions swap correctly, including for
    non-square selections) as well as each tile's own sprite rotation/flip.
- **Move**: with an existing selection, click inside it in the Scene view and drag; releasing moves every
  tile in the selection by that whole-tile offset (source cells cleared, destination cells written, in a
  data-safe two-phase pass).
- **Stamp**: pick a stamp in the **Tile Palette** tab's Stamps section, then click in the Scene view to
  place the whole saved arrangement, honoring the current **Placement Transform** (rotation/flip).

**Placement Transform** (Rotate CW/CCW, Flip H/V) applies to every tool that places a fresh arrangement of
tiles: Brush/Rectangle/Line's per-tile rotation, and Stamp/Paste's whole-arrangement rotation and flip.

## Terrain transitions (auto-tiling)

Tick **Paint As Terrain** in Terrain & Ground and pick a **Terrain Set** to paint auto-tiled edges/corners
instead of a single raw tile. A Terrain Set is created and edited in the **Tile Palette** tab's Terrain
Sets section:

- **Adjacency**: 4-neighbor (N/E/S/W, 16 possible masks) or 8-neighbor (adds the four diagonals, 256
  possible masks).
- **Fallback Tile**: used whenever no rule matches a cell's current neighbor mask.
- **Rules**: each rule pairs one neighbor-mask (edited with the small N/NE/E/SE/S/SW/W/NW toggle grid next
  to it -- diagonal toggles are disabled in 4-neighbor mode) with the tile to show for that mask.
  **Add Rule** adds one at a time; **Auto-Generate All Masks** fills every remaining mask combination at
  once (with a confirmation dialog before generating a large 8-neighbor set).

Painting, erasing, moving, rotating and flipping terrain-managed cells automatically re-resolves the
edited cell plus its neighbors (`TerrainTransitionResolver`). A cell locked with **Toggle Manual
Override** is skipped by that automatic re-resolution, so hand-placed exceptions are never silently
replaced.

## Notes on the math (why this isn't a straight 2D tilemap)

- **Placement** uses a real ray/plane intersection (`GroundPlaneMath.RayPlaneIntersect`) against the
  active elevation's Y plane, so painting stays correct regardless of how the Scene view camera happens
  to be oriented -- it never assumes screen space equals XZ world space.
- **Projection compensation** scales only the generated *visual* mesh's depth (Z) axis by
  `1 / sin(cameraTiltDegrees)`, computed from the world origin so a contiguous run of compensated tiles
  stays seamless. Collision geometry (`TileMeshBuilder`'s second, uncompensated mesh) and all gameplay
  math always use the untouched logical grid, so movement distances and adjacency are unaffected by
  whichever compensation setting is chosen.
- **Collision** is one combined `MeshCollider` per chunk (not one collider per tile), built only from
  tiles whose `TileDefinition.collisionEnabled` is checked.
- **Block transforms** (Rotate/Flip Selection, Stamp placement, Paste) all route through the same
  `TileTransformUtility` offset math, so a group of tiles rotates/flips identically no matter which
  feature triggered it. Rotation works in plain integer tile-coordinate offsets (no division), which
  matters for non-square selections: an earlier "rotate around a doubled center coordinate" approach was
  found and replaced during development because it silently mis-placed tiles for odd/even mixed-parity
  selection sizes.
- **Flip and Move** both compute every source/destination pair from an immutable snapshot before writing
  anything, and clear all sources before writing any destination. Because a flip or move can send one
  cell's contents to a location another cell is simultaneously vacating, doing the clear-then-write per
  cell in a single pass can silently delete data -- the three-phase snapshot/clear/write pattern avoids
  that class of bug.

## Testing / verification steps

1. Open the `2.5D Scene` scene.
2. `Tools > World Builder` > **New World**, assign the scene's `Main Camera` as the reference camera.
3. Create a palette, drag in a few ground sprites, paint a small area with Brush, and confirm:
   - A `Chunks` child appears under the `World` GameObject with `Chunk_0_0_L0`-style children, each with a
     `MeshFilter`/`MeshRenderer` (and a `MeshCollider` if any painted tile has collision enabled).
   - Undo (Ctrl/Cmd+Z) removes the last brush stroke; Redo restores it.
   - Save the scene, close and reopen Unity: the painted ground reappears without any manual "rebuild"
     step.
4. Try each Phase 2 tool: drag a Rectangle and a Line, Fill an enclosed area, Select a region and Copy/
   Paste/Rotate/Flip/Move it, and save part of a build as a Stamp then place it elsewhere. Confirm Undo
   cleanly reverts each one as a single step.
5. Create a Terrain Set with a fallback tile and a couple of rules, enable **Paint As Terrain**, and paint
   a blob with it -- confirm edges auto-update as you extend the blob, and that toggling **Manual
   Override** on a cell stops it from changing when its neighbors do.
6. The **Optimization & Debugging** tab reports 0 issues once a palette and camera are assigned, and flags
   real problems (a non-Point-filtered texture, a Terrain Set with no fallback tile and an under-specified
   rule, an empty Stamp, etc.) when they exist.

## What's next (explicitly not implemented yet)

- Object/prop placement, sprite orientation presets (billboard/vertical/horizontal/custom), and the
  sorting/occlusion system for characters walking in front of/behind props (Phase 3).
- Dedicated wall-building, raised platforms/stairs, structures, and the water adapter that wires up the
  existing `OrthographicWaterBase` shader and `WaterTrigger`/`WaterInteractor` scripts (Phase 4). Elevation
  levels already exist in the data model and can be switched today via the Active Elevation Level control
  in Terrain & Ground, but there is no dedicated elevation-editing UI yet.
- Per-object collision authoring, walkable/non-walkable painting, hazards/interaction regions, and a
  dedicated collision visualization mode beyond the current combined ground `MeshCollider` (Phase 5).
- Render-to-texture game camera preview panel, sorting/framing overlays, and further chunk/collision
  optimization passes and asset-validation tooling (Phase 5-6).
