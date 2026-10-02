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
        private readonly Dictionary<int, int> slotIndexByLane = new();
        private LaneInputRouter input;
        private PlayerCombatant player;
        private int candidateLane = -1;
        private float holdProgress;
        private bool selecting;
        private bool waitingForCommit;

        public IReadOnlyDictionary<int, AbilityRuntimeInstance> Slots => slots;
        /// <summary>The build the slots were made from (null = scene assignments / DefaultLoadout).</summary>
        public RunBuildState SourceBuild { get; private set; }
        /// <summary>Ability slot index (0-3) of the slot selected on this input lane; -1 if none.</summary>
        public int SlotIndexOfLane(int laneId) => slotIndexByLane.TryGetValue(laneId, out int index) ? index : -1;
        /// <summary>Highest lane with an ability (0 = none): how many lanes ability selection needs.</summary>
        public int HighestLaneId => slots.Count == 0 ? 0 : slots.Keys.Max();
        public event Action<IReadOnlyDictionary<int, AbilityRuntimeInstance>> SlotsChanged;
        public event Action<int, AbilityRuntimeInstance> SelectionStarted;
        public event Action<int, float> SelectionProgressed;
        public event Action<int> SelectionCancelled;
        /// <summary>The hold on a lane completed and the ability was paid for; raised just before <see cref="AbilitySelected"/>.</summary>
        public event Action<int, AbilityRuntimeInstance> SelectionCommitted;
        public event Action<int, AbilityRuntimeInstance> AbilitySelected;
        public Func<int, AbilityRuntimeInstance, bool> RequestChoice;
        public CastChoice SelectedChoice { get; private set; }

        private void Update()
        {
            if (!selecting || candidateLane < 0 || input == null) return;
            if (!input.IsHeld(candidateLane)) return;
            holdProgress = Mathf.Min(1f, holdProgress + Time.unscaledDeltaTime / Mathf.Max(0.05f, holdDuration));
            SelectionProgressed?.Invoke(candidateLane, holdProgress);
            if (holdProgress < 1f) return;

            AbilityRuntimeInstance selected = slots[candidateLane];
            int lane = candidateLane;
            selecting = false;
            waitingForCommit = true;
            input.RequireRelease(lane);
            if (RequestChoice?.Invoke(lane, selected) == true) return;
            ConfirmChoice(lane, selected, null);
        }

        public bool ConfirmChoice(int lane, AbilityRuntimeInstance selected, CastChoice choice)
        {
            if (!waitingForCommit || lane != candidateLane) return false;
            if (!slots.TryGetValue(lane, out AbilityRuntimeInstance current) || current != selected || !selected.Commit(player))
            {
                BeginSelection();
                return false;
            }
            SelectedChoice = choice;
            waitingForCommit = false;
            selecting = false;
            SelectionCommitted?.Invoke(lane, selected);
            AbilitySelected?.Invoke(lane, selected);
            return true;
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
            SelectedChoice = null;
            waitingForCommit = false;
            selecting = true;
            candidateLane = -1;
            holdProgress = 0f;
            SlotsChanged?.Invoke(slots);
        }

        public void SetHoldDuration(float seconds) => holdDuration = Mathf.Max(0.05f, seconds);

        public void EndSelection()
        {
            waitingForCommit = false;
            selecting = false;
            CancelCandidate();
        }

        public void TickCooldowns()
        {
            foreach (AbilityRuntimeInstance instance in slots.Values.Distinct()) instance.TickPlayerTurn();
            SlotsChanged?.Invoke(slots);
        }

        /// <summary>Reduces one equipped ability with the longest live cooldown. Returns the amount removed.</summary>
        public int ReduceLongestCooldown(int amount = 1)
        {
            AbilityRuntimeInstance target = slots.OrderBy(pair => pair.Key).Select(pair => pair.Value).Distinct()
                .Where(instance => instance != null && instance.RemainingCooldown > 0)
                .OrderByDescending(instance => instance.RemainingCooldown).FirstOrDefault();
            int reduced = target?.ReduceCooldown(amount) ?? 0;
            if (reduced > 0) SlotsChanged?.Invoke(slots);
            return reduced;
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
            slotIndexByLane.Clear();
            SourceBuild = null;
            // A run build owns the loadout: slot i is selected with input lane i + 1. Ability runtimes are rebuilt
            // for each encounter from the retained instances, so upgrades and ownership survive encounter resets.
            RunBuildState build = RunBuild.Current;
            if (build != null && build.Equipped.Any())
            {
                SourceBuild = build;
                for (int index = 0; index < RunBuildState.SlotCount; index++)
                {
                    AbilityInstance instance = build.GetSlot(index);
                    if (instance?.Definition == null) continue;
                    int lane = index + 1;
                    slots[lane] = new AbilityRuntimeInstance(instance, runtime => AbilityResolver.Resolve(runtime.Definition, runtime.BuildInstance, build));
                    slotIndexByLane[lane] = index;
                }
                SlotsChanged?.Invoke(slots);
                return;
            }

            IEnumerable<AbilitySlotAssignment> source = assignments;
            if (assignments.All(assignment => assignment?.Ability == null))
            {
                AbilityLoadout loadout = Resources.Load<AbilityLoadout>(AbilityLoadout.ResourcePath);
                if (loadout != null) source = loadout.Slots;
            }
            foreach (AbilitySlotAssignment assignment in source.Where(assignment => assignment?.Ability != null))
            {
                slots[assignment.LaneId] = new AbilityRuntimeInstance(assignment.Ability);
                slotIndexByLane[assignment.LaneId] = slotIndexByLane.Count;
            }

            if (slots.Count == 0 && defaultAbility == null)
                defaultAbility = Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack");
            if (slots.Count == 0 && defaultAbility != null)
            {
                int firstLane = input?.Bindings.FirstOrDefault()?.LaneId ?? 1;
                slots[firstLane] = new AbilityRuntimeInstance(defaultAbility);
                slotIndexByLane[firstLane] = 0;
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
