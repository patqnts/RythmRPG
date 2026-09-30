using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// A row of icons for what is in effect right now, each with its turns left: above the player's health bar (buffs,
    /// shield, wards, counter charges) or under the enemy's (statuses such as Burn). The icon is the ability / passive
    /// that applied the effect (fallback: its first letter). Anything implementing <see cref="ICombatEffectIcon"/> in the
    /// <see cref="CombatModifierSystem"/> shows up here. Look: <see cref="BuildHudStyle"/>.
    /// </summary>
    public sealed class CombatEffectIconsView : MonoBehaviour
    {
        [Tooltip("Empty = Resources/Combat/UI/BuildHudStyle (or built-in defaults).")]
        [SerializeField] private BuildHudStyle style;
        [Tooltip("Show enemy statuses (on) or the player's buffs (off).")]
        [SerializeField] private bool enemySide;

        private sealed class Entry
        {
            public object Key;
            public Sprite Icon;
            public string Label;
            public int Count;
            public Color Frame;
            /// <summary>Count is turns left (last turn is highlighted), not charges.</summary>
            public bool Turns = true;
        }

        private readonly Dictionary<object, BuildIconTile> tiles = new();
        private readonly Dictionary<object, int> shownCounts = new();
        private readonly List<Entry> entries = new();
        private readonly object counterKey = new();
        private CombatModifierSystem modifiers;
        private CombatBuildRuntime runtime;

        private BuildHudStyle Style
        {
            get
            {
                if (style == null) style = BuildHudStyle.LoadOrDefault();
                return style;
            }
        }

        public bool EnemySide => enemySide;
        public int Count => entries.Count;

        /// <summary>Creates the row as a child of a health bar: above it (player) or under it (enemy).</summary>
        public static CombatEffectIconsView Create(RectTransform bar, bool enemy, BuildHudStyle hudStyle)
        {
            BuildHudStyle s = hudStyle != null ? hudStyle : BuildHudStyle.LoadOrDefault();
            var go = new GameObject(enemy ? "Enemy Effects" : "Player Effects", typeof(RectTransform));
            go.layer = bar != null ? bar.gameObject.layer : go.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(bar, false);
            if (enemy)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = s.EnemyEffectsOffset;
            }
            else
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = s.PlayerEffectsOffset;
            }
            rect.sizeDelta = s.EffectTile.size;
            CombatEffectIconsView view = go.AddComponent<CombatEffectIconsView>();
            view.style = hudStyle;
            view.enemySide = enemy;
            return view;
        }

        public void Bind(CombatModifierSystem modifierSystem, CombatBuildRuntime buildRuntime)
        {
            Unbind();
            modifiers = modifierSystem;
            runtime = buildRuntime;
            if (modifiers != null) modifiers.Changed += Refresh;
            if (runtime != null) runtime.Changed += Refresh;
            Clear();
            Refresh();
        }

        public void Unbind()
        {
            if (modifiers != null) modifiers.Changed -= Refresh;
            if (runtime != null) runtime.Changed -= Refresh;
            modifiers = null;
            runtime = null;
        }

        private void OnDestroy() => Unbind();

        public void Clear()
        {
            foreach (BuildIconTile tile in tiles.Values)
                if (tile != null) Destroy(tile.gameObject);
            tiles.Clear();
            shownCounts.Clear();
            entries.Clear();
        }

        // ---------- refresh ----------

        public void Refresh()
        {
            BuildHudStyle s = Style;
            Collect(s);
            IconTileLook look = s.EffectTile;
            var keep = new HashSet<object>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                keep.Add(entry.Key);
                bool created = false;
                if (!tiles.TryGetValue(entry.Key, out BuildIconTile tile) || tile == null)
                {
                    tile = BuildIconTile.Create((enemySide ? "Status " : "Effect ") + entry.Label, transform, look, s.FontAsset,
                        s.OutlineColor, s.EffectCountSize, s.EffectCountSize);
                    tiles[entry.Key] = tile;
                    created = true;
                }
                tile.SetFrameColor(entry.Frame);
                tile.SetContent(entry.Icon, PassiveIconColumnView.Initial(entry.Label), entry.Frame);
                tile.SetCount(entry.Count > 0 ? entry.Count.ToString() : string.Empty,
                    entry.Turns && entry.Count == 1 ? s.ExpiringCountColor : s.EffectCountColor);
                RectTransform rect = tile.Rect;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(i * (look.size.x + s.EffectSpacing), 0f);
                bool changed = shownCounts.TryGetValue(entry.Key, out int previous) && previous != entry.Count;
                shownCounts[entry.Key] = entry.Count;
                if (created || changed) tile.Pop(s.EffectPopSeconds);
            }
            foreach (object key in new List<object>(tiles.Keys))
            {
                if (keep.Contains(key)) continue;
                if (tiles[key] != null) Destroy(tiles[key].gameObject);
                tiles.Remove(key);
                shownCounts.Remove(key);
            }
            float width = entries.Count == 0 ? look.size.x : entries.Count * look.size.x + (entries.Count - 1) * s.EffectSpacing;
            ((RectTransform)transform).sizeDelta = new Vector2(width, look.size.y);
        }

        private void Collect(BuildHudStyle s)
        {
            entries.Clear();
            if (!s.ShowEffectIcons || (enemySide && !s.ShowEnemyEffects)) return;
            if (modifiers != null)
            {
                foreach (ICombatModifierRuntime modifier in modifiers.ActiveModifiers)
                {
                    if (modifier == null || modifier.IsExpired || !(modifier is ICombatEffectIcon effect)) continue;
                    if (effect.IconOnEnemy != enemySide) continue;
                    entries.Add(new Entry
                    {
                        Key = modifier,
                        Icon = effect.Icon,
                        Label = effect.IconLabel,
                        Count = effect.IconCount,
                        Frame = effect.IconIsDebuff ? s.DebuffFrameColor : s.BuffFrameColor
                    });
                }
            }
            if (!enemySide && s.ShowCounterCharges && runtime != null && runtime.IsActive && runtime.Counters != null
                && runtime.Counters.Charges > 0)
            {
                entries.Add(new Entry
                {
                    Key = counterKey,
                    Icon = s.CounterIcon,
                    Label = "Counter",
                    Count = runtime.Counters.Charges,
                    Frame = s.CounterFrameColor,
                    Turns = false
                });
            }
        }
    }
}
