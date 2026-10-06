using System.Collections.Generic;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>How an Actor Vanish View makes a combatant disappear.</summary>
    public enum VanishStyle
    {
        /// <summary>Fade the sprites' alpha.</summary>
        Fade,
        /// <summary>Drive a float on the renderers' material (a dissolve shader: 0 = whole, 1 = gone by default).</summary>
        MaterialFloat,
        /// <summary>Switch the renderers off and on (pairs well with a disintegration effect or animation).</summary>
        Hide
    }

    /// <summary>
    /// Shows and hides combatants for notes (Actor Vanish View). Keeps each renderer's original colour / state from the
    /// first claim, so overlapping notes never "restore" to invisible; the newest note owns the actor.
    /// <see cref="ActorLock.RestoreAll"/> also shows everyone again (battle cancelled or over mid-note).
    /// </summary>
    public static class ActorVisibility
    {
        private sealed class State
        {
            public object Owner;
            public readonly List<Renderer> Renderers = new();
            public readonly List<Color> Colors = new();
            public readonly List<bool> ForcedOff = new();
            public string Property;
            public float Visible;
            public float Gone;
            public bool UsedBlock;
        }

        private static readonly Dictionary<Transform, State> states = new();
        private static MaterialPropertyBlock block;

        /// <summary>Claims <paramref name="actor"/> for <paramref name="owner"/> (captures its look the first time).</summary>
        public static void Acquire(Transform actor, object owner)
        {
            if (actor == null) return;
            if (!states.TryGetValue(actor, out State state))
            {
                state = new State();
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                    state.Renderers.Add(renderer);
                    state.Colors.Add(renderer is SpriteRenderer sprite ? sprite.color : Color.white);
                    state.ForcedOff.Add(renderer.forceRenderingOff);
                }
                states[actor] = state;
            }
            state.Owner = owner;
        }

        public static bool Owns(Transform actor, object owner) =>
            actor != null && states.TryGetValue(actor, out State state) && ReferenceEquals(state.Owner, owner);

        /// <summary>Sets how visible the actor is: 1 = as it was, 0 = gone.</summary>
        public static void Apply(Transform actor, object owner, float visibility, VanishStyle style, string property, float visibleValue, float goneValue)
        {
            if (actor == null || !states.TryGetValue(actor, out State state) || !ReferenceEquals(state.Owner, owner)) return;
            visibility = Mathf.Clamp01(visibility);
            for (int i = 0; i < state.Renderers.Count; i++)
            {
                Renderer renderer = state.Renderers[i];
                if (renderer == null) continue;
                switch (style)
                {
                    case VanishStyle.Fade:
                        if (renderer is SpriteRenderer sprite)
                        {
                            Color color = state.Colors[i];
                            color.a *= visibility;
                            sprite.color = color;
                        }
                        else renderer.forceRenderingOff = state.ForcedOff[i] || visibility <= 0.001f;
                        break;
                    case VanishStyle.MaterialFloat:
                        if (string.IsNullOrWhiteSpace(property)) break;
                        block ??= new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        block.SetFloat(property, Mathf.Lerp(goneValue, visibleValue, visibility));
                        renderer.SetPropertyBlock(block);
                        state.Property = property;
                        state.Visible = visibleValue;
                        state.Gone = goneValue;
                        state.UsedBlock = true;
                        break;
                    default:
                        renderer.forceRenderingOff = state.ForcedOff[i] || visibility < 0.5f;
                        break;
                }
            }
        }

        /// <summary>Lets go: shows the actor exactly as it was. False when a newer note owns it.</summary>
        public static bool Release(Transform actor, object owner)
        {
            if (actor == null || !states.TryGetValue(actor, out State state) || !ReferenceEquals(state.Owner, owner)) return false;
            Restore(state);
            states.Remove(actor);
            return true;
        }

        /// <summary>The actor's sprites' bounds (for effects and flakes), or a small box at its position.</summary>
        public static Bounds BoundsOf(Transform actor)
        {
            if (actor == null) return default;
            bool any = false;
            Bounds bounds = new(actor.position, Vector3.one * 0.5f);
            foreach (SpriteRenderer sprite in actor.GetComponentsInChildren<SpriteRenderer>(false))
            {
                if (sprite == null || sprite.sprite == null) continue;
                if (!any) bounds = sprite.bounds;
                else bounds.Encapsulate(sprite.bounds);
                any = true;
            }
            return bounds;
        }

        public static void RestoreAll()
        {
            foreach (State state in states.Values) Restore(state);
            states.Clear();
        }

        public static void Clear() => states.Clear();

        private static void Restore(State state)
        {
            for (int i = 0; i < state.Renderers.Count; i++)
            {
                Renderer renderer = state.Renderers[i];
                if (renderer == null) continue;
                if (renderer is SpriteRenderer sprite) sprite.color = state.Colors[i];
                renderer.forceRenderingOff = state.ForcedOff[i];
                if (state.UsedBlock && !string.IsNullOrWhiteSpace(state.Property))
                {
                    block ??= new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetFloat(state.Property, state.Visible);
                    renderer.SetPropertyBlock(block);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => states.Clear();
    }
}
