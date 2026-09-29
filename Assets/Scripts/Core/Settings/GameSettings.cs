using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RythmRPG.Core
{
    public enum DisplayMode { Fullscreen, Borderless, Windowed }

    /// <summary>
    /// Player settings. Display mode and resolution are read from and written to <see cref="Screen"/> (Unity already
    /// remembers them between launches); the rest is stored in PlayerPrefs.
    /// </summary>
    public static class GameSettings
    {
        private const string CountdownKey = "RythmRPG.Settings.ResumeCountdown";
        private const string FocusPauseKey = "RythmRPG.Settings.PauseOnFocusLoss";
        private const string FrameRateKey = "RythmRPG.Settings.FrameRateLimit";
        private const string VSyncKey = "RythmRPG.Settings.VSync";

        /// <summary>Frame rate cap used until the player picks another one. 0 = unlimited.</summary>
        public const int DefaultFrameRateLimit = 60;

        /// <summary>Choices for a settings menu (0 = unlimited).</summary>
        public static readonly int[] FrameRateOptions = { 30, 60, 120, 144, 165, 240, 0 };

        public const int MaxResumeCountdown = 5;

        /// <summary>Seconds of "3, 2, 1" before the game continues after a pause. 0 = resume immediately.</summary>
        public static int ResumeCountdownSeconds
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(CountdownKey, 3), 0, MaxResumeCountdown);
            set
            {
                PlayerPrefs.SetInt(CountdownKey, Mathf.Clamp(value, 0, MaxResumeCountdown));
                PlayerPrefs.Save();
            }
        }

        /// <summary>Pause automatically when the game window loses focus (builds only).</summary>
        public static bool PauseOnFocusLoss
        {
            get => PlayerPrefs.GetInt(FocusPauseKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(FocusPauseKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // ---------- Frame rate ----------

        /// <summary>
        /// Most frames per second the game renders (0 = unlimited). Saved between launches and applied at startup.
        /// Ignored while <see cref="VSync"/> is on (then the monitor's refresh rate sets the pace).
        /// </summary>
        public static int FrameRateLimit
        {
            get => Mathf.Max(0, PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRateLimit));
            set
            {
                PlayerPrefs.SetInt(FrameRateKey, Mathf.Max(0, value));
                PlayerPrefs.Save();
                ApplyFrameRate();
            }
        }

        /// <summary>Sync to the monitor's refresh rate (no tearing). Overrides <see cref="FrameRateLimit"/>.</summary>
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
                PlayerPrefs.Save();
                ApplyFrameRate();
            }
        }

        /// <summary>Applies the saved frame rate limit / VSync (done automatically at startup).</summary>
        public static void ApplyFrameRate()
        {
            if (VSync)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                int limit = FrameRateLimit;
                Application.targetFrameRate = limit > 0 ? limit : -1;
            }
        }

        public static string DescribeFrameRate(int limit) => limit > 0 ? $"{limit} FPS" : "Unlimited";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStartup() => ApplyFrameRate();

        // ---------- Display ----------

        public static DisplayMode CurrentDisplayMode => Screen.fullScreenMode switch
        {
            FullScreenMode.ExclusiveFullScreen => DisplayMode.Fullscreen,
            FullScreenMode.FullScreenWindow => DisplayMode.Borderless,
            FullScreenMode.MaximizedWindow => DisplayMode.Borderless,
            _ => DisplayMode.Windowed
        };

        public static Vector2Int CurrentResolution => new(Screen.width, Screen.height);

        /// <summary>Native size of the monitor the game is on.</summary>
        public static Vector2Int NativeResolution
        {
            get
            {
                Resolution current = Screen.currentResolution;
                return current.width > 0 && current.height > 0
                    ? new Vector2Int(current.width, current.height)
                    : new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);
            }
        }

        /// <summary>Distinct resolutions the monitor supports, smallest first (refresh rates merged).</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = Screen.resolutions
                .Select(resolution => new Vector2Int(resolution.width, resolution.height))
                .Where(size => size.x >= 640 && size.y >= 360)
                .Distinct()
                .ToList();
            Vector2Int current = CurrentResolution;
            if (!list.Contains(current)) list.Add(current);
            Vector2Int native = NativeResolution;
            if (!list.Contains(native)) list.Add(native);
            return list.OrderBy(size => size.x * size.y).ThenBy(size => size.x).ToList();
        }

        /// <summary>
        /// Applies a display mode and resolution. Borderless always uses the monitor's native resolution.
        /// (Has no effect inside the Editor's Game view; test it in a build.)
        /// </summary>
        public static void ApplyDisplay(DisplayMode mode, Vector2Int resolution)
        {
            FullScreenMode fullScreenMode = mode switch
            {
                DisplayMode.Fullscreen => FullScreenMode.ExclusiveFullScreen, // falls back to borderless off Windows
                DisplayMode.Borderless => FullScreenMode.FullScreenWindow,
                _ => FullScreenMode.Windowed
            };
            if (mode == DisplayMode.Borderless) resolution = NativeResolution;
            if (resolution.x <= 0 || resolution.y <= 0) resolution = CurrentResolution;
            Screen.SetResolution(resolution.x, resolution.y, fullScreenMode);
        }

        public static string Describe(DisplayMode mode) => mode switch
        {
            DisplayMode.Fullscreen => "Fullscreen",
            DisplayMode.Borderless => "Borderless",
            _ => "Windowed"
        };
    }
}
