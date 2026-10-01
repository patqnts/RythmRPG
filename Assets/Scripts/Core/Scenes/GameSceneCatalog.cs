using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RythmRPG.Core
{
    [CreateAssetMenu(menuName = "Rythm RPG/Scenes/Scene Catalog", fileName = "GameSceneCatalog")]
    public sealed class GameSceneCatalog : ScriptableObject
    {
        public const string ResourcePath = "Scenes/GameSceneCatalog";
        [Header("Game flow")]
        public GameSceneDefinition menu;
        public GameSceneDefinition firstArea;
        public List<GameSceneDefinition> scenes = new();
        [Header("Loading screen")]
        [Min(0f)] public float fadeOutSeconds = .3f;
        [Min(0f)] public float fadeInSeconds = .3f;
        [Min(0f)] public float minimumLoadingSeconds = .25f;
        public TMP_FontAsset font;
        public Color background = new(.035f, .035f, .045f, 1f);
        public Color textColor = Color.white;
        [Header("Title menu")]
        [Tooltip("Uses the scene's Scene Title Menu controls, or creates a fallback start prompt / Quit / Cancel when none exist. Disable when providing your own menu system.")]
        public bool createMenuControls = true;
        public Vector2 menuPosition = new(0f, -430f);

        public bool TryGet(string id, out GameSceneDefinition destination)
        {
            destination = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            foreach (var candidate in scenes)
                if (candidate != null && string.Equals(candidate.id, id, StringComparison.Ordinal))
                {
                    if (destination != null) { destination = null; return false; }
                    destination = candidate;
                }
            return destination != null;
        }

        public GameSceneDefinition ForPath(string path)
        {
            foreach (var candidate in scenes)
                if (candidate != null && candidate.Path == path) return candidate;
            return null;
        }

        public string Problem(GameSceneDefinition destination)
        {
            if (destination == null) return "No destination was selected.";
            if (!TryGet(destination.id, out var registered) || registered != destination)
                return "The destination needs a unique ID in the scene catalog.";
            if (string.IsNullOrEmpty(destination.Path)) return "Assign a scene asset to " + destination.Label + ".";
            return null;
        }
    }
}
