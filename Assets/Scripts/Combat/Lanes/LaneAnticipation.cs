using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>How a stationary note's charge shows on its lane's key marker.</summary>
    public enum LaneAnticipationKind
    {
        /// <summary>Stationary Note: an outer copy of the marker outline shrinks onto the marker.</summary>
        ApproachOutline,
        /// <summary>Stationary Hold Note: a fill grows from the centre until it fills the marker.</summary>
        ChargeFill
    }

    /// <summary>A note that is charging up on a lane (see <see cref="LaneAnticipation"/>).</summary>
    public interface ILaneAnticipation
    {
        int AnticipationLaneId { get; }
        LaneAnticipationKind AnticipationKind { get; }
        /// <summary>True while the note is charging (not hit, missed or over yet).</summary>
        bool IsAnticipating { get; }
        /// <summary>0 when the charge starts, 1 at the perfect hit time.</summary>
        float AnticipationProgress { get; }
    }

    /// <summary>
    /// Notes that are charging on a lane register here; each lane's <see cref="LaneKeyMarker"/> draws the one closest to
    /// its hit time. Progress is read when the marker draws, from the note's own clock, so order of updates does not matter.
    /// </summary>
    public static class LaneAnticipation
    {
        private static readonly List<ILaneAnticipation> active = new();

        public static void Register(ILaneAnticipation source)
        {
            if (source != null && !active.Contains(source)) active.Add(source);
        }

        public static void Unregister(ILaneAnticipation source) => active.Remove(source);

        /// <summary>The most advanced charge of <paramref name="kind"/> on <paramref name="laneId"/>, if any.</summary>
        public static bool TryGet(int laneId, LaneAnticipationKind kind, out float progress)
        {
            progress = 0f;
            bool found = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ILaneAnticipation source = active[i];
                if (source == null || (source is Object unityObject && unityObject == null))
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (source.AnticipationLaneId != laneId || source.AnticipationKind != kind || !source.IsAnticipating) continue;
                float value = Mathf.Clamp01(source.AnticipationProgress);
                if (found && value <= progress) continue;
                progress = value;
                found = true;
            }
            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => active.Clear();
    }
}
