using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RythmRPG.Rhythm.Audio;
using UnityEngine;
using UnityEngine.Networking;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>One loaded music file.</summary>
    public sealed class AudioEntry
    {
        public string Path;
        public AudioClip Clip;
        public WaveformPeaks Peaks;
        public string Error;
        public bool Loading;
        public DateTime LoadedWriteTime;

        public bool Ready { get { return Clip != null && !Loading; } }
        /// <summary>Exact length (samples / frequency), as the game measures it.</summary>
        public double Seconds { get { return Clip != null && Clip.frequency > 0 ? (double)Clip.samples / Clip.frequency : 0d; } }
        public string FileName { get { return System.IO.Path.GetFileName(Path); } }
    }

    /// <summary>
    /// Loads WAV / OGG / MP3 / AIFF files from disk at run time (UnityWebRequestMultimedia), decompressed so the
    /// waveform can be read, and reloads them when the file changes on disk (so re-exported music shows up by itself).
    /// </summary>
    public sealed class AudioLibrary
    {
        private readonly MonoBehaviour host;
        private readonly Dictionary<string, AudioEntry> entries = new Dictionary<string, AudioEntry>(StringComparer.OrdinalIgnoreCase);
        private float nextWatch;

        public event Action<AudioEntry> Loaded;

        public static readonly string[] Extensions = { ".wav", ".ogg", ".mp3", ".aif", ".aiff" };

        public AudioLibrary(MonoBehaviour host)
        {
            this.host = host;
        }

        /// <summary>The entry for a file; starts loading it the first time. Null for an empty path.</summary>
        public AudioEntry Get(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            AudioEntry e;
            if (entries.TryGetValue(fullPath, out e)) return e;
            e = new AudioEntry { Path = fullPath };
            entries[fullPath] = e;
            host.StartCoroutine(LoadRoutine(e));
            return e;
        }

        public void Reload(string fullPath)
        {
            AudioEntry e;
            if (string.IsNullOrEmpty(fullPath) || !entries.TryGetValue(fullPath, out e) || e.Loading) return;
            host.StartCoroutine(LoadRoutine(e));
        }

        /// <summary>Checks every couple of seconds whether a loaded file changed on disk and reloads it.</summary>
        public void Watch()
        {
            if (Time.unscaledTime < nextWatch) return;
            nextWatch = Time.unscaledTime + 2f;
            foreach (AudioEntry e in entries.Values)
            {
                if (e.Loading || e.Clip == null) continue;
                try
                {
                    if (File.Exists(e.Path) && File.GetLastWriteTimeUtc(e.Path) != e.LoadedWriteTime) host.StartCoroutine(LoadRoutine(e));
                }
                catch (Exception)
                {
                    // Ignore files that are being written.
                }
            }
        }

        public static AudioType TypeFor(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".mp3": return AudioType.MPEG;
                case ".aif":
                case ".aiff": return AudioType.AIFF;
                default: return AudioType.UNKNOWN;
            }
        }

        private IEnumerator LoadRoutine(AudioEntry e)
        {
            e.Loading = true;
            e.Error = null;
            if (!File.Exists(e.Path))
            {
                e.Loading = false;
                e.Error = "File not found";
                RaiseLoaded(e);
                yield break;
            }

            AudioType type = TypeFor(e.Path);
            if (type == AudioType.UNKNOWN)
            {
                e.Loading = false;
                e.Error = "Unsupported format (use WAV, OGG, MP3 or AIFF)";
                RaiseLoaded(e);
                yield break;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(e.Path);
            string uri = new Uri(e.Path).AbsoluteUri;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                var handler = (DownloadHandlerAudioClip)req.downloadHandler;
                handler.streamAudio = false;
                handler.compressed = false; // decompress on load so GetData works (waveform)
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    e.Loading = false;
                    e.Error = "Could not load: " + req.error;
                    RaiseLoaded(e);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null || clip.samples <= 0)
                {
                    e.Loading = false;
                    e.Error = "The file has no audio data";
                    RaiseLoaded(e);
                    yield break;
                }

                clip.name = System.IO.Path.GetFileNameWithoutExtension(e.Path);
                while (clip.loadState == AudioDataLoadState.Loading) yield return null;

                // Waveform: read the samples on the main thread, summarise them on a worker thread.
                WaveformPeaks peaks = null;
                float[] data = new float[clip.samples * clip.channels];
                if (clip.GetData(data, 0))
                {
                    int channels = clip.channels, rate = clip.frequency;
                    bool done = false;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { peaks = WaveformPeaks.Build(data, channels, rate); }
                        catch (Exception) { peaks = null; }
                        done = true;
                    });
                    while (!done) yield return null;
                }

                AudioClip old = e.Clip;
                e.Clip = clip;
                e.Peaks = peaks;
                e.LoadedWriteTime = writeTime;
                e.Loading = false;
                if (old != null && old != clip) UnityEngine.Object.Destroy(old);
                RaiseLoaded(e);
            }
        }

        private void RaiseLoaded(AudioEntry e)
        {
            Action<AudioEntry> h = Loaded;
            if (h != null) h(e);
        }
    }

    /// <summary>Short generated sounds (metronome clicks and hit sounds) so the app needs no audio assets.</summary>
    public static class SynthClips
    {
        public const int Rate = 44100;

        public static AudioClip Click(string name, float frequency, float seconds, float decay, float noise = 0f, float gain = 0.8f)
        {
            int n = Mathf.Max(1, (int)(Rate * seconds));
            var data = new float[n];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Clamp01(i / 40f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float nz = noise > 0f ? (float)(rng.NextDouble() * 2d - 1d) * noise : 0f;
                data[i] = (tone * (1f - noise) + nz) * env * gain;
            }

            AudioClip clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
