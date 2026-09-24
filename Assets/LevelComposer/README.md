# Level Composer (team app)

A standalone app for composing **combat levels**: one enemy attack sequence = its song (intro, main loop,
player-turn stem, end, defeat end) plus an ordered list of attack steps, each with its own chart. It has a
flat lane preview with auto-hit, and it saves `.combatlevel.json` files that Unity imports as regular game assets.

## Set up and build (once, in Unity)

1. **Tools > Rythm RPG > Level Composer > Create (Or Rebuild) Composer Scene** creates `Assets/Scenes/LevelComposer.unity`
   (camera and audio listener, a UIDocument running `ComposerApp`, an EventSystem) and `Runtime/Resources/LevelComposer/`
   (PanelSettings and the default runtime theme).
2. Press Play in that scene to try it, or use **Play Composer In Editor**.
3. **Build Composer App (Windows)** builds only that scene into a folder you pick. The build uses the name
   "RythmRPG Level Composer", runs windowed at 1600x900 and resizable, and puts your player settings back afterwards.
   Zip the folder and share it with the team.

## Composing (in the app)

- **Music tab**: pick the Main loop (and optionally Intro, Player turn stem, End, Defeat end). Set the BPM, turn on
  the metronome (M) and nudge the Offset until the clicks land on the beat. The same warnings as the `CombatSong`
  inspector are shown here: the stem must match the loop's length, the loop should be a whole number of bars, and MP3
  files break gapless loops.
- **Steps tab**: one card per enemy attack, in order. Each step has an enemy animation, a wind-up time, an end policy
  and a lane count (1-4; keys J / S J / S J K / A S J K, same as the game).
- **Palette** (above the timeline): pick a note type (keys 1-9) and click in a lane. For holds, drag while placing or
  drag the bar's end. **Pattern...** inserts sweeps, stairs, streams, trills and more as ordinary notes.
- **Inspector**: shows every property of the selected notes. The fields come from each note type's parameter list.
  Travel has quick buttons for ½, 1, 1½ and 2 bars. Dragging the dot at the start of a selected note's dashed line
  also changes its travel time.
- **Preview**: Space plays. **Step** mode plays the selected step over the loop. **Level** mode plays the whole
  fight: intro, then each step followed by a player turn with the stem cross-fade (or the low-pass when there is no
  stem), then the end section. Each chart's beat 0 lands on the loop's next bar line, as in combat.
  **Auto-hit** (T) plays every note perfectly; turn it off to play the lanes yourself with A S J K.
- **Problems**: validation for notes inside holds, notes too close together, mash notes that are too fast, windows in
  the wrong order, missing music, unknown note types and so on. Click a problem to jump to the note.
- Files: Ctrl+S saves. Music paths are stored **relative to the level file**, so keep levels next to the game repo
  (for example `RythmRPG/CombatLevels/`) and they open on every machine. Unsaved work is autosaved to
  `Documents/RythmRPG Composer/Autosave`. F1 lists all shortcuts.

## Into the game

- **Tools > Rythm RPG > Level Composer > Import Combat Level...** reads a `.combatlevel.json` and creates or updates
  these assets in `Assets/CombatLevels/<id>/`:
  - `<id>_Song` (CombatSong)
  - `<id>_StepNN` (RhythmChart, one per step, audio offset 0)
  - `<id>_Sequence` (EnemyAttackSequence)

  Add the sequence to an enemy phase. Re-importing updates the same assets, so references stay intact. Music from
  outside `Assets/` is copied into `Audio/`. **Reimport Last Combat Level** repeats the last import.
- **Export Selected Attack Sequence...** (or right-click the asset: Rythm RPG > Export To Combat Level) writes an
  existing sequence as JSON so it can be edited in the app. Arrow and Cluster notes are left out, and notes generated
  by programmed patterns are exported as plain notes.

## Adding a new note gimmick

Note types are data. The whole app (palette, inspector, validator, preview) and the importer read them from
`NoteTypeRegistry`, never from an enum.

1. **Describe it in JSON.** Put the file in `Assets/StreamingAssets/ComposerNoteTypes/` so it ships with the app and
   the importer sees it. A team member can also drop one into `Documents/RythmRPG Composer/NoteTypes/` to try it out
   without a rebuild. See `ComposerNoteTypes/examples/bomb.json`. The fields are:
   - `id`, `name`, `category`, `color`, `shape`, `description`
   - `archetype`: how the preview plays it (`tap`, `hold`, `stationary`, `stationary_hold`, `mash`, `pingpong`)
   - `output` + `legacyType` (or `sequenceKind`): what the game spawns
   - `prefab`: an optional prefab override
   - `extends`: copies another type's parameters
   - `params`: a list of `{key, label, kind: int|float|bool|choice|beats|seconds|text, default, min, max, step, options, group, tooltip, bind}`

   A param with a `bind` writes a known game field (`travelBeats`, `damage`, `badWindow`, `mashPresses`,
   `seq.volleys`, ...). A param without one is imported into `RhythmNoteData.Metadata` under its key. Custom types
   also get a `noteType` metadata entry.
2. **Make the game side**: a prefab whose note script reads its metadata. Nothing else in the chart pipeline changes.
3. **If none of the archetypes preview it well**, implement `ISimBehaviour` (Core/Simulation/SimBehaviours.cs),
   register it with `SimBehaviours.Register("myArchetype", () => new MyBehaviour())` from the app at startup, and use
   that archetype name in the JSON.

A JSON file can also override a built-in type by `id`, for example to change its colour, defaults or limits.
Parameters are merged by key.

## Code layout

| Folder | What |
|---|---|
| `Core/` (`RythmRPG.LevelComposer.Core`, no engine references) | Level model and JSON (`Model/`, `Json/MiniJson`), note-type registry (`Types/`), edit session with undo/clipboard (`Editing/`), validator, simulator and level-flow planner (`Simulation/`). Unit-tested. |
| `Runtime/` (`RythmRPG.LevelComposer`) | The app: `ComposerApp`, UI Toolkit views built in code (`UI/`), audio loading, music scheduling, metronome and hit sounds (`Audio/`), the preview controller, workspace and preferences. Theme: `Resources/LevelComposer/Composer.uss`. |
| `Editor/` | Scene setup and build, importer, exporter. |
| `Tests/EditMode/` | `LevelComposerCoreTests` (JSON, registry, editing, validation, simulation, flow timing). |

The timing rules follow the game. Beats are authoritative. Moving-note windows default to the project's
JudgementConfig (45/90/125/180 ms). Stationary notes judge presses before the charge completes using their own
windows. Mash uses MashRules (clear by 60% of travel for Perfect, 85% for Good, 0.15 s line grace). Ping-Pong follows
PingPongAttack. Chart start uses `CombatSong.NextGridTime`, the same rule as `RhythmPatternRunner.PlanStart`.
