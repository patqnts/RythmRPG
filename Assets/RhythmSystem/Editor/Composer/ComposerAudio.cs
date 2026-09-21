using System;
using System.Text;
using RythmRPG.Rhythm.Audio;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor.Composer
{
    /// <summary>
    /// Editor-side audio for the composer: waveform extraction and preview playback with an optional metronome.
    /// Unity's editor preview API plays one clip at a time, so with the metronome on the music and clicks are
    /// mixed into one temporary clip (sample-accurate; rebuilt whenever tempo, offset or audio changes).
    /// </summary>
    internal sealed class ComposerAudio : IDisposable
    {
        private const float ClickGain = 0.6f;
        private const double MaxMixSeconds = 3600d;

        private readonly EditorAudioPreviewAdapter adapter = new EditorAudioPreviewAdapter();
        private AudioClip mixClip;
        private string mixKey;
        private AudioClip mixMusic;

        public bool PlaybackAvailable { get { return adapter.IsAvailable; } }

        /// <summary>Extracts a waveform summary. Returns null with an explanation when Unity will not hand over the samples.</summary>
        public static WaveformPeaks BuildPeaks(AudioClip clip, out string error)
        {
            error = null;
            if (clip == null) return null;
            try
            {
                clip.LoadAudioData();
                var data = new float[clip.samples * clip.channels];
                if (!clip.GetData(data, 0))
                {
                    error = "Waveform unavailable: set the clip's Load Type to Decompress On Load (or Compressed In Memory).";
                    return null;
                }

                return WaveformPeaks.Build(data, clip.channels, clip.frequency);
            }
            catch (Exception e)
            {
                error = "Waveform unavailable: " + e.Message;
                return null;
            }
        }

        public void PlayFrom(double seconds, AudioClip music, TempoMap tempo, double duration, bool metronome)
        {
            Stop();
            AudioClip clip = metronome ? GetMix(music, tempo, duration) : music;
            if (clip == null || seconds >= clip.length) return;
            int start = Mathf.Clamp((int)Math.Round(seconds * clip.frequency), 0, Math.Max(0, clip.samples - 1));
            adapter.Play(clip, start);
        }

        public void Stop()
        {
            adapter.Stop();
        }

        public void Dispose()
        {
            adapter.Stop();
            if (mixClip != null)
            {
                UnityEngine.Object.DestroyImmediate(mixClip);
                mixClip = null;
            }
        }

        private AudioClip GetMix(AudioClip music, TempoMap tempo, double duration)
        {
            string key = KeyFor(music, tempo, duration);
            if (mixClip != null && key == mixKey && ReferenceEquals(mixMusic, music)) return mixClip;
            if (mixClip != null)
            {
                UnityEngine.Object.DestroyImmediate(mixClip);
                mixClip = null;
            }

            int rate = music != null ? music.frequency : 44100;
            int channels = music != null ? Math.Max(1, music.channels) : 2;
            double seconds = Math.Min(MaxMixSeconds, Math.Max(duration, music != null ? music.length : 0d));
            int frames = Math.Max(1, (int)Math.Ceiling(seconds * rate));
            var data = new float[frames * channels];
            if (music != null)
            {
                try
                {
                    music.LoadAudioData();
                    var source = new float[music.samples * music.channels];
                    if (music.GetData(source, 0)) Array.Copy(source, data, Math.Min(source.Length, data.Length));
                }
                catch (Exception)
                {
                    // Music samples unavailable: the metronome still plays on its own.
                }
            }

            MetronomeClicks.MixInto(data, channels, rate, tempo, ClickGain);
            mixClip = AudioClip.Create("ComposerMix", frames, channels, rate, false);
            mixClip.hideFlags = HideFlags.HideAndDontSave;
            mixClip.SetData(data, 0);
            mixKey = key;
            mixMusic = music;
            return mixClip;
        }

        private static string KeyFor(AudioClip music, TempoMap tempo, double duration)
        {
            var sb = new StringBuilder();
            sb.Append(tempo.AudioOffsetSeconds).Append('|').Append(tempo.BeatsPerMeasure).Append('|');
            for (int i = 0; i < tempo.Segments.Count; i++) sb.Append(tempo.Segments[i].Beat).Append('@').Append(tempo.Segments[i].Bpm).Append(';');
            sb.Append(Math.Ceiling(duration));
            return sb.ToString();
        }
    }
}
