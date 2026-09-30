# Abilities & Passives Reference

Last updated: 2026-09-30 · Source: Claude Docs "Abilities & Passives Reference" (update both together)

## How to read this

The game has 18 abilities, 9 dedicated upgrades and 20 passives. Every number here is a starting value for playtesting, not final balance.

- **Power** = the ability's Base Power. An effect's output = Base Power × the effect's scale × chart performance (0–1, the average judgement weight: Perfect 1, Good 0.8, Bad 0.5, Miss 0).
- **Sample power scales from Basic Attack** (currently 22), so the samples follow your tuning. The values below use 22.
- **Tags are independent.** Role (Damage, Healing, Buff, Defense, Resource), Delivery (Melee, Ranged, Spell, Technique) and Element (Physical, Fire, Water, Lightning, Earth, Wind) each filter different passives.
- **Element inheritance:** a damage effect takes the ability's element unless set otherwise. A heal never inherits it.
- **Passive levels** read as "level 1 value, +per extra level". Duplicate rewards level a passive up to its max.
- **Where it lives:** the four original abilities are in `Resources/Combat/Abilities`. Everything else is in `Resources/Combat/Build/Samples` (exported assets) and `Build/Samples/SampleBuildLibrary.cs` (the code source).

## Original abilities (DefaultLoadout)

These four are the legacy loadout, used when no run build is active. They have no role or delivery tags set, so roles come from their Ability Type and no delivery-filtered passive applies to them.

| Ability | Type | Element | MP | Cooldown | Power | Effect |
| --- | --- | --- | --- | --- | --- | --- |
| Basic Attack | Basic Attack | Physical | 0 | 0 | 22 | Damage ×1 |
| Flamethrower | Special Attack | Fire | 20 | 0 | 50 | Fire damage ×1 |
| Heal | Healing | – | 30 | 2 | 150 | Heal ×1 (default for Healing type) |
| Death Faith | Defensive | – | 25 | 4 | 0 | Ward the used lane: Bad and Miss deal no damage for 3 enemy turns (+1 turn at 90%+ performance); fails below 25% |

## Sample build abilities

There are 14 sample abilities, all in the reward pool. Two cost 0 MP (Strike, Siphon), so every build keeps an affordable action. Buffs, shields and statuses fail below 25% chart performance.

| Ability | Role | Delivery | Element | MP | Cooldown | Power | Effects |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Strike | Damage | Melee | Physical | 0 | 0 | 22 | Damage ×1 |
| Siphon | Damage, Resource | Melee | Physical | 0 | 0 | 13 | Damage ×1; restores 8 MP × performance |
| Shield Bash | Damage, Defense | Melee | Physical | 6 | 0 | 18 | Damage ×1; shield ×2 power (about 35) for 2 enemy turns |
| Riposte | Damage | Melee, Technique | Physical | 8 | 0 | 26 | Damage ×1; spends up to 5 counter charges, +30% damage each |
| Focus | Buff | Technique | – | 10 | 2 | 0 | Next attack +60% × performance, lasts 2 player turns |
| Spellblade | Damage | Melee, Spell | Fire | 12 | 0 | 35 | Physical ×0.6 + Fire ×0.6, resolved as separate components |
| Thunder Clap | Damage | Technique | Lightning | 14 | 0 | 40 | Lightning damage ×1 (not a spell) |
| Mirror Stance | Buff, Defense | Technique | – | 15 | 3 | 0 | 2 enemy turns: Perfect defense reflects 60% × performance of the note's damage (Good half), max 60 per turn; +1 counter charge (+2 at 85%+) |
| Frost Lance | Damage | Spell, Ranged | Water | 16 | 0 | 44 | Water damage ×1 |
| Rally | Healing, Buff | Spell | – | 18 | 2 | 75 | Heal ×1; next attack +30% × performance for 2 player turns |
| Fire Bolt | Damage | Spell, Ranged | Fire | 20 | 0 | 53 | Fire damage ×1 |
| Bulwark | Defense | Technique | – | 20 | 3 | 60 | Take 35% less note damage for 2 enemy turns; shield ×1 (60) for 2 enemy turns |
| Mend | Healing | Spell | Water | 25 | 2 | 135 | Water-attributed heal ×1 |
| Inferno | Damage | Spell | Fire | 45 | 2 | 99 | Fire damage ×1; Burn: about 15 × performance fire damage at each enemy turn start, 3 ticks |

Presentation (icon, chart, attack animation) is borrowed from the original abilities: melee ones from Basic Attack, spells from Flamethrower, heals from Heal, stances from Death Faith.

## Dedicated ability upgrades

There are 9 upgrades. Each attaches to one ability instance, moves with it between slots and stays with it in reserve. An ability takes each upgrade once.

| Upgrade | Attaches to | Change |
| --- | --- | --- |
| Honed Riposte | Riposte | +25% power |
| Lasting Mirror | Mirror Stance | Cooldown −1 (3 → 2) |
| Quickcast | Fire Bolt | Cost −5 MP (20 → 15) |
| Ember Heart | Inferno | Cooldown −1 (2 → 1), cost +5 MP (45 → 50) |
| Overflowing Mend | Mend | +20% power |
| Searing Edge | Strike | Adds a separate Fire damage component ×0.4 |
| Ignite | Spellblade | Adds Burn (fire, ×0.08 power per tick, 2 ticks); cost +3 MP |
| Heavy Shield | Shield Bash | +30% power (damage and shield), cost +2 MP |
| Honed Technique | Any melee damage ability | +15% power |

## Passives

There are 20 passives. The playstyle column only weights reward offers; any build can take any passive whose need is met. A passive with no compatible ability equipped stays owned but inactive.

| Passive | Playstyle | Max Lv | Needs | Effect (Lv 1, +per level) | Bound |
| --- | --- | --- | --- | --- | --- |
| Counter Preparation | Parry | 3 | – | Each Perfect defense stores 1 counter charge. Attacks without their own spender use up to 3 for +10% each | 2 charges per enemy turn (+1); 5 stored; fade after 2 idle player turns |
| Deflection | Parry | 3 | – | Perfect defense reflects 30% (+10%) of the note's damage as physical damage | 40 (+15) per enemy turn |
| Mirror Mastery | Parry | 2 | A buff ability | Reflect buffs last +1 enemy turn and are 15% (+15%) stronger | – |
| Glass Heart | Glass Cannon | 2 | A spell damage ability | −35% max HP; +30% (+15%) spell damage | Exclusive with Fortitude |
| Mana Surge | Glass Cannon | 3 | – | Attacks that paid 30+ MP deal +20% (+10%) damage | Uses paid cost, never refunds |
| Pyromancy | Glass Cannon, Adaptable | 3 | A fire component | Fire damage components +25% (+10%) | Fire bonuses share a +60% cap |
| Conservation | Glass Cannon, Adaptable | 3 | – | Attacks paying 8 MP or less, played at 60%+, restore 5 (+3) MP | Once per cast |
| Kindling | Glass Cannon | 2 | A status ability | Burn gets +1 tick and +25% (+15%) damage per tick | – |
| Fortitude | Tank | 3 | – | +30% (+10%) max HP | Exclusive with Glass Heart |
| Iron Skin | Tank | 3 | – | Note damage −25% (+5%) | Prevents at most 40 (+15) per enemy turn |
| Second Wind | Tank | 3 | A healing ability | 50% (+15%) of overheal becomes shield for 2 enemy turns | Shared shield cap: 30% max HP |
| Tidal Healing | Tank | 3 | A water heal | Water-attributed healing +25% (+10%) | – |
| Steady Guard | Tank, Parry | 3 | – | An enemy turn with 3 or fewer misses grants a 30 (+15) shield for 1 enemy turn | Once per enemy turn; needs 1+ note and an uninterrupted turn |
| Bastion | Tank | 2 | A buff ability | Damage-reduction buffs last +1 enemy turn | – |
| Versatility | Adaptable | 3 | – | An action whose role differs from the previous one: +20% (+5%) damage, healing and shield | Not on the first action |
| Follow-Through | Adaptable, Tank | 3 | – | A non-damage buff / heal / defense action at 50%+ gives the next attack +25% (+10%) | One pending bonus; 2 player turns |
| Measured Execution | Adaptable, Glass Cannon | 3 | A damage ability | +15% (+5%) damage when the chart is played at 85%+ | Once per cast; cancelled or empty charts don't count |
| Arcane Thrift | Adaptable, Glass Cannon | 3 | A spell | Spells cost 20% (+5%) less mana | Minimum 1 MP |
| Blade Discipline | Parry, Adaptable | 3 | A melee damage ability | Melee damage +20% (+10%) | Melee bonuses share a +60% cap |
| Vitality | Fallback | 5 | – | +5% (+5%) max HP | Offered only when too few other rewards are eligible |

All percentage bonuses in the same group add up, groups multiply, each group is capped at +200% and a cast's total at ×4.

## Sample builds

Six presets combine the content above. Pick one with F11 in play mode. Slot 1–4 = lane key 1–4 during ability selection.

| Build | Playstyle | Slots 1–4 | Passives | Trade-off |
| --- | --- | --- | --- | --- |
| Mirror Guard | Parry / Deflect | Strike, Riposte, Mirror Stance, Mend | Counter Preparation, Deflection | Needs Perfect defense; charges fade if unused |
| Pyromancer | Glass Cannon | Strike, Fire Bolt, Inferno, Focus | Glass Heart, Mana Surge, Pyromancy, Conservation | 650 max HP; Inferno competes with survival |
| Bulwark | Tank | Shield Bash, Strike, Bulwark, Mend | Fortitude, Iron Skin, Second Wind, Tidal Healing | Low damage, long fights; shields cap and expire |
| Wanderer | Adaptable | Strike (+Searing Edge), Spellblade, Rally, Frost Lance | Versatility, Follow-Through, Measured Execution, Arcane Thrift | Lower peaks; needs sequencing |
| Riposte Mage | Parry + Glass hybrid | Strike, Riposte (+Honed Riposte), Fire Bolt (+Quickcast), Siphon | Counter Preparation, Glass Heart | Keeps both styles' costs: 650 HP and Perfect-only charges |
| Blank Slate | Reward progression | Strike, Mend | – | Grow a build through rewards |

## Enemy test profiles and statuses

The original enemies have no affinities (everything ×1). Four test profiles can be swapped onto the current enemy from F11 → Enemy. Affinity multiplies matching damage components only, clamped to ×0.25–×2, and never touches healing or buffs.

| Profile | Damage affinities | Status response |
| --- | --- | --- |
| Fire-resistant | Fire ×0.5, Water ×1.5 | Burn lasts half as long |
| Armored | Physical ×0.6, Lightning ×1.5, Water ×1.25 | – |
| Burn-immune | – | Burn immune |
| Frail | Physical, Fire, Water, Lightning ×1.25 | – |

**Burn** is the only status so far. It deals fire damage at the start of each enemy turn, using a potency snapshotted when it is applied. A new application refreshes the duration and keeps the higher potency (1 stack). Affinity applies once per tick, and a lethal tick ends the battle before the enemy attacks.
