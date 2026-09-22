# World Builder (Phase 1-3 -- Functional Core + Advanced Tile Editing + Object Placement)

A custom Unity Editor tool for building this game's tilted-orthographic, pixel-art, 2D-in-3D ground
environments, purpose-built for the ~35 degree presentation camera rather than behaving like a generic
terrain editor or a standard 2D Tilemap.

Phase 1 delivered world configuration, the camera-aware grid, tile data, the tile palette, ground
painting (Brush / Eraser / Eyedropper), chunked mesh generation, and saving. Phase 2 builds directly on
that same architecture -- no rewrite -- and adds every remaining tool from the spec's tool table:
Rectangle, Fill (flood fill), Line, Select (with Move / Copy / Cut / Paste / Rotate / Flip), and Stamp,
plus automatic terrain transitions (auto-tiling). Phase 3 adds object/prop placement: a `PropDefinition`
asset type, sprite orientation presets (Vertical/Horizontal/Billboard/Custom), a Scene-view placement
tool, and real depth-buffer occlusion via an alpha-cutout material so props and characters sort correctly
against each other automatically, without a manual per-frame sorting-order system. Phases 4-6 (elevation/
walls/water, full collision & navigation editing, camera-accurate preview, and further optimization)
still extend this same foundation without requiring a rewrite -- see "What's next" below.

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
      TilePalette.cs           -- ScriptableObject: a reusable collection of TileDefinitions, TileStamps,
                                  TerrainTransitionSets and PropDefinitions (Phase 3 adds the props list)
      TileStamp.cs             -- ScriptableObject: a reusable multi-tile arrangement (Phase 2)
      TerrainTransitionSet.cs  -- ScriptableObject: an auto-tiling rule set (Phase 2)
      PropDefinition.cs        -- ScriptableObject: one placeable prop (sprite, orientation mode, footprint) (Phase 3)
      PropOrientationMode.cs   -- enum: Vertical / Horizontal / Billboard / Custom (Phase 3)
      WorldBuilderSettings.cs  -- per-world grid/camera/compensation/rendering configuration
    GroundPlaneMath.cs         -- ray-plane intersection, grid snap, camera-space pixel snap
    TileMaterialCache.cs       -- one shared Material per source texture
    TileMeshBuilder.cs         -- builds a chunk's visual mesh and collision mesh, both projection-compensated
    TileGridUtility.cs         -- Rectangle iteration, Bresenham line, iterative capped flood fill (Phase 2)
    TileTransformUtility.cs    -- shared rotate/flip offset math for Stamp/Paste/Select (Phase 2)
    TerrainTransitionResolver.cs -- computes and applies auto-tiling neighbor masks (Phase 2)
    GroundChunk.cs              -- MonoBehaviour: one chunk's generated mesh/collider
    WorldBuilderWorld.cs         -- MonoBehaviour: the world root (layers, settings, chunk & prop management)
    PropMeshBuilder.cs          -- builds a prop's flat billboard-quad mesh from its sprite (Phase 3)
    PropOrientationUtility.cs   -- resolves a prop's rotation from its PropOrientationMode (Phase 3)
    PropMaterialCache.cs        -- one shared alpha-cutout Material per prop texture, for real depth occlusion (Phase 3)
    WorldBuilderProp.cs         -- MonoBehaviour: one placed prop instance's generated mesh/material/collider (Phase 3)
  Editor/                                    -- Editor-only; excluded from builds
    RythmRPG.WorldBuilder.Editor.asmdef
    WorldBuilderWindow.cs      -- Tools > World Builder window (the 9 spec'd sections, plus Object Placement)
    WorldBuilderSceneTool.cs   -- Scene view raycasting, per-tool preview, mouse/keyboard routing for all 9 tile tools
    WorldBuilderPaintController.cs -- every tool's edit logic (Brush/Rectangle/Fill/Line/Stamp/Select/Move/
                                      Copy/Paste/Rotate/Flip/terrain-aware placement) with Undo integration
    WorldBuilderSelection.cs   -- the Select tool's current rectangular selection (Phase 2)
    WorldBuilderClipboard.cs   -- in-memory copy/paste buffer (Phase 2)
    WorldBuilderPaletteView.cs -- searchable thumbnail palette grid (favorites/recents/categories)
    WorldBuilderAssetFactory.cs -- "New World" / "New Palette" / sprite-to-tile / sprite-to-prop / stamp /
                                    terrain set creation
    WorldBuilderGizmos.cs      -- grid / chunk bounds / brush / rectangle / line / selection / stamp / prop
                                  preview drawing
    WorldBuilderDiagnostics.cs -- Optimization & Debugging warnings (missing palette, non-point filtering,
                                  unconfigured terrain sets/stamps, etc.)
    WorldBuilderPrefs.cs       -- EditorPrefs-backed tool state (current tool, brush size, placement transform,
                                  terrain mode, selected prop, favorites...)
    WorldBuilderPropTool.cs    -- Scene view object placement: click to place the selected PropDefinition (Phase 3)
  Generated/                                  -- created on demand by the tool
    Tiles/       -- TileDefinition assets created via sprite drag-and-drop
    Stamps/      -- TileStamp assets created via "Save as Stamp" / "New Stamp From Selection"
    TerrainSets/ -- TerrainTransitionSet assets created via "New Terrain Set"
    Props/       -- PropDefinition assets created via sprite drag-and-drop (Phase 3)
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

## Object placement (Object Placement tab, Phase 3)

1. In the **Tile Palette** tab (or the Object Placement tab's own drop zone), drag Sprites onto the
   "Drag Sprites here" box under **Props**. Each drop creates a `PropDefinition` asset under
   `Assets/WorldBuilder/Generated/Props/` and adds it to the active palette.
2. Select a prop in the **Props** list, set its **Orientation** (see below), and tick **Enable Object
   Placement in Scene View**.
3. Click in the Scene view (on the active elevation plane, same ray/plane raycast as ground painting) to
   place an instance. Each placed prop is a `WorldBuilderProp` GameObject under a `Props` child of the
   `World` GameObject, created with `Undo.RegisterCreatedObjectUndo` like everything else in this tool, so
   Ctrl/Cmd+Z removes it normally.
4. The window's **Rebuild All** button also rebuilds every placed prop's mesh/material (`RebuildAllProps`),
   the same safety net Ground Painting has always had for chunks.

**Orientation presets** (`PropOrientationMode`, mirroring the project's existing manual
`ScreenAlignmentTools` conventions so props read the same as anything hand-placed):

- **Vertical** (`Quaternion.identity`, "Level World Axes"): for anything meant to stand upright and face
  the world's own axes -- most props (trees, rocks, signs, walls) want this.
- **Horizontal** (`Quaternion.Euler(-90,0,0)`, "Lay Flat on XZ"): for anything meant to lie flat on the
  ground plane, such as a rug, a puddle decal, or a manhole cover.
- **Billboard** (`Quaternion.LookRotation(propPosition - referenceCameraPosition, Vector3.up)`, "Face Main
  Camera"): rotates to continuously face the world's reference camera. Recomputed on every `Rebuild`
  (Editor-time only -- see the known limitation below), so this is meant for props whose facing should
  track the fixed presentation camera, not runtime billboarding toward a moving player camera.
- **Custom**: a fixed `Quaternion.Euler(customEulerAngles)` set on the `PropDefinition` itself.

Orientation is a separate transform rotation, never baked into the generated quad mesh, so switching a
prop's orientation mode never requires a mesh rebuild.

**Occlusion**: props render with the same alpha-cutout ("Opaque" + `_AlphaClip`) material mode as the
rest of this project's cutout shaders, via `PropMaterialCache`. This means props and characters occlude
each other correctly through Unity's ordinary depth buffer -- whichever is physically nearer the camera
draws in front -- with no manual per-frame sorting-order system to maintain. This was an explicit design
choice over a manual sorter (see `claude/world-builder-phase3.md` in the project docs for the tradeoff).

**Known limitations (Phase 3, not yet implemented -- see `world-builder-phase3.md` for the full list):**

- No per-instance rotation override yet: every instance of a given `PropDefinition` shares that
  definition's orientation. You cannot yet, for example, randomize a rock prop's facing per placement.
- **Ground Painting** and **Object Placement** can both be enabled at the same time, and nothing currently
  stops that -- a single click will paint a tile *and* place a prop. Keep only one enabled at a time for
  now.
- The Props list is a flat list (no thumbnails/favorites/search yet, unlike the Tile Palette's richer
  `WorldBuilderPaletteView`).
- No dedicated `WorldBuilderDiagnostics.cs` checks for props yet (missing sprite, zero footprint, etc.).

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
- **Projection compensation** scales the generated ground mesh's depth (Z) axis by
  `1 / sin(cameraTiltDegrees)`, computed from the world origin so a contiguous run of compensated tiles
  stays seamless. As of this project's collision-accuracy fix, the collision mesh (`TileMeshBuilder`'s
  second mesh) is built from the **same compensated coordinates as the visual mesh**, so a
  `CharacterController`'s footprint always matches the rendered ground art exactly, whichever compensation
  setting is chosen -- there is intentionally no separate "uncompensated" collision path anymore. Seams
  are still expected wherever compensated and uncompensated tiles sit directly adjacent, since that is a
  property of the differing Z-scale between them, not a bug in either mesh.
- **Collision** is one combined `MeshCollider` per chunk (not one collider per tile), built only from
  tiles whose `TileDefinition.collisionEnabled` is checked. Props use a separate per-instance `BoxCollider`
  sized from `PropDefinition.footprintSize`/`footprintOffset`/`footprintHeight` (Phase 3).
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
- **Undo safety**: every procedurally-created GameObject in this tool (chunk roots, individual chunks, the
  props root, individual props) is registered with `Undo.RegisterCreatedObjectUndo` at creation time, and
  `WorldBuilderWorld` subscribes to `Undo.undoRedoPerformed` to rebuild chunk meshes after an undo/redo, so
  undoing a paint stroke or a prop placement never leaves a dangling, unregistered GameObject behind.

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
6. Go to **Object Placement**, drag in a prop sprite, set its orientation to **Vertical**, enable
   **Object Placement in Scene View**, and click to place a few instances. Confirm:
   - A `Props` child appears under the `World` GameObject with `Prop`-named children, each with a
     `MeshFilter`/`MeshRenderer` (and a `BoxCollider` if the prop's collision is enabled).
   - The placed prop visually occludes/is occluded by a character or other 3D object correctly as the
     Scene camera moves, with no flicker or z-fighting (confirms the alpha-cutout depth-buffer occlusion
     is working).
   - Undo (Ctrl/Cmd+Z) removes the last placed prop; Redo restores it.
   - Switching the prop's orientation between Vertical/Horizontal/Billboard/Custom in the window updates
     its rotation immediately in the Scene view.
7. The **Optimization & Debugging** tab reports 0 issues once a palette and camera are assigned, and flags
   real problems (a non-Point-filtered texture, a Terrain Set with no fallback tile and an under-specified
   rule, an empty Stamp, etc.) when they exist.

## What's next (explicitly not implemented yet)

- Per-instance prop rotation override, mutual exclusion between Ground Painting and Object Placement,
  palette UI polish (thumbnails/favorites/search) for props, and prop-aware diagnostics -- see the
  "Known limitations" list under Object Placement above and `world-builder-phase3.md`.
- Dedicated wall-building, raised platforms/stairs, structures, and the water adapter that wires up the
  existing `OrthographicWaterBase` shader and `WaterTrigger`/`WaterInteractor` scripts (Phase 4). Elevation
  levels already exist in the data model and can be switched today via the Active Elevation Level control
  in Terrain & Ground, but there is no dedicated elevation-editing UI yet.
- Per-object collision authoring, walkable/non-walkable painting, hazards/interaction regions, and a
  dedicated collision visualization mode beyond the current combined ground `MeshCollider` (Phase 5).
- Render-to-texture game camera preview panel, sorting/framing overlays, and further chunk/collision
  optimization passes and asset-validation tooling (Phase 5-6).
