using System.Collections.Generic;
using RythmRPG.Core;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Cast effect prefabs that take the ability's accent colour: the wisp, charge and burst spawners call
    /// <see cref="ApplyTint"/> on every component that implements this, right after instantiating the prefab.
    /// </summary>
    public interface ICastEffectTint
    {
        void ApplyTint(Color accent);
    }

    /// <summary>
    /// Shared helpers for the slime bubble effects: camera facing and tinting particle systems toward a colour while
    /// keeping their authored colour as the base.
    /// </summary>
    internal static class SlimeVfx
    {
        public static Camera ViewCamera() => Camera.main;

        public static Quaternion Facing(Camera camera) =>
            camera != null ? ObliqueProjection.BillboardRotation(camera) : Quaternion.identity;

        public static Color[] CaptureStartColors(ParticleSystem[] systems)
        {
            var colors = new Color[systems.Length];
            for (int i = 0; i < systems.Length; i++)
                colors[i] = systems[i] != null ? systems[i].main.startColor.color : Color.white;
            return colors;
        }

        public static void Tint(ParticleSystem[] systems, Color[] authored, Color accent, float amount)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                Color baseColor = authored[i];
                // Paler authored layers (the core, the shine-ish bits) stay paler: the accent is whitened by how
                // white the authored colour is, so the bubble keeps its light centre in any ability colour.
                float whiteness = Mathf.Min(baseColor.r, Mathf.Min(baseColor.g, baseColor.b));
                Color accented = Color.Lerp(accent, Color.white, whiteness * whiteness * 0.8f);
                Color tinted = Color.Lerp(baseColor, accented, amount);
                tinted.a = baseColor.a;
                ParticleSystem.MainModule main = systems[i].main;
                main.startColor = tinted;
            }
        }
    }

    /// <summary>Applies an accent colour to every <see cref="ICastEffectTint"/> under a spawned cast effect.</summary>
    public static class CastEffectTint
    {
        private static readonly List<ICastEffectTint> Buffer = new();

        public static void Apply(GameObject instance, Color accent)
        {
            if (instance == null) return;
            Buffer.Clear();
            instance.GetComponentsInChildren(true, Buffer);
            foreach (ICastEffectTint tint in Buffer) tint.ApplyTint(accent);
            Buffer.Clear();
        }
    }
}
