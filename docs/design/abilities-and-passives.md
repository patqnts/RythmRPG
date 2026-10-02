# Abilities & Passives Reference

Last updated: 2026-10-02 · Reference for the local Unity content.

## How to read this

The game has 35 abilities, 9 dedicated upgrades and 38 passives. A build equips **four abilities** and can own any number of compatible passives. Numbers are starting values for playtesting. See [Rhythm synergy](rhythm-synergy.md) for the four new abilities, seven passives, shared limits and five additional builds.

- **Power** = the ability's Base Power. An effect's output = Base Power × the effect's scale × chart performance (0–1, the average judgement weight: Perfect 1, Good 0.8, Bad 0.5, Miss 0).
- **Sample power scales from Basic Attack** (currently 22), so the samples follow your tuning. The values below use 22.
- **Tags are independent.** Role (Damage, Healing, Buff, Defense, Resource), Delivery (Melee, Ranged, Spell, Technique) and Element (Physical, Fire, Water, Lightning, Earth, Wind) each filter different passives.
- **Element inheritance:** a damage effect takes the ability's element unless set otherwise. A heal never inherits it.
- **Passive levels** read as "level 1 value, +per extra level". Duplicate rewards level a passive up to its max.
- **Elements have marks and reactions** (see "Elements" below). Elemental abilities and passives are listed in their own sections.
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

## Elements: marks, board quirks and reactions

Each element leaves a **mark** on the enemy and has a **quirk on the rhythm board**. Hitting (or marking) an enemy that carries another element's mark **reacts**. Marks are statuses: they show as icons under the enemy's health bar (stack count for Burn and Static, turns left for the others) and respect enemy status responses (immunity, duration, stack limits).

| Element | Mark on the enemy | Quirk on the board | Plays like |
| --- | --- | --- | --- |
| Fire | **Burn**: stacks (max 5), deals damage per stack at each enemy turn start, 3 enemy turns (refreshed by new stacks) | Flame Guard: Perfect blocks add Burn | Build up, then detonate |
| Water | **Soaked**: enemy notes deal 20% less damage, 2 enemy turns | Heals and shields that pay off later | Sustain, weaken the enemy |
| Lightning | **Static**: stacks (max 5), 3 enemy turns. A Lightning hit at the cap discharges every stack as Lightning damage | Storm Ward / Conductor: Perfects zap the next notes (destroyed + Lightning damage) | Chain hits, clear dense patterns |
| Earth | **Cracked**: the enemy takes +20% melee damage, 2 enemy turns | Stone Wall absorbs the notes of one lane | Heavy, protective |
| Wind | No mark. Every Wind hit adds 1 stack to Burn and Static and extends each mark by 1 turn (once per mark per cast) | Gale Step: dodge the first Miss | Tempo, feeds the other elements |

**Reactions.** Each one happens at most once per cast (or per enemy note). Unless noted, a reaction uses up the marks involved. Reaction damage never triggers further reactions.

| Reaction | Trigger | Effect |
| --- | --- | --- |
| Conduct | Lightning on a Soaked enemy | Zaps chain to 1 extra note; Chain Spark arcs hit a second time at 50%. Soaked stays |
| Steam | Fire on Soaked, or Water on a Burning enemy | Burst = Burn damage per tick × stacks × 2 (at least 30% of the hit that caused it). Uses up Soaked and Burn |
| Overload | Fire on Static | Static discharges at double damage |
| Wildfire | Wind on a Burning enemy | Burn stacks double (up to the cap). Burn stays |
| Mudlock | Water on Cracked, or Earth on Soaked | Stagger. Uses up both marks |
| Magnetize | Earth on Static | Static becomes a shield for you (= its discharge damage, 2 enemy turns) |

**Board rules.**

- **Zapped notes** are destroyed, deal no damage and don't count for anything else: no judgement popup, no combo, no mana, no counter charges, and they are neither misses nor opportunities for whole-turn passives. Zaps reach notes up to 1 second from the hit line, soonest first, any lane. Hold notes, mash notes and Ping-Pong shots can't be zapped. At most 8 zaps per enemy turn from all sources (enemy profiles can lower it).
- **Stone Wall** picks the lane with the most notes still to come at each attack step and breaks notes there just before the hit line.
- **Stagger**: the enemy's next attack loses its last step. A single-step attack keeps its notes but they deal 50% damage. An enemy can't be staggered two enemy turns in a row, and profiles can cap staggers per battle (the Boss test profile allows 1).

All of these numbers are in the Build Balance Rules asset, under Elements.

## Elemental abilities

13 abilities, all in the reward pool. Values use Basic Attack power 22. Tremor and Gale Step cost 0 MP, so Earth and Wind builds keep an affordable action.

| Ability | Role | Delivery | Element | MP | Cooldown | Power | Effects |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Ember Lash | Damage | Melee | Fire | 10 | 0 | 24 | Damage ×1; +1 Burn stack per Perfect in its chart (max 3), 4 damage per stack per tick |
| Flame Guard | Defense | Technique | Fire | 20 | 2 | 44 | 2 enemy turns: each Perfect block adds 1 Burn stack (about 4 per stack per tick), max 5 per turn |
| Combust | Damage | Spell | Fire | 35 | 2 | 22 | Damage ×0.5; removes Burn and deals its remaining damage ×1.5 at once |
| Undertow | Damage | Spell | Water | 20 | 0 | 35 | Water damage ×1; Soaked for 2 turns |
| Tidal Veil | Defense | Spell | Water | 25 | 2 | 18 | Shield ×1.2 (about 22) for 2 enemy turns; heals 50% of it when it breaks |
| Rain Dance | Healing | Spell | Water | 20 | 3 | 135 | Heal ×1.2 spread over 3 player turns (about 54 each), Water healing |
| Chain Spark | Damage | Spell | Lightning | 15 | 0 | 26 | Damage ×1; each Perfect in its chart fires an arc right away (5 Lightning) and adds 1 Static (discharge 5 per stack) |
| Storm Ward | Defense | Spell | Lightning | 20 | 3 | 44 | 2 enemy turns: each Perfect block zaps the next 2 notes (about 4 Lightning each), max 6 per turn |
| Quake Slam | Damage | Melee | Earth | 20 | 2 | 53 | Earth damage ×1; Cracked for 2 turns. Includes a Hold chart |
| Stone Wall | Defense | Technique | Earth | 20 | 3 | 0 | Walls the busiest lane: absorbs the next 4 notes there, up to 2 enemy turns |
| Tremor | Damage | Technique | Earth | 0 | 2 | 11 | Earth damage ×1; stagger (needs 50%+ chart performance) |
| Gale Step | Damage, Defense | Technique | Wind | 0 | 2 | 13 | Wind damage ×1; dodge the first Miss of the next enemy turn |
| Cyclone | Damage | Spell | Wind | 30 | 2 | 48 | Wind damage ×1 as 6 separate hits (each hit feeds the marks) |

Defensive elemental abilities (Flame Guard, Storm Ward, Stone Wall) fail below 25% chart performance, like the other buffs.

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

There are 38 passives (20 below, 11 elemental after them, and 7 in [Rhythm synergy](rhythm-synergy.md)). The playstyle column only weights reward offers; any build can take any passive whose need is met. A passive with no compatible ability equipped stays owned but inactive.

| Passive | Playstyle | Max Lv | Needs | Effect (Lv 1, +per level) | Bound |
| --- | --- | --- | --- | --- | --- |
| Counter Preparation | Parry | 3 | – | Each Perfect defense or completed 70%+ defense phrase stores 1 charge. Both fill the same allowance. Attacks without their own spender use up to 3 for +10% each | 2 charges per enemy turn (+1); 5 stored; fade after 2 idle player turns |
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

### Elemental passives

| Passive | Playstyle | Max Lv | Needs | Effect (Lv 1, +per level) | Bound |
| --- | --- | --- | --- | --- | --- |
| Pyre Keeper | Elemental, Glass Cannon | 3 | A Fire ability | Burn max stacks +2 (+1); Burn damage +10% (+10%) | – |
| Conductor | Elemental, Parry | 3 | A Lightning ability | The first 3 (+1) Perfects each enemy turn zap 1 note (8 +4 Lightning) | Shares the per-turn zap limit |
| Capacitor | Elemental | 3 | A Lightning ability | Static max stacks +3 (+1); discharge +50% (+25%) | – |
| Riptide | Elemental, Tank | 3 | A Water heal | 50% (+25%) of Water overheal hits the enemy as Water damage (can react) | Secondary-effect budget per cast |
| Bedrock | Elemental, Tank | 3 | A melee damage ability | While shielded, melee attacks deal +10% (+5%) of the shield as damage | Once per cast |
| Aftershock | Elemental | 3 | An Earth ability | Earth abilities hit again for 30% (+10%) of their damage at the next enemy turn start | – |
| Tailwind | Elemental, Adaptable | 3 | A Wind ability | After a Wind ability, the next ability costs 30% (+10%) less MP | One pending discount |
| Catalyst | Elemental | 3 | Two different elements equipped | Reactions +25% (+10%); the first reaction each battle keeps its marks | – |
| Attunement | Elemental, Adaptable | 3 | An elemental ability | +4% (+2%) elemental damage per different element used this battle | Max 5 elements; exclusive with Purist |
| Purist | Elemental | 3 | Every equipped elemental ability shares one element (physical ones don't count) | That element's damage +15% (+5%); its marks last +1 turn | Exclusive with Attunement |
| Unyielding | Tank, Glass Cannon | 1 | – | Once per battle, a lethal hit leaves you at 1 HP | Once per battle |

All percentage bonuses in the same group add up, groups multiply, each group is capped at +200% and a cast's total at ×4.

## Sample builds

Ten presets combine the content above. Pick one with F11 in play mode. Slot 1–4 = lane key 1–4 during ability selection.

| Build | Playstyle | Slots 1–4 | Passives | Trade-off |
| --- | --- | --- | --- | --- |
| Mirror Guard | Parry / Deflect | Strike, Riposte, Mirror Stance, Mend | Counter Preparation, Deflection | Needs accurate defense; charges fade if unused |
| Pyromancer | Glass Cannon | Strike, Fire Bolt, Inferno, Focus | Glass Heart, Mana Surge, Pyromancy, Conservation | 650 max HP; Inferno competes with survival |
| Bulwark | Tank | Shield Bash, Strike, Bulwark, Mend | Fortitude, Iron Skin, Second Wind, Tidal Healing | Low damage, long fights; shields cap and expire |
| Wanderer | Adaptable | Strike (+Searing Edge), Spellblade, Rally, Frost Lance | Versatility, Follow-Through, Measured Execution, Arcane Thrift | Lower peaks; needs sequencing |
| Riposte Mage | Parry + Glass hybrid | Strike, Riposte (+Honed Riposte), Fire Bolt (+Quickcast), Siphon | Counter Preparation, Glass Heart | Keeps both styles' costs: 650 HP and charges earned through accurate defense |
| Blank Slate | Reward progression | Strike, Mend | – | Grow a build through rewards |
| Storm Caller | Elemental (Lightning + Water) | Strike, Chain Spark, Storm Ward, Undertow | Conductor, Capacitor, Catalyst | Needs Perfects on both turns; zaps are capped per turn |
| Pyre Warden | Elemental (Fire + Water) | Strike, Ember Lash, Flame Guard, Undertow (reserve: Combust) | Pyre Keeper, Catalyst | Burn is slow until detonated; Steam uses up both marks |
| Tidecaller | Elemental (Water) | Strike, Undertow, Tidal Veil, Rain Dance | Riptide, Purist, Unyielding | Slow damage; Purist breaks with a second element |
| Stonewarden | Elemental (Earth + Wind) | Tremor, Quake Slam, Stone Wall, Cyclone (reserve: Gale Step) | Aftershock, Tailwind, Attunement, Bedrock | Bosses resist stagger after their limit; the wall covers one lane |

## Enemy test profiles and statuses

The original enemies have no affinities (everything ×1). Five test profiles can be swapped onto the current enemy from F11 → Enemy. Affinity multiplies matching damage components only, clamped to ×0.25–×2, and never touches healing or buffs.

| Profile | Damage affinities | Status response |
| --- | --- | --- |
| Fire-resistant | Fire ×0.5, Water ×1.5 | Burn lasts half as long |
| Armored | Physical ×0.6, Lightning ×1.5, Water ×1.25 | – |
| Burn-immune | – | Burn immune |
| Frail | Physical, Fire, Water, Lightning ×1.25 | – |
| Boss (control limits) | – | Staggered at most once per battle; at most 3 zapped notes per enemy turn |

**Burn** deals fire damage at the start of each enemy turn: potency per stack (snapshotted, the higher one is kept) × stacks. New applications add a stack (up to 5) and refresh the duration; Inferno and Ignite now stack with the elemental Burn sources. Affinity applies once per tick, and a lethal tick ends the battle before the enemy attacks. Soaked, Static and Cracked are the other marks (see "Elements").
