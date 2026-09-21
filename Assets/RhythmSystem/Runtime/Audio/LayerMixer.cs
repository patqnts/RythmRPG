using System;

namespace RythmRPG.Rhythm.Audio
{
    /// <summary>Cross-fade math for layered music (e.g. a "thinking" layer and an "action" layer that play in lockstep).</summary>
    public static class LayerMixer
    {
        /// <summary>Moves each volume toward 1 (the active layer) or 0 (the others) at 1/fadeSeconds per second. fadeSeconds &lt;= 0 snaps.</summary>
        public static void Step(float[] volumes, int activeLayer, float deltaSeconds, float fadeSeconds)
        {
            if (volumes == null) throw new ArgumentNullException("volumes");
            float step = fadeSeconds <= 0f ? 1f : Math.Max(0f, deltaSeconds) / fadeSeconds;
            for (int i = 0; i < volumes.Length; i++)
            {
                float target = i == activeLayer ? 1f : 0f;
                float v = volumes[i];
                if (v < target) v = Math.Min(target, v + step);
                else if (v > target) v = Math.Max(target, v - step);
                volumes[i] = v;
            }
        }
    }
}
