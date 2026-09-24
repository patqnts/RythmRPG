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
                 "Settings > Controls). Lane N uses action \"LaneN\".")]
        [SerializeField] private KeyCode keyCode = KeyCode.A;
        [SerializeField] private KeyButton view;

        public int LaneId => laneId;
        /// <summary>Legacy inspector value; lane input now comes from <see cref="GameInput.Lane"/>.</summary>
        public KeyCode KeyCode => keyCode;
        public KeyButton View => view;
        /// <summary>The lane's current key as shown on screen (follows rebinding), e.g. "A".</summary>
        public string DisplayName => GameInput.LaneLabel(laneId);
        public InputAction Action => GameInput.Lane(laneId);

        public LaneKeyBinding(int laneId, KeyCode keyCode, KeyButton view)
        {
            this.laneId = laneId;
            this.keyCode = keyCode;
            this.view = view;
        }

        public void SetView(KeyButton laneView) => view = laneView;
    }

    // Runs after the notes' Update, so a press is judged against where the note is drawn this frame, not last frame.
    [DefaultExecutionOrder(100)]
    public sealed class LaneInputRouter : MonoBehaviour
    {
        [SerializeField] private List<LaneKeyBinding> bindings = new();

        private readonly HashSet<int> requireRelease = new();
        private readonly HashSet<int> heldLanes = new();
        private static readonly HashSet<int> warnedLanes = new();
        public CombatState CurrentState { get; private set; }
        public IReadOnlyList<LaneKeyBinding> Bindings => bindings;
        public event Action<int> RhythmLanePressed;
        public event Action<int> RhythmLaneReleased;
        public event Action<int> SelectionLanePressed;
        public event Action<int> SelectionLaneReleased;

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
                InputAction action = binding.Action;
                if (action == null)
                {
                    if (warnedLanes.Add(lane))
                        Debug.LogWarning($"[Input] Lane {lane} has no input action (GameInput supports lanes 1-{GameInput.LaneCount}).", this);
                    continue;
                }

                bool isDown = action.IsPressed();
                if (binding.View != null) binding.View.SetPressed(isDown);

                // WasPressedThisFrame also catches a tap that goes down and up between two frames.
                if (action.WasPressedThisFrame())
                {
                    heldLanes.Add(lane);
                    if (!requireRelease.Contains(lane))
                    {
                        if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLanePressed?.Invoke(lane);
                        else if (IsRhythmState(CurrentState)) RhythmLanePressed?.Invoke(lane);
                    }
                }

                if (!isDown && (heldLanes.Remove(lane) || action.WasReleasedThisFrame()))
                {
                    requireRelease.Remove(lane);
                    if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLaneReleased?.Invoke(lane);
                    else if (IsRhythmState(CurrentState)) RhythmLaneReleased?.Invoke(lane);
                }
            }
        }

        public void SetState(CombatState state) => CurrentState = state;
        public bool IsHeld(int laneId) => GameInput.Lane(laneId)?.IsPressed() ?? false;

        public void RequireRelease(int laneId) => requireRelease.Add(laneId);
        public void SetView(int laneId, KeyButton view)
        {
            LaneKeyBinding binding = bindings.FirstOrDefault(candidate => candidate != null && candidate.LaneId == laneId);
            binding?.SetView(view);
            if (view != null) view.SetKeyLabel(GameInput.LaneLabel(laneId));
        }
        public KeyButton GetView(int laneId) => bindings.FirstOrDefault(binding => binding != null && binding.LaneId == laneId)?.View;
        public KeyButton[] GetViews() => bindings.Where(binding => binding?.View != null).Select(binding => binding.View).ToArray();

        public void ConfigureForTests(IEnumerable<LaneKeyBinding> laneBindings)
        {
            bindings = laneBindings?.Where(binding => binding != null).ToList() ?? new List<LaneKeyBinding>();
        }

        private void RefreshKeyLabels()
        {
            foreach (LaneKeyBinding binding in bindings)
                if (binding?.View != null) binding.View.SetKeyLabel(binding.DisplayName);
        }

        // One binding per GameInput lane (1..LaneCount). Extra KeyButtons in the scene (e.g. a leftover 5th legacy
        // button from the 5-lane layout) and serialized bindings past LaneCount are ignored.
        private void EnsureBindings()
        {
            int removed = bindings.RemoveAll(binding => binding == null || binding.LaneId < 1 || binding.LaneId > GameInput.LaneCount);
            if (removed > 0)
                Debug.LogWarning($"[Input] Ignored {removed} lane binding(s) outside lanes 1-{GameInput.LaneCount}.", this);
            if (bindings.Count > 0) return;
            KeyButton[] views = FindObjectsByType<KeyButton>(FindObjectsInactive.Include);
            for (int laneId = 1; laneId <= GameInput.LaneCount; laneId++)
            {
                int id = laneId;
                KeyButton view = views.FirstOrDefault(candidate => candidate != null && candidate.keyIdentity == id);
                bindings.Add(new LaneKeyBinding(laneId, KeyCode.None, view));
            }
        }

        private static bool IsRhythmState(CombatState state) => state == CombatState.EnemyTurnExecuting
            || state == CombatState.PlayerAbilityExecuting;
    }
}
