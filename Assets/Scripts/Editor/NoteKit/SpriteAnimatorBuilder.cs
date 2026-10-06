using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RythmRPG.EditorTools
{
    /// <summary>
    /// Builds animation clips and an Animator Controller from sprite frames, one state per phase (Approach, Hold, Hit,
    /// Miss...). Clips key a SpriteRenderer's sprite at the given frame rate; looping phases loop.
    /// </summary>
    public static class SpriteAnimatorBuilder
    {
        [System.Serializable]
        public sealed class Phase
        {
            public string state = "Approach";
            public List<Sprite> frames = new();
            public bool loop = true;

            public Phase() { }

            public Phase(string state, bool loop)
            {
                this.state = state;
                this.loop = loop;
            }

            public bool HasFrames => frames != null && frames.Exists(f => f != null);
        }

        /// <summary>Default phases for a note: Approach (loop), Hold (loop), Hit, Miss.</summary>
        public static List<Phase> DefaultPhases() => new()
        {
            new Phase("Approach", true),
            new Phase("Hold", true),
            new Phase("Hit", false),
            new Phase("Miss", false)
        };

        /// <summary>
        /// Creates (or overwrites) "<paramref name="name"/>.controller" and one clip per phase with frames in
        /// <paramref name="folder"/>. <paramref name="spritePath"/> is the SpriteRenderer's path under the Animator.
        /// </summary>
        public static AnimatorController Build(string folder, string name, IReadOnlyList<Phase> phases, float fps, string spritePath)
        {
            if (!AssetDatabase.IsValidFolder(folder)) CreateFolders(folder);
            string controllerPath = $"{folder}/{name}.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState first = null;
            foreach (Phase phase in phases)
            {
                if (phase == null || !phase.HasFrames || string.IsNullOrWhiteSpace(phase.state)) continue;
                AnimationClip clip = BuildClip($"{folder}/{name} {phase.state}.anim", phase, fps, spritePath);
                AnimatorState state = FindState(machine, phase.state) ?? machine.AddState(phase.state);
                state.motion = clip;
                first ??= state;
            }
            if (first != null && (machine.defaultState == null || FindState(machine, machine.defaultState.name) == null))
                machine.defaultState = first;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimationClip BuildClip(string path, Phase phase, float fps, string spritePath)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.frameRate = Mathf.Max(1f, fps);
            var binding = EditorCurveBinding.PPtrCurve(spritePath ?? string.Empty, typeof(SpriteRenderer), "m_Sprite");
            var keys = new List<ObjectReferenceKeyframe>();
            float step = 1f / Mathf.Max(1f, fps);
            int index = 0;
            foreach (Sprite frame in phase.frames)
            {
                if (frame == null) continue;
                keys.Add(new ObjectReferenceKeyframe { time = index * step, value = frame });
                index++;
            }
            // Hold the last frame for one frame so the clip's length covers every frame.
            if (keys.Count > 0) keys.Add(new ObjectReferenceKeyframe { time = index * step, value = keys[keys.Count - 1].value });
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = phase.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states)
                if (child.state != null && child.state.name == name) return child.state;
            return null;
        }

        public static void CreateFolders(string folder)
        {
            string[] parts = folder.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        /// <summary>Folder of an asset ("Assets/Prefab" for "Assets/Prefab/Rat.prefab").</summary>
        public static string FolderOf(Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? "Assets" : Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        }
    }
}
