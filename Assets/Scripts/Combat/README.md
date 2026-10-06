# Turn-based rhythm combat

`CombatController` coordinates the encounter but delegates input, chart playback, judgement,
abilities, combatant stats, modifiers, UI, and VFX to focused services. Runtime communication is
event-driven: notes report a `RhythmJudgementResult` to `RhythmPatternRunner`; the runner applies
enemy-defense misses or produces an aggregated ability performance; ability executors turn that
performance into a combat outcome.

The turn flow is:

`BattleStart -> EnemyTurnStart -> EnemyTurnExecuting -> EnemyTurnEnd -> PlayerTurnStart ->
PlayerAbilitySelection -> PlayerAbilityExecuting -> PlayerTurnEnd`, then either another enemy turn,
`Victory`, or `Defeat`.

Configurable content lives under `Assets/Resources/Combat` for the initial scene migration:
`Abilities`, `Enemies`, `Patterns`, `Judgement`, `UI`, and `VFX`. New abilities implement
`IAbilityExecutor` and use an `AbilityDefinition`. Enemy content is assembled from
`EnemyDefinition`, `EnemyPhaseDefinition`, `EnemyAttackSequenceDefinition`, and `RhythmChart`
assets. The lane count comes from `LaneInputRouter` bindings and chart lane data; only the default
scene bindings use five lanes.

Basic Attack uses `Assets/Resources/Combat/Abilities/BasicAttack.asset`. Its final outgoing hit
projectile and timing windows are edited on the linked `AbilityVFXProfile.asset`: assign
`Impact Projectile Prefab` for a custom projectile, or leave it empty to use the generated fallback
bolt. `Impact Anticipation Duration` gives character animation time before firing, and
`Impact Settle Duration` gives the enemy hit reaction time before the turn advances.

The 2.5D lane presentation uses a level X/Z gameplay plane. At encounter start, the enemy's
`ProjectileHolder`, the judgement line, lane targets, and spawned notes are assigned the same
world height just above their ground-contact bounds. The player is positioned a short distance past
the judgement line in the notes' direction of travel,
which keeps the line between the enemy and player while the A/S/D/J/K controls remain screen UI.

## Run builds (abilities, passives, upgrades, affinities)

`Build/` implements `docs/design/run-resonance-architecture.md`: a run-owned `RunBuildState`
(`RunBuild.Current`) holds four ability slots + reserve, dedicated upgrades and passives.
`AbilitySlotController` builds its slots from it when one is active (otherwise the old
`DefaultLoadout`). Each encounter `CombatBuildRuntime` is rebuilt from the build; every damage,
heal, mana and shield transaction goes through its `CombatDamageService`. In play mode press **F11**
for the Run Build panel (sample builds, rewards, enemy affinity profiles). Details and the
sample-build test guide: `docs/design/run-resonance-implementation.md`.

## Combat Preview (attacks and note prefabs)

**Tools > Rythm RPG > Combat > Combat Preview** sets up how the player's attack sequences and note /
projectile prefabs look in combat. In Play mode it starts a preview battle against a scene enemy
(`CombatController.BeginPreview`): staged like a real battle, but it waits in Battle Start, notes deal
no damage, no mana is gained and the run build is left out. `Debug/CombatPreviewDriver` then plays a
`CharacterAttackSequence` (hits flash the enemy and show their share) or spawns note prefabs through the
real `RhythmPatternRunner` from a throwaway chart (lanes, count, travel time, auto-play / miss / play
yourself). The sequence can be edited inside the window; asset and prefab edits made in Play mode are kept.

Where a note spawns can be moved per prefab with a `NoteSpawnOffset` component (lane space: X side, Y up, Z forward
toward the hit line, or world space; separately for enemy attacks and player ability charts). `RhythmPatternRunner`
adds it to the spawn point in combat; the Combat Preview window edits it and shows a draggable handle in the Scene view.

## Note Kit (note / projectile prefabs) and the timing editors

`Notes/Kit/` is the data-driven note: one `CombatNote` component whose **Kind** sets how it is played (Tap, Hold,
Stationary, Stationary Hold, Mash, Pong) and a stack of **views** that set how it looks. Views never change timing or
judgement. Built-in views: Animator (the note's own, the attacker's or the target's animator, a state per moment),
Effect (spawn a prefab at the note / an enemy socket / the hit point, aim it, let it ride along, stop it), Beam, Link
(a line or chain segments from a socket to the note), Move (step the enemy up to the lane and back), Phase Objects,
Hold Tail, Mash Meter and Feedback (sound, shake). Scripts on a spawned effect or on the note itself that implement
`INoteViewListener` get every moment and tick (custom lasers, mimic chains...). `CombatSocket` names points on an
enemy (Mouth, Hand); a child with that name works too.

Every view action is a **cue**: a moment (Spawned, Reached Beat, Pressed, Hold Started, Hit, Missed, Resolved, Hold
End...) plus an offset in seconds. Spawned / Reached Beat / Hold End follow the chart clock (Reached Beat and Hold End
may be negative: "0.35 s before the beat"); the others fire that long after they happen.

**Tools > Rythm RPG > Combat > Note Designer** creates notes from recipes (sprite + animator built from sprite frames,
effect, chain, enemy-is-the-note, enemy + effect, empty), converts an old note prefab into a Combat Note copy (the
original is not touched), and shows the note's life on a frame timeline (spawn, approach, beat, hold, linger) with a
row per view: drag markers and bars to time them, drag a bar's end to change its length. It also checks the setup,
spawns the note in the Combat Preview battle (the playhead follows it) and assigns it to a chart's note type.

**Tools > Rythm RPG > Combat > Attack Sequence Editor** (or double-click a Character Attack Sequence) shows the
sequence on the same timeline, laid out exactly like `CharacterAttackPerformer` runs it: drag a bar to change the
step's delay, its end to change its length, red diamonds to move hits. Set Animation Lengths to the character's
controller for exact clip lengths (otherwise "?" marks a guess). Play runs it in Combat Preview, with slow motion.
