using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class AbilitySlotController : MonoBehaviour
    {
        [SerializeField] private List<AbilitySlotAssignment> assignments = new();
        [SerializeField] private AbilityDefinition defaultAbility;
        [SerializeField, Min(0.05f)] private float holdDuration = 0.75f;

        private readonly Dictionary<int, AbilityRuntimeInstance> slots = new();
        private LaneInputRouter input;
        private PlayerCombatant player;
        private int candidateLane = -1;
        private float holdProgress;
        private bool selecting;

        public IReadOnlyDictionary<int, AbilityRuntimeInstance> Slots => slots;
        public event Action<IReadOnlyDictionary<int, AbilityRuntimeInstance>> SlotsChanged;
        public event Action<int, AbilityRuntimeInstance> SelectionStarted;
        public event Action<int, float> SelectionProgressed;
        public event Action<int> SelectionCancelled;
        public event Action<int, AbilityRuntimeInstance> AbilitySelected;

        private void Update()
        {
            if (!selecting || candidateLane < 0 || input == null) return;
            if (!input.IsHeld(candidateLane)) return;
            holdProgress = Mathf.Min(1f, holdProgress + Time.unscaledDeltaTime / Mathf.Max(0.05f, holdDuration));
            SelectionProgressed?.Invoke(candidateLane, holdProgress);
            if (holdProgress < 1f) return;

            AbilityRuntimeInstance selected = slots[candidateLane];
            if (!selected.Commit(player))
            {
                CancelCandidate();
                return;
            }
            selecting = false;
            input.RequireRelease(candidateLane);
            AbilitySelected?.Invoke(candidateLane, selected);
        }

        public void Initialize(LaneInputRouter inputRouter, PlayerCombatant combatant)
        {
            if (input != null)
            {
                input.SelectionLanePressed -= HandlePressed;
                input.SelectionLaneReleased -= HandleReleased;
            }
            input = inputRouter;
            player = combatant;
            if (input != null)
            {
                input.SelectionLanePressed += HandlePressed;
                input.SelectionLaneReleased += HandleReleased;
            }
            BuildSlots();
        }

        public void BeginSelection()
        {
            selecting = true;
            candidateLane = -1;
            holdProgress = 0f;
            SlotsChanged?.Invoke(slots);
        }

        public void SetHoldDuration(float seconds) => holdDuration = Mathf.Max(0.05f, seconds);

        public void EndSelection()
        {
            selecting = false;
            CancelCandidate();
        }

        public void TickCooldowns()
        {
            foreach (AbilityRuntimeInstance instance in slots.Values.Distinct()) instance.TickPlayerTurn();
            SlotsChanged?.Invoke(slots);
        }

        public void ResetRuntime()
        {
            foreach (AbilityRuntimeInstance instance in slots.Values.Distinct()) instance.Reset();
            SlotsChanged?.Invoke(slots);
        }

        public void ConfigureForTests(IEnumerable<AbilitySlotAssignment> newAssignments)
        {
            assignments = newAssignments?.ToList() ?? new List<AbilitySlotAssignment>();
            BuildSlots();
        }

        private void BuildSlots()
        {
            slots.Clear();
            foreach (AbilitySlotAssignment assignment in assignments.Where(assignment => assignment?.Ability != null))
                slots[assignment.LaneId] = new AbilityRuntimeInstance(assignment.Ability);

            if (slots.Count == 0 && defaultAbility == null)
                defaultAbility = Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack");
            if (slots.Count == 0 && defaultAbility != null)
            {
                int firstLane = input?.Bindings.FirstOrDefault()?.LaneId ?? 1;
                slots[firstLane] = new AbilityRuntimeInstance(defaultAbility);
            }
            SlotsChanged?.Invoke(slots);
        }

        private void HandlePressed(int laneId)
        {
            if (!selecting || candidateLane >= 0 || !slots.TryGetValue(laneId, out AbilityRuntimeInstance ability)
                || !ability.CanUse(player)) return;
            candidateLane = laneId;
            holdProgress = 0f;
            SelectionStarted?.Invoke(laneId, ability);
        }

        private void HandleReleased(int laneId)
        {
            if (selecting && laneId == candidateLane) CancelCandidate();
        }

        private void CancelCandidate()
        {
            if (candidateLane >= 0) SelectionCancelled?.Invoke(candidateLane);
            candidateLane = -1;
            holdProgress = 0f;
        }

        private void OnDisable()
        {
            if (input == null) return;
            input.SelectionLanePressed -= HandlePressed;
            input.SelectionLaneReleased -= HandleReleased;
        }
    }
}
