using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    [Serializable]
    public sealed class LaneKeyBinding
    {
        [SerializeField] private int laneId = 1;
        [SerializeField] private KeyCode keyCode = KeyCode.A;
        [SerializeField] private KeyButton view;

        public int LaneId => laneId;
        public KeyCode KeyCode => keyCode;
        public KeyButton View => view;

        public LaneKeyBinding(int laneId, KeyCode keyCode, KeyButton view)
        {
            this.laneId = laneId;
            this.keyCode = keyCode;
            this.view = view;
        }

        public void SetView(KeyButton laneView) => view = laneView;
    }

    public sealed class LaneInputRouter : MonoBehaviour
    {
        private static readonly KeyCode[] DefaultKeys = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.J, KeyCode.K };
        [SerializeField] private List<LaneKeyBinding> bindings = new();

        private readonly HashSet<int> requireRelease = new();
        public CombatState CurrentState { get; private set; }
        public IReadOnlyList<LaneKeyBinding> Bindings => bindings;
        public event Action<int> RhythmLanePressed;
        public event Action<int> RhythmLaneReleased;
        public event Action<int> SelectionLanePressed;
        public event Action<int> SelectionLaneReleased;

        private void Awake() => EnsureBindings();

        private void Update()
        {
            foreach (LaneKeyBinding binding in bindings)
            {
                if (binding?.View != null) binding.View.SetPressed(Input.GetKey(binding.KeyCode));
                if (Input.GetKeyUp(binding.KeyCode))
                {
                    requireRelease.Remove(binding.LaneId);
                    if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLaneReleased?.Invoke(binding.LaneId);
                    else if (IsRhythmState(CurrentState)) RhythmLaneReleased?.Invoke(binding.LaneId);
                }
                if (!Input.GetKeyDown(binding.KeyCode) || requireRelease.Contains(binding.LaneId)) continue;
                if (CurrentState == CombatState.PlayerAbilitySelection) SelectionLanePressed?.Invoke(binding.LaneId);
                else if (IsRhythmState(CurrentState)) RhythmLanePressed?.Invoke(binding.LaneId);
            }
        }

        public void SetState(CombatState state) => CurrentState = state;
        public bool IsHeld(int laneId)
        {
            LaneKeyBinding binding = bindings.FirstOrDefault(candidate => candidate != null && candidate.LaneId == laneId);
            return binding != null && Input.GetKey(binding.KeyCode);
        }

        public void RequireRelease(int laneId) => requireRelease.Add(laneId);
        public void SetView(int laneId, KeyButton view)
        {
            LaneKeyBinding binding = bindings.FirstOrDefault(candidate => candidate != null && candidate.LaneId == laneId);
            binding?.SetView(view);
        }
        public KeyButton GetView(int laneId) => bindings.FirstOrDefault(binding => binding != null && binding.LaneId == laneId)?.View;
        public KeyButton[] GetViews() => bindings.Where(binding => binding?.View != null).Select(binding => binding.View).ToArray();

        public void ConfigureForTests(IEnumerable<LaneKeyBinding> laneBindings)
        {
            bindings = laneBindings?.Where(binding => binding != null).ToList() ?? new List<LaneKeyBinding>();
        }

        private void EnsureBindings()
        {
            if (bindings.Count > 0) return;
            KeyButton[] views = FindObjectsByType<KeyButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(view => view.keyIdentity).ToArray();
            int laneCount = Mathf.Max(DefaultKeys.Length, views.Length);
            for (int index = 0; index < laneCount; index++)
            {
                KeyCode key = index < DefaultKeys.Length ? DefaultKeys[index] : KeyCode.None;
                KeyButton view = index < views.Length ? views[index] : null;
                int laneId = view != null && view.keyIdentity > 0 ? view.keyIdentity : index + 1;
                bindings.Add(new LaneKeyBinding(laneId, key, view));
            }
        }

        private static bool IsRhythmState(CombatState state) => state == CombatState.EnemyTurnExecuting
            || state == CombatState.PlayerAbilityExecuting;
    }
}
