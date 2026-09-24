# Core: pause, settings and input

No scene setup is needed. `GamePause` creates itself when Play starts (`[Game Pause]`, kept across scenes) and builds its
own pause menu. It also makes sure the scene has an EventSystem with `InputSystemUIInputModule`, and swaps out an old
`StandaloneInputModule` if it finds one.

## Controls (`GameInput`)

The actions are built in code (see `GameInput.EnsureCreated`) and use the new Input System.

| Map | Action | Keyboard | Gamepad |
|---|---|---|---|
| Combat | Lane1–Lane4 | A S J K | D-pad Left / Down, South, East |
| Explore | Move | WASD + arrows | Left stick, D-pad |
| Explore | Interact | Enter | South |
| System | Pause | Esc | Start |

Players rebind them in **Pause > Settings > Controls**. Their changes are saved to PlayerPrefs (`RythmRPG.Input.BindingOverrides`).
If a new key is already used by another action in the same context, the two actions swap keys.

In gameplay code, use the helpers: `GameInput.MoveValue`, `InteractPressed`, `InteractHeld`, `LanePressed(id)`.
They return "no input" while the game is paused. For fixed debug keys that are still set up as `KeyCode` fields, use
`LegacyKeys.WasPressed(KeyCode)` or `LegacyKeys.IsHeld(KeyCode)`.

To add an action: create it in `EnsureCreated`, then add a `RebindRow` in `BuildRows` so it appears on the Controls page.

## Pause and resume

- The pause key sets `Time.timeScale` to 0, freezes `GameAudioClock`, stops scheduled music and pauses all other
  playing AudioSources.
- In combat, **Resume** starts a countdown (Settings > Gameplay, 0–5 s, default 3). Outside combat, the game continues
  right away. `CombatController` registers "a battle is on" with `GamePause.AddCountdownCondition`; add your own
  conditions the same way. The game stays frozen during the countdown. On
  the last tick, the music is re-scheduled to continue from the exact sample where it stopped, and the notes start moving
  again. Pressing Pause during the countdown pauses the game again.
- When the window loses focus, a build pauses automatically. You can turn this off in Settings.

### Rules for rhythm and audio code

- Read time from `GameAudioClock.Now` (or `SmoothedDspTime.Now`), **not** `AudioSettings.dspTime`.
- Schedule audio with `PausableAudio.PlayScheduled / SetScheduledEndTime / Stop`, **not** directly on the AudioSource.
- For hit-stop or slow motion, use `GamePause.SetTimeScale(x)`. For realtime waits, use
  `GamePause.WaitUnpausedRealtime(seconds)`.
- For realtime clocks that should not count paused time, use `GamePause.UnpausedRealtime`.

## Display

Settings > Display offers Fullscreen, Borderless and Windowed, plus a resolution (Borderless always uses the
monitor's native resolution). Changes apply only in a build, not in the Editor's Game view.

## Crisp world UI (`Rendering/`)

The world is rendered by Main Camera into the 480x270 pixel render texture. World-space UI on the **CrispWorldUI**
layer skips that texture and is drawn at full screen resolution on top of it, in the same place on screen.

- **Setup (once per scene):** Tools > Rythm RPG > Rendering > Set Up Crisp World UI Camera, then save the scene.
  It adds the layer and takes it out of Main Camera's culling mask. It adds a `Pixel Display Rig` with two cameras:
  `Pixel Display Camera` (Base) draws the render texture canvas, which is switched to Screen Space - Camera, and
  `Crisp World UI Camera` (Overlay, in its stack) draws only the CrispWorldUI layer. "Remove Crisp World UI Camera"
  undoes all of this.
- `CrispWorldUICamera` copies Main Camera's pose, lens and projection every frame, after Cinemachine and camera shake.
  It also fits the projection to the RawImage's on-screen rect (Envelope crop, uvRect).
- **Crisp by code:** judgement and damage text, the hit line with its lane key markers and caps, the key-marker morph
  and the ability slots above the player. Notes stay pixelated.
- **Other objects:** add `CrispWorldUIObject`, or select them and use "Put Selection On Crisp World UI Layer".
  In code, call `CrispWorldUI.Apply(root)`. It does nothing until the crisp camera exists, so without the setup
  everything renders into the pixel texture as before.
- Screen Space - Overlay canvases (HUD, results, pause) still draw on top of everything. The crisp layer shares no
  depth with the world and gets none of Main Camera's post-processing.
- **Occlusion (Play mode):** the player, the enemy and notes have a `CrispWorldUIOccluder` (added by
  `CombatVFXController.Bind` and `RhythmPatternRunner` when a note spawns). Each frame, the crisp camera draws their
  sprite silhouettes into a 480x270 mask with Main Camera's view, then sets stencil bit 128 on those screen pixels.
  Materials passed to `CrispWorldUI.MakeOccludable` skip those pixels, so the pixel character or note underneath
  shows in front. That covers the hit line, caps and key markers (the Hit Line UI shader and a per-font copy of the
  TMP label material). Judgement text and ability slots do not test the stencil and stay on top.
  Graphics in the hit line that use your own material are not occluded unless their shader has the UI stencil
  properties and you call `MakeOccludable` on it. Particles, lines and trails do not occlude.
  Turn it off with **Occlusion** on the `Crisp World UI Camera` component.

## Styling

Colors are in `PauseTheme` (`UI/PauseUi.cs`), and the layout is in `Pause/PauseMenu.cs`. Text uses the default TMP font.
