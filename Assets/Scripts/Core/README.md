# Core: pause, settings and input

No scene setup is needed. `GamePause` creates itself when Play starts (`[Game Pause]`, kept across scenes) and builds its
own pause menu. It also makes sure the scene has an EventSystem with `InputSystemUIInputModule`, and swaps out an old
`StandaloneInputModule` if it finds one.

## Controls (`GameInput`)

The actions are built in code (see `GameInput.EnsureCreated`) and use the new Input System.

| Map | Action | Keyboard | Gamepad |
|---|---|---|---|
| Combat | Lane1–Lane5 | A S D J K | D-pad Left / Down / Right, South, East |
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

## Styling

Colors are in `PauseTheme` (`UI/PauseUi.cs`), and the layout is in `Pause/PauseMenu.cs`. Text uses the default TMP font.
