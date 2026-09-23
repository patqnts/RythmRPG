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
