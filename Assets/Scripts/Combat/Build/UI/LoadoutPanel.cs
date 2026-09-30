using System.Collections.Generic;
using System.Linq;
using RythmRPG.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// The loadout panel, toggled with the Loadout action (Tab / gamepad Select): the four ability slots, the reserve
    /// and every owned passive, with a details box for the highlighted entry.
    /// <list type="bullet">
    /// <item><b>Outside battle</b> (run build active): rearrange. Pick a slot or reserve ability, then place it on a slot
    /// (slots swap; a reserve ability replaces the slot's occupant, which goes to reserve). X / gamepad West sends a
    /// slot to reserve. A change that would leave no 0 MP action (or no ability) is refused and undone.
    /// Exploration input (Move / Interact) is held while it is open.</item>
    /// <item><b>During ability selection</b>: view only (cooldowns and mana shown). It never takes input the lanes use and
    /// closes itself when the choice is made.</item>
    /// </list>
    /// Created automatically (see <see cref="BuildHudStyle.EnableLoadoutPanel"/>); one placed in a scene takes over.
    /// </summary>
    public sealed class LoadoutPanel : MonoBehaviour
    {
        private enum ItemKind { Slot, Reserve, Passive }

        private sealed class Item
        {
            public ItemKind Kind;
            public int Column;
            public int Slot = -1;
            public string KeyLabel;
            public AbilityInstance Ability;
            public AbilityRuntimeInstance Runtime;
            public AbilityDefinition Definition;
            public PassiveInstance Passive;
            public RectTransform Row;
            public Image Background;
            public BuildIconTile Tile;
            public TMP_Text Title;
            public TMP_Text Detail;
            public float Top;
            public float Height;
        }

        private const float SlotRowHeight = 84f;
        private const float ReserveRowHeight = 70f;
        private const float PassiveRowHeight = 70f;
        private const float RowGap = 6f;
        private const float SubheaderHeight = 40f;
        private const float StickThreshold = 0.6f;

        public static LoadoutPanel Instance { get; private set; }
        /// <summary>Set false to keep the panel from opening (cutscenes, menus...). Reset on play mode start.</summary>
        public static bool Allowed { get; set; } = true;
        public static bool IsOpen => Instance != null && Instance.open;

        [Tooltip("Empty = Resources/Combat/UI/BuildHudStyle (or built-in defaults).")]
        [SerializeField] private BuildHudStyle style;

        [Header("Parts")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image dim;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private TMP_Text abilityHeader;
        [SerializeField] private TMP_Text passiveHeader;
        [SerializeField] private RectTransform abilityViewport;
        [SerializeField] private RectTransform abilityContent;
        [SerializeField] private RectTransform passiveViewport;
        [SerializeField] private RectTransform passiveContent;
        [SerializeField] private TMP_Text detailTitle;
        [SerializeField] private TMP_Text detailBody;
        [SerializeField] private TMP_Text message;
        [SerializeField] private TMP_Text prompt;
        [SerializeField] private AudioSource audioSource;

        private readonly List<Item> items = new();
        private readonly List<GameObject> spawned = new();
        private bool autoCreated;
        private bool open;
        private bool closing;
        private bool editable;
        private bool inBattle;
        private float fadeFrom;
        private float fadeAt;
        private float messageUntil;
        private int selected;
        private Item picked;
        private int builtVersion = -1;
        private CombatController controller;
        private RunBuildState build;
        private Vector2Int stickDirection;
        private float stickRepeatAt;
        private float refreshAt;

        private BuildHudStyle Style
        {
            get
            {
                if (style == null) style = BuildHudStyle.LoadOrDefault();
                return style;
            }
        }

        public bool IsEditable => open && editable;

        // ---------- lifetime ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Enter Play Mode Options with domain reload off.
            Instance = null;
            Allowed = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null || !BuildHudStyle.LoadOrDefault().EnableLoadoutPanel) return;
            if (FindAnyObjectByType<LoadoutPanel>(FindObjectsInactive.Include) != null) return;
            LoadoutPanel panel = CreateTemplate(null);
            panel.autoCreated = true;
            DontDestroyOnLoad(panel.gameObject);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A panel placed in a scene replaces the generated one.
                if (Instance.autoCreated) Destroy(Instance.gameObject);
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
            Instance = this;
            HideImmediate();
        }

        private void OnDestroy()
        {
            GameInput.UnblockGameplay(this);
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            if (open) HideImmediate();
        }

        // ---------- open / close ----------

        /// <summary>Opens the panel if the game allows it right now (not paused, outside battle or while choosing an ability).</summary>
        public bool TryOpen()
        {
            if (open || !Allowed || GamePause.IsPaused) return false;
            controller = ResolveController();
            inBattle = controller != null && controller.IsBattleActive;
            if (inBattle && controller.CurrentState != CombatState.PlayerAbilitySelection) return false;
            build = inBattle
                ? (controller.AbilitySlots != null && controller.AbilitySlots.SourceBuild != null ? controller.AbilitySlots.SourceBuild
                    : controller.BuildRuntime != null && controller.BuildRuntime.Build != null ? controller.BuildRuntime.Build : RunBuild.Current)
                : RunBuild.Current;
            editable = !inBattle && build != null;

            open = true;
            closing = false;
            picked = null;
            selected = 0;
            if (canvas != null) canvas.enabled = true;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
                group.interactable = true;
            }
            fadeFrom = 0f;
            fadeAt = GamePause.UnpausedRealtime;
            messageUntil = 0f;
            if (message != null) message.text = string.Empty;
            GamePause.EnsureEventSystem();
            if (editable) GameInput.BlockGameplay(this);
            ApplyStaticTexts();
            Rebuild();
            Play(Style.OpenSound);
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            closing = true;
            fadeFrom = group != null ? group.alpha : 1f;
            fadeAt = GamePause.UnpausedRealtime;
            picked = null;
            if (group != null) group.blocksRaycasts = false;
            GameInput.UnblockGameplay(this);
        }

        public void Toggle()
        {
            if (open && !closing) Close();
            else if (!open) TryOpen();
        }

        private void HideImmediate()
        {
            open = false;
            closing = false;
            picked = null;
            GameInput.UnblockGameplay(this);
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            if (canvas != null) canvas.enabled = false;
            ClearRows();
        }

        private CombatController ResolveController()
        {
            if (controller == null) controller = FindAnyObjectByType<CombatController>();
            return controller;
        }

        // ---------- update ----------

        private void Update()
        {
            float now = GamePause.UnpausedRealtime;
            if (closing)
            {
                float t = Style.FadeSeconds <= 0f ? 1f : Mathf.Clamp01((now - fadeAt) / Style.FadeSeconds);
                if (group != null) group.alpha = Mathf.Lerp(fadeFrom, 0f, t);
                if (t >= 1f) HideImmediate();
                return;
            }

            if (!open)
            {
                if (!GameInput.IsRebinding && GameInput.LoadoutPressed) TryOpen();
                return;
            }

            if (group != null)
                group.alpha = Style.FadeSeconds <= 0f ? 1f : Mathf.Clamp01((now - fadeAt) / Style.FadeSeconds);
            if (message != null && messageUntil > 0f && now > messageUntil)
            {
                message.text = string.Empty;
                messageUntil = 0f;
            }

            // View-only in battle: gone as soon as the ability is chosen (or the battle ends).
            // Editing: a battle starting underneath closes it.
            bool battleNow = controller != null && controller.IsBattleActive;
            if (inBattle ? !battleNow || controller.CurrentState != CombatState.PlayerAbilitySelection : battleNow)
            {
                Close();
                return;
            }

            if (build != null && build.Version != builtVersion) Rebuild(); // changed elsewhere (F11 panel, rewards)
            else if (inBattle && now >= refreshAt)
            {
                refreshAt = now + 0.25f;
                RefreshTexts(); // cooldowns / mana
            }

            if (GamePause.IsPaused || GameInput.IsRebinding) return;
            ReadInput(now);
        }

        private void ReadInput(float now)
        {
            if (GameInput.LoadoutPressed)
            {
                Close();
                return;
            }
            BuildHudStyle s = Style;
            Gamepad pad = Gamepad.current;
            // In battle the d-pad and face buttons are lane keys: only keyboard arrows and the right stick navigate.
            bool padNav = pad != null && !inBattle;
            bool up = LegacyKeys.WasPressed(KeyCode.UpArrow) || (!inBattle && LegacyKeys.WasPressed(KeyCode.W)) || (padNav && pad.dpad.up.wasPressedThisFrame);
            bool down = LegacyKeys.WasPressed(KeyCode.DownArrow) || (!inBattle && LegacyKeys.WasPressed(KeyCode.S)) || (padNav && pad.dpad.down.wasPressedThisFrame);
            bool left = LegacyKeys.WasPressed(KeyCode.LeftArrow) || (!inBattle && LegacyKeys.WasPressed(KeyCode.A)) || (padNav && pad.dpad.left.wasPressedThisFrame);
            bool right = LegacyKeys.WasPressed(KeyCode.RightArrow) || (!inBattle && LegacyKeys.WasPressed(KeyCode.D)) || (padNav && pad.dpad.right.wasPressedThisFrame);
            Vector2Int stick = StickStep(pad, now);
            up |= stick.y > 0;
            down |= stick.y < 0;
            left |= stick.x < 0;
            right |= stick.x > 0;

            if (CombatResultStyle.AnyKeyDown(s.CloseKeys) || (padNav && pad.buttonEast.wasPressedThisFrame))
            {
                if (picked != null)
                {
                    picked = null;
                    ApplySelection();
                }
                else Close();
                return;
            }
            if (up) Move(-1);
            if (down) Move(+1);
            if (left) SwitchColumn(0);
            if (right) SwitchColumn(1);
            if (!editable) return;
            if (CombatResultStyle.AnyKeyDown(s.ConfirmKeys) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) Confirm();
            else if (CombatResultStyle.AnyKeyDown(s.ReserveKeys) || (pad != null && pad.buttonWest.wasPressedThisFrame)) SendToReserve();
        }

        // One step per push, repeating while held.
        private Vector2Int StickStep(Gamepad pad, float now)
        {
            if (pad == null) return Vector2Int.zero;
            Vector2 value = pad.rightStick.ReadValue();
            if (!inBattle)
            {
                Vector2 leftStick = pad.leftStick.ReadValue();
                if (leftStick.sqrMagnitude > value.sqrMagnitude) value = leftStick;
            }
            Vector2Int direction = Vector2Int.zero;
            if (Mathf.Abs(value.y) >= StickThreshold && Mathf.Abs(value.y) >= Mathf.Abs(value.x)) direction.y = value.y > 0f ? 1 : -1;
            else if (Mathf.Abs(value.x) >= StickThreshold) direction.x = value.x > 0f ? 1 : -1;
            if (direction == Vector2Int.zero)
            {
                stickDirection = Vector2Int.zero;
                return direction;
            }
            if (direction != stickDirection)
            {
                stickDirection = direction;
                stickRepeatAt = now + 0.35f;
                return direction;
            }
            if (now < stickRepeatAt) return Vector2Int.zero;
            stickRepeatAt = now + 0.12f;
            return direction;
        }

        // ---------- navigation ----------

        private Item Current => selected >= 0 && selected < items.Count ? items[selected] : null;

        private void Move(int step)
        {
            Item current = Current;
            if (current == null) return;
            List<int> column = ColumnIndices(current.Column);
            if (column.Count == 0) return;
            int position = column.IndexOf(selected);
            int next = column[((position + step) % column.Count + column.Count) % column.Count];
            Select(next);
        }

        private void SwitchColumn(int column)
        {
            Item current = Current;
            if (current == null || current.Column == column) return;
            List<int> target = ColumnIndices(column);
            if (target.Count == 0) return;
            int position = ColumnIndices(current.Column).IndexOf(selected);
            Select(target[Mathf.Clamp(position, 0, target.Count - 1)]);
        }

        private List<int> ColumnIndices(int column)
        {
            var result = new List<int>();
            for (int i = 0; i < items.Count; i++)
                if (items[i].Column == column) result.Add(i);
            return result;
        }

        private void Select(int index)
        {
            if (index < 0 || index >= items.Count || index == selected) return;
            selected = index;
            Play(Style.MoveSound);
            ApplySelection();
        }

        private void HoverRow(int index)
        {
            if (open && !closing && !GamePause.IsPaused) Select(index);
        }

        private void ClickRow(int index)
        {
            if (!open || closing || GamePause.IsPaused) return;
            Select(index);
            if (editable) Confirm();
        }

        // ---------- editing ----------

        private void Confirm()
        {
            Item item = Current;
            if (item == null || build == null) return;
            if (item.Kind == ItemKind.Passive)
            {
                Warn("Passives always apply; they are not equipped.");
                return;
            }
            if (picked == null)
            {
                if (item.Ability == null)
                {
                    Warn("Empty slot: pick an ability first, then place it here.");
                    return;
                }
                picked = item;
                Play(Style.ConfirmSound);
                ApplySelection();
                return;
            }
            if (picked == item || (picked.Kind == ItemKind.Reserve && item.Kind == ItemKind.Reserve))
            {
                // Same entry: drop the pick. Another reserve entry: pick that one instead.
                picked = picked == item ? null : item;
                ApplySelection();
                return;
            }

            AbilityInstance incoming;
            int slot;
            if (item.Kind == ItemKind.Slot)
            {
                incoming = picked.Ability; // slot <-> slot swaps, reserve -> slot replaces
                slot = item.Slot;
            }
            else
            {
                incoming = item.Ability; // slot -> reserve entry: that reserve ability takes the picked slot
                slot = picked.Slot;
            }
            AbilityInstance[] before = Snapshot();
            bool changed = build.Equip(incoming, slot);
            picked = null;
            if (changed && !KeepIfValid(before)) return;
            Play(Style.ConfirmSound);
            RebuildKeepingSelection(ItemKind.Slot, slot);
        }

        private void SendToReserve()
        {
            Item item = Current;
            if (item == null || build == null) return;
            if (item.Kind != ItemKind.Slot || item.Ability == null)
            {
                Warn(item.Kind == ItemKind.Slot ? "That slot is already empty." : "Only equipped abilities can go to reserve.");
                return;
            }
            AbilityInstance[] before = Snapshot();
            build.Unequip(item.Slot);
            picked = null;
            if (!KeepIfValid(before)) return;
            Play(Style.ConfirmSound);
            RebuildKeepingSelection(ItemKind.Slot, item.Slot);
        }

        private AbilityInstance[] Snapshot()
        {
            var slots = new AbilityInstance[RunBuildState.SlotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = build.GetSlot(i);
            return slots;
        }

        /// <summary>Undoes the change (and says why) when the loadout would be unplayable.</summary>
        private bool KeepIfValid(AbilityInstance[] before)
        {
            string problem = build.LoadoutProblem();
            if (problem == null) return true;
            for (int i = 0; i < RunBuildState.SlotCount; i++) build.Unequip(i);
            for (int i = 0; i < before.Length; i++)
                if (before[i] != null) build.Equip(before[i], i);
            Warn(problem);
            Rebuild();
            return false;
        }

        private void Warn(string text)
        {
            Play(Style.DeniedSound);
            if (message == null) return;
            message.text = RewardCardView.Plain(text);
            message.color = Style.WarningColor;
            messageUntil = GamePause.UnpausedRealtime + 2.5f;
        }

        // ---------- rows ----------

        private void RebuildKeepingSelection(ItemKind kind, int slot)
        {
            Rebuild();
            int index = items.FindIndex(item => item.Kind == kind && item.Slot == slot);
            if (index >= 0)
            {
                selected = index;
                ApplySelection();
            }
        }

        private void Rebuild()
        {
            ClearRows();
            builtVersion = build != null ? build.Version : -1;
            BuildHudStyle s = Style;
            float y = 0f;

            if (abilityContent != null)
            {
                if (build == null && !inBattle)
                {
                    y = AddSubheader(abilityContent, y, s.NoBuildText, s.MutedColor, 120f);
                }
                else
                {
                    for (int slot = 0; slot < RunBuildState.SlotCount; slot++)
                    {
                        Item item = SlotItem(slot);
                        if (item == null) continue;
                        y = AddRow(abilityContent, item, y, SlotRowHeight);
                    }
                    if (build != null)
                    {
                        y = AddSubheader(abilityContent, y + 4f, "RESERVE", s.TitleColor, SubheaderHeight);
                        List<AbilityInstance> reserve = build.Reserve.ToList();
                        if (reserve.Count == 0) y = AddSubheader(abilityContent, y, "Nothing in reserve.", s.MutedColor, 34f);
                        foreach (AbilityInstance ability in reserve)
                            y = AddRow(abilityContent, new Item { Kind = ItemKind.Reserve, Column = 0, Ability = ability, Definition = ability.Definition },
                                y, ReserveRowHeight);
                    }
                }
                abilityContent.sizeDelta = new Vector2(abilityContent.sizeDelta.x, y);
            }

            y = 0f;
            if (passiveContent != null)
            {
                if (build == null || build.Passives.Count == 0)
                    y = AddSubheader(passiveContent, y, build == null ? "No run build." : "No passives yet. Win fights to earn some.", s.MutedColor, 40f);
                else
                    foreach (PassiveInstance passive in build.Passives)
                        if (passive?.Definition != null)
                            y = AddRow(passiveContent, new Item { Kind = ItemKind.Passive, Column = 1, Passive = passive }, y, PassiveRowHeight);
                passiveContent.sizeDelta = new Vector2(passiveContent.sizeDelta.x, y);
            }

            if (picked != null) picked = items.FirstOrDefault(item => item.Kind == picked.Kind && item.Ability == picked.Ability && item.Slot == picked.Slot);
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, items.Count - 1));
            RefreshTexts();
            ApplySelection();
        }

        private Item SlotItem(int slot)
        {
            var item = new Item { Kind = ItemKind.Slot, Column = 0, Slot = slot, KeyLabel = GameInput.LaneLabel(GameInput.LaneKeySlot(RunBuildState.SlotCount, slot + 1)) };
            if (inBattle && controller != null && controller.AbilitySlots != null)
            {
                // What the battle actually uses (legacy loadouts have no build).
                foreach (KeyValuePair<int, AbilityRuntimeInstance> pair in controller.AbilitySlots.Slots)
                {
                    int index = controller.AbilitySlots.SlotIndexOfLane(pair.Key);
                    if (index < 0) index = pair.Key - 1;
                    if (index != slot) continue;
                    item.Runtime = pair.Value;
                    item.Definition = pair.Value?.Definition;
                    item.Ability = pair.Value?.BuildInstance;
                    item.KeyLabel = GameInput.LaneLabel(GameInput.LaneKeySlot(controller.AbilitySlots.HighestLaneId, pair.Key));
                }
                if (item.Runtime == null && build == null) return null;
            }
            if (item.Runtime == null && build != null)
            {
                item.Ability = build.GetSlot(slot);
                item.Definition = item.Ability?.Definition;
            }
            return item;
        }

        private float AddRow(RectTransform content, Item item, float y, float height)
        {
            BuildHudStyle s = Style;
            int index = items.Count;
            RectTransform row = NewRect("Row " + index, content);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.offsetMin = new Vector2(0f, -y - height);
            row.offsetMax = new Vector2(0f, -y);
            Image background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.05f);
            background.raycastTarget = true;
            RewardChoiceButton button = row.gameObject.AddComponent<RewardChoiceButton>();
            button.Index = index;
            button.Hovered += HoverRow;
            button.Clicked += ClickRow;

            IconTileLook look = s.PanelTile;
            float tileSize = Mathf.Min(look.size.y, height - 10f);
            BuildIconTile tile = BuildIconTile.Create("Icon", row, look, s.FontAsset, s.OutlineColor, s.RowDetailSize, s.RowDetailSize);
            tile.Rect.sizeDelta = new Vector2(tileSize, tileSize);
            tile.Rect.anchorMin = tile.Rect.anchorMax = tile.Rect.pivot = new Vector2(0f, 0.5f);
            tile.Rect.anchoredPosition = new Vector2(10f, 0f);

            float textLeft = 10f + tileSize + 14f;
            TMP_Text rowTitle = NewText("Title", row, s.RowTitleSize, s.TextColor, TextAlignmentOptions.Left);
            rowTitle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            rowTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            rowTitle.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            rowTitle.rectTransform.offsetMax = new Vector2(-12f, -4f);
            rowTitle.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text rowDetail = NewText("Detail", row, s.RowDetailSize, s.MutedColor, TextAlignmentOptions.Left);
            rowDetail.rectTransform.anchorMin = new Vector2(0f, 0f);
            rowDetail.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            rowDetail.rectTransform.offsetMin = new Vector2(textLeft, 4f);
            rowDetail.rectTransform.offsetMax = new Vector2(-12f, 0f);
            rowDetail.overflowMode = TextOverflowModes.Ellipsis;

            item.Row = row;
            item.Background = background;
            item.Tile = tile;
            item.Title = rowTitle;
            item.Detail = rowDetail;
            item.Top = y;
            item.Height = height;
            items.Add(item);
            spawned.Add(row.gameObject);
            return y + height + RowGap;
        }

        private float AddSubheader(RectTransform content, float y, string text, Color color, float height)
        {
            BuildHudStyle s = Style;
            TMP_Text label = NewText("Subheader", content, s.RowDetailSize + 2, color, TextAlignmentOptions.BottomLeft);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(8f, -y - height);
            rect.offsetMax = new Vector2(-8f, -y);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.text = RewardCardView.Plain(text);
            spawned.Add(label.gameObject);
            return y + height + RowGap;
        }

        private void ClearRows()
        {
            foreach (GameObject go in spawned)
                if (go != null) Destroy(go);
            spawned.Clear();
            items.Clear();
        }

        // ---------- texts ----------

        private void ApplyStaticTexts()
        {
            BuildHudStyle s = Style;
            if (dim != null) dim.color = s.DimColor;
            if (title != null)
            {
                title.text = s.PanelTitle;
                title.color = s.TitleColor;
            }
            if (subtitle != null)
            {
                string mode = inBattle ? s.BattleSubtitle : build != null ? s.EditSubtitle : string.Empty;
                string name = build != null ? build.DisplayName : inBattle ? "Default loadout" : string.Empty;
                subtitle.text = RewardCardView.Plain(string.IsNullOrEmpty(name) ? mode : string.IsNullOrEmpty(mode) ? name : name + "  |  " + mode);
            }
            if (abilityHeader != null) abilityHeader.text = "ABILITY SLOTS";
            if (passiveHeader != null) passiveHeader.text = build != null ? $"PASSIVES ({build.Passives.Count})" : "PASSIVES";
            string closeKey = GameInput.DisplayString(GameInput.Loadout, GameInput.FindBindingIndex(GameInput.Loadout, GameInput.KeyboardGroup));
            closeKey = string.IsNullOrEmpty(closeKey) ? "TAB" : closeKey.ToUpperInvariant();
            if (prompt != null) prompt.text = editable ? s.EditPrompt(closeKey) : s.ViewPrompt(closeKey);
        }

        private void RefreshTexts()
        {
            BuildHudStyle s = Style;
            PlayerCombatant player = inBattle && controller != null ? controller.Encounter.Player : null;
            List<AbilityQuote> equipped = build?.EquippedQuotes();
            foreach (Item item in items)
            {
                switch (item.Kind)
                {
                    case ItemKind.Passive:
                        FillPassive(item, s, equipped);
                        break;
                    default:
                        FillAbility(item, s, player);
                        break;
                }
            }
            UpdateDetails();
        }

        private void FillAbility(Item item, BuildHudStyle s, PlayerCombatant player)
        {
            AbilityDefinition definition = item.Definition;
            string key = item.Kind == ItemKind.Slot ? item.KeyLabel : null;
            if (item.Tile != null)
            {
                item.Tile.SetContent(definition != null ? definition.Icon : null,
                    definition != null ? PassiveIconColumnView.Initial(definition.DisplayName) : "-", s.HighlightColor);
                item.Tile.SetCorner(string.Empty, s.TitleColor);
                item.Tile.SetCount(string.Empty, s.TextColor);
            }
            if (definition == null)
            {
                SetText(item.Title, $"Slot {item.Slot + 1}  (empty)", s.MutedColor);
                SetText(item.Detail, editable ? "Pick a reserve ability and place it here." : string.Empty, s.MutedColor);
                return;
            }
            AbilityQuote quote = QuoteOf(item);
            int upgrades = item.Ability != null ? item.Ability.Upgrades.Count : 0;
            string name = definition.DisplayName + (upgrades > 0 ? $"  +{upgrades}" : string.Empty);
            SetText(item.Title, item.Kind == ItemKind.Slot ? $"[{key}] {name}" : name, s.TextColor);

            string stats = $"{quote.CostText} | CD {quote.Cooldown} | {quote.Roles.ToString().Replace(", ", "/")}"
                           + (quote.Element != ElementType.None ? " | " + quote.Element : string.Empty);
            Color detailColor = s.MutedColor;
            if (item.Runtime != null && player != null)
            {
                if (item.Runtime.RemainingCooldown > 0)
                {
                    stats = $"COOLDOWN {item.Runtime.RemainingCooldown} | " + stats;
                    detailColor = s.WarningColor;
                }
                else if (!item.Runtime.CanUse(player))
                {
                    stats = "NOT ENOUGH MP | " + stats;
                    detailColor = s.WarningColor;
                }
                else stats = "READY | " + stats;
            }
            SetText(item.Detail, stats, detailColor);
        }

        private void FillPassive(Item item, BuildHudStyle s, List<AbilityQuote> equipped)
        {
            PassiveInstance passive = item.Passive;
            PassiveDefinition definition = passive.Definition;
            bool active = IsPassiveActive(passive, equipped);
            if (item.Tile != null)
            {
                item.Tile.SetContent(definition.Icon, PassiveIconColumnView.Initial(definition.DisplayName), s.CategoryColor(definition.Category));
                item.Tile.SetFrameColor(s.PassiveFrameColor(definition, s.PanelTile));
                item.Tile.SetCorner(definition.MaxLevel > 1 ? BuildHudStyle.Roman(passive.Level) : string.Empty, s.PassiveLevelColor);
                item.Tile.Alpha = active ? 1f : s.InactiveAlpha;
            }
            string level = definition.MaxLevel > 1 ? $"  Lv {passive.Level}/{definition.MaxLevel}" : string.Empty;
            SetText(item.Title, definition.DisplayName + level, active ? s.TextColor : s.MutedColor);
            string summary = !string.IsNullOrEmpty(definition.Description) ? definition.Description : definition.DescribeLevel(passive.Level);
            string inactive = definition.Requirement.IsEmpty ? "INACTIVE with this loadout" : "INACTIVE: needs " + definition.Requirement.Describe();
            SetText(item.Detail, active ? summary : inactive, active ? s.MutedColor : s.WarningColor);
        }

        private bool IsPassiveActive(PassiveInstance passive, List<AbilityQuote> equipped)
        {
            CombatBuildRuntime runtime = inBattle && controller != null ? controller.BuildRuntime : null;
            if (runtime != null && runtime.IsActive)
                return runtime.ActivePassives.Any(hook => hook.Instance == passive);
            return equipped != null && RunBuildState.IsPassiveActive(passive, equipped);
        }

        private AbilityQuote QuoteOf(Item item)
        {
            if (item.Runtime != null) return item.Runtime.Quote();
            if (item.Ability != null && build != null) return build.Quote(item.Ability);
            return AbilityQuote.FromDefinition(item.Definition);
        }

        private void UpdateDetails()
        {
            BuildHudStyle s = Style;
            Item item = Current;
            if (detailTitle == null || detailBody == null) return;
            if (item == null)
            {
                detailTitle.text = string.Empty;
                detailBody.text = string.Empty;
                return;
            }
            var lines = new List<string>();
            if (item.Kind == ItemKind.Passive)
            {
                PassiveDefinition definition = item.Passive.Definition;
                detailTitle.text = RewardCardView.Plain($"{definition.DisplayName}  Lv {item.Passive.Level}/{definition.MaxLevel}  |  {Words(definition.Category.ToString())}");
                detailTitle.color = s.CategoryColor(definition.Category);
                if (!string.IsNullOrEmpty(definition.Description)) lines.Add(definition.Description);
                lines.Add("Now: " + definition.DescribeLevel(item.Passive.Level));
                if (!item.Passive.IsMaxLevel) lines.Add("Next level: " + definition.DescribeLevel(item.Passive.Level + 1));
                if (!definition.Requirement.IsEmpty) lines.Add("Active with " + definition.Requirement.Describe() + " equipped.");
            }
            else if (item.Definition == null)
            {
                detailTitle.text = $"Slot {item.Slot + 1}";
                detailTitle.color = s.MutedColor;
                lines.Add(editable ? "Empty. Pick an ability from the reserve (or another slot) and place it here." : "Empty.");
            }
            else
            {
                AbilityQuote quote = QuoteOf(item);
                string where = item.Kind == ItemKind.Slot ? $"Slot {item.Slot + 1} [{item.KeyLabel}]" : "Reserve";
                detailTitle.text = RewardCardView.Plain($"{item.Definition.DisplayName}  |  {where}");
                detailTitle.color = picked == item ? s.PickedColor : s.HighlightColor;
                lines.Add(RewardDirector.DescribeAbility(quote));
                if (!string.IsNullOrEmpty(item.Definition.Description)) lines.Add(item.Definition.Description);
                foreach (AbilityEffect effect in quote.Effects)
                    if (effect != null) lines.Add("- " + effect.Describe(item.Definition));
                if (item.Ability != null)
                    foreach (AbilityUpgradeDefinition upgrade in item.Ability.Upgrades)
                        if (upgrade != null) lines.Add("+ " + upgrade.DisplayName + ": " + upgrade.Summary());
                foreach (string change in quote.Changes) lines.Add("> " + change);
            }
            detailBody.text = RewardCardView.Plain(string.Join("\n", lines));
            detailBody.color = s.TextColor;
        }

        private static string Words(string pascal)
        {
            var result = new System.Text.StringBuilder();
            foreach (char c in pascal)
            {
                if (char.IsUpper(c) && result.Length > 0) result.Append(' ');
                result.Append(c);
            }
            return result.ToString();
        }

        private static void SetText(TMP_Text text, string value, Color color)
        {
            if (text == null) return;
            text.text = RewardCardView.Plain(value);
            text.color = color;
        }

        // ---------- selection ----------

        private void ApplySelection()
        {
            BuildHudStyle s = Style;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.Background == null) continue;
                Color color = item == picked ? s.PickedColor : s.HighlightColor;
                float alpha = item == picked ? 0.32f : i == selected ? 0.24f : 0.05f;
                item.Background.color = item == picked || i == selected ? new Color(color.r, color.g, color.b, alpha) : new Color(1f, 1f, 1f, alpha);
            }
            Item current = Current;
            if (current != null) ScrollTo(current);
            UpdateDetails();
        }

        private void ScrollTo(Item item)
        {
            RectTransform content = item.Column == 0 ? abilityContent : passiveContent;
            RectTransform viewport = item.Column == 0 ? abilityViewport : passiveViewport;
            if (content == null || viewport == null) return;
            float view = viewport.rect.height;
            float scroll = content.anchoredPosition.y;
            if (item.Top < scroll) scroll = item.Top;
            else if (item.Top + item.Height > scroll + view) scroll = item.Top + item.Height - view;
            float max = Mathf.Max(0f, content.sizeDelta.y - view);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(scroll, 0f, max));
        }

        private void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.PlayOneShot(clip, Style.Volume);
        }

        // ---------- template ----------

        /// <summary>Builds the panel on its own overlay canvas (above the battle HUD, below the reward screen).</summary>
        public static LoadoutPanel CreateTemplate(BuildHudStyle hudStyle)
        {
            BuildHudStyle s = hudStyle != null ? hudStyle : BuildHudStyle.LoadOrDefault();
            var root = new GameObject("Loadout Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AudioSource));
            Canvas rootCanvas = root.GetComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 1005;
            rootCanvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            AudioSource audio = root.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            var rootRect = (RectTransform)root.transform;
            TMP_FontAsset font = s.FontAsset;
            Color outline = s.OutlineColor;

            Image dimImage = NewImage("Dim", rootRect, s.DimColor, true);
            Stretch(dimImage.rectTransform, 0f);

            RectTransform panel = NewRect("Panel", rootRect);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = s.PanelSize;
            Image frame = NewImage("Frame", panel, s.PanelFrameColor, true);
            Stretch(frame.rectTransform, 0f);
            Image background = NewImage("Background", panel, s.PanelColor, false);
            Stretch(background.rectTransform, 4f);

            float width = s.PanelSize.x;
            float height = s.PanelSize.y;
            float columnTop = 150f;
            float detailHeight = 170f;
            float bottomLines = 90f;
            float viewportHeight = height - columnTop - 40f - detailHeight - bottomLines - 20f;
            float abilityWidth = Mathf.Round((width - 120f) * 0.53f);
            float passiveWidth = width - 120f - abilityWidth;

            TMP_Text titleText = CreateText("Title", panel, font, s.PanelTitleSize, s.TitleColor, outline, TextAlignmentOptions.Center);
            TopBand(titleText.rectTransform, 40f, 22f, s.PanelTitleSize + 10f);
            TMP_Text subtitleText = CreateText("Subtitle", panel, font, s.HeaderSize - 4, s.MutedColor, outline, TextAlignmentOptions.Center);
            TopBand(subtitleText.rectTransform, 40f, 30f + s.PanelTitleSize, s.HeaderSize + 6f);

            TMP_Text abilityHeaderText = CreateText("Abilities Header", panel, font, s.HeaderSize, s.TitleColor, outline, TextAlignmentOptions.BottomLeft);
            Box(abilityHeaderText.rectTransform, 40f, columnTop - 6f, abilityWidth, 40f);
            TMP_Text passiveHeaderText = CreateText("Passives Header", panel, font, s.HeaderSize, s.TitleColor, outline, TextAlignmentOptions.BottomLeft);
            Box(passiveHeaderText.rectTransform, 80f + abilityWidth, columnTop - 6f, passiveWidth, 40f);

            RectTransform abilityView = Viewport("Abilities", panel, 40f, columnTop + 40f, abilityWidth, viewportHeight, out RectTransform abilityRows);
            RectTransform passiveView = Viewport("Passives", panel, 80f + abilityWidth, columnTop + 40f, passiveWidth, viewportHeight, out RectTransform passiveRows);

            float detailTop = columnTop + 40f + viewportHeight + 16f;
            Image detailBox = NewImage("Details", panel, new Color(1f, 1f, 1f, 0.05f), false);
            Box(detailBox.rectTransform, 40f, detailTop, width - 80f, detailHeight);
            TMP_Text detailTitleText = CreateText("Title", detailBox.rectTransform, font, s.RowTitleSize, s.HighlightColor, outline, TextAlignmentOptions.TopLeft);
            TopBand(detailTitleText.rectTransform, 16f, 10f, s.RowTitleSize + 8f);
            detailTitleText.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text detailBodyText = CreateText("Body", detailBox.rectTransform, font, s.RowDetailSize, s.TextColor, outline, TextAlignmentOptions.TopLeft);
            RectTransform bodyRect = detailBodyText.rectTransform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(16f, 8f);
            bodyRect.offsetMax = new Vector2(-16f, -(s.RowTitleSize + 20f));
            detailBodyText.textWrappingMode = TextWrappingModes.Normal;
            detailBodyText.overflowMode = TextOverflowModes.Ellipsis;
            detailBodyText.lineSpacing = -6f;

            TMP_Text messageText = CreateText("Message", panel, font, s.RowDetailSize + 2, s.WarningColor, outline, TextAlignmentOptions.Center);
            Box(messageText.rectTransform, 40f, height - bottomLines + 4f, width - 80f, 34f);
            TMP_Text promptText = CreateText("Prompt", panel, font, s.PromptSize, s.MutedColor, outline, TextAlignmentOptions.Center);
            Box(promptText.rectTransform, 40f, height - bottomLines + 42f, width - 80f, 38f);

            LoadoutPanel view = root.AddComponent<LoadoutPanel>();
            view.style = hudStyle;
            view.canvas = rootCanvas;
            view.group = root.GetComponent<CanvasGroup>();
            view.dim = dimImage;
            view.title = titleText;
            view.subtitle = subtitleText;
            view.abilityHeader = abilityHeaderText;
            view.passiveHeader = passiveHeaderText;
            view.abilityViewport = abilityView;
            view.abilityContent = abilityRows;
            view.passiveViewport = passiveView;
            view.passiveContent = passiveRows;
            view.detailTitle = detailTitleText;
            view.detailBody = detailBodyText;
            view.message = messageText;
            view.prompt = promptText;
            view.audioSource = audio;
            view.HideImmediate();
            return view;
        }

        private static RectTransform Viewport(string name, RectTransform parent, float left, float top, float width, float height,
            out RectTransform content)
        {
            RectTransform viewport = NewRect(name + " Viewport", parent);
            Box(viewport, left, top, width, height);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = NewRect(name + " Rows", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, height);
            return viewport;
        }

        /// <summary>Box measured from the parent's top-left corner.</summary>
        private static void Box(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void TopBand(RectTransform rect, float sideInset, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sideInset, -top - height);
            rect.offsetMax = new Vector2(-sideInset, -top);
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : go.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Color color, bool raycast)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private TMP_Text NewText(string name, Transform parent, int size, Color color, TextAlignmentOptions alignment)
        {
            BuildHudStyle s = Style;
            return CreateText(name, parent, s.FontAsset, size, color, s.OutlineColor, alignment);
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, int size, Color color, Color outline,
            TextAlignmentOptions alignment)
        {
            TMP_Text text = CombatText.CreateUGUI(name, parent, font, size, color, alignment, outline,
                CombatText.OutlineWidthFromPixels(3f, size) + 0.1f);
            text.text = string.Empty;
            return text;
        }
    }
}
