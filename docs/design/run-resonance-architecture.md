# Combat, Build, Magic, and Enemy System

**Design specification · Version 1.0**

## 1. Purpose and scope

Define a run-based build system for a dungeon-crawler RPG with turn-based rhythm combat. Players develop a recognizable playstyle through equipped active abilities and acquired passive upgrades, while adapting to enemy patterns and affinities.

This specification covers combat rules, abilities, magic, passive rewards, playstyles, enemy interactions, runtime architecture, and validation. Story, exploration, world progression, and character presentation are outside its scope.

The architecture and constraints below define the system. Ability names, element lists, status catalogs, reward amounts, and numerical balance values are content data. Examples illustrate supported designs rather than a mandatory content roster.

## 2. Core combat rules

1. The player has four active ability slots, each associated with an input button.
2. The player selects and executes one ability per player turn.
3. Mana controls ability affordability; it does not grant additional actions.
4. The selected ability uses a rhythm chart. Performance determines its outcome according to its effect definitions.
5. During enemy turns, the player defends by playing the enemy's rhythm sequence. Missed notes cause damage under the existing combat rules.
6. The slime's transformation into lane markers and the existing input/judgement system remain established gameplay.
7. Passive upgrades apply automatically and do not occupy active ability slots.
8. Damage, healing, or other effects triggered by passives do not create additional player ability selections.
9. An affordable basic action must remain available when the player has insufficient mana for other abilities.
10. Enemy defeat or player defeat can end combat during either side's turn.

Combat flow:

```text
Encounter begins
  → Enemy rhythm sequence
  → Player-turn resource and cooldown updates
  → Select one ability
  → Perform its rhythm chart
  → Resolve ability and eligible passive effects
  → Repeat until victory or defeat
```

Preserve existing judgement windows, lane mapping, music timing, and chart behavior. An ability may modify combat outcomes through explicit effects; an element attribute alone never changes these systems.

## 3. Build composition

| Layer | Contents | Lifetime |
|---|---|---|
| Active loadout | Four equipped ability instances | Run-owned; changed through ability acquisition/replacement |
| Passive collection | Acquired modifiers, conditional rewards, and conversions | Run-owned |
| Dedicated ability upgrades | Improvements attached to a specific ability instance | Follow that ability instance |
| Temporary combat state | Buffs, statuses, counters, stored bonuses, cooldowns | Defined encounter/action/turn lifetime |
| Enemy configuration | HP, attack sequences, phases, affinities, effect responses | Enemy content and encounter state |

**Active abilities + passive upgrades + tradeoffs create the playstyle.**

Players develop a strategy through reward choices. Playstyles are not exclusive character classes, and elements are not assigned to particular playstyles. Hybrids are supported within the same action, resource, and reward budgets.

## 4. Active abilities

### 4.1 Independent properties

| Property | Responsibility |
|---|---|
| Identity | Stable definition ID and runtime instance ID |
| Role tags | Damage, healing, buff, defense, resource support; multiple roles allowed |
| Delivery tags | Melee, ranged, spell, body technique, or another authored category |
| Element attributes | Identify elemental affiliation and compatible modifiers |
| Effects | Explicit damage, healing, protection, resource changes, buffs, or status applications |
| Cost | Mana required at commitment |
| Cooldown | Availability across player turns |
| Rhythm profile | Chart, judgement weights, performance scaling, success thresholds |
| Presentation | Animation, VFX, sound, and impact timing |

Role, delivery, element, and effect are independent. A melee attack can carry elemental damage. A spell can heal or protect without dealing damage. One ability can combine multiple effects, with its cost and output balanced against the complete package.

### 4.2 Costs and execution

- Basic physical actions provide an inexpensive or free foundation.
- Advanced melee techniques generally cost less mana than powerful spells.
- Magical enhancements can add mana cost when explicitly defined by the ability or enhancement.
- A buff or heal selected from an active slot consumes the player's action like an attack.
- Adding an element tag does not automatically add cost, damage, a status, or an extra input mode.
- Show the resolved mana cost and cooldown before commitment. Pay mana once when the action is committed.
- Execution quality affects results according to the ability's authored scaling. A poor result does not automatically refund its cost.

### 4.3 Slot ownership

Keep ability-slot IDs, input-action IDs, and chart-lane IDs distinct. Rhythm lane mappings can change between charts without changing the equipped build.

Global passives follow eligible actions wherever equipped. Dedicated upgrades follow their ability instance when moved between slots. When replacing an ability, preview the treatment of its dedicated upgrades and any passive that becomes inactive. Never transfer an ability-specific improvement to an unrelated replacement implicitly.

## 5. Element attributes and effect resolution

### 5.1 Elements describe affiliation

An element does not prescribe offense, defense, healing, or control. Each ability's explicit effects define its behavior.

For example, abilities sharing placeholder Element E can independently:

- Deal E damage.
- Heal the player through an E-attributed healing effect.
- Apply a protective effect.
- Apply a buff to a later action.
- Add an E damage component to a melee attack.

An element does not automatically cause damage over time, projectile slowing, interruption, reflection, or a reaction with another element. Such mechanics exist only through authored effects.

### 5.2 Precise modifier scope

| Modifier | Eligible target |
|---|---|
| Melee damage enhancement | Damage effects from eligible Melee-tagged actions |
| Spell efficiency | Mana costs of eligible Spell-tagged actions |
| Element E damage enhancement | Damage components attributed to E |
| Element E healing enhancement | Healing effects attributed to E |
| Status S duration enhancement | Explicit applications of S from eligible sources |
| Buff enhancement | The named strength, duration, or other supported parameter of an eligible buff |

Avoid unspecified bonuses such as 'elemental effects improved' unless the description lists the exact parameters changed.

For mixed-effect abilities, each effect must have explicit attributes or a documented inheritance rule. A damaging component's element must not accidentally determine the treatment of an unrelated healing effect.

### 5.3 Component damage

Resolve physical and elemental components separately before combining the cast's total damage:

```text
Component damage = authored amount
                 × applicable rhythm scaling
                 × applicable offensive modifiers
                 × target affinity for that damage type
```

Use declared modifier groups, a deterministic calculation order, caps, and consistent rounding. Calculate total amounts before distributing them over animation hits.

An elemental damage modifier affects only matching components. A modifier explicitly applying to all melee damage can affect all qualifying components of a melee action. Enemy resistance does not reduce unrelated self-healing or self-buffs.

Damage-over-time uses its declared damage type and applies affinity once per damage transaction. Its application strength and duration follow explicit snapshot and refresh rules.

## 6. Passive upgrades

Passives are acquired as rewards and remain separate from the active loadout. Several compatible passives may modify the same ability. Power is bounded through acquisition count, level limits, effect caps, stacking rules, and specific exclusivity groups.

### 6.1 Passive categories

| Category | Purpose |
|---|---|
| Damage enhancement | Improve eligible damage output |
| Melee enhancement | Improve physical-action or melee-specific effectiveness |
| Magic efficiency | Improve mana costs or bounded resource recovery |
| Elemental enhancement | Improve an explicitly scoped elemental effect |
| Support enhancement | Improve healing, protection, or temporary buffs |
| Rhythm-conditioned benefit | Reward defined execution quality |
| Action-sequence benefit | Connect actions across successive player turns |
| Conversion | Convert one earned benefit into another under a cap |

Use both unconditional and conditional upgrades. A build must not require every reward to depend on Perfect input.

### 6.2 Passive contract

Each passive defines:

- Stable ID, description, level values, and maximum level.
- Acquisition prerequisites and compatible ability/effect tags.
- Scope: player-wide, tag-filtered, element/effect-filtered, or a specific ability instance.
- Trigger and conditions, including combat phase and accepted event sources.
- Exact effect or parameter modified.
- Stacking group and replace, refresh, or capped-addition policy.
- Per-cast/per-turn limits, consumption point, and expiry.
- Whether generated effects can trigger other effects.

Acquisition eligibility and activation conditions are separate. Owning a spell can make a passive eligible to appear; meeting a rhythm threshold can determine when its benefit activates.

### 6.3 Illustrative passive patterns

| Pattern | Effect | Required bound |
|---|---|---|
| Melee enhancement | Increase qualifying melee damage | Defined stacking group |
| Elemental specialization | Increase a named effect for one element | Exact component/effect scope |
| Measured execution | Improve an attack after a sufficiently strong chart result | Once per cast; visible threshold |
| Counter preparation | Successful enemy-turn defense prepares a later attack bonus | Fixed turn budget and expiry |
| Conservation | Successful low-cost attacks help fund later spells | Once per cast; resource cap |
| Follow-through | A successful buff or heal improves a later attack | One defined pending benefit |
| Buff mastery | Improve an eligible buff's strength or duration | Named parameter; bounded extension |
| Recovery conversion | Convert a portion of overheal into protection | Shared capacity and expiry |

These patterns demonstrate the system's content range. They do not require these names or every pattern to be implemented.

## 7. Playstyle system

A playstyle must have a recognizable combat objective, supporting abilities and passives, and a meaningful tradeoff. It should change action selection, resource use, or the value of defensive execution.

The following reference archetypes guide content design. The system supports additional archetypes and hybrids.

### 7.1 Parry / Deflect

**Objective:** turn accurate enemy-turn defense into offensive value.

- Active support: explicit reflection buffs, counter-spending attacks, and dependable direct offense.
- Passive support: conditional reflection, improved counter yield, or stored next-attack benefits.
- Decision: invest in preparing counters, capitalize on earned counterpower, or attack directly.
- Tradeoff: reliance on successful defense and qualifying opportunities; counter investment competes with direct offensive upgrades.

An active enabler consumes a preparation action and can justify a stronger payoff. A passive enabler can function without that action but needs a correspondingly bounded benefit. A functional build must not require obtaining both unless a specific combination explicitly calls for them.

Prevented damage from a missed note is not a successful parry. Reflection follows explicit eligibility and source rules. Required enemies must provide meaningful opportunities for the supported counter strategy, with declared alternatives where a particular effect is inapplicable.

### 7.2 Glass Cannon

**Objective:** spend mana on high damage while accepting low survivability.

- Active support: powerful spells, burst buffs, and an affordable conservation action.
- Passive support: output improvements with a survivability cost, plus bounded mana-spending benefits.
- Decision: commit to burst now or conserve enough resources to survive and attack later.
- Tradeoff: low HP tolerance, high mana expenditure, and vulnerable conservation turns.

Express survivability sacrifices clearly. Reduced maximum HP is a suitable implementation option; do not make deliberately missing rhythm inputs the efficient route to damage bonuses. Mana-spending triggers use actual paid cost and cannot repeatedly count refunded resources as new spending.

### 7.3 Tank

**Objective:** maintain survivability while making steady offensive progress.

- Active support: protection, recovery, and reliable attacks.
- Passive support: greater HP, finite mitigation, and improved defensive efficiency.
- Decision: attack now or spend an action maintaining enough protection for upcoming enemy pressure.
- Tradeoff: lower damage output and exposure to more enemy sequences.

Protection and recovery need action, resource, capacity, or duration limits. High HP does not change raw judgements or make missed notes successful. Balance total endurance, including healing and mitigation together; avoid permanent safety loops.

### 7.4 Jack of All Trades / Adaptable

**Objective:** gain value from selecting and combining different tools.

- Active support: a useful mix of attack, magic, buff/defense, and recovery actions.
- Passive support: resource benefits and bonuses connecting distinct action roles.
- Decision: choose the action that best meets the current threat and prepares a useful next turn.
- Tradeoff: lower specialized peaks and greater dependence on action sequencing.

Its strength is useful flexibility, not merely small bonuses to every stat. Support-to-attack or melee-to-spell links can provide a positive identity. Do not require every element, a rigid rotation, or duplicate abilities placed on different buttons to satisfy variety conditions.

### 7.5 Build development

Rewards perform three functions:

1. **Enable:** establish a usable strategy immediately.
2. **Reinforce:** improve effectiveness, reliability, or tactical options.
3. **Connect:** provide a bounded interaction with another strategy.

An early enabler should work without additional rare prerequisites. Players can deepen a strategy or combine it with another. Hybrid rewards must not cheaply remove all costs of both archetypes; high durability and top-tier burst cannot coexist without meaningful investment and constraints.

## 8. Reward system

Support three clearly identified reward types:

| Reward | Result |
|---|---|
| New ability | Adds an available action and may require loadout replacement |
| Dedicated ability upgrade | Improves a specific ability instance |
| Passive upgrade | Adds or levels a modifier in the passive collection |

A reward choice presents eligible options based on the current loadout, owned effects, prerequisites, maximum levels, and exclusions. Include opportunities to reinforce the player's direction and useful alternatives. Passive acquisition remains available when all active slots are occupied.

A status upgrade requires an available source of that status. A melee improvement requires a usable compatible action. If eligibility produces too few options, use a broadly useful fallback rather than an unusable reward.

Duplicate rewards either level an existing passive under its defined rules or are excluded. A passive whose last compatible action is removed remains owned but visibly inactive. Reward previews identify affected abilities, expected changes, costs, conditions, and any replacement consequences.

Save generated offers and unique claim IDs. Reopening results, retrying an encounter, or reloading must not generate duplicate rewards or free rerolls. Acquisition frequency, reward weights, and numerical progression remain configurable content parameters.

## 9. Enemies, affinities, and bosses

### 9.1 Independent enemy properties

| Property | Responsibility |
|---|---|
| Attack behavior | Charts, sequences, selection rules, damage pressure, and phases |
| Damage affinity | Effective modifier for each incoming damage type |
| Status response | Susceptibility, duration, potency, and stacking limits for individual statuses |
| Control response | Explicit limits on any authored interruption or action-disruption effect |

Elemental resistance affects matching damage components, not every effect on an ability. Status immunity and control restrictions are independently authored and communicated.

### 9.2 Completion safeguards

- Required enemies must not demand a particular randomly acquired element.
- Keep resistance differences moderate enough for supported specialized builds to remain productive.
- Preserve a usable affordable action, but do not rely on that fallback to justify making the rest of a build ineffective.
- Apply one resolved affinity per component. Combine other mitigation systems explicitly rather than multiplying hidden resistances toward immunity.
- Self-healing and self-buffs are unaffected by an opponent's unrelated elemental affinity.
- Bosses can limit specific control effects without disabling the entire ability or all associated passive value.
- Required boss gates or shields need an intended broadly available solution; elements may improve efficiency.
- Show relevant affinities, effect restrictions, and phase changes clearly.

Keep fixed enemy identities and patterns consistent. Different ability and passive combinations can provide replay variation without randomizing resistances each encounter.

## 10. Rhythm and trigger rules

1. Preserve raw judgements independently of passive benefits and damage prevention.
2. Grant precision rewards only from eligible player execution, not modifier-generated results or automatic clears.
3. Treat a cast as one cast even when its animation hits multiple times.
4. Holds, repeated presses, and dynamic rallies must not create unintended repeated payouts.
5. Use bounded per-cast/per-enemy-turn rewards. Comparable execution on denser charts must not produce unbounded advantages.
6. Freeze opportunity budgets before resolution; failed or cancelled opportunities do not redistribute their shares into free rewards.
7. A whole-enemy-turn trigger spans all attack steps and fires once.
8. Zero-opportunity or interrupted charts do not qualify as flawless completions.
9. Whole-cast performance bonuses resolve before the cast's effect amounts are frozen.
10. Passive-generated effects carry source identity and cannot recursively trigger themselves by default.
11. Buffs and statuses declare the turn boundary at which they tick and expire. Time spent selecting an ability does not consume combat durations.
12. Pause/resume preserves counters and timing.

Good execution should remain useful, while stronger execution can earn greater benefits. Provide unconditional upgrades alongside rhythm-conditioned ones.

## 11. Runtime architecture

### 11.1 Responsibilities

| Component | Responsibility |
|---|---|
| Ability definitions | Immutable ability tags, attributes, charts, costs, and explicit effects |
| Passive definitions | Immutable filters, triggers, modifiers, stacking rules, and caps |
| Build state | Run-owned ability instances, dedicated upgrades, and passive instances |
| Ability resolver | Effective action configuration for preview and commitment |
| Resolved action snapshot | Committed cost, effects, source identity, and reserved bonuses |
| Passive runtime | Continuous modifiers and conditional trigger evaluation |
| Damage service | Component affinity resolution and authoritative damage/prevention outcomes |
| Buff/status runtime | Temporary state, counters, duration, refresh, and expiry |
| Combat event queue | Ordered transactions, source attribution, duplicate protection, bounded secondary effects |
| Reward director | Eligible offers, previews, persistence, and unique claims |

Keep acquired build state outside encounter resets. Keep shared definitions immutable. Reconstruct encounter runtime from the retained build at encounter start.

### 11.2 Event context

Events carry encounter, turn, pattern-run, cast, event, and root-cause IDs as appropriate. Include source ability/passive instance, role/delivery/effect attributes, combat phase, resolution source, raw judgement, and actual outcomes.

Keep selected ability slot, input action, and chart lane as distinct fields. Damage events distinguish attempted, prevented, and actual HP damage. Healing events distinguish actual healing from overheal. Mana events distinguish paid cost, refunds, and restoration.

### 11.3 Resolution order

1. Resolve and preview the selected action using abilities, upgrades, passives, and temporary buffs.
2. Freeze cost/cooldown and reserve any next-action resources at commitment. Pay once.
3. Play the existing rhythm chart and collect raw judgements.
4. Finalize performance, apply qualifying pre-outcome modifiers, and freeze total effect amounts.
5. Apply those effects through authoritative services, distributing amounts across presentation hits without changing their total.
6. Evaluate post-outcome triggers and queue permitted secondary effects.
7. Check defeat after each damaging transaction. Stop gameplay payouts when combat ends.
8. Resolve completed-turn effects and duration expiry in a documented stable order.

Incoming-note damage follows existing rules plus explicitly applicable effects. A raw judgement event is not proof that damage was dealt or prevented; emit those facts after the damage transaction.

Choose stacking and trigger priority deterministically. Use idempotency keys and per-root effect limits. When a chart is cancelled by combat ending, clear remaining notes without creating misses or completion rewards. VFX cannot apply a second copy of an already-resolved effect.

## 12. Integration with the current project

Extend the existing ability/effect pipeline rather than building a second damage route for passives.

| Existing area | Integration requirement |
|---|---|
| `AbilityDefinition` and `AbilityEffect` | Add independent tags/attributes and precisely scoped modifiers |
| `AbilityRuntimeInstance` and `AbilitySlotController` | Use effective cost/cooldown consistently; preserve build ownership across slot construction |
| `RhythmAbilitySystem` | Finalize conditional bonuses before weighted effect settlement |
| `CombatModifierSystem` | Extend temporary effects without treating every acquired passive as a short-lived modifier |
| `RhythmPatternRunner` | Supply event context and authoritative opportunity identities |
| `CombatController` | Reconstruct encounter passives and handle terminal outcomes from either turn |
| `EnemyDefinition` / phases | Add affinity and effect-response data independently of attack sequences |
| Combat reports | Attribute base output, passive contributions, resource changes, and unused benefits |

Important existing behavior:

- Slots currently commit mana/cooldown before chart execution. Cooldowns tick at player-turn start.
- `BeginBattle` clears temporary modifiers and rebuilds ability runtimes; acquired passive ownership must survive this.
- `NoteResolved` currently fires before incoming damage is applied. Damage-triggered passives need post-transaction events.
- Existing lane wards block damage; they do not improve raw judgements or inherently reflect damage.
- Existing Pong return visuals do not constitute a general reflected-damage system.
- The state-machine transition table allows enemy-turn victory, but reflected/status lethal damage needs a complete terminal path through the controller and runner.
- Current ability role enums do not independently distinguish melee from spell delivery.
- The current element label does not automatically create a status effect.

Preserve existing serialized enum values and asset behavior during migration. Keep current numeric tuning until an explicit content/balance pass changes it.

Relevant source files, relative to the repository root:

- `Assets/Scripts/Combat/Architecture/AbilityDefinition.cs`
- `Assets/Scripts/Combat/Abilities/AbilityRuntime.cs`
- `Assets/Scripts/Combat/Abilities/AbilitySlotController.cs`
- `Assets/Scripts/Combat/Abilities/Effects/AbilityEffects.cs`
- `Assets/Scripts/Combat/Abilities/RhythmAbilitySystem.cs`
- `Assets/Scripts/Combat/Modifiers/CombatModifierSystem.cs`
- `Assets/Scripts/Combat/Notes/RhythmPatternRunner.cs`
- `Assets/Scripts/Combat/Architecture/CombatController.cs`
- `Assets/Scripts/Combat/Architecture/EnemyDefinition.cs`

## 13. Acceptance criteria

### Functional correctness

- One committed ability still constitutes the player turn regardless of passive trigger count.
- Passive ownership persists across encounters and never consumes active slots.
- Displayed effective costs equal the amounts paid.
- Melee, spell, element, and effect filters affect only compatible outcomes.
- Damage enhancements do not accidentally improve healing; enemy affinity does not affect unrelated self-support.
- Multi-hit animation, holds, and repeated callbacks cannot duplicate per-cast rewards.
- Cancelled charts and generated judgements cannot claim successful player execution.
- Slot movement and replacement preserve defined upgrade ownership and display inactive passives accurately.
- Reflection or status damage can end either turn exactly once without late payouts.
- Saved offers, duplicate acquisitions, retries, and reloads cannot duplicate rewards.

### Combat and build quality

- Different playstyles produce meaningful differences in action selection, resource management, or defensive payoff.
- Each strategy functions before acquiring rare combinations.
- At comparable reward budgets and intended execution skill, supported builds retain useful primary actions across the required enemy roster.
- Tank sustain cannot remove meaningful danger indefinitely.
- Glass-cannon damage retains its survivability and mana costs.
- Counter rewards remain bounded across sparse and dense sequences.
- Adaptable builds gain useful flexibility without outperforming specialists at every task.
- Hybrids cannot cheaply remove all contributing playstyles' tradeoffs.
- Good-heavy execution remains viable; Perfect execution improves efficiency rather than being mandatory for all builds.

Evaluate damage and protection together with mana economy, enemy turns survived, encounter duration, accuracy, passive contribution, and reward count. Numerical tuning remains configurable and must be validated through combat tests.