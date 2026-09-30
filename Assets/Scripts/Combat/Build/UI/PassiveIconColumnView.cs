using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Battle HUD: the run's passives as a vertical column of framed icons on the left side of the screen, in acquisition
    /// order. Inactive passives (no compatible ability equipped) are dimmed; a tile flashes when its passive triggers
    /// (any combat event it sources, or a modifier it added to a cast). Look and position: <see cref="BuildHudStyle"/>.
    /// </summary>
    public sealed class PassiveIconColumnView : MonoBehaviour
    {
        private const string PassivePrefix = "passive:";

        [Tooltip("Empty = Resources/Combat/UI/BuildHudStyle (or built-in defaults).")]
        [SerializeField] private BuildHudStyle style;

        private readonly List<BuildIconTile> tiles = new();
        private readonly Dictionary<string, BuildIconTile> tileById = new();
        private readonly HashSet<string> flashedThisFrame = new();
        private int flashFrame = -1;
        private RunBuildState build;
        private CombatBuildRuntime runtime;

        private BuildHudStyle Style
        {
            get
            {
                if (style == null) style = BuildHudStyle.LoadOrDefault();
                return style;
            }
        }

        public RectTransform Rect => (RectTransform)transform;
        public int Count => tiles.Count;

        public static PassiveIconColumnView Create(RectTransform parent, BuildHudStyle hudStyle)
        {
            BuildHudStyle s = hudStyle != null ? hudStyle : BuildHudStyle.LoadOrDefault();
            var go = new GameObject("Passive Column", typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = s.PassiveAnchor;
            rect.anchoredPosition = s.PassivePosition;
            rect.sizeDelta = new Vector2(s.PassiveTile.size.x, s.PassiveTile.size.y);
            PassiveIconColumnView view = go.AddComponent<PassiveIconColumnView>();
            view.style = hudStyle;
            return view;
        }

        public void Bind(CombatBuildRuntime buildRuntime, RunBuildState runBuild)
        {
            Unbind();
            runtime = buildRuntime;
            build = runBuild;
            if (build != null) build.Changed += Rebuild;
            if (runtime != null)
            {
                runtime.Changed += RefreshStates;
                runtime.Events.Recorded += HandleEvent;
            }
            Rebuild();
        }

        public void Unbind()
        {
            if (build != null) build.Changed -= Rebuild;
            if (runtime != null)
            {
                runtime.Changed -= RefreshStates;
                runtime.Events.Recorded -= HandleEvent;
            }
            build = null;
            runtime = null;
        }

        private void OnDestroy() => Unbind();

        // ---------- tiles ----------

        public void Rebuild()
        {
            BuildHudStyle s = Style;
            IconTileLook look = s.PassiveTile;
            var wanted = new List<PassiveInstance>();
            if (build != null && s.ShowPassiveColumn)
                foreach (PassiveInstance passive in build.Passives)
                    if (passive?.Definition != null) wanted.Add(passive);

            var keep = new HashSet<string>();
            tiles.Clear();
            foreach (PassiveInstance passive in wanted)
            {
                string id = passive.Definition.Id;
                keep.Add(id);
                if (!tileById.TryGetValue(id, out BuildIconTile tile) || tile == null)
                {
                    tile = BuildIconTile.Create("Passive " + id, transform, look, s.FontAsset, s.OutlineColor,
                        s.EffectCountSize, s.PassiveLevelSize, s.ShowPassiveNames ? s.PassiveNameSize : 0);
                    tile.Key = id;
                    tileById[id] = tile;
                    tile.Pop(s.EffectPopSeconds);
                }
                tiles.Add(tile);
            }
            foreach (string id in new List<string>(tileById.Keys))
            {
                if (keep.Contains(id)) continue;
                if (tileById[id] != null) Destroy(tileById[id].gameObject);
                tileById.Remove(id);
            }
            Layout();
            RefreshStates();
        }

        private void Layout()
        {
            BuildHudStyle s = Style;
            Vector2 size = s.PassiveTile.size;
            float step = size.y + s.PassiveSpacing;
            float height = tiles.Count == 0 ? size.y : tiles.Count * size.y + (tiles.Count - 1) * s.PassiveSpacing;
            Rect.sizeDelta = new Vector2(size.x, height);
            // Many passives: shrink the whole column rather than run off the screen.
            float fit = height > s.PassiveMaxHeight ? s.PassiveMaxHeight / height : 1f;
            Rect.localScale = new Vector3(fit, fit, 1f);
            for (int i = 0; i < tiles.Count; i++)
            {
                RectTransform rect = tiles[i].Rect;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(0f, -i * step);
            }
        }

        /// <summary>Content, level and active state of every tile (active state follows this encounter's passives).</summary>
        public void RefreshStates()
        {
            if (build == null) return;
            BuildHudStyle s = Style;
            IconTileLook look = s.PassiveTile;
            List<AbilityQuote> equipped = null;
            foreach (PassiveInstance passive in build.Passives)
            {
                if (passive?.Definition == null || !tileById.TryGetValue(passive.Definition.Id, out BuildIconTile tile) || tile == null) continue;
                PassiveDefinition definition = passive.Definition;
                Color category = s.CategoryColor(definition.Category);
                tile.SetContent(definition.Icon, Initial(definition.DisplayName), category);
                tile.SetFrameColor(s.PassiveFrameColor(definition, look));
                tile.SetCorner(s.ShowPassiveLevel && definition.MaxLevel > 1 ? BuildHudStyle.Roman(passive.Level) : string.Empty,
                    s.PassiveLevelColor);
                tile.SetLabel(s.ShowPassiveNames ? definition.DisplayName : string.Empty, s.TextColor);
                bool active;
                if (runtime != null && runtime.IsActive) active = IsHooked(passive);
                else
                {
                    equipped ??= build.EquippedQuotes();
                    active = RunBuildState.IsPassiveActive(passive, equipped);
                }
                tile.Alpha = active ? 1f : s.InactiveAlpha;
            }
        }

        private bool IsHooked(PassiveInstance passive)
        {
            foreach (PassiveHook hook in runtime.ActivePassives)
                if (hook.Instance == passive) return true;
            return false;
        }

        // ---------- triggers ----------

        private void HandleEvent(CombatEvent combatEvent)
        {
            if (combatEvent == null) return;
            Flash(combatEvent.SourceId);
            if (combatEvent.Kind != CombatEventKind.CastResolved || runtime == null) return;
            // Cast modifiers (damage bonuses...) are not events of their own: flash the passives that shaped this cast.
            CastSnapshot cast = runtime.CurrentCast != null && runtime.CurrentCast.CastId == combatEvent.CastId ? runtime.CurrentCast
                : runtime.LastCast != null && runtime.LastCast.CastId == combatEvent.CastId ? runtime.LastCast : null;
            if (cast == null) return;
            foreach (CastModifier modifier in cast.Modifiers) Flash(modifier.SourceId);
        }

        private void Flash(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId) || !sourceId.StartsWith(PassivePrefix)) return;
            string id = sourceId.Substring(PassivePrefix.Length);
            if (Time.frameCount != flashFrame)
            {
                flashFrame = Time.frameCount;
                flashedThisFrame.Clear();
            }
            if (!flashedThisFrame.Add(id)) return;
            if (!tileById.TryGetValue(id, out BuildIconTile tile) || tile == null) return;
            BuildHudStyle s = Style;
            tile.Flash(s.TriggerFlashColor, s.TriggerFlashSeconds, s.TriggerPunchScale);
        }

        public static string Initial(string name)
        {
            string trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) ? "?" : trimmed.Substring(0, 1).ToUpperInvariant();
        }
    }
}
