# Scene loading

`Assets/Resources/Scenes/GameSceneCatalog.asset` controls the flow. Menu is the first build scene; Start Game opens Dungeon. Dungeon and Grassland are registered by stable IDs (`dungeon`, `grassland`). You can still press Play directly in either area. The loader creates itself and survives scene changes; no bootstrap scene is required.

The menu keeps its existing title artwork. Menu input stays locked while **Title Logo Image (1)** assembles, then **PRESS ANY KEY TO START** fades and lifts into place. A fresh keyboard/gamepad button press or left mouse click starts Dungeon only after that reveal completes. **Esc** replaces the prompt with **QUIT** and **CANCEL**, navigable with arrows, gamepad or mouse. Cancel is selected initially, and a visible `>` follows the selected choice. Choosing Cancel, pressing Esc again, or pressing the gamepad's back button restores the prompt. Closing this choice consumes that input so it cannot also start the game. Disable **Create Menu Controls** when supplying your own menu system. Set the loading font, fade timings and loading-screen colors in the catalog. The pause menu includes Main Menu.

Menu controls are authored in `Assets/Scenes/Menu.unity`. Edit **Title Canvas > Press Any Key To Start** for the prompt's font, material, size, color and wording. Edit **Scene Menu Controls > Actions > Quit or Cancel > QUIT Button > Quit Label** or **CANCEL Button > Cancel Label** for the choice labels. Each button also has a **Selection Arrow** TextMeshPro child whose glyph, font, size, color and position are editable. These are ordinary scene components, editable before Play, and the loader preserves their styling. **Scene Menu Controls** has a **Scene Title Menu** component linking the title effect, prompt Canvas Group, choices, buttons, arrows and error text. **Prompt Reveal Seconds** controls the reveal after assembly. The catalog's font/position only supplies defaults for newly generated fallback controls; it does not overwrite this scene UI.

All requests use asynchronous single-scene loading beneath an opaque cover, then reveal the destination after its Start methods and arrival setup. Fades use unscaled time, including when leaving pause. Input and pausing are blocked through the transition. Repeated clicks, unknown IDs, duplicate IDs, missing build scenes and travel to the current scene are rejected before changing the screen. A backend failure clears the screen and input lock. Lifecycle listeners are isolated so one failed listener cannot strand the loader.

Area travel keeps the same Character prefab instance alive. Its current health, mana, movement state, and other runtime components stay with it, while the run build keeps abilities, upgrades, passives, rewards, claims, growth and depth. Each destination's placed character acts as a spawn and camera guide, then is replaced by the surviving character. The Grassland scene's older inactive Character is treated as a guide; entering Grassland directly creates the prefab assigned in **Game Scene Catalog > Character Prefab**. An area without a placed character can supply a **Scene Spawn Point** with **Default Spawn** checked.

Start Game and entering a scene marked **Starts New Run** begin with a new character and the **Blank Slate** preset (Strike and Mend, no passives or upgrades). Dungeon has this flag. Pressing Play directly in any area also starts a fresh build for testing. Health and mana refill from the character prefab; a new reward seed and empty progression start at depth zero. Returning to Menu releases the character and clears the run. Reload Current retains the current character and run. `Assets/Resources/Combat/Build/SceneRunSettings.asset` controls the starting preset and seed. Main Menu ends the current encounter/conversation; regular travel waits until they finish.

## Add an area

1. Create **Rythm RPG > Scenes > Scene** in the Project window, assign its Unity scene, and give it a unique stable ID and display name. IDs remain stable even if scene filenames change.
2. Add it to the catalog's **Scenes** list. Use **Tools > Rythm RPG > Scenes > Sync Catalog to Build Settings**. The build preprocessor also refreshes paths and checks the catalog automatically, while retaining unrelated build entries.
3. Add **Scene Exit** to a door, portal or UI event host and assign the destination. Call its `Load()` from a UnityEvent, or enable **Load On Player Enter** on a 3D/2D trigger. **Require Interact** uses the game's rebindable Interact control. Existing levels are not given invented exits.
   **Wait Until Available** lets a dialogue-entry UnityEvent call `Load()` while its conversation is still open; travel is queued and begins immediately after the conversation closes. Leave **Load On Player Enter** off when the exit should be controlled only by dialogue.
4. Optionally add **Scene Spawn Point** in the destination and give it an ID. Enter that ID on the exit. Put **Scene Player Spawn** on the placed character root. That scene instance becomes a spawn/camera guide during travel; the surviving player takes its place. If the area has no placed character, check **Default Spawn** on one spawn point. Empty Entry Point uses that start or the placed character's position. Unknown entry points fall back to the area's start with a warning. CharacterController, rigidbody velocity and Cinemachine follow positions are handled during arrival.
5. Check **Starts New Run** on an area's scene definition only when entering it should replace the player and clear run progression. Dungeon is configured this way; regular areas leave it off.

## Code and future integration

```csharp
GameSceneLoader.Ensure().Load("grassland", "from-dungeon");
GameSceneLoader.Ensure().StartGame();
GameSceneLoader.Ensure().ReturnToMenu();
GameSceneLoader.Ensure().ReloadCurrent();
```

`CanTransition` adds game-specific gates. `TransitionStarted`, `PreparingActivation`, `DestinationReady`, `TransitionCompleted` and `TransitionFailed` connect future music, save data or arrival systems. `IGameSceneBackend` separates Unity loading from the transition flow so a future Addressables backend can reuse the cover, input handling and lifecycle. The current backend deliberately replaces one gameplay scene at a time; additive world streaming requires its own ownership/unloading policy.

Validation covers catalog IDs, build availability, repeated requests, guards, backend failure cleanup and activation/event ordering. Character persistence and the Dungeon reset should be checked in Play Mode after changing character prefab or scene camera bindings.
