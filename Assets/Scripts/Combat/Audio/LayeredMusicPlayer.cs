using System;
using RythmRPG.Rhythm.Audio;
using UnityEngine;

namespace RythmRPG.Combat
{
    /// <summary>
    /// Plays several music layers sample-locked (all scheduled for the same dsp time) and cross-fades between them,
    /// e.g. a calm "thinking" layer during ability selection and a driving "action" layer during rhythm sections.
    /// With a single layer it is just a scheduled looping player. Exposes <see cref="DspStartTime"/> so beat
    /// logic can align to the same start.
    /// </summary>
    public sealed class LayeredMusicPlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class Layer
        {
            public string name = "Layer";
            public AudioClip clip;
            [Range(0f, 1f)] public float maxVolume = 1f;
            [NonSerialized] public AudioSource source;
        }

        [SerializeField] private Layer[] layers = Array.Empty<Layer>();
        [SerializeField] private int startLayer;
        [SerializeField, Min(0f)] private float defaultFadeSeconds = 0.75f;
        [SerializeField] private bool loop = true;

        private float[] weights = Array.Empty<float>();
        private int activeLayer;
        private float fadeSeconds;

        public bool IsPlaying { get; private set; }
        public double DspStartTime { get; private set; }
        public int ActiveLayer => activeLayer;

        public void Play(double leadInSeconds = 0.1d)
        {
            Stop();
            if (layers == null || layers.Length == 0) return;
            weights = new float[layers.Length];
            activeLayer = Mathf.Clamp(startLayer, 0, layers.Length - 1);
            weights[activeLayer] = 1f;
            fadeSeconds = defaultFadeSeconds;
            DspStartTime = AudioSettings.dspTime + Math.Max(0.05d, leadInSeconds);
            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                if (layer == null || layer.clip == null) continue;
                if (layer.source == null)
                {
                    layer.source = gameObject.AddComponent<AudioSource>();
                    layer.source.playOnAwake = false;
                }

                layer.source.clip = layer.clip;
                layer.source.loop = loop;
                layer.source.volume = weights[i] * layer.maxVolume;
                layer.source.PlayScheduled(DspStartTime);
            }

            IsPlaying = true;
        }

        public void SetActiveLayer(int index, float fadeOverSeconds = -1f)
        {
            if (layers == null || layers.Length == 0) return;
            activeLayer = Mathf.Clamp(index, 0, layers.Length - 1);
            fadeSeconds = fadeOverSeconds >= 0f ? fadeOverSeconds : defaultFadeSeconds;
        }

        public bool SetActiveLayer(string layerName, float fadeOverSeconds = -1f)
        {
            if (layers == null) return false;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null && layers[i].name == layerName)
                {
                    SetActiveLayer(i, fadeOverSeconds);
                    return true;
                }
            }

            return false;
        }

        public void Stop()
        {
            IsPlaying = false;
            if (layers == null) return;
            foreach (Layer layer in layers)
            {
                if (layer != null && layer.source != null) layer.source.Stop();
            }
        }

        private void Update()
        {
            if (!IsPlaying) return;
            LayerMixer.Step(weights, activeLayer, Time.unscaledDeltaTime, fadeSeconds);
            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                if (layer != null && layer.source != null) layer.source.volume = weights[i] * layer.maxVolume;
            }
        }
    }
}
