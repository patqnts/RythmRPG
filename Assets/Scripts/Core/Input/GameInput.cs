using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RythmRPG.Core
{
    /// <summary>
    /// The game's controls (new Input System), built in code so every scene and assembly shares one set of actions.
    /// Players can rebind them from the pause menu; overrides are saved to PlayerPrefs and loaded on start.
    /// <list type="bullet">
    /// <item><b>Combat</b>: four lane keys, Lane1..Lane4 (keyboard A S J K by default). A chart plays with 1-4 lanes;
    /// <see cref="LaneKeySlot"/> picks which of the four keys each active lane uses.</item>
    /// <item><b>Explore</b>: Move (WASD / arrows / left stick / d-pad) and Interact (Enter / south button).</item>
    /// <item><b>System</b>: Pause (Esc / Start). Always enabled.</item>
    /// </list>
    /// Gameplay helpers (<see cref="MoveValue"/>, <see cref="InteractPressed"/>, <see cref="LanePressed"/>...) read as
    /// "no input" while the game is paused, so gameplay scripts do not have to check the pause state themselves.
    /// </summary>
    public static class GameInput
    {
        /// <summary>
        /// Maximum number of rhythm lanes, and the number of lane keys (Lane1..Lane4 = left outer, left inner,
        /// right inner, right outer). Each chart uses 1 to this many lanes (set in the Rhythm Composer).
        /// </summary>
        public const int LaneCount = 4;

        // Which lane key each lane uses, per active lane count: index fingers first, then outward, so both hands share
        // the work. 1 lane: J. 2 lanes: S J. 3 lanes: S J K. 4 lanes: A S J K.
        private static readonly int[][] LaneKeyLayouts =
        {
            new[] { 3 },
            new[] { 2, 3 },
            new[] { 2, 3, 4 },
            new[] { 1, 2, 3, 4 },
        };
        private static readonly string[] LaneKeyNames = { "Left Outer", "Left Inner", "Right Inner", "Right Outer" };
        public const string KeyboardGroup = "Keyboard";
        public const string GamepadGroup = "Gamepad";
        private const string PrefsKey = "RythmRPG.Input.BindingOverrides";

        // Two keys per hand. One entry per lane (LaneCount).
        private static readonly string[] DefaultLaneKeys = { "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/j", "<Keyboard>/k" };
        private static readonly string[] DefaultLanePads =
            { "<Gamepad>/dpad/left", "<Gamepad>/dpad/down", "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast" };

        private static InputActionAsset asset;
        private static InputAction[] lanes;
        private static readonly List<RebindRow> rows = new();
        private static InputActionRebindingExtensions.RebindingOperation activeRebind;

        /// <summary>Raised after a rebind, a reset or loading overrides (refresh key labels here).</summary>
        public static event Action BindingsChanged;

        public static InputActionAsset Asset { get { EnsureCreated(); return asset; } }
        public static InputAction Move { get { EnsureCreated(); return move; } }
        public static InputAction Interact { get { EnsureCreated(); return interact; } }
        public static InputAction Pause { get { EnsureCreated(); return pause; } }
        private static InputAction move, interact, pause;

        /// <summary>True while the player is choosing a new key for a binding.</summary>
        public static bool IsRebinding => activeRebind != null;
        /// <summary>Frame the last rebind finished (the key that ended it must not also act as a menu input).</summary>
        public static int RebindEndedFrame { get; private set; } = -1;

        /// <summary>Row that gave up its key in the last rebind (keys were swapped), or null.</summary>
        public static RebindRow LastSwappedRow { get; private set; }

        /// <summary>Rows shown on the Controls page, in order.</summary>
        public static IReadOnlyList<RebindRow> Rows { get { EnsureCreated(); return rows; } }

        // ---------- Gameplay helpers (read as "no input" while paused) ----------

        public static Vector2 MoveValue => GamePause.IsPaused ? Vector2.zero : Move.ReadValue<Vector2>();
        public static bool InteractPressed => !GamePause.IsPaused && Interact.WasPressedThisFrame();
        public static bool InteractHeld => !GamePause.IsPaused && Interact.IsPressed();
        public static bool LanePressed(int laneId) => !GamePause.IsPaused && (Lane(laneId)?.WasPressedThisFrame() ?? false);

        /// <summary>
        /// The lane key (1-based slot, see <see cref="LaneCount"/>) that gameplay lane <paramref name="laneId"/> uses when
        /// <paramref name="activeLanes"/> lanes are active. Lanes past the active count fall back to their own slot.
        /// </summary>
        public static int LaneKeySlot(int activeLanes, int laneId)
        {
            int count = Mathf.Clamp(activeLanes, 1, LaneCount);
            int[] layout = LaneKeyLayouts[count - 1];
            return laneId >= 1 && laneId <= layout.Length ? layout[laneId - 1] : laneId;
        }

        /// <summary>Lane key action for a key slot (1-based, 1..<see cref="LaneCount"/>); null outside that range.
        /// Gameplay lanes map to slots through <see cref="LaneKeySlot"/>.</summary>
        public static InputAction Lane(int laneId)
        {
            EnsureCreated();
            return laneId >= 1 && laneId <= lanes.Length ? lanes[laneId - 1] : null;
        }

        /// <summary>Short label for a lane's key (keyboard first, gamepad if the lane has no key), e.g. "A".</summary>
        public static string LaneLabel(int laneId)
        {
            InputAction action = Lane(laneId);
            if (action == null) return laneId.ToString();
            string label = DisplayString(action, FindBindingIndex(action, KeyboardGroup));
            if (string.IsNullOrEmpty(label)) label = DisplayString(action, FindBindingIndex(action, GamepadGroup));
            return string.IsNullOrEmpty(label) ? laneId.ToString() : label;
        }

        public static string DisplayString(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count) return string.Empty;
            string path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path)) return "-";
            return action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }

        // ---------- Setup ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode Options" with domain reload disabled.
            activeRebind?.Dispose();
            activeRebind = null;
            if (asset != null) asset.Disable();
            asset = null;
            lanes = null;
            move = interact = pause = null;
            rows.Clear();
            LastSwappedRow = null;
            BindingsChanged = null;
            RebindEndedFrame = -1;
        }

        public static void EnsureCreated()
        {
            if (asset != null) return;

            asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "GameControls";

            InputActionMap combat = asset.AddActionMap("Combat");
            lanes = new InputAction[LaneCount];
            for (int i = 0; i < LaneCount; i++)
            {
                InputAction lane = combat.AddAction($"Lane{i + 1}", InputActionType.Button);
                lane.AddBinding(i < DefaultLaneKeys.Length ? DefaultLaneKeys[i] : string.Empty, groups: KeyboardGroup);
                lane.AddBinding(i < DefaultLanePads.Length ? DefaultLanePads[i] : string.Empty, groups: GamepadGroup);
                lanes[i] = lane;
            }

            InputActionMap explore = asset.AddActionMap("Explore");
            move = explore.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", KeyboardGroup)
                .With("Down", "<Keyboard>/s", KeyboardGroup)
                .With("Left", "<Keyboard>/a", KeyboardGroup)
                .With("Right", "<Keyboard>/d", KeyboardGroup);
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", KeyboardGroup)
                .With("Down", "<Keyboard>/downArrow", KeyboardGroup)
                .With("Left", "<Keyboard>/leftArrow", KeyboardGroup)
                .With("Right", "<Keyboard>/rightArrow", KeyboardGroup);
            move.AddBinding("<Gamepad>/leftStick", groups: GamepadGroup);
            move.AddBinding("<Gamepad>/dpad", groups: GamepadGroup);

            interact = explore.AddAction("Interact", InputActionType.Button);
            interact.AddBinding("<Keyboard>/enter", groups: KeyboardGroup);
            interact.AddBinding("<Gamepad>/buttonSouth", groups: GamepadGroup);

            InputActionMap system = asset.AddActionMap("System");
            pause = system.AddAction("Pause", InputActionType.Button);
            pause.AddBinding("<Keyboard>/escape", groups: KeyboardGroup);
            pause.AddBinding("<Gamepad>/start", groups: GamepadGroup);

            BuildRows();
            LoadOverrides();
            asset.Enable();
        }

        private static void BuildRows()
        {
            rows.Clear();
            for (int i = 0; i < LaneCount; i++)
                rows.Add(new RebindRow($"Lane Key {i + 1} ({LaneKeyNames[i]})", RebindContext.Combat, lanes[i],
                    FindBindingIndex(lanes[i], KeyboardGroup), FindBindingIndex(lanes[i], GamepadGroup)));

            foreach (string part in new[] { "Up", "Down", "Left", "Right" })
                rows.Add(new RebindRow($"Move {part}", RebindContext.Explore, move, FindBindingIndex(move, KeyboardGroup, part), -1,
                    "Left Stick"));

            rows.Add(new RebindRow("Interact", RebindContext.Explore, interact,
                FindBindingIndex(interact, KeyboardGroup), FindBindingIndex(interact, GamepadGroup)));
            rows.Add(new RebindRow("Pause", RebindContext.System, pause,
                FindBindingIndex(pause, KeyboardGroup), FindBindingIndex(pause, GamepadGroup)));
        }

        /// <summary>First binding of <paramref name="action"/> in <paramref name="group"/> (optionally a composite part).</summary>
        public static int FindBindingIndex(InputAction action, string group, string compositePart = null)
        {
            if (action == null) return -1;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (binding.isComposite) continue;
                if (compositePart != null && (!binding.isPartOfComposite
                    || !string.Equals(binding.name, compositePart, StringComparison.OrdinalIgnoreCase))) continue;
                if (binding.groups != null && binding.groups.Contains(group)) return i;
            }
            return -1;
        }

        // ---------- Rebinding ----------

        /// <summary>
        /// Waits for the next key (or gamepad button) and binds it. Esc cancels. If another control in the same context
        /// already uses that key, the two swap so nothing ends up doubly bound. <paramref name="onFinished"/> gets true
        /// when a new key was bound.
        /// </summary>
        public static void StartRebind(RebindRow row, bool gamepad, Action<bool> onFinished)
        {
            CancelRebind();
            if (row == null) return;
            InputAction action = row.Action;
            int index = gamepad ? row.GamepadIndex : row.KeyboardIndex;
            if (action == null || index < 0)
            {
                onFinished?.Invoke(false);
                return;
            }

            string previousPath = action.bindings[index].effectivePath;
            bool wasEnabled = action.enabled;
            action.Disable(); // an action must be disabled while it is being rebound

            void Finish(bool bound)
            {
                activeRebind?.Dispose();
                activeRebind = null;
                RebindEndedFrame = Time.frameCount;
                if (wasEnabled) action.Enable();
                if (bound)
                {
                    SwapConflicts(row, gamepad, action.bindings[index].effectivePath, previousPath);
                    SaveOverrides();
                    BindingsChanged?.Invoke();
                }
                onFinished?.Invoke(bound);
            }

            activeRebind = action.PerformInteractiveRebinding(index)
                .WithControlsHavingToMatchPath(gamepad ? "<Gamepad>" : "<Keyboard>")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithControlsExcluding("<Mouse>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(_ => Finish(true))
                .OnCancel(_ => Finish(false));
            activeRebind.Start();
        }

        public static void CancelRebind()
        {
            activeRebind?.Cancel();
        }

        private static void SwapConflicts(RebindRow changed, bool gamepad, string newPath, string previousPath)
        {
            LastSwappedRow = null;
            if (string.IsNullOrEmpty(newPath)) return;
            foreach (RebindRow other in rows)
            {
                if (!other.SharesContextWith(changed)) continue;
                int otherIndex = gamepad ? other.GamepadIndex : other.KeyboardIndex;
                if (otherIndex < 0 || (other.Action == changed.Action && otherIndex == (gamepad ? changed.GamepadIndex : changed.KeyboardIndex)))
                    continue;
                string otherPath = other.Action.bindings[otherIndex].effectivePath;
                if (!string.Equals(otherPath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                other.Action.ApplyBindingOverride(otherIndex, previousPath ?? string.Empty);
                LastSwappedRow = other;
            }
        }

        public static void ResetToDefaults()
        {
            CancelRebind();
            Asset.RemoveAllBindingOverrides();
            SaveOverrides();
            BindingsChanged?.Invoke();
        }

        private static void SaveOverrides()
        {
            PlayerPrefs.SetString(PrefsKey, asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private static void LoadOverrides()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Input] Saved key bindings could not be loaded and were reset: {exception.Message}");
                asset.RemoveAllBindingOverrides();
                PlayerPrefs.DeleteKey(PrefsKey);
            }
        }
    }

    /// <summary>Which bindings may not share a key. System (Pause) conflicts with everything.</summary>
    public enum RebindContext { Combat, Explore, System }

    /// <summary>One line on the Controls page: an action's keyboard binding and its gamepad binding.</summary>
    public sealed class RebindRow
    {
        public string Label { get; }
        public RebindContext Context { get; }
        public InputAction Action { get; }
        public int KeyboardIndex { get; }
        /// <summary>-1 when the gamepad side is fixed (see <see cref="FixedGamepadLabel"/>).</summary>
        public int GamepadIndex { get; }
        public string FixedGamepadLabel { get; }

        public RebindRow(string label, RebindContext context, InputAction action, int keyboardIndex, int gamepadIndex,
            string fixedGamepadLabel = null)
        {
            Label = label;
            Context = context;
            Action = action;
            KeyboardIndex = keyboardIndex;
            GamepadIndex = gamepadIndex;
            FixedGamepadLabel = fixedGamepadLabel;
        }

        public string KeyboardLabel => GameInput.DisplayString(Action, KeyboardIndex);
        public string GamepadLabel => GamepadIndex >= 0 ? GameInput.DisplayString(Action, GamepadIndex) : FixedGamepadLabel ?? "-";

        public bool SharesContextWith(RebindRow other) =>
            other != null && (Context == other.Context || Context == RebindContext.System || other.Context == RebindContext.System);
    }
}
