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
