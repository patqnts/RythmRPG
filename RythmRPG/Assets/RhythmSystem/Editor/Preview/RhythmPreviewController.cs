using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    public sealed class RhythmPreviewController : IDisposable
    {
        private readonly Action repaint;
        private readonly EditorAudioPreviewAdapter audio = new EditorAudioPreviewAdapter();
        private RhythmChart chart;
        private double lastEditorTime;

        public double Playhead { get; private set; }
        public bool IsPlaying { get; private set; }
        public string AudioWarning => chart != null && chart.AudioClip != null && !audio.IsAvailable
            ? "Editor audio preview is unavailable in this Unity version. Visual preview remains active."
            : string.Empty;

        public RhythmPreviewController(Action repaintCallback)
        {
            repaint = repaintCallback;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += StopForEditorLifecycle;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += StopForEditorLifecycle;
        }

        public void SetChart(RhythmChart value)
        {
            StopForEditorLifecycle();
            chart = value;
            Playhead = 0d;
            repaint?.Invoke();
        }

        public void TogglePlayPause()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void Play()
        {
            if (chart == null)
            {
                return;
            }

            if (Playhead >= chart.EffectiveDuration)
            {
                Playhead = 0d;
            }

            IsPlaying = true;
            lastEditorTime = EditorApplication.timeSinceStartup;
            StartAudioAtPlayhead();
            repaint?.Invoke();
        }

        public void Pause()
        {
            if (!IsPlaying)
            {
                return;
            }

            AdvancePlayhead();
            IsPlaying = false;
            audio.Pause();
            repaint?.Invoke();
        }

        public void Stop()
        {
            if (IsPlaying)
            {
                AdvancePlayhead();
            }

            IsPlaying = false;
            audio.Stop();
            repaint?.Invoke();
        }

        public void Reset()
        {
            IsPlaying = false;
            audio.Stop();
            Playhead = 0d;
            repaint?.Invoke();
        }

        public void Seek(double time)
        {
            if (chart == null)
            {
                Playhead = 0d;
                return;
            }

            Playhead = Math.Max(0d, Math.Min(time, chart.EffectiveDuration));
            lastEditorTime = EditorApplication.timeSinceStartup;
            if (IsPlaying)
            {
                StartAudioAtPlayhead();
            }

            repaint?.Invoke();
        }

        public void Dispose()
        {
            StopForEditorLifecycle();
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= StopForEditorLifecycle;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= StopForEditorLifecycle;
        }

        private void Update()
        {
            if (!IsPlaying || chart == null)
            {
                return;
            }

            AdvancePlayhead();
            if (Playhead >= chart.EffectiveDuration)
            {
                Playhead = chart.EffectiveDuration;
                IsPlaying = false;
                audio.Stop();
            }

            repaint?.Invoke();
        }

        private void AdvancePlayhead()
        {
            double now = EditorApplication.timeSinceStartup;
            Playhead += Math.Max(0d, now - lastEditorTime);
            lastEditorTime = now;
        }

        private void StartAudioAtPlayhead()
        {
            audio.Stop();
            AudioClip clip = chart?.AudioClip;
            if (clip == null || Playhead >= clip.length)
            {
                return;
            }

            int startSample = Mathf.Clamp((int)Math.Round(Playhead * clip.frequency), 0, Math.Max(0, clip.samples - 1));
            audio.Play(clip, startSample);
        }

        private void StopForEditorLifecycle()
        {
            IsPlaying = false;
            audio.Stop();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange ignored)
        {
            StopForEditorLifecycle();
        }
    }

    internal sealed class EditorAudioPreviewAdapter
    {
        private readonly MethodInfo playMethod;
        private readonly MethodInfo pauseMethod;
        private readonly MethodInfo stopMethod;
        private bool failed;

        public bool IsAvailable => playMethod != null && stopMethod != null && !failed;

        public EditorAudioPreviewAdapter()
        {
            Type audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtil == null)
            {
                return;
            }

            MethodInfo[] methods = audioUtil.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            playMethod = methods
                .Where(method => method.Name == "PlayPreviewClip")
                .OrderByDescending(method => method.GetParameters().Length)
                .FirstOrDefault(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(AudioClip)));
            pauseMethod = methods.FirstOrDefault(method => method.Name == "PausePreviewClip" && method.GetParameters().Length == 0);
            stopMethod = methods.FirstOrDefault(method => method.Name == "StopAllPreviewClips" && method.GetParameters().Length == 0);
        }

        public void Play(AudioClip clip, int startSample)
        {
            if (!IsAvailable || clip == null)
            {
                return;
            }

            try
            {
                ParameterInfo[] parameters = playMethod.GetParameters();
                object[] arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type type = parameters[i].ParameterType;
                    arguments[i] = type == typeof(AudioClip) ? clip
                        : type == typeof(int) ? startSample
                        : type == typeof(bool) ? false
                        : parameters[i].HasDefaultValue ? parameters[i].DefaultValue
                        : null;
                }

                playMethod.Invoke(null, arguments);
            }
            catch
            {
                // Unity's preview API is internal and may vary. Visual playback
                // deliberately continues when editor audio cannot be started.
                failed = true;
            }
        }

        public void Pause()
        {
            if (pauseMethod == null)
            {
                Stop();
                return;
            }

            if (!InvokeNoArguments(pauseMethod))
            {
                failed = true;
            }
        }

        public void Stop()
        {
            if (!InvokeNoArguments(stopMethod) && stopMethod != null)
            {
                failed = true;
            }
        }

        private static bool InvokeNoArguments(MethodInfo method)
        {
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(null, null);
                return true;
            }
            catch
            {
                // See Play: the adapter is intentionally best-effort.
                return false;
            }
        }
    }
}
