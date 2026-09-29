using RythmRPG.Dialogue;
using UnityEngine;

// Dialogue System sequencer commands for the pixel camera. The Dialogue System finds commands by class name
// (SequencerCommand + command name) in this namespace, so they are used in a Sequence field like:
//
//   PixelCamFocus(listener, 0.8)                    glide to the listener in 0.8 s and stay on them
//   PixelCamFocus(Warp Char, 1, 0, 16)              glide to a GameObject by name, framed 16 px lower (x, y px)
//   PixelCamBetween(speaker, listener, 0.6)         frame the midpoint of two characters
//   PixelCamReturn(0.5)                             glide back to the player and give the camera back
//   PixelCamShake(3, 0.4)                           shake up to 3 game pixels, fading over 0.4 s
//
// Subjects: speaker, listener, a GameObject name, or tag=SomeTag. Durations are in seconds (0 = cut).
// Every command ends ("->Message" / "@Message" chaining works) when its move finishes.
// The camera also glides back to the player by itself when the conversation ends.
namespace PixelCrushers.DialogueSystem.SequencerCommands
{
    /// <summary>PixelCamFocus([subject=speaker], [duration=0.6], [xPixels=0], [yPixels=0])</summary>
    public class SequencerCommandPixelCamFocus : SequencerCommand
    {
        private float endTime;

        public void Start()
        {
            Transform subject = GetSubject(0, speaker);
            float duration = GetParameterAsFloat(1, 0.6f);
            var offset = new Vector2(GetParameterAsFloat(2, 0f), GetParameterAsFloat(3, 0f));
            if (subject == null && DialogueDebug.logWarnings)
                Debug.LogWarning($"Dialogue System: Sequencer: PixelCamFocus({GetParameters()}): subject not found.");
            if (subject == null || !PixelDialogueCamera.Instance.PanTo(subject, null, offset, duration))
            {
                Stop();
                return;
            }
            endTime = DialogueTime.time + duration;
        }

        public void Update()
        {
            if (DialogueTime.time >= endTime) Stop();
        }
    }

    /// <summary>PixelCamBetween([a=speaker], [b=listener], [duration=0.6], [xPixels=0], [yPixels=0])</summary>
    public class SequencerCommandPixelCamBetween : SequencerCommand
    {
        private float endTime;

        public void Start()
        {
            Transform a = GetSubject(0, speaker);
            Transform b = GetSubject(1, listener);
            float duration = GetParameterAsFloat(2, 0.6f);
            var offset = new Vector2(GetParameterAsFloat(3, 0f), GetParameterAsFloat(4, 0f));
            if (!PixelDialogueCamera.Instance.PanTo(a, b == a ? null : b, offset, duration))
            {
                Stop();
                return;
            }
            endTime = DialogueTime.time + duration;
        }

        public void Update()
        {
            if (DialogueTime.time >= endTime) Stop();
        }
    }

    /// <summary>PixelCamReturn([duration=0.6])</summary>
    public class SequencerCommandPixelCamReturn : SequencerCommand
    {
        private float endTime;

        public void Start()
        {
            float duration = GetParameterAsFloat(0, 0.6f);
            PixelDialogueCamera.Instance.Return(duration);
            endTime = DialogueTime.time + duration;
        }

        public void Update()
        {
            if (DialogueTime.time >= endTime) Stop();
        }
    }

    /// <summary>PixelCamShake([pixels=2], [duration=0.3])</summary>
    public class SequencerCommandPixelCamShake : SequencerCommand
    {
        private float endTime;

        public void Start()
        {
            float pixels = GetParameterAsFloat(0, 2f);
            float duration = GetParameterAsFloat(1, 0.3f);
            if (!PixelDialogueCamera.Instance.Shake(pixels, duration))
            {
                Stop();
                return;
            }
            endTime = DialogueTime.time + duration;
        }

        public void Update()
        {
            if (DialogueTime.time >= endTime) Stop();
        }
    }
}
