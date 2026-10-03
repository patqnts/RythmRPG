using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RythmRPG.Combat
{
    /// <summary>A player-facing meaning and colour for a gameplay word shown in the Resonance Archive.</summary>
    public sealed class GameplayTerm
    {
        public string Id { get; }
        public string Label { get; }
        public string Definition { get; }
        public string Color { get; }
        public IReadOnlyList<string> Words { get; }

        public GameplayTerm(string id, string label, string definition, string color, params string[] words)
        {
            Id = id;
            Label = label;
            Definition = definition;
            Color = color;
            Words = words == null || words.Length == 0 ? new[] { label } : words;
        }
    }

    /// <summary>
    /// Shared, plain-language glossary for build descriptions. Matching words become coloured TMP links; the archive can
    /// then open their definitions with the keyboard or mouse without changing the authored ability/passive text.
    /// </summary>
    public static class GameplayGlossary
    {
        private const string Rhythm = "FFD866";
        private const string Resource = "69D9FF";
        private const string Support = "7EE6A1";
        private const string Danger = "FF8A78";
        private const string Utility = "C9A8FF";
        private const string Fire = "FF784F";
        private const string Water = "62C8FF";
        private const string Lightning = "D7A6FF";
        private const string Earth = "D8B16B";
        private const string Wind = "80E6C2";

        private static readonly GameplayTerm[] terms =
        {
            T("mana", "MP / Mana", "The resource paid when an ability is committed. Having more mana does not grant another action.", Resource, "MP", "Mana"),
            T("cooldown", "Cooldown", "How many player-turn updates must pass before that ability can be chosen again.", Utility, "Cooldown", "Cooldowns"),
            T("power", "Power", "An ability's starting amount. Damage, healing or shield then applies its effect scale, rhythm performance and eligible bonuses.", Danger, "Power"),
            T("performance", "Performance", "The average quality of the judgements in the current ability chart. It usually scales damage, healing and shield.", Rhythm, "Performance", "Accuracy"),
            T("phrase", "Phrase", "One authored musical bar. Phrase rewards normally succeed at 70% performance or better.", Rhythm, "Closing phrase", "Defense phrase", "Phrase", "Phrases"),
            T("milestone", "Combo Milestone", "A named step in the visible Combo, such as 15, 30 or 45. Several effects can respond to the same step while keeping their own per-turn limits.", Rhythm, "Combo milestones", "Combo milestone", "Milestones", "Milestone"),
            T("hold", "Hold", "A long rhythm note that must be held through its timing window. Completing it at Good or better can trigger challenge rewards.", Rhythm, "Hold notes", "Hold note", "Holds", "Hold"),
            T("mash", "Mash", "A rhythm note cleared by pressing repeatedly before its window ends. Good-or-better completion can trigger challenge rewards.", Rhythm, "Mash notes", "Mash note", "Mash"),
            T("pingpong", "Ping-Pong", "A rhythm rally that travels between lanes. Challenge rewards require every return in the full rally to be Good or better.", Rhythm, "Ping-Pong rally", "Ping-Pong"),
            T("perfect", "Perfect", "The highest timing judgement. For ability performance it is normally worth 100%.", Rhythm, "Perfect", "Perfects"),
            T("good", "Good", "A solid timing judgement. For ability performance it is normally worth 80%.", Rhythm, "Good"),
            T("bad", "Bad", "A weak timing judgement. For ability performance it is normally worth 50%.", Danger, "Bad"),
            T("miss", "Miss", "A missed note. For ability performance it is worth 0%; during enemy attacks it can deal note damage.", Danger, "Miss", "Misses"),
            T("combo", "Combo", "Your current hit streak. Combo is not spent; some effects trigger when it reaches milestones such as 15, 30 or 45.", Rhythm, "Combo"),
            T("counter", "Counter", "A stored charge earned from certain defensive or rhythm effects. Ordinary attacks, Riposte and Improvisation can spend it in different ways.", Rhythm, "Counter charges", "Counter charge", "Counters", "Counter"),
            T("shield", "Shield", "Temporary protection that absorbs damage before health. Total shield is normally capped at 40% of maximum health.", Support, "Shielded", "Shields", "Shield"),
            T("ward", "Ward", "Protection with a specific rule, such as guarding one rhythm lane from Bad and Miss damage.", Support, "Wards", "Ward"),
            T("reflect", "Reflect", "Damage sent back to the enemy after an eligible defensive judgement. It bypasses enemy affinity, but is still limited by its source and per-turn cap.", Utility, "Reflection", "Reflects", "Reflect"),
            T("buff", "Buff", "A temporary helpful effect on the player, such as a stronger next attack or damage reduction.", Support, "Buffs", "Buff"),
            T("status", "Status", "A named temporary effect with its own duration, stacks and enemy response rules.", Utility, "Statuses", "Status"),
            T("mark", "Elemental Mark", "A status placed on an enemy for elemental setup: Burn, Soaked, Static or Cracked. Wind supports existing marks instead of adding its own.", Utility, "Elemental marks", "Elemental mark", "Marks", "Mark"),
            T("burn", "Burn", "A Fire mark that stores stacks and deals damage at the start of enemy turns. New stacks refresh its duration.", Fire, "Burning", "Burn"),
            T("soaked", "Soaked", "A Water mark that makes the enemy's notes deal 20% less damage and can set up Conduct, Steam or Mudlock.", Water, "Soaked"),
            T("static", "Static", "A Lightning mark that stores stacks. A Lightning hit at its cap discharges the stacks as damage.", Lightning, "Static"),
            T("cracked", "Cracked", "An Earth mark that makes the enemy take 20% more melee damage and can set up Mudlock.", Earth, "Cracked"),
            T("reaction", "Elemental Reaction", "An extra result caused by hitting an enemy carrying another element's mark. A reaction is bounded and its damage cannot start another reaction.", Utility, "Elemental reactions", "Elemental reaction", "Reactions", "Reaction"),
            T("conduct", "Conduct", "Lightning on Soaked: zaps chain farther and Chain Spark arcs gain a second, weaker hit. Soaked remains.", Lightning, "Conduct"),
            T("steam", "Steam", "Fire on Soaked, or Water on Burn: cause a burst based on Burn, then consume Burn and Soaked.", Water, "Steam"),
            T("overload", "Overload", "Fire on Static: discharge the Static at double damage, then consume it.", Fire, "Overload"),
            T("wildfire", "Wildfire", "Wind on Burn: double Burn stacks up to the cap while keeping Burn.", Wind, "Wildfire"),
            T("mudlock", "Mudlock", "Water on Cracked, or Earth on Soaked: stagger the enemy and consume the involved marks.", Earth, "Mudlock"),
            T("magnetize", "Magnetize", "Earth on Static: consume Static and turn its discharge value into a temporary shield.", Earth, "Magnetize"),
            T("zap", "Zap", "Destroy an approaching enemy note and deal a small Lightning hit. A zapped note gives no judgement, combo, mana or counter.", Lightning, "Zapped", "Zaps", "Zap"),
            T("stagger", "Stagger", "Shorten the enemy's next attack by removing its final step. A single-step attack stays, but its note damage is halved.", Earth, "Staggered", "Stagger"),
            T("melee", "Melee", "A delivery tag. Melee bonuses apply only to damage effects on actions carrying this tag.", Danger, "Melee"),
            T("ranged", "Ranged", "A delivery tag for distance-based abilities. It is separate from element and from being a spell.", Utility, "Ranged"),
            T("spell", "Spell", "A delivery tag. Spell cost passives apply to it; an elemental technique is not automatically a spell.", Lightning, "Spells", "Spell"),
            T("technique", "Technique", "A delivery tag for body or combat techniques. A technique can still carry an element or defensive effect.", Earth, "Techniques", "Technique"),
            T("damage", "Damage", "An effect that reduces health after performance, bonuses, affinity and protection are resolved.", Danger, "Damage"),
            T("healing", "Healing", "An effect that restores health. Enemy damage affinity does not reduce your healing.", Support, "Healing", "Heal"),
            T("overheal", "Overheal", "The part of a heal left over after health is full. Some passives convert it into shield or damage.", Support, "Overhealing", "Overheal"),
            T("affinity", "Affinity", "The enemy's multiplier for one damage type. It changes matching damage components, not your healing or unrelated buffs.", Utility, "Affinities", "Affinity"),
            T("physical", "Physical", "A damage type used by many basic and melee attacks. It has its own enemy affinity.", Danger, "Physical"),
            T("fire", "Fire", "An element associated with Burn, Steam, Overload and Wildfire. A Fire tag alone does not automatically apply Burn.", Fire, "Fire"),
            T("water", "Water", "An element associated with Soaked, sustain and several reactions. A Water tag alone does not automatically apply Soaked.", Water, "Water"),
            T("lightning", "Lightning", "An element associated with Static, discharge and zapping incoming notes.", Lightning, "Lightning"),
            T("earth", "Earth", "An element associated with Cracked, shield use, lane protection and stagger.", Earth, "Earth"),
            T("wind", "Wind", "An element that grows and extends existing Burn and Static marks rather than applying a separate mark.", Wind, "Wind"),
            T("cast", "Cast", "One committed ability use. Multi-hit animations and delayed follow-ups remain part of that same cast unless stated otherwise.", Utility, "Casts", "Cast"),
            T("stack", "Stack", "One count of a stackable effect such as Burn or Static. Stack caps limit how many can be stored.", Utility, "Stacks", "Stack"),
            T("duration", "Duration", "How many stated turn boundaries an effect remains active. Different effects can count down on player or enemy turns.", Utility, "Duration", "Turns remaining"),
        };

        private static readonly Dictionary<string, GameplayTerm> byId = terms.ToDictionary(term => term.Id, StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameplayTerm> byWord = BuildWords();
        private static readonly Regex matcher = new(
            @"(?<![\p{L}\p{N}])(" + string.Join("|", byWord.Keys.OrderByDescending(word => word.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IReadOnlyList<GameplayTerm> All => terms;
        public static GameplayTerm Find(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out GameplayTerm term) ? term : null;

        public static string Format(string plainText, out List<GameplayTerm> encountered)
        {
            var found = new List<GameplayTerm>();
            encountered = found;
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return matcher.Replace(plainText, match =>
            {
                if (!byWord.TryGetValue(match.Value, out GameplayTerm term)) return match.Value;
                if (seen.Add(term.Id)) found.Add(term);
                return $"<link=\"{term.Id}\"><color=#{term.Color}>{match.Value}</color></link>";
            });
        }

        private static GameplayTerm T(string id, string label, string definition, string color, params string[] words) =>
            new(id, label, definition, color, words);

        private static Dictionary<string, GameplayTerm> BuildWords()
        {
            var result = new Dictionary<string, GameplayTerm>(StringComparer.OrdinalIgnoreCase);
            foreach (GameplayTerm term in terms)
                foreach (string word in term.Words)
                    if (!string.IsNullOrWhiteSpace(word)) result[word] = term;
            return result;
        }
    }

    /// <summary>Turns coloured TMP glossary links into inspect requests for their owning panel.</summary>
    public sealed class GameplayTermText : MonoBehaviour, IPointerClickHandler
    {
        private TMP_Text text;
        public event Action<string> TermClicked;

        private void Awake() => text = GetComponent<TMP_Text>();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (text == null || eventData == null || eventData.button != PointerEventData.InputButton.Left) return;
            int index = TMP_TextUtilities.FindIntersectingLink(text, eventData.position, eventData.pressEventCamera);
            if (index < 0 || index >= text.textInfo.linkCount) return;
            TermClicked?.Invoke(text.textInfo.linkInfo[index].GetLinkID());
        }
    }
}
