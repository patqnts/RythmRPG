# Rhythm synergy

Added 2026-10-02. These additions connect existing counters, shield, mana and elemental marks. There is no Momentum meter. Combo measures your hit streak; counters are a bank of charges to spend on actions.

There are still **four ability slots**, with unlimited passive ownership. Guard/Break and choosing a Reprise target are options within an ability. Each new passive has one level.

## Rhythm rewards

A **phrase** is one musical bar containing authored notes. Complete it at 70% accuracy or better to succeed. Good hits normally score 80%, so every note need not be Perfect. Backbeat and Closeout reward the closing phrase; Counter Preparation and Stoke reward defense phrases. The HUD shows current phrase progress and the target.

Hold/Mash rewards require Good or better completion. Ping-Pong rewards require every return in a full rally to be Good or better. Dynamic rally shots earn this reward rather than being added to a fixed authored phrase.

Automatic clears cannot earn execution rewards or make remaining phrase notes worth more: the original authored note count stays the denominator. Empty and cancelled phrases do not succeed. Timing windows, directional inputs and combo scoring stay the same.

## Four new abilities

Values use Basic Attack power 22. Damage, healing and shield still scale with chart performance.

| Ability | MP / cooldown | Effect | Connections |
|---|---|---|---|
| **Backbeat** | 8 / 0 | Wind melee attack, power 18. Closing phrase: 1 counter at 70%, 2 at 85%, gained after damage. | Wind feeds existing Burn/Static. Charges help ordinary attacks, Riposte or support through Improvisation. Conservation and Tailwind also apply. |
| **Breakwater** | 12 / 0 | Water melee attack, power 26. **Guard:** also shield ×0.7. **Break:** instead spend one third of existing shield, max 30, for extra Water damage. | Works without shield through Guard. Its Hold chart can earn Sustained Guard; remaining shield feeds Bedrock. |
| **Cauterize** | 18 / 2 | Fire healing, power 75. Optionally consume one enemy Burn stack for +30 healing before rhythm scaling. | Keep Burn for Combust or trade one stack to survive. Works without Burn; Improvisation and Second Wind connect. Healing does not cause Fire reactions. |
| **Reprise** | 12 / 3 | Choose a live player shield, ward or buff. At 70% chart accuracy, extend it one turn, once per application. | Preserve protection, Flame Guard, Mirror Stance or a next-attack bonus. It changes duration, so Improvisation does not spend charges on it. |

Hold the ability lane as usual. Resource choices open **before payment and cooldown**. Use mouse buttons, Up/Down + Enter, or gamepad D-pad + A. Cancel with its button, Backspace or gamepad B; Escape remains pause. Cancelling spends nothing. Reprise with no eligible target costs nothing.

## Seven new passives

| Passive | Effect | Existing abilities/passives it connects |
|---|---|---|
| **Improvisation** | Pure support spends up to 2 counters for +10% healing, shield and numerical buff strength each. | Charges from defense, Mirror Stance or Backbeat strengthen Mend, Rally, Focus and other support. |
| **Sustained Guard** | Complete a Hold, Mash or full Good-or-better rally for 8 shield, once per cast/whole enemy turn. | Any suitable chart feeds Bedrock, Pressure Cast, Breakwater or survival. Breakwater and Quake Slam include Hold charts. |
| **Pressure Cast** | Any damaging ability can optionally spend 20% of existing shield, max 20, for extra damage of its element. | Shield Bash, Second Wind, Tidal Veil and rhythm rewards supply it. Works on melee and spells. Breakwater never converts shield twice. |
| **Stoke** | First successful defense phrase per enemy turn adds 1 existing Burn and Static stack. No new mark, refresh or reaction. | Flame Guard supplies Burn at every 15 Combo during defense; Ember Lash and Kindling supply Burn from ability charts. Chain Spark supplies Static. Helps Combust, Capacitor and reactions. |
| **Reaction Shelter** | First elemental reaction per cast/whole enemy turn gives 10 shield. | All existing reaction pairs work, including Riptide's Water damage. Delayed ticks and Aftershock cannot earn another payout. |
| **Reservoir** | Normal end-of-defense mana overflow becomes shield, max 10. | Accurate defense remains useful at full mana. Shield connects to defense, Bedrock and shield spending. Siphon/refunds do not feed it. |
| **Closeout** | Successful closing phrase restores 20% of actually paid MP, rounded down, max 6. | Works on every paid action. Discounts also reduce the refund. Shares a limit with Conservation. |

**Counter Preparation is broadened rather than duplicated.** Perfects and successful defense phrases fill the same allowance: 2 charges per enemy turn at level 1, +1 per level. Its bank remains 5 charges, expiring after 2 idle player turns. Active abilities' charges also help ordinary attacks without owning Counter Preparation.

## Five builds, four abilities each

These are starting passives; you can acquire more.

| Build | Four abilities | Starting passives | How to play |
|---|---|---|---|
| **Improvising Duelist** | Siphon, Riposte, Mirror Stance, Rally | Counter Preparation, Improvisation, Deflection, Follow-Through | Earn counters in defense. Choose stronger healing/buffs or a big Riposte. Rally prepares the next attack; Mirror supplies reflection and charges. |
| **Armored Spellcaster** | Siphon, Shield Bash, Fire Bolt, Rally | Second Wind, Pressure Cast, Closeout, Reservoir | Build shield through attacks/overheal. Keep protection or spend part on Fire Bolt. Closing phrases save mana; full-mana defense still helps. |
| **Living Furnace** | Strike, Flame Guard, Cauterize, Combust | Stoke, Pyre Keeper, Kindling, Second Wind | Every 15 Combo during defense builds Burn. Keep it for Combust or consume one stack to heal. Overheal adds protection; Strike is free. |
| **Breakwater Knight** | Siphon, Breakwater, Quake Slam, Reprise | Bedrock, Sustained Guard, Aftershock, Reaction Shelter | Play Holds for shield. Keep or spend it with Breakwater. Quake benefits from protection and creates Water/Earth reaction opportunities. Reprise preserves a key effect. |
| **Storm Conductor** | Siphon, Backbeat, Chain Spark, Undertow | Counter Preparation, Capacitor, Catalyst, Reaction Shelter | Water/Lightning create reactions. Wind feeds marks and earns counters. Spend charges on the attack that suits the enemy's current marks. |

Later combinations: add Closeout/Conservation to the Duelist; Bedrock/Sustained Guard to the Spellcaster; Pressure Cast to the Furnace; Reservoir to the Knight; or Improvisation to a Storm build that adds healing. Connections follow actions/resources rather than a preset name.

## Shared limits

- Existing total shield cap: **30% max HP**. Sustained Guard, Reaction Shelter and Reservoir together add at most **20 small-reward shield per cast or whole enemy turn**, across all attack steps. Paid shields, Second Wind and existing reaction shields use the normal capacity limit.
- Shield spending happens before the chart. It cannot trigger Tidal Veil's break-heal and removes the spent share of future break-healing. Bonus damage follows rhythm, damage modifiers and affinity but cannot cause extra reactions.
- Conservation + Closeout restore at most **12 MP per cast**. Siphon's ability effect remains separate. Refunds stop when combat ends.
- Combined passive/live discounts cannot reduce a positive upgraded listed cost by more than **60%**. Previews and payment use the same quote. Mana Surge and Closeout use actual payment.
- Player effects gain at most **2 extra turns**. Reprise adds one turn per application and counts alongside Mirror Mastery, Bastion and strong-chart ward bonuses.
- Follow-ups are not new casts. Refunds, shield conversions and delayed echoes cannot earn another closing reward. Fully rounded-out cost level-ups are not offered when they cannot reduce any equipped action's cost.

These values are editable under **Rhythm connections** in Build Balance Rules.

## Content and artwork

New abilities/passives enter the normal reward pool; five presets join the existing ten. Every saved ability and passive has an icon. Existing assignments are preserved. The project Fire/Ice/Lightning folders supply matching art; selected missing categories from the supplied fantasy pack are copied into `Assets/Art/Icons/RhythmSynergy`.

`Assets/Scripts/Editor/RhythmSynergyIcons.json` records selections. `BuildIconCatalog.asset` supplies icons to code-defined fallback content. **Tools → Rythm RPG → Combat → Build → Add Rhythm Synergy Content** installs missing additions without replacing tuned assets or GUIDs. The older Export command remains an explicit full re-export.
