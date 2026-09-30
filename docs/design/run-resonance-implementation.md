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
- `Combat/Tests/EditMode/RunBuildTests.cs`: 27 tests covering the acceptance criteria.

### Reward selection screen

`Build/UI/`: `RewardSelectionScreen` (code-generated uGUI + TMP, overlay canvas above the result screen, same pattern as `CombatResultScreen`), `RewardCardView`, `RewardChoiceButton` (mouse), `RewardSelectionStyle` (texts, colours per reward kind, sizes, motion, keys, sounds; font falls back to the result style's). `CombatController.TerminalRoutine` shows it after a victory when a run build is active (`showRewardScreen`, optional scene/prefab screen). Menus: *Tools > Rythm RPG > Combat > Build > Create Reward Selection Style Asset* / *Create Reward Selection Screen In Scene* (editable copy the controller will find).

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

- The ability icons don't show mana cost. The panel shows the effective cost, and usability dimming uses it.
- The result screen doesn't show the new build attribution rows yet. The data is on `CombatReport`.
- `RunBuild.Current` is held in memory for the session. The panel saves it to PlayerPrefs. It is not wired into a game save.
- Only one sample status (Burn). Interrupt / control effects and boss control limits (spec 9.1, 9.2) have data hooks (status responses) but no control effect yet.
- None of this has been run in Unity yet. Everything compiles against Unity API stubs, and the 27 new EditMode tests pass there, together with the existing `AbilityResolutionTests`. Run the EditMode tests in Unity and a play session per build.
- The numbers are placeholders. Evaluate per the spec's §13 (mana economy, turns survived, passive contribution) after playtesting.
