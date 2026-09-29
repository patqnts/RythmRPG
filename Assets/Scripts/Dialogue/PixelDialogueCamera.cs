using System.Collections;
using PixelCrushers.DialogueSystem;
using Unity.Cinemachine;
using UnityEngine;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Camera moves for conversations and cutscenes, driven by the PixelCam sequencer commands
    /// (<c>PixelCamFocus</c>, <c>PixelCamBetween</c>, <c>PixelCamReturn</c>, <c>PixelCamShake</c>).
    /// <para>
    /// It doesn't add a camera: it borrows the live Cinemachine camera by pointing its Follow target at a hidden
    /// "focus" object and gliding that object between characters. The view keeps the gameplay camera's angle,
    /// size and oblique projection, so the Pixel Perfect Rig keeps everything on the pixel grid. When the
    /// conversation ends the focus glides back to the original Follow target (the player) and hands it back.
    /// </para>
    /// <para>Created automatically the first time a PixelCam command runs.</para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-200)]
    public sealed class PixelDialogueCamera : MonoBehaviour
    {
        [Tooltip("Glide back to the player when the conversation that moved the camera ends.")]
        public bool returnWhenConversationEnds = true;
        [Min(0f)] public float returnDuration = 0.6f;
        [Tooltip("Game pixels per world unit (Pixel Perfect Rig). Used for pixel offsets and shake.")]
        [Min(1f)] public float pixelsPerUnit = 32f;
        [Tooltip("Height above the pivot used for characters without renderers.")]
        public float fallbackHeight = 1.5f;

        private static PixelDialogueCamera instance;

        private Transform focus;
        private CinemachineBrain brain;
        private CinemachineVirtualCameraBase vcam;
        private Transform originalFollow;
        private bool engaged;
        private bool returning;
        private bool startedInConversation;

        private Transform subjectA;
        private Transform subjectB;
        private Vector3 worldOffset;
        private Vector3 panFrom;
        private float panStart;
        private float panDuration;
        private Vector3 basePosition;

        private float shakeStart;
        private float shakeEnd;
        private float shakePixels;

        public static PixelDialogueCamera Instance
        {
            get
            {
                if (instance != null) return instance;
                instance = FindAnyObjectByType<PixelDialogueCamera>();
                if (instance == null) instance = new GameObject("Pixel Dialogue Camera").AddComponent<PixelDialogueCamera>();
                return instance;
            }
        }

        /// <summary>True while the gameplay camera is borrowed.</summary>
        public bool IsEngaged => engaged;
        /// <summary>True while gliding back to the camera's own Follow target.</summary>
        public bool IsReturning => engaged && returning;

        /// <summary>The dialogue camera if one exists (doesn't create one, unlike <see cref="Instance"/>).</summary>
        public static PixelDialogueCamera Existing => instance != null ? instance : FindAnyObjectByType<PixelDialogueCamera>();

        /// <summary>
        /// Glides back (unless already on the way) and waits until the camera is handed back. Battle scenes use this so
        /// the fight doesn't carry on (and take over the camera) while the dialogue camera still has it.
        /// </summary>
        public IEnumerator ReturnAndWait()
        {
            if (!engaged) yield break;
            if (!returning) Return(returnDuration);
            float giveUpAt = Time.unscaledTime + returnDuration + 1f;
            while (engaged && Time.unscaledTime < giveUpAt) yield return null;
            if (engaged) Release();
        }

        private void Awake()
        {
            if (instance == null) instance = this;
            focus = new GameObject("Dialogue Camera Focus").transform;
            focus.SetParent(transform, false);
        }

        private void OnDisable() => Release();

        private void OnDestroy()
        {
            Release();
            if (instance == this) instance = null;
        }

        // ---------- Commands ----------

        /// <summary>
        /// Glides to <paramref name="a"/> (or the midpoint of a and b) over <paramref name="duration"/> seconds and
        /// keeps following it. <paramref name="pixelOffset"/> shifts the framing (x right, y up the screen).
        /// </summary>
        public bool PanTo(Transform a, Transform b, Vector2 pixelOffset, float duration)
        {
            if (a == null && b == null) return false;
            if (!Engage()) return false;
            subjectA = a;
            subjectB = b;
            // On the ground plane, screen x = world x and screen y = ground depth (world z) under the oblique projection.
            worldOffset = new Vector3(pixelOffset.x, 0f, pixelOffset.y) / pixelsPerUnit;
            returning = false;
            StartPan(duration);
            return true;
        }

        /// <summary>Glides back to the camera's own Follow target, then gives it back.</summary>
        public void Return(float duration)
        {
            if (!engaged) return;
            returning = true;
            subjectA = subjectB = null;
            StartPan(duration);
        }

        /// <summary>Shakes the view by up to <paramref name="pixels"/> game pixels, fading out over the duration.</summary>
        public bool Shake(float pixels, float duration)
        {
            if (!Engage()) return false;
            if (!returning && subjectA == null && subjectB == null) subjectA = originalFollow;
            shakePixels = Mathf.Max(0f, pixels);
            shakeStart = DialogueTime.time;
            shakeEnd = shakeStart + Mathf.Max(0f, duration);
            return true;
        }

        // ---------- Driving ----------

        private void Update()
        {
            if (!engaged) return;
            if (vcam == null || originalFollow == null || brain == null || !ReferenceEquals(brain.ActiveVirtualCamera, vcam))
            {
                // The camera was destroyed or another camera (e.g. combat) took over: give everything back now.
                Release();
                return;
            }
            if (vcam.Follow != focus)
            {
                // Something else (e.g. the battle framing the enemy) retargeted the camera: let go and leave its choice.
                Release();
                return;
            }

            bool inConversation = DialogueManager.hasInstance && DialogueManager.isConversationActive;
            if (returnWhenConversationEnds && startedInConversation && !inConversation && !returning)
                Return(returnDuration);

            float now = DialogueTime.time;
            Vector3 target = returning ? originalFollow.position : SubjectPoint() + worldOffset;
            float t = panDuration <= 0f ? 1f : Mathf.Clamp01((now - panStart) / panDuration);
            basePosition = Vector3.LerpUnclamped(panFrom, target, EaseInOut(t));

            if (returning && t >= 1f)
            {
                Release();
                return;
            }

            // A shake on the player outside any conversation lets go once it's over.
            if (!startedInConversation && t >= 1f && now >= shakeEnd && subjectB == null && subjectA == originalFollow)
            {
                Release();
                return;
            }

            focus.position = basePosition + ShakeOffset(now);
        }

        private bool Engage()
        {
            if (engaged && vcam != null && brain != null && ReferenceEquals(brain.ActiveVirtualCamera, vcam)) return true;
            Release();

            Camera main = Camera.main;
            brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
            if (brain == null) brain = FindAnyObjectByType<CinemachineBrain>();
            vcam = brain != null ? brain.ActiveVirtualCamera as CinemachineVirtualCameraBase : null;
            if (vcam == null || vcam.Follow == null)
            {
                Debug.LogWarning("[Pixel Dialogue] PixelCam: no live Cinemachine camera with a Follow target to move.", this);
                vcam = null;
                return false;
            }

            originalFollow = vcam.Follow;
            basePosition = originalFollow.position;
            focus.position = basePosition;
            vcam.Follow = focus;
            engaged = true;
            returning = false;
            startedInConversation = DialogueManager.hasInstance && DialogueManager.isConversationActive;
            return true;
        }

        private void Release()
        {
            if (engaged && vcam != null && originalFollow != null && vcam.Follow == focus) vcam.Follow = originalFollow;
            engaged = false;
            returning = false;
            subjectA = subjectB = null;
            vcam = null;
            originalFollow = null;
            shakeEnd = 0f;
        }

        private void StartPan(float duration)
        {
            panFrom = basePosition;
            panStart = DialogueTime.time;
            panDuration = Mathf.Max(0f, duration);
        }

        private Vector3 SubjectPoint()
        {
            if (subjectA != null && subjectB != null) return (GroundPoint(subjectA) + GroundPoint(subjectB)) * 0.5f;
            if (subjectA != null) return GroundPoint(subjectA);
            if (subjectB != null) return GroundPoint(subjectB);
            return originalFollow.position;
        }

        // Where a character stands (the bottom of its sprites), which is what the gameplay camera frames (the player's shadow).
        private Vector3 GroundPoint(Transform subject)
        {
            if (subject == originalFollow) return subject.position;
            return PixelSpeechAnchor.Resolve(subject, fallbackHeight).Feet;
        }

        private Vector3 ShakeOffset(float now)
        {
            if (now >= shakeEnd || shakePixels <= 0f) return Vector3.zero;
            float fade = 1f - Mathf.InverseLerp(shakeStart, shakeEnd, now);
            float amplitude = shakePixels * fade;
            // Whole game pixels, so the shake reads as pixel-art rather than a blur.
            float x = Mathf.Round(Random.Range(-amplitude, amplitude));
            float y = Mathf.Round(Random.Range(-amplitude, amplitude));
            return new Vector3(x, 0f, y) / pixelsPerUnit;
        }

        private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
