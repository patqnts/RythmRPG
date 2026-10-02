using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RythmRPG.Combat;
using RythmRPG.Rhythm;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.EditorTools
{
    /// <summary>Installs missing content without replacing tuned assets or changing their GUIDs.</summary>
    public static class RhythmSynergyInstaller
    {
        private const string Root = "Assets/Resources/Combat/Build";
        private const string Samples = Root + "/Samples";
        [Serializable] private sealed class IconMap { public IconRow[] entries; }
        [Serializable] private sealed class IconRow { public string id; public string path; }

        [MenuItem("Tools/Rythm RPG/Combat/Build/Add Rhythm Synergy Content")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Install content outside play mode.");
            AssetDatabase.Refresh();
            var mapping = JsonUtility.FromJson<IconMap>(File.ReadAllText("Assets/Scripts/Editor/RhythmSynergyIcons.json"));
            var entries = new List<BuildIconCatalog.Entry>();
            foreach (IconRow row in mapping.entries)
            {
                Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(row.path).OfType<Sprite>().FirstOrDefault();
                if (sprite == null)
                {
                    var importer = AssetImporter.GetAtPath(row.path) as TextureImporter;
                    if (importer == null) throw new InvalidOperationException("Missing icon: " + row.path);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                    sprite = AssetDatabase.LoadAllAssetsAtPath(row.path).OfType<Sprite>().FirstOrDefault();
                }
                if (sprite == null) throw new InvalidOperationException("Icon has no sprite: " + row.path);
                entries.Add(new BuildIconCatalog.Entry { id = row.id, icon = sprite });
            }
            BuildIconCatalog icons = AssetDatabase.LoadAssetAtPath<BuildIconCatalog>(Root + "/BuildIconCatalog.asset");
            if (icons == null)
            {
                icons = ScriptableObject.CreateInstance<BuildIconCatalog>();
                AssetDatabase.CreateAsset(icons, Root + "/BuildIconCatalog.asset");
            }
            icons.Set(entries.ToArray());
            EditorUtility.SetDirty(icons);
            AssetDatabase.SaveAssets();
            CreateCharts();
            SampleBuildLibrary.Content content = SampleBuildLibrary.CreateFresh();
            var abilities = content.Abilities.Values.ToDictionary(a => a.Id, a => SaveMissing(a, Samples + "/Abilities/" + a.Id + ".asset"));
            var passives = content.Passives.Values.ToDictionary(p => p.Id, p => SaveMissing(p, Samples + "/Passives/" + p.Id + ".asset"));
            var upgrades = content.Upgrades.Values.ToDictionary(u => u.Id, u => SaveMissing(u, Samples + "/Upgrades/" + u.Id + ".asset"));
            var presets = new List<BuildPreset>();
            foreach (BuildPreset preset in content.Presets)
            {
                string path = Samples + "/Presets/" + preset.Id + ".asset";
                BuildPreset existing = AssetDatabase.LoadAssetAtPath<BuildPreset>(path);
                if (existing != null) { presets.Add(existing); continue; }
                foreach (PresetSlot slot in preset.Slots)
                {
                    slot.ability = abilities[slot.ability.Id];
                    slot.upgrades = slot.upgrades.Select(u => upgrades[u.Id]).ToList();
                }
                foreach (PresetPassive passive in preset.Passives) passive.passive = passives[passive.passive.Id];
                var serialized = new SerializedObject(preset);
                SerializedProperty reserve = serialized.FindProperty("reserve");
                for (int i = 0; i < reserve.arraySize; i++)
                {
                    var ability = reserve.GetArrayElementAtIndex(i).objectReferenceValue as AbilityDefinition;
                    if (ability != null) reserve.GetArrayElementAtIndex(i).objectReferenceValue = abilities[ability.Id];
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                presets.Add(SaveMissing(preset, path));
            }
            BuildContentCatalog catalog = AssetDatabase.LoadAssetAtPath<BuildContentCatalog>(Root + "/SampleBuildCatalog.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BuildContentCatalog>();
                AssetDatabase.CreateAsset(catalog, Root + "/SampleBuildCatalog.asset");
            }
            // Keep any manually registered additions as well as the existing authored sample objects.
            catalog.Set(catalog.Abilities.Concat(abilities.Values).Where(a => a != null).GroupBy(a => a.Id).Select(g => g.First()),
                catalog.Upgrades.Concat(upgrades.Values).Where(a => a != null).GroupBy(a => a.Id).Select(g => g.First()),
                catalog.Passives.Concat(passives.Values).Where(a => a != null).GroupBy(a => a.Id).Select(g => g.First()),
                catalog.Presets.Concat(presets).Where(a => a != null).GroupBy(a => a.Id).Select(g => g.First()));
            EditorUtility.SetDirty(catalog);
            var iconLookup = entries.ToDictionary(e => e.id, e => e.icon);
            foreach (string guid in AssetDatabase.FindAssets("t:AbilityDefinition"))
            {
                var ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (ability.Icon == null) SetIcon(ability, iconLookup.TryGetValue(ability.Id, out Sprite sprite) ? sprite : iconLookup["sample-strike"]);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:PassiveDefinition"))
            {
                var passive = AssetDatabase.LoadAssetAtPath<PassiveDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (passive.Icon == null) SetIcon(passive, iconLookup.TryGetValue(passive.Id, out Sprite sprite) ? sprite : iconLookup["ps-vitality"]);
            }
            // A Hold chart was already promised in Quake Slam's description. Keep any custom chart.
            AbilityDefinition quake = abilities["sample-quake-slam"];
            AbilityDefinition basic = Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack");
            if (quake.RhythmPattern == null || quake.RhythmPattern == basic.RhythmPattern)
            {
                var serialized = new SerializedObject(quake);
                serialized.FindProperty("rhythmPattern").objectReferenceValue = Resources.Load<RhythmChart>("Combat/Charts/QuakeSlamHold");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            BuildContentRegistry.Reset();
            // Destroy only the fresh temporary objects that were not persisted.
            foreach (Object temporary in content.Abilities.Values.Cast<Object>().Concat(content.Passives.Values)
                         .Concat(content.Upgrades.Values).Concat(content.Presets))
                if (!EditorUtility.IsPersistent(temporary)) Object.DestroyImmediate(temporary);
            Debug.Log($"[Rhythm synergy] Installed {abilities.Count} sample abilities, {passives.Count} passives and {presets.Count} presets; filled missing icons without replacing assigned artwork.");
        }

        private static T SaveMissing<T>(T value, string path) where T : Object
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            value.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(value, path);
            return value;
        }
        private static void SetIcon(Object asset, Sprite icon)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }
        private static void CreateCharts()
        {
            string folder = "Assets/Resources/Combat/Charts";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            RhythmChart presentation = Resources.Load<AbilityDefinition>("Combat/Abilities/BasicAttack").RhythmPattern;
            foreach (string name in new[]
                     {
                         "Backbeat", "Breakwater", "Cauterize", "Reprise", "QuakeSlamHold",
                         "Fortissimo", "GraceNote", "Heartbeat", "DrumBarrage", "Catalyze"
                     })
            {
                string path = folder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<RhythmChart>(path) != null) continue;
                RhythmChart chart = Object.Instantiate(presentation);
                chart.name = name;
                chart.Notes.Clear();
                chart.Patterns.Clear();
                chart.Sequences.Clear();
                RhythmNoteDefinition hold = chart.FindDefinition(RhythmNoteType.Hold);
                if (hold == null)
                {
                    hold = new RhythmNoteDefinition(RhythmNoteType.Hold, new Color(.4f, .8f, 1f));
                    chart.NoteDefinitions.Add(hold);
                }
                hold.DefaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/HoldNote.prefab");
                if (hold.DefaultPrefab == null) throw new InvalidOperationException("Hold note prefab missing.");
                RhythmNoteDefinition mash = chart.FindDefinition(RhythmNoteType.Mash);
                if (mash == null)
                {
                    mash = new RhythmNoteDefinition(RhythmNoteType.Mash, new Color(1f, .65f, .25f));
                    chart.NoteDefinitions.Add(mash);
                }
                mash.DefaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Mash.prefab");
                if (mash.DefaultPrefab == null) throw new InvalidOperationException("Mash note prefab missing.");
                double beat = 60d / chart.Bpm;
                bool holds = name == "Breakwater" || name == "QuakeSlamHold";
                double[] beats = holds ? new[] { 1d, 5d } : new[] { .5d, 1.5d, 2.5d, 4.5d, 5.5d, 6.5d };
                for (int i = 0; i < beats.Length; i++)
                {
                    RhythmLaneData lane = chart.Lanes[i % chart.Lanes.Count];
                    bool isHold = holds || name == "Reprise" && i == beats.Length - 1;
                    bool isMash = name == "DrumBarrage" && i == 2;
                    var note = new RhythmNoteData(lane.Id, chart.AudioOffsetSeconds + beats[i] * beat,
                        isMash ? RhythmNoteType.Mash : isHold ? RhythmNoteType.Hold : RhythmNoteType.Normal)
                    {
                        HoldDuration = isHold ? beat * (holds ? 1.5d : .5d) : 0d,
                        MashRequiredPresses = isMash ? 8 : 1,
                        Damage = 0, TravelTime = 2.5d, Speed = 8f
                    };
                    chart.Notes.Add(note);
                }
                AssetDatabase.CreateAsset(chart, path);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
