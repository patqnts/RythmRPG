using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Dialogue
{
    /// <summary>
    /// Applies <see cref="PixelDialoguePalette"/>s to the pixel dialogue UI. Lives on the Dialogue UI prefab root.
    /// Bubble sprites get a Pixel Palette material (one per palette, shared), shadows and text get plain colours.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelDialogueTheme : MonoBehaviour
    {
        private static readonly int[] KeyIds = PropertyIds("_Key");
        private static readonly int[] ColorIds = PropertyIds("_Col");

        [Tooltip("Palette used by every bubble unless the speaker's PixelSpeechAnchor has its own.")]
        [SerializeField] private PixelDialoguePalette palette;
        [Tooltip("RythmRPG/UI/Pixel Palette (assigned by the builder so it is included in builds).")]
        [SerializeField] private Shader paletteShader;

        private readonly Dictionary<PixelDialoguePalette, (Material material, int revision)> materials = new();
        private readonly List<PixelPaletteGraphic> graphics = new();
        private readonly List<PixelChoiceButton> choices = new();

        /// <summary>Changes whenever the default palette is swapped or a palette asset is edited.</summary>
        public int Version => (palette != null ? palette.Revision : 0) * 7919 + swaps;
        private int swaps;

        public PixelDialoguePalette Palette => palette;
        private PixelDialoguePalette startPalette;
        private bool startCaptured;

        private void Awake()
        {
            startPalette = palette;
            startCaptured = true;
        }

        /// <summary>Back to the palette the prefab was built with.</summary>
        public void ResetPalette()
        {
            if (startCaptured) SetPalette(startPalette);
        }

        /// <summary>A palette by asset name among the ones currently loaded (the default, characters' palettes, ...).</summary>
        public static PixelDialoguePalette FindLoaded(string paletteName)
        {
            if (string.IsNullOrWhiteSpace(paletteName)) return null;
            foreach (PixelDialoguePalette candidate in Resources.FindObjectsOfTypeAll<PixelDialoguePalette>())
            {
                if (string.Equals(candidate.name, paletteName.Trim(), System.StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            return null;
        }

        public static PixelDialogueTheme For(Component child) =>
            child != null ? child.GetComponentInParent<PixelDialogueTheme>(true) : null;

        /// <summary>Swap the default palette (all bubbles without their own palette follow).</summary>
        public void SetPalette(PixelDialoguePalette newPalette)
        {
            if (newPalette == palette) return;
            palette = newPalette;
            swaps++;
        }

        /// <summary>The palette for bubbles on <paramref name="character"/>: its own, else the default.</summary>
        public PixelDialoguePalette Resolve(Transform character)
        {
            if (character != null)
            {
                PixelSpeechAnchor anchor = character.GetComponentInChildren<PixelSpeechAnchor>();
                if (anchor != null && anchor.bubblePalette != null) return anchor.bubblePalette;
            }
            return palette;
        }

        /// <summary>Recolours every marked graphic and response button under <paramref name="root"/>.</summary>
        public void Apply(Transform root, PixelDialoguePalette colors)
        {
            if (root == null || colors == null) return;
            Material material = MaterialFor(colors);

            graphics.Clear();
            root.GetComponentsInChildren(true, graphics);
            foreach (PixelPaletteGraphic marked in graphics)
            {
                switch (marked.role)
                {
                    case PixelPaletteGraphic.Role.Art:
                        if (material != null && marked.TryGetComponent(out Graphic art) && art.material != material)
                            art.material = material;
                        break;
                    case PixelPaletteGraphic.Role.Shadow:
                        if (marked.TryGetComponent(out Graphic shadow)) shadow.color = colors.shadow;
                        break;
                    case PixelPaletteGraphic.Role.NameText:
                        if (marked.TryGetComponent(out TMP_Text nameText)) nameText.color = colors.nameText;
                        break;
                    case PixelPaletteGraphic.Role.BodyText:
                        if (marked.TryGetComponent(out TMP_Text bodyText)) bodyText.color = colors.bodyText;
                        break;
                }
            }
            graphics.Clear();

            choices.Clear();
            root.GetComponentsInChildren(true, choices);
            foreach (PixelChoiceButton choice in choices)
            {
                choice.normalColor = colors.choiceDim;
                choice.selectedColor = colors.choiceText;
                choice.disabledColor = colors.choiceDisabled;
            }
            choices.Clear();
        }

        private Material MaterialFor(PixelDialoguePalette colors)
        {
            if (paletteShader == null) paletteShader = Shader.Find("RythmRPG/UI/Pixel Palette");
            if (paletteShader == null) return null;

            if (!materials.TryGetValue(colors, out var entry) || entry.material == null)
            {
                entry.material = new Material(paletteShader) { name = $"Pixel Palette ({colors.name})", hideFlags = HideFlags.DontSave };
                entry.revision = int.MinValue;
            }
            if (entry.revision != colors.Revision)
            {
                for (int i = 0; i < KeyIds.Length; i++)
                {
                    bool used = i < PixelDialoguePalette.ArtKeys.Length;
                    entry.material.SetColor(KeyIds[i], used ? PixelDialoguePalette.ArtKeys[i] : new Color(4f, 4f, 4f, 1f));
                    entry.material.SetColor(ColorIds[i], used ? colors.Target(i) : Color.white);
                }
                entry.revision = colors.Revision;
            }
            materials[colors] = entry;
            return entry.material;
        }

        private void OnDestroy()
        {
            foreach (var entry in materials.Values)
                if (entry.material != null) Destroy(entry.material);
            materials.Clear();
        }

        private static int[] PropertyIds(string prefix)
        {
            var ids = new int[8];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID(prefix + i);
            return ids;
        }
    }
}
