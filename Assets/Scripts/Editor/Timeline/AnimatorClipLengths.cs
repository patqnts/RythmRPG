using System;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>Reads animator state lengths (layer 0, sub-state machines included) for the timelines.</summary>
    public static class AnimatorClipLengths
    {
        /// <summary>Seconds of <paramref name="state"/> in <paramref name="controller"/> (0 when not found).</summary>
        public static float StateSeconds(RuntimeAnimatorController controller, string state)
        {
            if (controller == null || string.IsNullOrWhiteSpace(state)) return 0f;
            var overrides = controller as AnimatorOverrideController;
            AnimatorController source = overrides != null
                ? overrides.runtimeAnimatorController as AnimatorController
                : controller as AnimatorController;
            if (source != null && source.layers.Length > 0)
            {
                AnimatorState found = Find(source.layers[0].stateMachine, state);
                if (found != null)
                {
                    float length = MotionSeconds(found.motion, overrides);
                    float speed = Mathf.Abs(found.speed) > 0.0001f ? Mathf.Abs(found.speed) : 1f;
                    if (length > 0f) return length / speed;
                }
            }
            foreach (AnimationClip clip in controller.animationClips)
                if (clip != null && string.Equals(clip.name, state, StringComparison.OrdinalIgnoreCase)) return clip.length;
            return 0f;
        }

        /// <summary>Names of the controller's states (layer 0), for pickers.</summary>
        public static List<string> StateNames(RuntimeAnimatorController controller)
        {
            var names = new List<string>();
            var overrides = controller as AnimatorOverrideController;
            AnimatorController source = overrides != null
                ? overrides.runtimeAnimatorController as AnimatorController
                : controller as AnimatorController;
            if (source != null && source.layers.Length > 0) Collect(source.layers[0].stateMachine, names);
            return names;
        }

        private static AnimatorState Find(AnimatorStateMachine machine, string state)
        {
            if (machine == null) return null;
            foreach (ChildAnimatorState child in machine.states)
                if (child.state != null && string.Equals(child.state.name, state, StringComparison.OrdinalIgnoreCase)) return child.state;
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
            {
                AnimatorState found = Find(sub.stateMachine, state);
                if (found != null) return found;
            }
            return null;
        }

        private static void Collect(AnimatorStateMachine machine, List<string> names)
        {
            if (machine == null) return;
            foreach (ChildAnimatorState child in machine.states)
                if (child.state != null) names.Add(child.state.name);
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines) Collect(sub.stateMachine, names);
        }

        private static float MotionSeconds(Motion motion, AnimatorOverrideController overrides)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    AnimationClip used = overrides != null && overrides[clip] != null ? overrides[clip] : clip;
                    return used.length;
                case BlendTree tree:
                    float longest = 0f;
                    foreach (ChildMotion child in tree.children) longest = Mathf.Max(longest, MotionSeconds(child.motion, overrides));
                    return longest;
                default:
                    return 0f;
            }
        }
    }
}
