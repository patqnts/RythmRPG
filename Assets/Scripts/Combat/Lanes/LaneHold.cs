using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Notes that are being held on a lane (Hold, Stationary Hold). Each lane's <see cref="LaneKeyMarker"/> fills with the
    /// colour of the most recent one while it is held. Owners are Unity objects, so a destroyed note drops out on its own.
    /// </summary>
    public static class LaneHold
    {
        private sealed class Entry
        {
            public Object Owner;
            public int LaneId;
            public Color Color;
            public float StartedAt;
        }

        private static readonly List<Entry> active = new();

        public static void Begin(Object owner, int laneId, Color color)
        {
            if (owner == null) return;
            End(owner);
            active.Add(new Entry { Owner = owner, LaneId = laneId, Color = color, StartedAt = Time.time });
        }

        public static void End(Object owner)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].Owner == owner || active[i].Owner == null) active.RemoveAt(i);
        }

        /// <summary>The latest hold on <paramref name="laneId"/>: its colour and how long it has been held.</summary>
        public static bool TryGet(int laneId, out Color color, out float heldSeconds)
        {
            color = Color.white;
            heldSeconds = 0f;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Entry entry = active[i];
                if (entry.Owner == null)
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (entry.LaneId != laneId) continue;
                color = entry.Color;
                heldSeconds = Time.time - entry.StartedAt;
                return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => active.Clear();
    }
}
