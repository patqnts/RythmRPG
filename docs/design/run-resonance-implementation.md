# Run build system — implementation notes

Companion to `run-resonance-architecture.md` (the spec). This file covers what was built, where it lives, and how to try the sample builds in play mode.

## How to test the sample builds

1. Enter play mode in the scene you normally fight in.
2. Press **F11** to open the **Run Build** panel. It is available in the Editor and in Development Builds only, and F1 now lists it too.
3. On the **Build** tab, click **Use** next to a sample build:

   | Build | Playstyle | Slots | Passives |
   |---|---|---|---|
   | Mirror Guard | Parry / Deflect | Strike, Riposte, Mirror Stance, Mend | Counter Preparation, Deflection |
   | Pyromancer | Glass Cannon | Strike, Fire Bolt, Inferno, Focus | Glass Heart, Mana Surge, Pyromancy, Conservation |
   | Bulwark | Tank | Shield Bash, Strike, Bulwark, Mend | Fortitude, Iron Skin, Second Wind, Tidal Healing |
   | Wanderer | Adaptable | Strike (+Searing Edge), Spellblade, Rally, Frost Lance | Versatility, Follow-Through, Measured Execution, Arcane Thrift |
   | Riposte Mage | Parry + Glass hybrid | Strike, Riposte (+Honed), Fire Bolt (+Quickcast), Siphon | Counter Preparation, Glass Heart |
   | Blank Slate | Reward progression | Strike, Mend | none |

   Slot 1–4 = lane key 1–4 during ability selection.

4. Start a battle. If a battle is already running, click **Restart battle with this build**.
5. Watch the **Combat** tab while you play. It shows:
   - counter charges, shields, buffs and statuses, plus which passives are active
   - the last cast's breakdown: paid cost, performance, base → modified → affinity → dealt, and each modifier line
   - the last enemy turn's judgements, the encounter totals and the event log
6. Win a battle. **F4** kills the enemy instantly. After the result screen closes, the **reward selection screen** shows 3 cards (new ability / upgrade / passive) with previews:
   - Pick with the arrow keys + Enter/Space, number keys 1-3, a gamepad (D-pad + South) or the mouse (hover + click). Backspace skips; the offer stays unclaimed and can still be taken from the panel.
   - A new ability with full slots opens a **Replace which ability?** step: each slot shows its consequences (upgrades go to reserve with it, passives that turn inactive). Your only 0 MP action is greyed out. Row 5 keeps the new ability in reserve.
   - Retrying the fight shows the same offer; a claimed offer is never shown again.
   - The panel's Rewards tab can reopen the screen for any unclaimed offer ("Open the reward screen for this offer").
7. On the **Enemy** tab, swap in a test affinity profile to try elements against the current enemy:
   - Fire-resistant
   - Armored (physical ×0.6, lightning ×1.5)
   - Burn-immune
   - Frail
8. **Build** tab extras:
   - Move abilities between slots or to reserve. Dedicated upgrades move with their ability.
   - Level passives and add any content from the registry.
   - **Save** / **Load** the build (PlayerPrefs).

Things worth trying per build:

- **Parry:**
  - Play the enemy turn for Perfects. The charges show on the Combat tab and Deflection reflects damage.
  - Cast **Mirror Stance** (reflect 60% for 2 enemy turns), then **Riposte** to cash in charges (+30% each).
  - An enemy can die on its own turn from reflection; the victory path handles that.
- **Glass Cannon:**
  - Max HP drops to 65%.
  - **Inferno** (45 MP) triggers Mana Surge and Burn. Burn ticks at each enemy turn start and can kill the enemy there.
  - A well-played **Strike** restores 5 MP (Conservation).
  - **Focus** primes the next attack by +60%.
- **Tank:**
  - Shields cap at 30% max HP.
  - Iron Skin prevents at most 40 damage per enemy turn.
  - Overhealing with **Mend** turns into shield (Second Wind).
- **Adaptable:**
  - Alternate roles, e.g. Rally → Spellblade → Frost Lance, to trigger Versatility (+20%).
  - Rally's buff and Follow-Through each give a next-attack bonus.
  - Against Fire-resistant, Frost Lance hits ×1.5 while Spellblade's fire half is halved.

Sample numbers scale from the current `BasicAttack` Base Power, so they follow your tuning. They are content for playtesting, not balanced values.

## Where things live

`Assets/Scripts/Combat/Build/`:

| File | Spec section | Role |
|---|---|---|
| `BuildTags.cs` | 4.1, 5.2 | `AbilityRole` / `AbilityDelivery` flags, `EffectKind`, `ModifierGroup`, `TurnBoundary`, `StackPolicy`, `EffectFilter` |
| `AbilityResolver.cs` | 11.1 | `AbilityQuote` (effective cost / cooldown / effects / power), `AbilityResolver` |
| `CastSnapshot.cs` | 5.3, 11.1 | Resolved action snapshot: paid cost, reserved buffs, grouped modifiers (caps, stacking keys), outcome breakdown and attribution |
| `BuildAbilityEffects.cs` | 4, 7 | New ability effects: `GainShieldEffect`, `ApplyBuffEffect`, `ApplyStatusEffect`, `SpendCountersEffect`, `GainCountersEffect` |
| `CombatBuffs.cs` | 10.11 | `ShieldBuff`, `ReflectBuff`, `DamageReductionBuff`, `NextAttackBonusBuff`, `StatusInstance` (each declares its tick boundary) |
| `PassiveDefinition.cs` | 6.2 | Passive asset (levels, requirement, exclusivity, stacking group, style tags) + `PassiveEffect` hook base |
| `PassiveEffects.cs` | 6.3, 7 | 15 passive behaviours, from `ActionModifierPassive` (melee / spell / element / healing) to `SteadyGuardPassive` (whole-turn trigger) |
| `AbilityUpgradeDefinition.cs` | 3, 8 | Dedicated upgrades (power, cost, cooldown, added effects) |
| `EnemyResponses.cs` | 9 | `EnemyResponseProfile`: per-element affinity + per-status response |
| `RunBuildState.cs` | 3, 8, 11.1 | Run-owned ability instances (4 slots + reserve), passives, offers, claim ids, JSON save; `RunBuild.Current` |
| `CombatDamageService.cs` | 11.1 | The single damage / heal / mana / shield path. One affinity per component, attributed events, payouts stop at combat end, secondary-effect budget |
| `CombatBuildRuntime.cs` | 11 | Encounter runtime rebuilt from the run build: passive hooks, counters, cast lifecycle, defense transactions, buffs / statuses, report attribution |
| `CombatEvents.cs` | 11.2 | `CombatEvent` (attempted / prevented / actual, slot vs input lane vs chart lane, root cause) + `CombatEventLog` (idempotency keys, per-root budgets) |
| `RewardDirector.cs` | 8 | Eligible offers, previews (replacement consequences, inactive passives), claim once |
| `BuildContentCatalog.cs` / `BuildPreset.cs` | – | Content lists, id registry (saves, reward pool), presets |
| `BuildBalanceRules.cs` | 5.3, 7 | Guard rails: group caps, affinity clamp (0.25–2), shield cap, counter budget, offer size |
| `Samples/SampleBuildLibrary.cs` | 7 | Code-defined sample abilities / upgrades / passives / presets / enemy profiles |

Also:

- `Combat/Debug/RunBuildDebugPanel.cs`: the F11 panel.
- `Scripts/Editor/BuildSampleExporter.cs`:
  - *Tools > Rythm RPG > Combat > Build > Export Sample Build Content* saves the samples as editable assets in `Resources/Combat/Build/Samples`. Exported assets win over the code definitions with the same ids.
  - *Create Build Balance Rules Asset* creates the rules asset.
- `Combat/Tests/EditMode/RunBuildTests.cs`: 29 tests covering the acceptance criteria, effect icons and loadout validation.

### Reward selection screen

`Build/UI/`: `RewardSelectionScreen` (code-generated uGUI + TMP, overlay canvas above the result screen, same pattern as `CombatResultScreen`), `RewardCardView`, `RewardChoiceButton` (mouse), `RewardSelectionStyle` (texts, colours per reward kind, sizes, motion, keys, sounds; font falls back to the result style's). `CombatController.TerminalRoutine` shows it after a victory when a run build is active (`showRewardScreen`, optional scene/prefab screen). Menus: *Tools > Rythm RPG > Combat > Build > Create Reward Selection Style Asset* / *Create Reward Selection Screen In Scene* (editable copy the controller will find).

### Build HUD and loadout panel

- **Passive column** (`Build/UI/PassiveIconColumnView`): every owned passive as a framed icon down the left side during battle, in acquisition order. The level shows in the corner (I, II, III). Inactive passives (no compatible ability equipped) are dimmed. A tile flashes when its passive triggers: any combat event it sources, or a modifier it added to a cast.
- **In-effect icons** (`Build/UI/CombatEffectIconsView`):
  - Above the player's health bar: shield, buffs, lane wards and counter charges.
  - Under the enemy's bar: statuses such as Burn.
  - Each icon is the ability or passive that applied the effect, with its turns left in the corner. The number turns red on the last turn. Anything in the `CombatModifierSystem` that implements `ICombatEffectIcon` shows up.
- **Shield barrier** (`ResourceBarView.SetBarrier`): the shield is a white segment right after the health fill, like League of Legends. When health + shield is more than max HP, the whole bar rescales so both fit. The number reads `80/100 +25`. Bars placed in a scene before this change get the barrier part automatically.
- **Loadout panel** (`Build/UI/LoadoutPanel`): toggle it with **Tab** (keyboard) or **Select / View** (gamepad). The key is rebindable as *Loadout* on the Controls page.
  - **Outside battle** you can rearrange:
    - Arrows / WASD / d-pad / stick move the cursor.
    - Enter / South picks an ability, then places it on a slot. Slots swap; a reserve ability replaces the slot's occupant, which goes to reserve.
    - X / West sends a slot to reserve.
    - Backspace / East cancels a pick or closes the panel.
    - A change that would leave no ability, or no 0 MP ability, is refused and undone (`RunBuildState.LoadoutProblem`).
    - Movement and Interact are held while it is open (`GameInput.BlockGameplay`).
  - **During ability selection** it is view only. It shows cooldowns and whether you can afford each ability. It only uses keyboard arrows and the right stick, so it never takes input the lanes use. It closes itself when you choose an ability.
  - It is created automatically in every scene. A panel placed in a scene takes over.
- **Style**: `BuildHudStyle` (*Create > Rythm RPG > Combat > Build > Build HUD Style*, saved as `Resources/Combat/UI/BuildHudStyle`). Every tile look (column, effect row, panel) has these settings:
  - **Frame sprite**: 9-sliced when the sprite has borders. When empty, the frame is a plain colour.
  - **Frame colour** and **frame thickness**.
  - **Frame on top**: for ornate frames with a transparent centre.
  - **Background sprite** and **background colour**.
  - **Icon inset**.
  - **Size**.

  The style also sets the column's position, spacing and maximum height, the category colours, the trigger flash, the effect-row offsets and colours, and the panel's texts, colours and keys.
- **Icons**: `PassiveDefinition` has a new `icon` field. With no icon, the tile shows the passive's first letter on its category colour. Buffs, statuses, shields and wards now remember the icon of the ability or passive that applied them. The reward cards use passive icons too.

### Elements: marks, reactions and board effects

Full content list and numbers: `abilities-and-passives.md` ("Elements", "Elemental abilities", "Elemental passives").

- **Where it lives:** `Build/Elements/`:
  - `ElementalRules`: tuning, inside the Build Balance Rules asset under Elements.
  - `ElementalMarks`: marks, the Wind quirk, discharge and the six reactions.
  - `ICombatNoteBoard`: the board seen by the runtime.
  - `ElementalBuffs`: Storm Ward, Flame Guard, Stone Wall, Dodge, Regen.
  - `ElementalEffects`: the 11 new ability effects.
  - `ElementalPassives`: the 11 new passives.
- **Also added:** `Notes/RunnerNoteBoard.cs` (the board over the pattern runner) and `VFX/LightningArcVfx.cs` (a code-made placeholder arc).
- **Marks are statuses** (`burn`, `soaked`, `static`, `cracked`), so enemy status responses, icons and the F11 panel all work with them. Burn from Inferno / Ignite now stacks with the new Burn sources.
- **Reactions** are checked on every elemental hit (ability, passive or zap damage, never status ticks or reaction damage) and whenever a mark is applied. Each reaction fires once per root (cast or enemy note), through the event log's idempotency keys. Passives adjust them through `PassiveEffect.ModifyReaction`.
- **Zaps and walls** resolve enemy notes as `NoteResolutionSource.Modifier` Perfects:
  - The controller gives them 0 damage and skips combo and stats; mana already ignores non-player notes.
  - The VFX skips their judgement popup.
  - The runtime counts them as `EnemyTurnSummary.Cleared`, not as misses or opportunities.
  - Limits: the per-source cap, then the per-turn cap in the rules, then the enemy profile's cap.
- **Live chart effects** (`ILiveChartEffect`, Chain Spark) receive the player's judgements during their own chart through `CombatBuildRuntime.OnChartJudgement`.
- **Stagger:** `TryStagger` queues it and `ConsumeStagger` runs in `EnemyTurnRoutine`. Enemy profiles have new `maxStaggers` / `maxZapsPerTurn` fields (0 = no limit).
- **Live costs:** `CombatBuildRuntime.Active` lets `AbilityResolver` apply live cost changes (Tailwind's one-shot discount) to quotes in battle. The discount is used up at commit.
- **Tests:** `Tests/EditMode/ElementalTests.cs`, 23 tests.

### Run progression (growth, healing, enemy scaling)

Design: enemies get harder mainly through **denser patterns**, with per-note damage growing slowly. The player grows by picking **growth cards** that keep max HP in step with enemy damage. All the numbers are in the Build Balance Rules asset, under Progression.

- **Growth card on every victory.** Every reward offer gets a 4th card next to the usual 3 (the card row shrinks to fit). It is one of three cards:

  | Card | Gives | Weight |
  | --- | --- | --- |
  | Vitality | +100 max HP | 2 |
  | Focus | +15 max MP | 1 |
  | Resolve | +60 max HP and +8 max MP | 1 |

  - Growth is stored on the run (`RunBuildState.BonusMaxHealth` / `BonusMaxMana`, saved with the build) and applies from the next battle.
  - Growth health is added before percentage passives, so Fortitude and Glass Heart scale it too.
  - Max mana now has a run override (`PlayerCombatant.SetMaxManaOverride`, `BaseMaxMana`).
- **Full heal after each win.** `healAfterVictory` = 1 restores all max HP after the reward screen. Defeat still restores the battle-start state.
- **Run depth** = victories this run (`RunBuildState.Depth`, +1 per win). It controls three things:
  - **Enemy max HP:** +10% per win, capped at x3. The scaling is applied at encounter start (`EnemyCombatant.SetMaxHealthScale`).
  - **Enemy note damage:** +5% per win, capped at x2 (`CombatBuildRuntime.NoteDamageScale`, applied in the controller's defense damage).
  - **Denser patterns (authored):** attack sequences have **Min Run Depth / Max Run Depth**. Add denser variants of an enemy's attacks with a Min Run Depth and they only appear later in a run; retire easy ones with a Max Run Depth. If no sequence fits the depth, all of them are used.
- **Rule of thumb:** a Vitality card is +10% of the default 1000 HP. Taking it about every other win keeps pace with +5% note damage per win, so a Miss keeps costing a similar share of health.
- **F11 > Build > Progression:** shows the growth and depth, and has buttons for +HP, +MP and depth -1 / +1 to test scaling.

### Mana from accuracy (end of the enemy turn)

Mana used to come per note hit (0.5 MP per Perfect), which meant denser patterns paid more mana. It is now paid **once, when the enemy turn ends, by the turn's accuracy**. So later, denser patterns make the turn harder without flooding the mana bar. The settings are in `Resources/Combat/Balance/CombatResourceRules`, under Mana Source and Turn Accuracy mana.

- **Accuracy:** each note the player had to play counts toward the average: Perfect 1, Good 0.7, Bad 0.3, Miss 0.
  - Notes that pass unplayed count as Misses.
  - Zapped or walled notes and notes cleared by the system do not count.
- **Payout tiers:**

  | Turn accuracy | Mana |
  | --- | --- |
  | 95%+ | 25 |
  | 85%+ | 18 |
  | 70%+ | 12 |
  | below 70% | 5 |

  - The flat +5 at the start of the player turn is unchanged.
  - A turn with no notes to play pays nothing.
  - A turn that ends because the enemy died pays nothing, since the battle is over.
- **Ability charts give no mana** in this mode.
- **Feedback:**
  - While the enemy turn plays, the mana bar shows a faint **pending segment** and a "+N" after the number. It shows what the turn would pay if it ended now, so it grows and shrinks as you hit and miss (`ResourceBarView.SetPending`, `CombatController.PendingMana`).
  - At the end of the turn, the fill grows into the segment and "+N MP  94%" pops above the player.
- **Per Hit** still exists as a Mana Source option; it uses the old per-judgement weights.
- **Test:** `ResourceRules_TurnAccuracyMana_PaysByTier_NotByNoteCount` in `AbilityResolutionTests`.

### Play mode without domain reload

The project has Enter Play Mode Options on (no domain reload), so statics survive between play sessions while every ScriptableObject made in code is destroyed when play stops. That emptied the chosen build on the next play. `RunBuild` now resets the sample cache and registry at play start and rebuilds the current build from its ids (`RuntimeInitializeLoadType.SubsystemRegistration`).

## Changes to existing code

All changes are additive. Serialized enum values, asset fields and numbers are unchanged, and with no run build active (**Use legacy loadout**) combat behaves as before.

- **`AbilityDefinition`:**
  - New `roles`, `delivery` and `description` fields.
  - `Roles` falls back to the legacy `AbilityType`.
  - A `Builder` for code-made definitions.
- **`AbilityEffects`:**
  - `AbilityResolution` freezes every total at construction. Hits only distribute it.
  - `DealDamageEffect` has an element source: ability (default), physical or a specific element.
  - `HealEffect` has its own element and never inherits the ability's.
  - Effects route through the damage service when a build runtime is present.
  - New `FreezeAmount` / `BeforeFreeze` / `Describe` hooks.
- **`AbilityRuntimeInstance`:**
  - Resolves an `AbilityQuote`, so `CanUse`, the preview and `Commit` use the same effective cost.
  - `LastCommit` keeps the paid cost.
- **`AbilitySlotController`:**
  - Builds slots from `RunBuild.Current` when it has equipped abilities (slot i = lane i+1). Otherwise it uses the old assignments / `DefaultLoadout`.
  - `SlotIndexOfLane` keeps slot and input lane distinct.
- **`RhythmAbilitySystem`:**
  - Passes the chart's cancelled flag through.
  - Runs `PreOutcome` before freezing and `CastResolved` after the last hit.
  - Uses the committed quote's effects, which include upgrade effects.
- **`CombatController`:**
  - Adds `CombatBuildRuntime`.
  - Reconstructs the build at `BeginBattle`, before the battle-start snapshot.
  - Adds turn-boundary hooks.
  - **Enemy defeat on the enemy's turn or at a boundary** (reflection, burn) goes through a single `EnemyDefeatedRoutine`. The pattern is cancelled without creating misses.
  - The report gets build attribution.
  - New public members: `BuildRuntime`, `Modifiers`, `EncounterKey`.
- **`RhythmPatternRunner`:**
  - `DefenseDamageApplier`: the controller's damage transaction.
  - `DefenseNoteSettled`: fires after the damage transaction. `NoteResolved` still fires before damage, as the spec notes.
- **`CombatModifierSystem`:**
  - `ITurnBoundaryModifier`.
  - Player-turn boundaries.
  - `OfType` / `Find` / `Remove` / `NotifyChanged`.
- **`PlayerCombatant`:** `BaseMaxHealth` + `SetMaxHealthOverride` (build max-HP changes).
- **`EnemyDefinition` / `EnemyCombatant`:** `responses` profile, `AffinityFor`, runtime `SetResponseOverride`.
- **`GameInput`:** `Loadout` action (Tab / gamepad Select) with a Controls row, `LoadoutPressed`, and `BlockGameplay` / `UnblockGameplay`. While blocked, `MoveValue` / `InteractPressed` / `InteractHeld` read as idle.
- **`CombatUIController`:** creates and binds the passive column and both effect rows, and draws the shield as a barrier on the player's health bar. It has new optional scene slots for all three.
- **`ResourceBarView` / `CombatHudStyle`:** barrier segment (`SetBarrier`, `Barrier`), plus `barrier` colour / `barrierSprite` on each bar style.
- **`LaneWardModifier`:** implements `ICombatEffectIcon`. Ward effects set the ability's icon and name.
- **Elements (this pass):**
  - `CombatController`: note board, stagger, cleared notes skip damage / combo, live chart judgements.
  - `CombatVFXController`: no popup for cleared notes.
  - `StatusSpec.dealsTickDamage`.
  - `StatusInstance`: stack helpers; icon shows stacks.
  - `ShieldBuff.BreakHeal`, and `GainShieldEffect` gets `healOnBreak`.
  - `AbilityQuote.AllElements` / `DamageElements` include marks and zaps.
  - New event kinds.
  - `PassiveEffect` hooks: `StatusStackBonus`, `ModifyReaction`, `PreventLethal`.
- **`CombatReport`:** build attribution fields (contributions, reflected, status, resisted, absorbed, prevented, overheal, MP restored, unused benefits).

## Resolution order (as implemented)

1. **Commit.** The slot pays the quote's cost once and starts its cooldown. `CombatBuildRuntime.BeginCast` snapshots the cast and reserves pending next-attack bonuses, for damaging casts only.
2. **Chart.** The chart plays; raw judgements are untouched.
3. **`PreOutcome`.** Performance is final. It adds reserved bonuses (Buff group) and passive pre-outcome modifiers (Passive / Execution / Counter groups). Charts that were cancelled or had no notes get no execution bonuses.
4. **Freeze.** `AbilityResolution` runs the effects' `BeforeFreeze` (Riposte spends counters), then freezes every total:
   - component damage = authored × rhythm × grouped modifiers × clamped affinity
   - healing never sees enemy affinity or damage bonuses
5. **Hits.** Hits distribute the frozen totals through `CombatDamageService`.
6. **`CastResolved`.** Post-outcome triggers (Conservation, Follow-Through), bounded once per cast by idempotency keys.

**Modifier maths:**

- Percentages add within a group; groups multiply in the order Upgrade → Passive → Buff → Execution → Counter.
- Each group is clamped to −90% … +200%, and the product is capped at ×4.
- Stacking groups (e.g. `melee-damage`, `element-fire`) add up to their cap.

**Turn boundaries:**

- Passive hooks run first, then buff / status countdowns, in the order they were added.
- Enemy turn end: whole-turn triggers fire once, then durations expire.
- Choosing an ability never consumes a duration.

**Enemy note damage:**

- Order: judgement rules → lane ward → passive mitigation → damage-reduction buffs → shield → health.
- After the transaction, `DefenseNoteSettled` drives counters, reflection and the whole-turn tally. Only notes resolved by player input count.

## Not done yet / open

- The elemental system compiles against stubs and its 23 tests pass there, but it hasn't been played. Check in play mode:
  - zap timing and feel
  - Stone Wall's lead time
  - the placeholder lightning arcs
  - whether cleared notes should play a different break animation

  Quake Slam wants its own hold-note chart; it borrows Basic Attack's for now.
- The HUD, barrier and loadout panel compile against stubs but have not been seen on screen yet. Check the layout in play mode and tune `BuildHudStyle`.
- The ability icons don't show mana cost. The panel shows the effective cost, and usability dimming uses it.
- The result screen doesn't show the new build attribution rows yet. The data is on `CombatReport`.
- `RunBuild.Current` is held in memory for the session. The panel saves it to PlayerPrefs. It is not wired into a game save.
- Only one sample status (Burn). Interrupt / control effects and boss control limits (spec 9.1, 9.2) have data hooks (status responses) but no control effect yet.
- None of this has been run in Unity yet. Everything compiles against Unity API stubs, and the 29 new EditMode tests pass there, together with the existing `AbilityResolutionTests`. Run the EditMode tests in Unity and a play session per build.
- The numbers are placeholders. Evaluate per the spec's §13 (mana economy, turns survived, passive contribution) after playtesting.
