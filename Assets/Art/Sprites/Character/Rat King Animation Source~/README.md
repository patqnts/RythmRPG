# Rat King — breathing idle

Source: `Assets/Art/Sprites/Character/Rat King Asset.aseprite`.
Animated file: `Assets/Art/Sprites/Character/Rat King Idle.aseprite`.

The new file retains the head, body, clothes and tail as separate editable layers,
plus the original hidden reference layer. The source file is unchanged.

`Idle_Breathing` is a forward, indefinitely looping tag: 24 frames at 100 ms each,
2.4 seconds total. The upper torso rises by up to two pixels, the head follows
one frame later, and the tail curl sways by up to two pixels while its base stays
anchored. All sampling uses original source pixels; there is no filtering or
new painted detail. The feet and lower robe (canvas rows 60–72) stay unchanged.

Unity imports the animation as merged frame sprites, while Aseprite keeps the
individual layers editable. It uses 100 PPU, a bottom-center canvas pivot, Point
filtering, no mipmaps, and uncompressed RGBA32. The imported asset includes its
model prefab, Animator Controller and looping `Idle_Breathing` clip.

The Rat King child in `Assets/Prefab/WHO.prefab` has an Animator referencing this
controller and the new resting sprite. Its existing material and transforms are
preserved. The current `WHO/Rat King` scene instance was checked for the same
assignment; the scene was not saved over its existing unsaved edits.

Validation:

- Native Aseprite first and last composited frames match exactly.
- Every ground-contact pixel in rows 60–72 is identical in all 24 frames.
- All 24 frame references were played through Unity's imported Animator in a
  temporary preview scene; each displayed the expected sprite.
- Sprite bottom bounds remain constant; the root transform is never animated.
- The imported clip is 2.4 seconds and has Loop Time enabled.

`Previews/RatKing-Idle.gif` is the animated 6× nearest-neighbor preview.
`Previews/RatKing-Idle-ContactSheet.png` shows all frames in reading order.

`create_idle.lua` rebuilds the animation with Aseprite batch scripting; pass the
Unity project root with `--script-param root=...`. `ImportIdle.cs` and `WireIdle.cs`
are standalone Unity editor snippets run through the connected Editor. They are
kept in this `~` folder so Unity does not compile them as game scripts.

Native authoring API: https://aseprite.org/api/sprite
