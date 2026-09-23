using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace RythmRPG.Core
{
    /// <summary>
    /// Pauses and resumes the whole game. Created automatically when the game starts (no scene setup needed) and kept
    /// across scene loads. Press Pause (Esc / Start) to open the pause menu.
    /// <para>
    /// While paused: <c>Time.timeScale</c> is 0, <see cref="GameAudioClock"/> stands still (charts and notes freeze),
    /// scheduled music is stopped and re-scheduled to continue on the exact sample, other playing AudioSources are
    /// paused, and <see cref="GameInput"/>'s gameplay helpers read as no input.
    /// </para>
    /// <para>
    /// Resume in combat runs a countdown (Settings > Gameplay, default 3 s); outside combat it continues right away. The
    /// game stays frozen during the countdown and continues on the last tick, with the music re-scheduled for that same audio sample. Pressing Pause during the countdown pauses
    /// again.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GamePause : MonoBehaviour
    {
        public enum PauseState { Running, Paused, Resuming }

        /// <summary>Shortest lead for re-scheduling music, so it starts sample-accurately even with no countdown.</summary>
        private const double MinResumeLead = 0.1d;

        public static GamePause Instance { get; private set; }
        public static PauseState State => Instance != null ? Instance.state : PauseState.Running;
        /// <summary>True while paused or counting down to resume.</summary>
        public static bool IsPaused => State != PauseState.Running;
        /// <summary>Set to false to block pausing (cutscenes, loading screens...).</summary>
        public static bool PauseAllowed { get; set; } = true;
        /// <summary>Realtime seconds (like <c>Time.unscaledTime</c>) that exclude time spent paused.</summary>
        public static float UnpausedRealtime => Time.unscaledTime - pausedRealtimeTotal
            - (IsPaused && Instance != null ? Time.unscaledTime - Instance.pausedAtRealtime : 0f);

        public static event Action<PauseState> StateChanged;

        private static float pausedRealtimeTotal;
        private static readonly List<Func<bool>> countdownConditions = new();

        private PauseState state;
        private float savedTimeScale = 1f;
        private float pausedAtRealtime;
        private bool savedCursorVisible;
        private CursorLockMode savedCursorLock;
        private readonly List<AudioSource> pausedSources = new();
        private PauseMenu menu;
        private int activeCountdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            PauseAllowed = true;
            pausedRealtimeTotal = 0f;
            countdownConditions.Clear();
            StateChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            GameObject host = new("[Game Pause]");
            DontDestroyOnLoad(host);
            host.AddComponent<GamePause>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            GameInput.EnsureCreated();
            EnsureEventSystem();
            SceneManager.sceneLoaded += OnSceneLoaded;
            menu = gameObject.AddComponent<PauseMenu>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (state != PauseState.Running) Time.timeScale = savedTimeScale;
            Instance = null;
        }

        private void Update()
        {
            if (state == PauseState.Resuming && !GameAudioClock.IsHeld) FinishResume();

            if (GameInput.IsRebinding || GameInput.RebindEndedFrame == Time.frameCount) return;
            bool pausePressed = GameInput.Pause.WasPressedThisFrame();
            bool cancelPressed = state == PauseState.Paused && UiCancelPressed();
            if (!pausePressed && !cancelPressed) return;

            switch (state)
            {
                case PauseState.Running:
                    Pause();
                    break;
                case PauseState.Paused:
                    menu.Back(); // closes a sub-page, or resumes from the main page
                    break;
                case PauseState.Resuming:
                    if (pausePressed) Pause();
                    break;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && !Application.isEditor && GameSettings.PauseOnFocusLoss && state == PauseState.Running) Pause();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && !Application.isEditor && state == PauseState.Running) Pause();
        }

        // ---------- Public API ----------

        /// <summary>Pauses the game and opens the pause menu (also cancels a running resume countdown).</summary>
        public void Pause()
        {
            if (state == PauseState.Paused || (!PauseAllowed && state == PauseState.Running)) return;

            if (state == PauseState.Running)
            {
                savedTimeScale = Time.timeScale;
                pausedAtRealtime = Time.unscaledTime;
                savedCursorVisible = Cursor.visible;
                savedCursorLock = Cursor.lockState;
                PauseLooseAudio();
            }

            GameAudioClock.Suspend();
            Time.timeScale = 0f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            EnsureEventSystem();
            SetState(PauseState.Paused);
        }

        /// <summary>Closes the menu and continues after the resume countdown.</summary>
        public void Resume()
        {
            if (state != PauseState.Paused) return;
            activeCountdown = CountdownApplies ? GameSettings.ResumeCountdownSeconds : 0;
            GameAudioClock.Release(AudioSettings.dspTime + Math.Max(activeCountdown, MinResumeLead));
            SetState(PauseState.Resuming);
        }

        public void TogglePause()
        {
            if (state == PauseState.Paused) Resume();
            else Pause();
        }

        /// <summary>Countdown seconds used by the current resume (0 = continue right away).</summary>
        public static int ActiveResumeCountdown => State == PauseState.Resuming && Instance != null ? Instance.activeCountdown : 0;

        /// <summary>
        /// The resume countdown only runs while at least one registered condition is true (e.g. "a battle is on", added
        /// by CombatController). Everywhere else Resume continues right away.
        /// </summary>
        public static void AddCountdownCondition(Func<bool> condition)
        {
            if (condition != null && !countdownConditions.Contains(condition)) countdownConditions.Add(condition);
        }

        public static void RemoveCountdownCondition(Func<bool> condition) => countdownConditions.Remove(condition);

        public static bool CountdownApplies
        {
            get
            {
                foreach (Func<bool> condition in countdownConditions)
                    if (condition()) return true;
                return false;
            }
        }

        /// <summary>Seconds left in the resume countdown (0 when not counting down).</summary>
        public static float ResumeCountdownRemaining =>
            State == PauseState.Resuming ? (float)Math.Max(0d, GameAudioClock.ReleaseAtDsp - AudioSettings.dspTime) : 0f;

        /// <summary>
        /// Sets <c>Time.timeScale</c> without breaking the pause: while paused the value is remembered and applied on
        /// resume. Use it for hit-stop / slow-motion effects.
        /// </summary>
        public static void SetTimeScale(float timeScale)
        {
            if (Instance != null && Instance.state != PauseState.Running) Instance.savedTimeScale = timeScale;
            else Time.timeScale = timeScale;
        }

        /// <summary>Like <c>WaitForSecondsRealtime</c>, but the time spent paused does not count.</summary>
        public static IEnumerator WaitUnpausedRealtime(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (!IsPaused) elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ---------- Internals ----------

        private void FinishResume()
        {
            GameAudioClock.Released();
            UnpauseLooseAudio();
            Time.timeScale = savedTimeScale;
            Cursor.visible = savedCursorVisible;
            Cursor.lockState = savedCursorLock;
            pausedRealtimeTotal += Time.unscaledTime - pausedAtRealtime;
            SetState(PauseState.Running);
        }

        private void SetState(PauseState next)
        {
            state = next;
            StateChanged?.Invoke(next);
        }

        private void PauseLooseAudio()
        {
            pausedSources.Clear();
            foreach (AudioSource source in FindObjectsByType<AudioSource>())
            {
                if (source == null || !source.isPlaying || source.ignoreListenerPause || PausableAudio.IsManaged(source)) continue;
                source.Pause();
                pausedSources.Add(source);
            }
        }

        private void UnpauseLooseAudio()
        {
            foreach (AudioSource source in pausedSources)
                if (source != null) source.UnPause();
            pausedSources.Clear();
        }

        private static bool UiCancelPressed()
        {
            if (EventSystem.current == null) return false;
            var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
            var cancel = module != null && module.cancel != null ? module.cancel.action : null;
            return cancel != null && cancel.WasPressedThisFrame();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureEventSystem();

        /// <summary>
        /// Makes sure the scene has an EventSystem driven by the new Input System (the pause menu needs one). A legacy
        /// StandaloneInputModule is swapped out, since it cannot run with "Input System Package" as the active input.
        /// </summary>
        public static void EnsureEventSystem()
        {
            EventSystem eventSystem = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

            StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                legacy.enabled = false;
                Destroy(legacy);
            }

            InputSystemUIInputModule module = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            if (module.actionsAsset == null) module.AssignDefaultActions();
        }
    }
}
