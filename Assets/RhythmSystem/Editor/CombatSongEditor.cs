using System.IO;
using UnityEditor;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor
{
    /// <summary>Combat Song inspector: the default fields plus warnings for anything that breaks a seamless section change.</summary>
    [CustomEditor(typeof(CombatSong))]
    public sealed class CombatSongEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var song = (CombatSong)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sections", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Intro", Describe(song.IntroClip, "none (starts on the loop)"));
            EditorGUILayout.LabelField("Loop", Describe(song.Clip, "missing"));
            EditorGUILayout.LabelField("Player Turn", Describe(song.PlayerTurnClip, "none (loop is filtered)"));
            EditorGUILayout.LabelField("End", Describe(song.EndClip, "none (fades out)"));
            if (song.DefeatEndClip != null) EditorGUILayout.LabelField("End (defeat)", Describe(song.DefeatEndClip, string.Empty));

            foreach (string warning in song.GetSectionWarnings())
                EditorGUILayout.HelpBox(warning, MessageType.Warning);

            WarnIfMp3(song.IntroClip, "Intro");
            WarnIfMp3(song.Clip, "Loop");
            WarnIfMp3(song.PlayerTurnClip, "Player Turn");
        }

        private static string Describe(AudioClip clip, string empty)
        {
            if (clip == null) return empty;
            double seconds = CombatSong.ExactSeconds(clip);
            return $"{clip.name}  ({seconds:0.000} s)";
        }

        private static void WarnIfMp3(AudioClip clip, string section)
        {
            if (clip == null) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!string.Equals(Path.GetExtension(path), ".mp3", System.StringComparison.OrdinalIgnoreCase)) return;
            EditorGUILayout.HelpBox($"{section} is an MP3. MP3 adds silence at the start and end of the file, so joins " +
                                    "and loops will have a small gap or stay out of sync. Export it as WAV (or Ogg).",
                MessageType.Warning);
        }
    }
}
