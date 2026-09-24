using System;
using System.Collections.Generic;
using System.Linq;
using RythmRPG.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class LaneKeyBinding
    {
        [SerializeField] private int laneId = 1;
        [Tooltip("Legacy, not used for input any more: lane keys come from GameInput (rebindable in the pause menu's " +
                 "Settings > Controls).")]
        [SerializeField] private KeyCode keyCode = KeyCode.A;
        [SerializeField] private KeyButton view;
        [Tooltip("Lane key (GameInput Lane1..Lane4) this lane uses. 0 = same number as the lane.")]
        [SerializeField] private int keySlot;

        public int LaneId => laneId;
        /// <summary>Legacy inspector value; lane input now comes from <see cref="GameInput.Lane"/>.</summary>
        public KeyCode KeyCode => keyCode;
        public KeyButton View => view;
        /// <summary>The lane key this lane uses right now (depends on how many lanes are active).</summary>
        public int KeySlot => keySlot > 0 ? keySlot : laneId;
        /// <summary>The lane's current key as shown on screen (follows rebinding), e.g. "A".</summary>
        public string DisplayName => GameInput.LaneLabel(KeySlot);
        public InputAction Action => GameInput.Lane(KeySlot);

        public LaneKeyBinding(int laneId, KeyCode keyCode, KeyButton view, int keySlot = 0)
        {
            this.laneId = laneId;
            this.keyCode = keyCode;
            this.view = view;
            this.keySlot = keySlot;
        }

        public void SetView(KeyButton laneView) => view = laneView;
    }

    /// <summary>
    /// Lane input. 1 to <see cref="GameInput.LaneCount"/> lanes are active at a time (<see cref="SetActiveLaneCount"/>,
    /// set per chart by <see cref="CombatController"/>). Gameplay lanes are always numbered 1..count (the chart's lane
    /// Key Identity); which physical key each one uses comes from <see cref="GameInput.LaneKeySlot"/>
    /// (1 lane: J, 2: S J, 3: S J K, 4: A S J K by default).
    /// </summary>
    // Runs after the notes' Update, so a press is judged against where the note is drawn this frame, not last frame.
    [DefaultExecutionOrder(100)]
    public sealed class LaneInputRouter : MonoBehaviour
    {
        [SerializeField] private List<LaneKeyBinding> bindings = new();

        // Tracked per lane KEY (slot), not per lane: the lane -> key mapping changes with the lane count.
        private readonly HashSet<int> requireRelease = new();
        private readonly HashSet<int> heldLanes = new();
        private readonly Dictionary<int, KeyButton> viewCache = new();
        private static readonly HashSet<int> warnedLanes = new();
        public CombatState CurrentState { get; private set; }
        public IReadOnlyList<LaneKeyBinding> Bindings => bindings;
        /// <summary>Number of lanes in play right now (1..<see cref="GameInput.LaneCount"/>).</summary>
        public int ActiveLaneCount => bindings.Count;
        public event Action<int> RhythmLanePressed;
        public event Action<int> RhythmLaneReleased;
        public event Action<int> SelectionLanePressed;
        public event Action<int> SelectionLaneReleased;
        /// <summary>Raised after <see cref="SetActiveLaneCount"/> changed the lanes (count or keys).</summary>
        public event Action LanesChanged;

        private void Awake() => EnsureBindings();

        private void OnEnable()
        {
            GameInput.BindingsChanged += RefreshKeyLabels;
            RefreshKeyLabels();
        }

        private void OnDisable()
        {
            GameInput.BindingsChanged -= RefreshKeyLabels;
            heldLanes.Clear();
        }

        private void Update()
        {
            // Paused: keep the held state as it was. A key let go during the pause is reported as released on resume.
            if (GamePause.IsPaused) return;

            foreach (LaneKeyBinding binding in bindings)
            {
                if (binding == null) continue;
                int lane = binding.LaneId;
                int slot = binding.KeySlot;
                InputAction action = binding.Action;
                if (action == null)
                {
                    if (warnedLanes.Add(lane))
                        Debug.LogWarning($"[Input] Lane {lane} has no input action (GameInput has lane keys 1-{GameInput.LaneCount}).", this);
                    continue;
                }

                bool isDown = action.IsPressed();
                if (binding.View != null) binding.View.SetPressed(isDown);

                // WasPressedThisFrame also catches a tap that goes down and up between two frames.
                if (action.WasPressedThisFrame())
                {
                    heldLanes.Add(slot);
                    if (!requireRelease.Contains(slot))
                    {
                        if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLanePressed?.Invoke(lane);
                        else if (IsRhythmState(CurrentState)) RhythmLanePressed?.Invoke(lane);
                    }
                }

                if (!isDown && (heldLanes.Remove(slot) || action.WasReleasedThisFrame()))
                {
                    requireRelease.Remove(slot);
                    if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLaneReleased?.Invoke(lane);
                    else if (IsRhythmState(CurrentState)) RhythmLaneReleased?.Invoke(lane);
                }
            }
        }

        public void SetState(CombatState state) => CurrentState = state;
        public bool IsHeld(int laneId) => FindBinding(laneId)?.Action?.IsPressed() ?? false;

        /// <summary>The lane's key must be let go before it counts as a press again.</summary>
        public void RequireRelease(int laneId)
        {
            LaneKeyBinding binding = FindBinding(laneId);
            requireRelease.Add(binding != null ? binding.KeySlot : laneId);
        }

        public void SetView(int laneId, KeyButton view)
        {
            LaneKeyBinding binding = FindBinding(laneId);
            binding?.SetView(view);
            if (view == null) return;
            viewCache[laneId] = view;
            view.SetKeyLabel(binding != null ? binding.DisplayName : GameInput.LaneLabel(laneId));
        }
        public KeyButton GetView(int laneId) => FindBinding(laneId)?.View;
        public KeyButton[] GetViews() => bindings.Where(binding => binding?.View != null).Select(binding => binding.View).ToArray();

        /// <summary>
        /// Plays with <paramref name="count"/> lanes (clamped to 1..<see cref="GameInput.LaneCount"/>), numbered 1..count,
        /// each on the key <see cref="GameInput.LaneKeySlot"/> gives it. Views (lane buttons) are kept per lane number.
        /// Keys still held from before must be let go before they count as a press.
        /// </summary>
        public void SetActiveLaneCount(int count)
        {
            count = Mathf.Clamp(count, 1, GameInput.LaneCount);
            bool same = bindings.Count == count;
            for (int i = 0; same && i < bindings.Count; i++)
                same = bindings[i] != null && bindings[i].LaneId == i + 1 && bindings[i].KeySlot == GameInput.LaneKeySlot(count, i + 1);
            if (same) return;

            foreach (LaneKeyBinding old in bindings)
            {
                if (old?.View == null) continue;
                viewCache[old.LaneId] = old.View;
                old.View.SetPressed(false);
            }
            KeyButton[] sceneViews = null;
            bindings.Clear();
            for (int laneId = 1; laneId <= count; laneId++)
            {
                if (!viewCache.TryGetValue(laneId, out KeyButton view) || view == null)
                {
                    sceneViews ??= FindObjectsByType<KeyButton>(FindObjectsInactive.Include);
                    int id = laneId;
                    view = sceneViews.FirstOrDefault(candidate => candidate != null && candidate.keyIdentity == id);
                }
                var binding = new LaneKeyBinding(laneId, KeyCode.None, view, GameInput.LaneKeySlot(count, laneId));
                bindings.Add(binding);
                if (binding.Action != null && binding.Action.IsPressed()) requireRelease.Add(binding.KeySlot);
            }
            heldLanes.Clear();
            RefreshKeyLabels();
            LanesChanged?.Invoke();
        }

        public void ConfigureForTests(IEnumerable<LaneKeyBinding> laneBindings)
        {
            bindings = laneBindings?.Where(binding => binding != null).ToList() ?? new List<LaneKeyBinding>();
        }

        private LaneKeyBinding FindBinding(int laneId) =>
            bindings.FirstOrDefault(binding => binding != null && binding.LaneId == laneId);

        private void RefreshKeyLabels()
        {
            foreach (LaneKeyBinding binding in bindings)
                if (binding?.View != null) binding.View.SetKeyLabel(binding.DisplayName);
        }

        // Starts with every lane (GameInput.LaneCount); the combat narrows it per chart.
        private void EnsureBindings()
        {
            int removed = bindings.RemoveAll(binding => binding == null || binding.LaneId < 1 || binding.LaneId > GameInput.LaneCount);
            if (removed > 0)
                Debug.LogWarning($"[Input] Ignored {removed} lane binding(s) outside lanes 1-{GameInput.LaneCount}.", this);
            if (bindings.Count > 0) return;
            SetActiveLaneCount(GameInput.LaneCount);
        }

        private static bool IsRhythmState(CombatState state) => state == CombatState.EnemyTurnExecuting
            || state == CombatState.PlayerAbilityExecuting;
    }
}
