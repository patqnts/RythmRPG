using System.Collections.Generic;
using RythmRPG.Rhythm.Editing;
using UnityEngine;

namespace RythmRPG.Rhythm.Editor.Composer
{
    public sealed class NoteCatalogEntry
    {
        public string Id;
        public string Name;
        public string Group;
        public Color Color;
        public bool IsHold;
        public double DefaultHoldBeats = 2d;
        public NoteBehaviorKind Behavior = NoteBehaviorKind.Moving;
        public NoteHandleDefaults HandleDefaults = new NoteHandleDefaults();
    }

    /// <summary>Library contents. Uses NoteDefinition assets when present, otherwise built-in defaults.</summary>
    public static class NoteCatalog
    {
        public static List<NoteCatalogEntry> Build()
        {
            var list = new List<NoteCatalogEntry>();
            list.Add(Make(NoteMigration.DefinitionIdNormal, "Default", "Moving", new Color(0.35f, 0.75f, 1f), false));
            list.Add(Make(NoteMigration.DefinitionIdHold, "Hold", "Moving", new Color(0.4f, 1f, 0.55f), true));
            list.Add(Make(NoteMigration.DefinitionIdPong, "Pong", "Moving", new Color(0.8f, 0.5f, 1f), false));
            list.Add(Make(NoteMigration.DefinitionIdStationary, "Default", "Stationary", new Color(1f, 0.4f, 0.4f), false));
            list.Add(Make(NoteMigration.DefinitionIdStationaryHold, "Hold", "Stationary", new Color(1f, 0.6f, 0.3f), true));
            list.Add(Make(NoteMigration.DefinitionIdMash, "Mash", "Moving", new Color(1f, 0.85f, 0.2f), false));
            SetBehavior(list, NoteMigration.DefinitionIdHold, NoteBehaviorKind.MovingHold);
            SetBehavior(list, NoteMigration.DefinitionIdPong, NoteBehaviorKind.Pong);
            SetBehavior(list, NoteMigration.DefinitionIdStationary, NoteBehaviorKind.Stationary);
            SetBehavior(list, NoteMigration.DefinitionIdStationaryHold, NoteBehaviorKind.StationaryHold);
            SetBehavior(list, NoteMigration.DefinitionIdMash, NoteBehaviorKind.Mash);

            for (int i = 0; i < PatternLibrary.All.Count; i++)
            {
                PatternTemplate template = PatternLibrary.All[i];
                list.Add(Make(PatternLibrary.IdPrefix + template.Id, template.Name, "Patterns", new Color(0.45f, 0.65f, 1f), false));
            }

            NoteDefinition[] assets = Resources.LoadAll<NoteDefinition>("Combat/NoteDefinitions");
            for (int i = 0; i < assets.Length; i++)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j].Id != assets[i].DefinitionId) continue;
                    list[j].Color = assets[i].DisplayColor;
                    list[j].IsHold = assets[i].IsHold;
                    list[j].Behavior = assets[i].Behavior;
                    list[j].HandleDefaults = new NoteHandleDefaults
                    {
                        TravelSeconds = assets[i].DefaultTravelSeconds,
                        BadWindow = assets[i].StationaryBadWindow,
                        GoodWindow = assets[i].StationaryGoodWindow,
                        PerfectWindow = assets[i].StationaryPerfectWindow
                    };
                    if (!string.IsNullOrEmpty(assets[i].DisplayName)) list[j].Name = assets[i].DisplayName;
                }
            }

            return list;
        }

        private static void SetBehavior(List<NoteCatalogEntry> list, string id, NoteBehaviorKind behavior)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id) list[i].Behavior = behavior;
            }
        }

        private static NoteCatalogEntry Make(string id, string name, string group, Color color, bool hold)
        {
            return new NoteCatalogEntry { Id = id, Name = name, Group = group, Color = color, IsHold = hold };
        }
    }
}
